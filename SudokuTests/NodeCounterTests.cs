namespace SudokuTests;

/// <summary>
/// Tests for <see cref="Solver.NodesVisited"/>. The counter exists to make pruning changes
/// measurable and to act as a parity check, so what matters is not a particular number but that
/// the number is <i>trustworthy</i>: non-zero when a search really ran, reproducible, inclusive of
/// nested searches, and never silently dropped. Each test below pins one of those.
/// </summary>
[TestClass]
public class NodeCounterTests
{
    // Blank 4x4: 288 solutions, a real search tree small enough to count exhaustively.
    private const int Blank4x4Solutions = 288;

    /// <summary>
    /// A search that explores a tree must report a non-zero count, and two fresh solvers given the
    /// same board must report the same one. Reproducibility is the whole reason to prefer nodes
    /// over milliseconds, so it is the first thing worth pinning.
    /// </summary>
    [TestMethod]
    public void NodesVisitedIsNonZeroAndReproducible()
    {
        Solver first = SolverFactory.CreateBlank(4);
        Assert.AreEqual(Blank4x4Solutions, first.CountSolutions());
        Assert.IsTrue(first.NodesVisited > 0, "A real search reported zero nodes.");

        Solver second = SolverFactory.CreateBlank(4);
        Assert.AreEqual(Blank4x4Solutions, second.CountSolutions());
        Assert.AreEqual(first.NodesVisited, second.NodesVisited,
            "Two identical single-threaded searches disagreed on node count.");
    }

    /// <summary>
    /// <see cref="Solver.FindSolution"/> and <see cref="Solver.TrueCandidates"/> run their own
    /// search loops, so each has to be wired up separately. A zero here would mean a whole search
    /// path is invisible to the counter.
    /// </summary>
    [TestMethod]
    public void EverySearchLoopCounts()
    {
        Solver findSolution = SolverFactory.CreateBlank(4);
        Assert.IsTrue(findSolution.FindSolution());
        Assert.IsTrue(findSolution.NodesVisited > 0, "FindSolution reported zero nodes.");

        Solver trueCandidates = SolverFactory.CreateBlank(4);
        Assert.IsNotNull(trueCandidates.TrueCandidates());
        Assert.IsTrue(trueCandidates.NodesVisited > 0, "TrueCandidates reported zero nodes.");
    }

    /// <summary>
    /// The multi-threaded path accumulates a separate local per task and joins them, so it needs
    /// its own check. The total is deliberately <i>not</i> asserted equal to the single-threaded
    /// one: conflict scores are shared and updated concurrently, so the threads can order branches
    /// differently and explore a genuinely different tree.
    /// </summary>
    [TestMethod]
    public void MultiThreadedSearchCountsEveryTask()
    {
        Solver solver = SolverFactory.CreateBlank(4);
        Assert.AreEqual(Blank4x4Solutions, solver.CountSolutions(multiThread: true));
        Assert.IsTrue(solver.NodesVisited > 0, "Multi-threaded search reported zero nodes.");
    }

    /// <summary>
    /// True candidates finishes leftover candidates by running <see cref="Solver.CountSolutions"/>
    /// on clones, and the estimator does the same for tiny branches. Those nodes are real work, so
    /// the counter is shared by reference across clones to catch them. This test pins that sharing:
    /// a search run on a clone must land in the original's total.
    /// </summary>
    [TestMethod]
    public void NestedSearchOnACloneCountsAgainstTheOriginal()
    {
        Solver original = SolverFactory.CreateBlank(4);
        Assert.AreEqual(0, original.NodesVisited);

        Solver clone = original.Clone(willRunNonSinglesLogic: true);
        Assert.AreEqual(Blank4x4Solutions, clone.CountSolutions());

        Assert.AreEqual(clone.NodesVisited, original.NodesVisited,
            "A clone's search did not accumulate into the original's counter.");
        Assert.IsTrue(original.NodesVisited > 0);
    }

    /// <summary>
    /// The count is cumulative over the solver's lifetime rather than per call — a nested search
    /// would otherwise have to decide whose count to reset. Callers measure one operation by
    /// reading it on a freshly built solver, which is what the benchmark harness does.
    ///
    /// Deliberately not asserted as exactly double: conflict scores survive the first call and are
    /// shared with the clones the second one branches on, so the two searches can legitimately
    /// explore different trees. What is pinned is that the second call adds rather than resets.
    /// </summary>
    [TestMethod]
    public void NodesVisitedIsCumulativeAcrossCalls()
    {
        Solver once = SolverFactory.CreateBlank(4);
        Assert.AreEqual(Blank4x4Solutions, once.CountSolutions());
        long afterFirst = once.NodesVisited;
        Assert.IsTrue(afterFirst > 0);

        Assert.AreEqual(Blank4x4Solutions, once.CountSolutions());
        Assert.IsTrue(once.NodesVisited > afterFirst,
            "A second search did not add its nodes to the running total.");
    }

    /// <summary>
    /// Zero is a real answer, and this is the case that produces it: a solved grid is finished by
    /// <c>DiscoverWeakLinks</c> during setup, so <see cref="Solver.CountSolutions"/> returns before
    /// the search loop is ever entered. Anyone reading a benchmark row will meet this, so pin it
    /// rather than leaving it to look like a broken counter.
    /// </summary>
    [TestMethod]
    public void SetupOnlyCompletionCountsZeroNodes()
    {
        const string solvedGrid = "483726159726159483159483726837261594261594837594837261372615948615948372948372615";
        Solver solver = SolverFactory.CreateFromGivens(solvedGrid);
        Assert.AreEqual(1, solver.CountSolutions());
        Assert.AreEqual(0, solver.NodesVisited,
            "A board completed during setup should report no search nodes.");
    }
}
