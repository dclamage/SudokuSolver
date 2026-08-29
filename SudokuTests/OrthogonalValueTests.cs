namespace SudokuTests;

/// <summary>
/// Covers <see cref="OrthogonalValueConstraint"/>'s brute-force arm — the arc consistency kropki,
/// difference, ratio and XV were missing. Same defect whispers and renban had, and the same cause:
/// the constraint declared that its weak links enforced it, but weak links only fire on
/// <c>SetValue</c>, so it deduced nothing at candidate level while brute forcing.
/// See docs/whisper-arc-consistency.md.
/// </summary>
/// <remarks>
/// Every case below uses the nonconsecutive negative constraint (<c>difference:neg1</c>), where a
/// value rules out itself and its two neighbours — a three-value mask, which is what makes the
/// popcount guard in the sweep bite.
///
/// End-to-end cover — that stronger propagation does not change how many solutions a board has —
/// already exists in <see cref="SolverEngineTests"/>, whose miracle-sudoku case counts 72 under a
/// nonconsecutive constraint in both threading modes. Not repeated here.
/// </remarks>
[TestClass]
public class OrthogonalValueTests
{
    private static OrthogonalValueConstraint Nonconsecutive(Solver solver)
        => solver.Constraints<OrthogonalValueConstraint>().Single();

    private static uint Candidates(Solver solver, int row, int col)
        => solver.Board[row, col] & ~valueSetMask;

    /// <summary>
    /// The deduction that was missing entirely. R1C2 restricted to {4,5,6} kills 5 in every
    /// orthogonal neighbour: a 5 next door would leave R1C2 no value, since 4, 5 and 6 are all
    /// equal or consecutive to it. No cell is solved, so weak links cannot reach this.
    /// </summary>
    [TestMethod]
    public void CandidateWithNoSupportInTheNeighbourIsEliminated()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["difference:neg1"]);
        Assert.AreEqual(LogicResult.Changed, solver.KeepMask(0, 1, ValuesMask(4, 5, 6)));

        Assert.AreEqual(LogicResult.Changed,
            Nonconsecutive(solver).StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true));

        Assert.AreEqual(ValuesMask(1, 2, 3, 4, 6, 7, 8, 9), Candidates(solver, 0, 0), "R1C1 kept 5");
        Assert.AreEqual(ValuesMask(1, 2, 3, 4, 6, 7, 8, 9), Candidates(solver, 0, 2), "R1C3 kept 5");
        Assert.AreEqual(ValuesMask(1, 2, 3, 4, 6, 7, 8, 9), Candidates(solver, 1, 1), "R2C2 kept 5");
        // The source cell has nine-candidate neighbours, so nothing comes back the other way.
        Assert.AreEqual(ValuesMask(4, 5, 6), Candidates(solver, 0, 1));
    }

    /// <summary>
    /// The same rule seen from the other end, and the reason the sweep needs only one direction.
    /// R1C1 in {4,5} rules out both 4 and 5 next door — 4 is equal to one and consecutive to the
    /// other, and likewise 5.
    /// </summary>
    /// <remarks>
    /// There was briefly a second pass computing this directly, folding a mask across R1C1's
    /// candidates. It was deleted as provably redundant: every marker relation here is symmetric in
    /// its two values (difference <c>v0+k==v1 || v1+k==v0</c>, ratio likewise, XV <c>v0+v1==k</c>),
    /// so "every candidate of R1C1 rules out 4" and "R1C1's candidates all lie inside 4's mask" are
    /// the same statement — and the second is what the pass above already tests when the sweep
    /// reaches R1C2. Removing it left all 33 corpus cases and all 398 ISS puzzles bit-identical on
    /// node count. This test is what keeps that equivalence honest.
    /// </remarks>
    [TestMethod]
    public void TheSameRuleAppliesFromTheNeighboursSide()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["difference:neg1"]);
        Assert.AreEqual(LogicResult.Changed, solver.KeepMask(0, 0, ValuesMask(4, 5)));

        Assert.AreEqual(LogicResult.Changed,
            Nonconsecutive(solver).StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true));

        Assert.AreEqual(ValuesMask(1, 2, 3, 6, 7, 8, 9), Candidates(solver, 0, 1), "R1C2 kept 4 or 5");
        Assert.AreEqual(ValuesMask(1, 2, 3, 6, 7, 8, 9), Candidates(solver, 1, 0), "R2C1 kept 4 or 5");
        Assert.AreEqual(ValuesMask(4, 5), Candidates(solver, 0, 0));
    }

    /// <summary>
    /// The boundary the sweep's popcount guard is drawn at, pinned so a future change cannot widen
    /// the guard without a failing test. Four candidates spanning {3,4,5,6} fit inside no
    /// three-value mask, so no value in a neighbour is refuted and there is genuinely nothing to
    /// find — which is exactly why skipping the pair on a popcount is exact rather than a heuristic.
    /// </summary>
    [TestMethod]
    public void NeighbourTooWideToRefuteAnythingYieldsNothing()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["difference:neg1"]);
        Assert.AreEqual(LogicResult.Changed, solver.KeepMask(0, 1, ValuesMask(3, 4, 5, 6)));

        Assert.AreEqual(LogicResult.None,
            Nonconsecutive(solver).StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true));

        Assert.AreEqual(ValuesMask(1, 2, 3, 4, 5, 6, 7, 8, 9), Candidates(solver, 0, 0));
        Assert.AreEqual(ValuesMask(3, 4, 5, 6), Candidates(solver, 0, 1));
    }

    /// <summary>
    /// The two arms are deliberately different code, so the explainable one needs its own guard:
    /// the logical arm must still stop at one deduction and describe it. A future "unify the arms"
    /// change that quietly routed logical solving through the silent sweep would fail here.
    /// </summary>
    [TestMethod]
    public void LogicalArmStillDescribesItsStep()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["difference:neg1"]);
        Assert.AreEqual(LogicResult.Changed, solver.KeepMask(0, 1, ValuesMask(4, 5, 6)));

        List<LogicalStepDesc> descs = [];
        Assert.AreEqual(LogicResult.Changed,
            Nonconsecutive(solver).StepLogic(solver, descs, isBruteForcing: false));

        Assert.AreEqual(1, descs.Count, "The logical arm must report exactly one step at a time.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(descs[0].ToString()));
    }
}
