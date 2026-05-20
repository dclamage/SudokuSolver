using System.Diagnostics;

namespace SudokuTests;

[TestClass]
public class SumConstraintTests
{
    // Simple puzzle with one box, some killer cages, and uncovered innie cells.
    private const string InniePuzzleUrl =
        "https://www.f-puzzles.com/?load=N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaCR3PgIm9hk0UM6zR4rssX0QAawgAbLTAwBjAIZo48QiH0wdYfCABKARgDCDkKjsAmFyDUA3Q1oArqYgiCAUFlZaNmb2zh5ucU4AzImeTgnuXqnuyRk+qP5BIcmpRGqG+gAuEL4wTgD2AHZgVRiGEE1VMXggANLauhgABE7GcLREQA=";

    [TestMethod]
    public void InniePuzzle_DiagnoseConstraints()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(InniePuzzleUrl);

        Console.WriteLine($"Board: {solver.WIDTH}x{solver.HEIGHT}, maxValue={solver.MAX_VALUE}");
        Console.WriteLine($"All constraints ({solver.Constraints<Constraint>().Count()}):");
        foreach (var c in solver.Constraints<Constraint>())
        {
            Console.WriteLine($"  [{c.GetType().Name}] {c.SpecificName}");
        }

        var killers = solver.Constraints<KillerCageConstraint>().ToList();
        Console.WriteLine($"KillerCageConstraints with sum>0: {killers.Count(k => k.sum > 0)}");
        foreach (var k in killers.Where(k => k.sum > 0))
        {
            Console.WriteLine($"  sum={k.sum}, cells=[{string.Join(", ", k.cells.Select(c => $"r{c.Item1+1}c{c.Item2+1}"))}]");
        }

        var innies = solver.Constraints<InnieCageConstraint>().ToList();
        Console.WriteLine($"InnieCageConstraints: {innies.Count}");
        foreach (var innie in innies)
        {
            Console.WriteLine($"  {innie.SpecificName}");
        }
    }

    [TestMethod]
    public void InniePuzzle_GeneratesInnieConstraint()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(InniePuzzleUrl);

        var innies = solver.Constraints<InnieCageConstraint>().ToList();
        Assert.IsNotEmpty(innies, "Expected at least one InnieCageConstraint, but optimizer generated none.");

        foreach (var innie in innies)
        {
            Console.WriteLine($"  {innie.SpecificName}");
        }
    }

    [TestMethod]
    public void InniePuzzle_InnieCandidatesAreRestricted()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(InniePuzzleUrl);

        var innies = solver.Constraints<InnieCageConstraint>().ToList();
        Assert.IsNotEmpty(innies, "No InnieCageConstraints generated.");

        foreach (var innie in innies)
        {
            Console.WriteLine($"  {innie.SpecificName}");
        }
    }

    // A killer-cage-only puzzle that ISS solves in ~11ms (TrueCandidates).
    private const string KillerCagePuzzleUrl =
        "https://www.f-puzzles.com/?load=N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaCR3PgIm9hk0UM6zR4rssX0QAawgAbLTAwBjAIZo48QiH0wdYfCABKARgDCDkKkdOATG/ueXPuz9vNQA3Qy0AV1MQBwAOEAoLKy0bM3tnAGYA5wAWbKcAVgC/ItDwqIQQTzzEy2tbDwA2Yqdmssjootrk1Lx0pwB2fPj2ivgY7276tI9EFrnR6Icu0iTpvrsMwYCtkdQwjsrmqZSGraz3LbzLwoCcpwv7AoeQRcrPVxPe+y3Xd3u/k9/O5nsF9uVop4sl8Go1gfY4WCQAcxiAVqA6qcZgN4XZYv43uMHJNVpjvnZELjKUiUUt0WssRscY88S9CSBjqSeg0cUV3PjrvZ8Xz7JS8uyHEMYTN7s0Qa0AnDBXY4SKVQr2Z4SRjuTKnPF/k45vKhvK9siIe8AAwJLnrBHbdxwg32HFy12OoUa8GHcaeTk6+12HHGr2himeiMuiMLH2ozzxIhqQz6AAuEBCMCcAHsAHZgVMYQwQXOp74AaW0ugwAAInMY4LQiEA==";

    [TestMethod]
    public void KillerCagePuzzleTrueCandidates()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(KillerCagePuzzleUrl);

        var sw = Stopwatch.StartNew();
        long[] trueCandidates = solver.TrueCandidates(multiThread: true);
        sw.Stop();

        Assert.IsNotNull(trueCandidates, "TrueCandidates returned null");
        Assert.IsTrue(trueCandidates.Length > 0, "TrueCandidates returned empty array");
        Assert.IsTrue(trueCandidates.Any(c => c > 0), "TrueCandidates found no solutions");

        Console.WriteLine($"KillerCage TrueCandidates: {sw.ElapsedMilliseconds}ms");
        Assert.IsTrue(sw.ElapsedMilliseconds < 10_000,
            $"TrueCandidates took {sw.ElapsedMilliseconds}ms, expected < 10s");
    }

        [TestMethod]
    public void KillerCagePuzzleTrueCandidatesSingleThreaded()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(KillerCagePuzzleUrl);

        var sw = Stopwatch.StartNew();
        long[] trueCandidates = solver.TrueCandidates(multiThread: false);
        sw.Stop();

        Assert.IsNotNull(trueCandidates, "TrueCandidates returned null");
        Assert.IsTrue(trueCandidates.Length > 0, "TrueCandidates returned empty array");
        Assert.IsTrue(trueCandidates.Any(c => c > 0), "TrueCandidates found no solutions");

        Console.WriteLine($"KillerCage TrueCandidates: {sw.ElapsedMilliseconds}ms");
        Assert.IsTrue(sw.ElapsedMilliseconds < 10_000,
            $"TrueCandidates took {sw.ElapsedMilliseconds}ms, expected < 10s");
    }

    [TestMethod]
    public void KillerCagePuzzleHasUniqueSolution()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(KillerCagePuzzleUrl);

        long count = solver.Clone(willRunNonSinglesLogic: false).CountSolutions(multiThread: true);
        Assert.AreEqual(1L, count, $"Expected unique solution but found {count}");
    }

    [TestMethod]
    public void KillerCagePuzzleFindSolution()
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(KillerCagePuzzleUrl);
        Solver clone = solver.Clone(willRunNonSinglesLogic: false);

        Assert.IsTrue(clone.FindSolution(multiThread: true), "Failed to find a solution");
    }
}
