namespace SudokuTests;

/// <summary>
/// Milestone 3: the first real derived-sum generator — combining parallel, adjacent, disjoint little
/// killers into a single fixed sum over their union. Combined sums are logical consequences of the
/// two little killers, so they must never change the solution count; the tests also confirm the
/// generator actually fires.
/// </summary>
[TestClass]
public class DerivedSumLittleKillerTests
{
    private static long Count(string givens, string[] constraints, bool discoveryEnabled, double gate, long cap)
    {
        string prevEnv = Environment.GetEnvironmentVariable("SUDOKU_DISABLE_DERIVED_SUMS");
        double prevGate = Solver.DerivedSumMinResidualEntropy;
        try
        {
            Environment.SetEnvironmentVariable("SUDOKU_DISABLE_DERIVED_SUMS", discoveryEnabled ? null : "1");
            Solver.DerivedSumMinResidualEntropy = gate;
            return SolverFactory.CreateFromGivens(givens, constraints).CountSolutions(maxSolutions: cap, multiThread: false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SUDOKU_DISABLE_DERIVED_SUMS", prevEnv);
            Solver.DerivedSumMinResidualEntropy = prevGate;
        }
    }

    /// <summary>
    /// On a wide-open grid, two adjacent anti-diagonal little killers should combine (they share rows,
    /// giving the cross-source uniqueness that makes the combined sum novel), and the (capped) count
    /// must be unchanged.
    /// </summary>
    [TestMethod]
    public void CombinedLittleKillerFiresOnOpenGrid()
    {
        string[] lks = ["lk:12;r1c3;DL", "lk:20;r1c4;DL"];
        string blank = new('0', 81);

        long off = Count(blank, lks, discoveryEnabled: false, gate: 10.0, cap: 3000);
        Solver.LastDerivedCommitCount = -1;
        long on = Count(blank, lks, discoveryEnabled: true, gate: 10.0, cap: 3000);

        Assert.AreEqual(off, on, "Capped count changed under discovery");
        Assert.IsTrue(Solver.LastDerivedCommitCount > 0, $"Discovery did not fire (committed {Solver.LastDerivedCommitCount})");
    }

    /// <summary>
    /// With discovery firing on the open grid, a brute-force solution must still satisfy both little
    /// killers exactly — confirming the committed combined-sum constraint does not corrupt the search
    /// (a bug in it could prune the true solution or admit an invalid one).
    /// </summary>
    [TestMethod]
    public void SolutionUnderDiscoverySatisfiesLittleKillersAndFires()
    {
        string[] lks = ["lk:12;r1c3;DL", "lk:20;r1c4;DL"];

        Solver.LastDerivedCommitCount = -1;
        Solver solver = SolverFactory.CreateBlank(9, lks);
        Assert.IsTrue(solver.FindSolution(multiThread: false), "No solution found");
        Assert.IsTrue(Solver.LastDerivedCommitCount > 0, $"Discovery did not fire (committed {Solver.LastDerivedCommitCount})");

        string s = solver.ToGivenString();
        int Val(int r, int c) => s[r * 9 + c] - '0';
        Assert.AreEqual(12, Val(0, 2) + Val(1, 1) + Val(2, 0), "Little killer A (sum 12) violated");
        Assert.AreEqual(20, Val(0, 3) + Val(1, 2) + Val(2, 1) + Val(3, 0), "Little killer B (sum 20) violated");
    }

    /// <summary>
    /// Discovery must not change a puzzle's satisfiability decision: an unsatisfiable little-killer
    /// combination still reports no solution with discovery on, and a satisfiable one still solves.
    /// </summary>
    [TestMethod]
    public void DiscoveryDoesNotChangeSatisfiability()
    {
        // Sum 6 forces {1,2,3} in box 0, making the adjacent sum-10 diagonal impossible.
        string[] unsat = ["lk:6;r1c3;DL", "lk:10;r1c4;DL"];
        Assert.AreEqual(0, Count(new string('0', 81), unsat, discoveryEnabled: true, gate: 10.0, cap: 5));

        string[] sat = ["lk:12;r1c3;DL", "lk:20;r1c4;DL"];
        Assert.IsTrue(Count(new string('0', 81), sat, discoveryEnabled: true, gate: 10.0, cap: 5) > 0);
    }
}
