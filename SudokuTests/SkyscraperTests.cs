namespace SudokuTests;

/// <summary>
/// Tests for <c>SkyscraperConstraint</c>, whose support search is the solver's only consumer of
/// <see cref="Solver.IsWeakLinkToAny"/>. The constraint had no tests when that path was added.
/// </summary>
/// <remarks>
/// <c>skyscraper:SrXcY</c> clues the line that enters the grid at r<c>X</c>c<c>Y</c>, so a column
/// index of 0 means "row X, scanned left to right" — not a column.
/// </remarks>
[TestClass]
public class SkyscraperTests
{
    /// <summary>
    /// The same shape as <c>benchmarks/corpus.json</c>'s <c>skyscraper-search</c>: a blank grid with
    /// a clue of 4 entering row 1 from the left and row 9 from the right, counted to a cap of 100.
    /// This is the heavy case — it drives the support search under real search pressure, which is
    /// what a change to the blocked test needs to be exercised by.
    /// </summary>
    /// <remarks>
    /// The cap is the assertion: the true solution count is far above it, so reaching exactly 100
    /// says the count stopped where it was told to and nothing on the way there was pruned wrongly.
    /// An over-eager blocked test would eliminate real candidates and come up short.
    /// </remarks>
    [TestMethod]
    public void OpposedCluesReachTheSolutionCap()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["skyscraper:4r1c0", "skyscraper:4r9c10"]);
        Assert.AreEqual(100, solver.CountSolutions(maxSolutions: 100, multiThread: false),
            "Counting to a cap of 100 should reach it. Coming up short means the support search "
            + "eliminated candidates it should have found support for.");
    }

    /// <summary>A clue of 1 forces the largest value into the first cell of the line.</summary>
    [TestMethod]
    public void ClueOfOneForcesTheMaximumIntoTheNearestCell()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["skyscraper:1r1c0"]);
        Assert.IsTrue(solver.FindSolution(multiThread: false), "A single skyscraper clue leaves solutions.");
        Assert.AreEqual(9, solver.GetValue((0, 0)), "Clue 1 means the first cell of the line holds the maximum.");
    }

    /// <summary>A clue of MAX_VALUE forces strictly increasing values along the whole line.</summary>
    [TestMethod]
    public void ClueOfMaxValueForcesStrictOrder()
    {
        Solver solver = SolverFactory.CreateBlank(9, ["skyscraper:9r1c0"]);
        Assert.IsTrue(solver.FindSolution(multiThread: false), "A full-order skyscraper clue leaves solutions.");
        for (int j = 0; j < 9; j++)
        {
            Assert.AreEqual(j + 1, solver.GetValue((0, j)), $"Cell {j + 1} of the clued line should hold {j + 1}.");
        }
    }

    /// <summary>
    /// Two clues that cannot both hold: a line cannot show 1 building and 9 buildings at once. Both
    /// are resolved by <c>InitCandidates</c> without any search, so the contradiction surfaces while
    /// the board is still being built.
    /// </summary>
    [TestMethod]
    public void ContradictoryCluesOnOneLineAreRejectedAtSetup()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SolverFactory.CreateBlank(9, ["skyscraper:1r1c0", "skyscraper:9r1c0"]),
            "Clues of 1 and 9 entering the same line from the same side cannot both be satisfied.");
    }
}
