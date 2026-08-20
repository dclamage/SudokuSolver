namespace SudokuTests;

/// <summary>
/// Covers <see cref="WhispersConstraint"/>'s pairwise arc consistency — the deduction ISS gets from
/// <c>BinaryConstraint.enforceConsistency</c>. Before it, a whisper propagated *nothing* during
/// brute force: its weak links only fire on <c>SetValue</c>. See docs/renban-required-values.md.
/// </summary>
[TestClass]
public class WhispersTests
{
    /// <summary>
    /// With a difference of 5, only 9 is at least 5 away from 4 and only 1 is at least 5 away from 6.
    /// So a neighbour of a {4,6} cell can only be 1 or 9 — six candidates eliminated from a cell
    /// whose partner is not solved, which is exactly what weak links cannot do.
    /// </summary>
    [TestMethod]
    public void CandidatesWithNoSupportInTheNeighbourAreEliminated()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".Whisper~5~R1C1~R1C2",
            ".~R1C2_4_6"));

        WhispersConstraint whisper = solver.Constraints<WhispersConstraint>().Single();
        LogicResult result = whisper.StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true);

        Assert.AreEqual(LogicResult.Changed, result);
        Assert.AreEqual(ValuesMask(1, 9), solver.Board[0, 0] & ~valueSetMask);
        // The partner is unchanged: both of its values still have support.
        Assert.AreEqual(ValuesMask(4, 6), solver.Board[0, 1] & ~valueSetMask);
    }

    /// <summary>
    /// Arc consistency propagates along the line, not just across one pair: R1C3's {4,6} forces
    /// R1C2 to {1,9}, which in turn forces R1C1 to {6,7,8,9} ∪ {1,2,3,4} minus what the row already
    /// removes. One call does a forward and a backward sweep, so the whole line settles at once.
    /// </summary>
    [TestMethod]
    public void EliminationsPropagateAlongTheWholeLineInOneCall()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".Whisper~5~R1C1~R1C2~R1C3",
            ".~R1C3_4_6"));

        WhispersConstraint whisper = solver.Constraints<WhispersConstraint>().Single();
        Assert.AreEqual(LogicResult.Changed, whisper.StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true));

        Assert.AreEqual(ValuesMask(1, 9), solver.Board[0, 1] & ~valueSetMask);
        // R1C1 must be 5+ away from 1 or from 9, so 6..9 or 1..4 — and the row forbids nothing more.
        Assert.AreEqual(ValuesMask(1, 2, 3, 4, 6, 7, 8, 9), solver.Board[0, 0] & ~valueSetMask);
    }

    /// <summary>
    /// Where a cell is *solved*, the weak links already do this work at <c>SetValue</c> time, which
    /// is why the old "weak links enforce it" comment was half right: 4 with a difference of 5 leaves
    /// only 9 next door, and that happens before any <c>StepLogic</c> runs. The gap was only ever the
    /// unsolved case, which the two tests above cover.
    /// </summary>
    [TestMethod]
    public void SolvedCellIsAlreadyHandledByTheWeakLinks()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".Whisper~5~R1C1~R1C2",
            ".~R1C1_4"));

        Assert.AreEqual(ValuesMask(9), solver.Board[0, 1] & ~valueSetMask);
    }

    /// <summary>
    /// "Zoom Out" (CTC <c>OqyXKDOhfDA</c>) is four thermometers and twelve two-cell whispers on three
    /// givens. ISS solves it in 411 ms with 3,936 guesses; this solver took 19.6 s and 42.7 million
    /// nodes with no whisper propagation at all, and takes 116 ms with it. Kept as a correctness
    /// oracle — the count is ISS's own recorded answer — and as a guard against losing the deduction.
    /// </summary>
    [TestMethod]
    public void PathologicalWhisperPuzzleHasOneSolution()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".~R1C8_5~R5C5_6~R9C2_8",
            ".Thermo~R4C1~R3C2~R2C3~R1C4",
            ".Thermo~R6C9~R7C8~R8C7~R9C6",
            ".Thermo~R8C5~R7C4~R6C3~R5C4",
            ".Thermo~R2C5~R3C6~R4C7~R5C6",
            ".Whisper~5~R1C2~R1C3",
            ".Whisper~5~R2C1~R3C1",
            ".Whisper~5~R7C9~R8C9",
            ".Whisper~5~R9C7~R9C8",
            ".Whisper~5~R3C5~R4C5",
            ".Whisper~5~R6C5~R7C5",
            ".Whisper~5~R5C7~R5C8",
            ".Whisper~5~R5C2~R5C3",
            ".Whisper~5~R9C3~R9C4",
            ".Whisper~5~R6C1~R7C1",
            ".Whisper~5~R3C9~R4C9",
            ".Whisper~5~R1C6~R1C7"));

        Assert.AreEqual(1, solver.CountSolutions());
    }
}
