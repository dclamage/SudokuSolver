namespace SudokuTests;

/// <summary>
/// Milestone 1 coverage for derived-sum-discovery plumbing: committing synthetic
/// <see cref="DerivedSumConstraint"/>s (which are consequences of the base rules) must never change
/// solution counts, and the discovery hook must be inert until a real template is enabled.
/// </summary>
[TestClass]
public class DerivedSumDiscoveryTests
{
    private static readonly List<(int, int)> Row0Of4 = [(0, 0), (0, 1), (0, 2), (0, 3)];
    private static readonly List<(int, int)> Row1Of4 = [(1, 0), (1, 1), (1, 2), (1, 3)];

    /// <summary>
    /// Every full house in a 4x4 sums to 1+2+3+4 = 10. A redundant derived fixed sum over a house
    /// must leave the count (288) unchanged in both threading modes, exercising the copy-on-write
    /// constraint list, the rebuilt propagation-queue maps, and the derived StepLogic in the loop.
    /// </summary>
    [TestMethod]
    public void CommitRedundantFixedSumPreservesCount()
    {
        long baseline = SolverFactory.CreateBlank(4).CountSolutions(multiThread: false);
        Assert.AreEqual(288, baseline);

        Solver solver = SolverFactory.CreateBlank(4);
        solver.CommitDerivedConstraints([DerivedSumConstraint.CreateFixedSum(solver, Row0Of4, [10])]);

        Assert.AreEqual(baseline, solver.CountSolutions(multiThread: false), "Single-threaded count changed after a redundant fixed sum");
        Assert.AreEqual(baseline, solver.CountSolutions(multiThread: true), "Multi-threaded count changed after a redundant fixed sum");
    }

    /// <summary>
    /// Two full houses in a 4x4 always have equal sums, so a redundant difference (= 0) must not
    /// change the count. Exercises the difference-relation path of the derived constraint.
    /// </summary>
    [TestMethod]
    public void CommitRedundantDifferencePreservesCount()
    {
        long baseline = SolverFactory.CreateBlank(4).CountSolutions(multiThread: false);

        Solver solver = SolverFactory.CreateBlank(4);
        solver.CommitDerivedConstraints([DerivedSumConstraint.CreateDifference(solver, Row0Of4, Row1Of4, 0)]);

        Assert.AreEqual(baseline, solver.CountSolutions(multiThread: false));
        Assert.AreEqual(baseline, solver.CountSolutions(multiThread: true));
    }

    /// <summary>
    /// Committing several redundant derived constraints at once still preserves the count and
    /// leaves FindSolution able to produce a valid complete board.
    /// </summary>
    [TestMethod]
    public void MultipleDerivedConstraintsPreserveSolvability()
    {
        Solver solver = SolverFactory.CreateBlank(4);
        solver.CommitDerivedConstraints(
        [
            DerivedSumConstraint.CreateFixedSum(solver, Row0Of4, [10]),
            DerivedSumConstraint.CreateFixedSum(solver, Row1Of4, [10]),
            DerivedSumConstraint.CreateDifference(solver, Row0Of4, Row1Of4, 0),
        ]);

        Assert.AreEqual(288, solver.CountSolutions(multiThread: false));
        Assert.IsTrue(solver.FindSolution(multiThread: false));
    }

    /// <summary>
    /// The discovery hook generates nothing yet, so a normal solve must be unaffected by it.
    /// (The full byte-for-byte guarantee is covered by the console differential corpus.)
    /// </summary>
    [TestMethod]
    public void DiscoveryHookIsInertByDefault()
    {
        const string u17 = "000000010400000000020000000000050407008000300001090000300400200050100000000806000";
        Assert.AreEqual(1, SolverFactory.CreateFromGivens(u17).CountSolutions(multiThread: false));
        Assert.AreEqual(1, SolverFactory.CreateFromGivens(u17).CountSolutions(multiThread: true));
    }
}
