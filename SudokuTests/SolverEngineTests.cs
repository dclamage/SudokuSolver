namespace SudokuTests;

/// <summary>
/// Tests targeting the brute-force search engine itself: the solver/search-stack
/// pools, the constraint propagation queue, and the setup-phase result handling in
/// <see cref="Solver.CountSolutions"/>. These paths are otherwise only exercised
/// indirectly by the puzzle-based constraint tests.
/// </summary>
[TestClass]
public class SolverEngineTests
{
    // Miracle Sudoku (anti-king, anti-knight, nonconsecutive) has exactly 72 solutions.
    private static readonly string[] MiracleConstraints = ["king", "knight", "difference:neg1"];

    /// <summary>
    /// Feeding a fully-solved grid as givens makes the setup pass (DiscoverWeakLinks)
    /// complete the puzzle before the search starts, exercising the
    /// <c>LogicResult.PuzzleComplete</c> setup branch in CountSolutions. The count must be 1.
    /// </summary>
    [TestMethod]
    public void CountSolutionsOnAlreadySolvedBoardIsOne()
    {
        int tested = 0;
        foreach (var (_, solution) in Puzzles.uniqueClassics)
        {
            if (tested++ >= 10)
            {
                break;
            }

            Assert.AreEqual(1, SolverFactory.CreateFromGivens(solution).CountSolutions(multiThread: false),
                $"Single-threaded count of a solved board was not 1: {solution}");
            Assert.AreEqual(1, SolverFactory.CreateFromGivens(solution).CountSolutions(multiThread: true),
                $"Multi-threaded count of a solved board was not 1: {solution}");
        }
    }

    /// <summary>
    /// A solved board with variant constraints also completes during setup and must count as 1.
    /// </summary>
    [TestMethod]
    public void CountSolutionsOnSolvedVariantBoardIsOne()
    {
        const string miracleSolution = "483726159726159483159483726837261594261594837594837261372615948615948372948372615";
        Assert.AreEqual(1, SolverFactory.CreateFromGivens(miracleSolution, MiracleConstraints).CountSolutions(multiThread: false));
        Assert.AreEqual(1, SolverFactory.CreateFromGivens(miracleSolution, MiracleConstraints).CountSolutions(multiThread: true));
    }

    /// <summary>
    /// Single- and multi-threaded solution counting must agree exactly. The multi-threaded
    /// path drives the thread-safe solver pool and search-stack pool.
    /// </summary>
    [TestMethod]
    public void CountSolutionsSingleAndMultiThreadAgree()
    {
        // Blank 4x4: exactly 288 solutions — a real search tree, small enough to run uncapped.
        Assert.AreEqual(288, SolverFactory.CreateBlank(4).CountSolutions(multiThread: false));
        Assert.AreEqual(288, SolverFactory.CreateBlank(4).CountSolutions(multiThread: true));

        // Miracle Sudoku: exactly 72 solutions, driving constraint propagation under both modes.
        Assert.AreEqual(72, SolverFactory.CreateBlank(9, MiracleConstraints).CountSolutions(multiThread: false));
        Assert.AreEqual(72, SolverFactory.CreateBlank(9, MiracleConstraints).CountSolutions(multiThread: true));

        // A classic puzzle with one given removed has multiple solutions
        // (see SolverTests.SolveMultiSolutionClassicGivens). Cap the count to bound runtime;
        // single- and multi-threaded search must still agree exactly on the (clamped) count.
        foreach (var (givens, _) in Puzzles.uniqueClassics[..3])
        {
            int idx = givens.IndexOfAny(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
            string partial = givens.Remove(idx, 1).Insert(idx, ".");
            long single = SolverFactory.CreateFromGivens(partial).CountSolutions(maxSolutions: 1000, multiThread: false);
            long multi = SolverFactory.CreateFromGivens(partial).CountSolutions(maxSolutions: 1000, multiThread: true);
            Assert.IsTrue(single > 1, $"Expected multiple solutions for a one-clue-removed board: {partial}");
            Assert.AreEqual(single, multi, $"Single/multi-threaded counts disagreed for: {partial}");
        }
    }

    /// <summary>
    /// A capped count must return exactly the cap when more solutions exist, in both modes.
    /// Exercises the pools while the search terminates early via MaxSolutionsReached.
    /// </summary>
    [TestMethod]
    public void CappedCountReturnsCapInBothModes()
    {
        Assert.AreEqual(100, SolverFactory.CreateBlank(4).CountSolutions(maxSolutions: 100, multiThread: false));
        Assert.AreEqual(100, SolverFactory.CreateBlank(4).CountSolutions(maxSolutions: 100, multiThread: true));
    }

    /// <summary>
    /// FindSolution must return a complete, valid solution for a multi-solution board in both
    /// modes. Re-counting the returned grid yields exactly 1, confirming it is a full valid board.
    /// </summary>
    [TestMethod]
    public void FindSolutionProducesValidBoardInBothModes()
    {
        string partial = Puzzles.uniqueClassics[0].Item2[..^12] + new string('0', 12);

        foreach (bool multiThread in new[] { false, true })
        {
            Solver solver = SolverFactory.CreateFromGivens(partial);
            Assert.IsTrue(solver.FindSolution(multiThread: multiThread), $"FindSolution failed (multiThread={multiThread})");

            string result = solver.ToGivenString();
            Assert.IsFalse(result.Contains('0') || result.Contains('.'), "Returned board is not fully filled");
            Assert.AreEqual(1, SolverFactory.CreateFromGivens(result).CountSolutions(multiThread: false),
                "Returned board is not a valid unique complete grid");
        }
    }
}
