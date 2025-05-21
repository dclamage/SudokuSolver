namespace SudokuTests;

static internal class TestUtility
{
    public static string ToGivenString(this Solver solver)
    {
        var flatBoard = solver.FlatBoard;
        StringBuilder stringBuilder = new(flatBoard.Count);
        if (solver.MAX_VALUE <= 9)
        {
            foreach (uint val in flatBoard)
            {
                stringBuilder.Append(GetValue(val));
            }
        }
        else
        {
            foreach (uint val in flatBoard)
            {
                if (val <= 9)
                {
                    stringBuilder.Append(0);
                }
                stringBuilder.Append(GetValue(val));
            }
        }
        return stringBuilder.ToString();
    }

    private static void TestUniqueSolutionImpl(this Solver solver, string expectedSolution, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        ulong solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.AreEqual(1u, solutionCount, $"Solution count not unique ({solutionCount}) for  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver2.FindSolution(multiThread: multiThread), $"Failed to find solution for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.AreEqual(expectedSolution, solver2.ToGivenString(), $"Solution found '{solver2.ToGivenString()}' does not match expected solution '{expectedSolution}' for puzzle: {solver.ToGivenString()}");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver3.FillRealCandidates(multiThread: multiThread), $"Failed to fill real candidates for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.IsTrue(solver3.IsComplete, $"Puzzle is not complete after filling real candidates for:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.AreEqual(expectedSolution, solver3.ToGivenString(), $"Solution after filling real candidates '{solver3.ToGivenString()}' does not match expected solution '{expectedSolution}' for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
    }

    public static void TestUniqueSolution(this Solver solver, string expectedSolution)
    {
        TestUniqueSolutionImpl(solver, expectedSolution, true);  // Test multi-threaded
        TestUniqueSolutionImpl(solver, expectedSolution, false); // Test single-threaded
    }

    private static void TestInvalidSolutionImpl(this Solver solver, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        ulong solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.AreEqual(0ul, solutionCount, $"Expected 0 solutions but found {solutionCount} solutions for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsFalse(solver2.FindSolution(multiThread: multiThread), $"Found solution for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsFalse(solver3.FillRealCandidates(multiThread: multiThread), $"Successfully filled real candidates for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
    }

    public static void TestInvalidSolution(this Solver solver)
    {
        TestInvalidSolutionImpl(solver, true);  // Test multi-threaded
        TestInvalidSolutionImpl(solver, false); // Test single-threaded
    }

    private static void TestMultipleSolutionImpl(this Solver solver, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        ulong solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.IsTrue(solutionCount > 1ul, $"Expected multiple solutions but found {solutionCount} solution(s) for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver2.FindSolution(multiThread: multiThread), $"Failed to find any solution for puzzle with multiple solutions:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver3.FillRealCandidates(multiThread: multiThread), $"Failed to fill real candidates for puzzle with multiple solutions:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.IsFalse(solver3.IsComplete, $"Puzzle unexpectedly complete after filling real candidates for multi-solution puzzle: {solver.Title}  by  {solver.Author}  ( {solver.ToGivenString()} )");
    }

    public static void TestMultipleSolution(this Solver solver)
    {
        TestMultipleSolutionImpl(solver, true);  // Test multi-threaded
        TestMultipleSolutionImpl(solver, false); // Test single-threaded
    }
}
