namespace SudokuTests;

/// <summary>
/// Covers <see cref="RenbanConstraint"/>'s required-value exclusion — the deduction ISS gets from
/// its <c>BinaryPairwise</c> handler's <c>requiredValues</c> table, and the one that separated us
/// from it by three orders of magnitude on renban puzzles. See docs/renban-required-values.md.
/// </summary>
[TestClass]
public class RenbanTests
{
    /// <summary>
    /// The four-cell line can only be the sequence 3-4-5-6, so 3 and 4 must both appear on it, and
    /// only R1C1 and R2C2 can hold them. Every cell seeing both — the rest of box 1 — therefore
    /// cannot be 3 or 4.
    ///
    /// The construction deliberately makes this unreachable by ordinary means: R1C1 and R2C2 each
    /// still have four candidates, so they are not a naked pair, and the elimination only follows
    /// once you know the *line* must contain 3 and 4.
    /// </summary>
    [TestMethod]
    public void RequiredValuesArePointedOutOfCellsSeeingAllTheirCandidates()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".Renban~R1C1~R2C2~R3C3~R1C4",
            ".~R1C1_3_4_5_6~R2C2_3_4_5_6~R3C3_5_6~R1C4_5_6"));

        // R3C1 sees both R1C1 (column 1) and R2C2 (box 1), and is not on the line.
        Assert.IsTrue(HasValue(solver.Board[2, 0], 3), "precondition: 3 is still a candidate in R3C1");
        Assert.IsTrue(HasValue(solver.Board[2, 0], 4), "precondition: 4 is still a candidate in R3C1");

        Assert.AreEqual(LogicResult.Changed, StepToFixpoint(solver));

        Assert.IsFalse(HasValue(solver.Board[2, 0], 3), "3 must be eliminated from R3C1");
        Assert.IsFalse(HasValue(solver.Board[2, 0], 4), "4 must be eliminated from R3C1");
        // The line itself keeps them: they have to go somewhere.
        Assert.IsTrue(HasValue(solver.Board[0, 0], 3));
        Assert.IsTrue(HasValue(solver.Board[1, 1], 3));
    }

    /// <summary>
    /// When a required value has only one cell left on the line, that cell takes it. This is a
    /// hidden single argued from the sequence rather than from a house.
    /// </summary>
    [TestMethod]
    public void RequiredValueWithASingleCandidateCellIsPlaced()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".Renban~R1C1~R2C2~R3C3~R1C4",
            ".~R1C1_3_4~R2C2_3_4~R3C3_5_6~R1C4_5_6"));

        // Only the sequence 3-4-5-6 fits, so all four values are required. Narrowing R3C3 to 6
        // leaves R1C4 as the only cell that can hold the required 5.
        Assert.AreEqual(LogicResult.Changed, solver.KeepMask(2, 2, ValuesMask(6)));

        Assert.AreEqual(LogicResult.Changed, StepToFixpoint(solver));

        Assert.AreEqual(5, GetValue(solver.Board[0, 3]), "R1C4 is the only cell left for the required 5");
        Assert.IsTrue(IsValueSet(solver.Board[0, 3]));
    }

    /// <summary>
    /// "Vivian" (CTC <c>h-ymyScJa2s</c>) is 14 renban lines and two givens. ISS solves it in 105 ms
    /// with 5,091 guesses; before required-value exclusion this solver explored 14 million nodes and
    /// took 8.3 s, and with it, 26 ms. Kept as both a correctness oracle — the solution count is
    /// ISS's own recorded answer — and a guard against losing the deduction.
    /// </summary>
    [TestMethod]
    public void PathologicalRenbanPuzzleHasOneSolution()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".~R2C9_2~R9C5_9",
            ".Renban~R1C1~R1C2~R2C3~R3C4~R3C5~R3C6",
            ".Renban~R1C4~R1C5~R1C6~R2C7",
            ".Renban~R2C8~R3C9",
            ".Renban~R4C8~R4C7~R5C7",
            ".Renban~R5C8~R5C9~R4C9",
            ".Renban~R6C7~R6C8~R6C9~R7C9",
            ".Renban~R6C6~R7C5~R7C4~R8C4",
            ".Renban~R5C4~R5C3~R6C3",
            ".Renban~R4C1~R5C1",
            ".Renban~R8C1~R9C1~R9C2",
            ".Renban~R6C2~R7C3~R8C3~R9C3",
            ".Renban~R9C6~R9C7~R8C7",
            ".Renban~R8C8~R9C8"));

        Assert.AreEqual(1, solver.CountSolutions());
    }

    /// <summary>
    /// Runs only the renban constraint's own propagation to a fixpoint. StepLogic returns after its
    /// first deduction by convention, and nothing else runs, so a test can attribute the result to
    /// this constraint alone.
    /// </summary>
    private static LogicResult StepToFixpoint(Solver solver)
    {
        RenbanConstraint renban = solver.Constraints<RenbanConstraint>().Single();
        bool changed = false;
        while (true)
        {
            LogicResult result = renban.StepLogic(solver, (List<LogicalStepDesc>)null, isBruteForcing: true);
            if (result == LogicResult.Invalid)
            {
                return result;
            }
            if (result == LogicResult.None)
            {
                return changed ? LogicResult.Changed : LogicResult.None;
            }
            changed = true;
        }
    }
}
