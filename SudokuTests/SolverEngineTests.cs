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
    /// Checks <see cref="Solver.TrueCandidates"/> against an independent brute-force oracle: for
    /// every candidate, count the solutions that contain it by forcing the cell and counting
    /// directly. Run with the stall limit at 0, which sends every candidate through the directed
    /// endgame, and again at its default, which uses the ordinary undirected search.
    /// </summary>
    /// <remarks>
    /// The endgame stops the bulk search early and resolves the remaining candidates one at a time,
    /// so it has two ways to be silently wrong: leaving a real candidate uncovered (reported as
    /// impossible) or stopping short of the cap. Neither shows up in the corpus, whose
    /// true-candidates cases all use a cap of 1 and only check a summed total. A cap above 1 is
    /// where the counts, not just the yes/no answers, have to agree.
    /// </remarks>
    [TestMethod]
    public void TrueCandidatesMatchesBruteForceOracle()
    {
        // Enough clues to keep the oracle's 729 counts affordable, few enough to leave a real
        // search with many solutions per candidate.
        string givens = Puzzles.uniqueClassics[0].Item2[..^24] + new string('0', 24);

        foreach (long cap in new long[] { 1, 8 })
        {
            long[] oracle = new long[81 * 9];
            for (int cellIndex = 0; cellIndex < 81; cellIndex++)
            {
                for (int value = 1; value <= 9; value++)
                {
                    Solver probe = SolverFactory.CreateFromGivens(givens);
                    // SetValue reports a trivial contradiction; a surviving board still has to be
                    // counted, and may yet turn out to have no solutions.
                    oracle[cellIndex * 9 + value - 1] = probe.SetValue(cellIndex, value)
                        ? probe.CountSolutions(maxSolutions: cap)
                        : 0;
                }
            }

            // 0 forces the directed endgame for every candidate; null leaves the shipped default,
            // which normally keeps the search on the undirected path.
            foreach (long? stallLimit in new long?[] { 0, null })
            {
                Solver solver = SolverFactory.CreateFromGivens(givens);
                if (stallLimit.HasValue)
                {
                    solver.TrueCandidatesStallLimit = stallLimit.Value;
                }
                long[] actual = solver.TrueCandidates(numSolutionsCap: cap);

                for (int i = 0; i < oracle.Length; i++)
                {
                    Assert.AreEqual(oracle[i], Math.Min(actual[i], cap),
                        $"candidate {i} disagreed with the oracle (cap={cap}, stallLimit={stallLimit?.ToString() ?? "default"})");
                }
            }
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

    /// <summary>
    /// SetValue applies weak links through a grouped (cell, mask) table that collapses every target
    /// sharing a cell into one masked clear. That must be indistinguishable from clearing the
    /// targets one candidate at a time, including the bookkeeping each clear performs — the
    /// naked-single queue, the hidden-single group counts and the "cell became empty" check.
    /// </summary>
    /// <remarks>
    /// Exact solution counts are the strongest available probe: a grouped clear that dropped a
    /// candidate it should not have, or skipped bookkeeping, changes the count. Constraints that
    /// link *different* digits across cells are the interesting ones, because those are what make a
    /// candidate's targets cluster into a cell at all — a vanilla grid produces no clustering.
    /// </remarks>
    [TestMethod]
    public void GroupedWeakLinksMatchPerCandidateClears()
    {
        // nonconsecutive is the case that matters: it links *different* digits across cells, so a
        // candidate's targets cluster into a cell and the grouped table actually has something to
        // collapse. A plain grid produces no clustering at all, and is here as the control.
        // Both counts were established by exhaustive enumeration; keep them small, because
        // WeakLinkDiscoveryMode.Never is pathologically slow on densely linked boards.
        (string name, Func<Solver> create, long expected)[] cases =
        [
            ("nonconsecutive 6x6", () => SolverFactory.CreateBlank(6, ["difference:neg1"]), 48),
            ("plain 4x4", () => SolverFactory.CreateBlank(4), 288),
        ];

        foreach ((string name, Func<Solver> create, long expected) in cases)
        {
            // The three modes end up with very different numbers of weak links, and therefore very
            // different amounts of clustering for the grouped table to collapse. All must agree.
            long never = WithDiscovery(create, WeakLinkDiscoveryMode.Never).CountSolutions();
            long always = WithDiscovery(create, WeakLinkDiscoveryMode.Always).CountSolutions();
            long deferred = WithDiscovery(create, WeakLinkDiscoveryMode.Deferred).CountSolutions();

            Assert.AreEqual(expected, never, $"{name}: wrong count with discovery Never");
            Assert.AreEqual(expected, always, $"{name}: wrong count with discovery Always");
            Assert.AreEqual(expected, deferred, $"{name}: wrong count with discovery Deferred");
        }
    }

    /// <summary>
    /// The grouped table is inherited by clones by reference and invalidated by AddWeakLink, so the
    /// invariant "non-null implies it matches these lists" has to survive discovery adding links
    /// between two searches on the same solver.
    /// </summary>
    [TestMethod]
    public void GroupedWeakLinksSurviveRepeatedSearchesOnOneSolver()
    {
        Solver solver = SolverFactory.CreateBlank(6, ["difference:neg1"]);
        solver.WeakLinkDiscovery = WeakLinkDiscoveryMode.Always;

        long first = solver.CountSolutions();
        long second = solver.CountSolutions();
        long third = solver.CountSolutions();

        Assert.AreEqual(48, first);
        Assert.AreEqual(first, second, "a second search on the same solver disagreed with the first");
        Assert.AreEqual(first, third, "a third search on the same solver disagreed with the first");
    }
}
