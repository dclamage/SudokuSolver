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

    /// <summary>
    /// Builds a solver with an explicit weak-link-discovery mode. The node threshold is forced to 1
    /// so that <see cref="WeakLinkDiscoveryMode.Deferred"/> abandons and retries on anything with a
    /// real search tree — otherwise most test puzzles finish inside the default budget and the
    /// retry path never runs.
    /// </summary>
    private static Solver WithDiscovery(Func<Solver> create, WeakLinkDiscoveryMode mode)
    {
        Solver solver = create();
        solver.WeakLinkDiscovery = mode;
        solver.WeakLinkDiscoveryNodeThreshold = 1;
        return solver;
    }

    /// <summary>
    /// Deferred discovery abandons its first search and restarts, which is only sound if the
    /// abandoned attempt's partial state is fully discarded. A restarted count that carried over
    /// the solutions it had already found would over-count — silently, and only on the puzzles big
    /// enough to trigger the retry.
    /// </summary>
    [TestMethod]
    public void DeferredDiscoveryCountsMatchImmediateDiscovery()
    {
        (string label, Func<Solver> create)[] cases =
        [
            ("blank 4x4", () => SolverFactory.CreateBlank(4)),
            ("miracle", () => SolverFactory.CreateBlank(9, MiracleConstraints)),
            ("classic minus one clue", () =>
            {
                string givens = Puzzles.uniqueClassics[0].Item1;
                int idx = givens.IndexOfAny(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
                return SolverFactory.CreateFromGivens(givens.Remove(idx, 1).Insert(idx, "."));
            }),
        ];

        foreach ((string label, Func<Solver> create) in cases)
        {
            foreach (bool multiThread in new[] { false, true })
            {
                long expected = WithDiscovery(create, WeakLinkDiscoveryMode.Always)
                    .CountSolutions(maxSolutions: 1000, multiThread: multiThread);
                long deferred = WithDiscovery(create, WeakLinkDiscoveryMode.Deferred)
                    .CountSolutions(maxSolutions: 1000, multiThread: multiThread);
                Assert.AreEqual(expected, deferred,
                    $"Deferred discovery changed the count for {label} (multiThread={multiThread})");
            }
        }
    }

    /// <summary>
    /// The other two deferrable operations must also survive the restart: FindSolution has to
    /// return a genuine solution rather than the abandoned attempt's "no solution found", and
    /// TrueCandidates has to report the same candidates rather than the partial counts it had
    /// accumulated when it gave up.
    /// </summary>
    [TestMethod]
    public void DeferredDiscoverySolveAndTrueCandidatesMatchImmediateDiscovery()
    {
        string partial = Puzzles.uniqueClassics[0].Item2[..^12] + new string('0', 12);
        Func<Solver> create = () => SolverFactory.CreateFromGivens(partial);

        foreach (bool multiThread in new[] { false, true })
        {
            Solver deferredSolver = WithDiscovery(create, WeakLinkDiscoveryMode.Deferred);
            Assert.IsTrue(deferredSolver.FindSolution(multiThread: multiThread),
                $"Deferred FindSolution found nothing (multiThread={multiThread})");
            Assert.AreEqual(1, SolverFactory.CreateFromGivens(deferredSolver.ToGivenString()).CountSolutions(),
                $"Deferred FindSolution returned an invalid grid (multiThread={multiThread})");
        }

        // Raw true-candidate counts are not deterministic, so compare them the way callers do:
        // clamped to the cap. See docs/HANDOFF.md section 4.
        const long cap = 8;
        static long[] Clamp(long[] counts) => [.. counts.Select(n => Math.Min(n, cap))];

        long[] expected = Clamp(WithDiscovery(create, WeakLinkDiscoveryMode.Always).TrueCandidates(numSolutionsCap: cap));
        long[] deferred = Clamp(WithDiscovery(create, WeakLinkDiscoveryMode.Deferred).TrueCandidates(numSolutionsCap: cap));
        CollectionAssert.AreEqual(expected, deferred, "Deferred discovery changed the true candidates");
    }

    /// <summary>
    /// True candidates must return identical <b>raw</b> counts on repeated runs, not merely
    /// identical clamped ones.
    /// </summary>
    /// <remarks>
    /// The search randomises its branch choice deliberately, but from a stream scoped to the
    /// invocation, so the sequence restarts every call. Reverting that to a time-seeded generator
    /// would still pass every correctness test — clamped results are stable either way — while
    /// silently making the operation unbenchmarkable, which is how it went unnoticed before.
    /// Branch order is worth up to 16x on these puzzles, so this is not a small effect.
    /// </remarks>
    [TestMethod]
    public void TrueCandidatesRawCountsAreReproducible()
    {
        string partial = Puzzles.uniqueClassics[0].Item2[..^20] + new string('0', 20);

        long[] first = SolverFactory.CreateFromGivens(partial).TrueCandidates();
        for (int run = 0; run < 3; run++)
        {
            CollectionAssert.AreEqual(first, SolverFactory.CreateFromGivens(partial).TrueCandidates(),
                $"Raw true-candidate counts differed on run {run + 1}");
        }
    }

    /// <summary>
    /// A <c>solutionEvent</c> handler must see each solution exactly once. Deferral opts out
    /// entirely when one is attached, because a restart would replay every solution the abandoned
    /// attempt had already reported.
    /// </summary>
    [TestMethod]
    public void DeferredDiscoveryDoesNotReplaySolutionEvents()
    {
        List<string> solutions = [];
        Solver solver = WithDiscovery(() => SolverFactory.CreateBlank(4), WeakLinkDiscoveryMode.Deferred);
        long count = solver.CountSolutions(solutionEvent: s => solutions.Add(s.ToGivenString()));

        Assert.AreEqual(288, count);
        Assert.AreEqual(288, solutions.Count, "solutionEvent fired a different number of times than the count");
        Assert.AreEqual(288, solutions.Distinct().Count(), "solutionEvent reported the same solution more than once");
    }
}
