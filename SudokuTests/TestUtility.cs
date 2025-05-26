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

    private static bool HasSolutions(long[] solutionCounts)
    {
        int boardSize = IntegerCubeRoot(solutionCounts.Length);
        Assert.AreEqual(boardSize * boardSize * boardSize, solutionCounts.Length, $"Invalid solution count length: {solutionCounts.Length}");

        int numCells = boardSize * boardSize;
        int numZero = solutionCounts.Count(count => count == 0);
        return numZero <= numCells * (boardSize - 1);
    }

    private static bool IsUniqueSolution(long[] solutionCounts)
    {
        int boardSize = IntegerCubeRoot(solutionCounts.Length);
        Assert.AreEqual(boardSize * boardSize * boardSize, solutionCounts.Length, $"Invalid solution count length: {solutionCounts.Length}");

        int numCells = boardSize * boardSize;
        int numZero = solutionCounts.Count(count => count == 0);
        int numOne = solutionCounts.Count(count => count == 1);
        int numMore = solutionCounts.Count(count => count > 1);
        return numMore == 0
            && numZero == numCells * (boardSize - 1)
            && numOne == numCells;
    }

    private static int IntegerCubeRoot(int n)
    {
        int x = 1;
        while (x * x * x < n)
        {
            x++;
        }
        return x;
    }

    private static bool TryExtractUniqueSolutionString(long[] solutionCounts, out string solutionString)
    {
        solutionString = string.Empty;

        int boardSize = IntegerCubeRoot(solutionCounts.Length);
        Assert.AreEqual(boardSize * boardSize * boardSize, solutionCounts.Length, $"Invalid solution count length: {solutionCounts.Length}");

        int numCells = boardSize * boardSize;
        int maxValue = boardSize;
        int[] board = new int[numCells];
        for (int i = 0; i < numCells; i++)
        {
            for (int v = 0; v < maxValue; v++)
            {
                int candidateIndex = i * maxValue + v;
                if (solutionCounts[candidateIndex] > 0)
                {
                    if (board[i] != 0)
                    {
                        // More than one candidate for cell i, so not unique
                        return false;
                    }

                    board[i] = v + 1;
                }
            }

            if (board[i] == 0)
            {
                // No candidate for cell i, so no solutions
                return false;
            }
        }

        // Convert the board to a string representation
        StringBuilder sb = new(numCells);
        for (int i = 0; i < numCells; i++)
        {
            sb.Append(board[i]);
        }
        solutionString = sb.ToString();
        return true;
    }

    private static void TestUniqueSolutionImpl(this Solver solver, string expectedSolution, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        long solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.AreEqual(1u, solutionCount, $"Solution count not unique ({solutionCount}) for  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver2.FindSolution(multiThread: multiThread), $"Failed to find solution for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.AreEqual(expectedSolution, solver2.ToGivenString(), $"Solution found '{solver2.ToGivenString()}' does not match expected solution '{expectedSolution}' for puzzle: {solver.ToGivenString()}");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        long[] solutionCounts = solver3.TrueCandidates(multiThread: multiThread);
        Assert.IsTrue(HasSolutions(solutionCounts), $"True candidates found no solutions for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.IsTrue(IsUniqueSolution(solutionCounts), $"True candidates found multiple solutions for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        if (TryExtractUniqueSolutionString(solutionCounts, out string solutionString))
        {
            Assert.AreEqual(expectedSolution, solutionString, $"Solution after filling true candidates '{solutionString}' does not match expected solution '{expectedSolution}' for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        }
        else
        {
            Assert.Fail($"Failed to extract unique solution string from solution counts for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        }
    }

    public static void TestUniqueSolution(this Solver solver, string expectedSolution)
    {
        TestUniqueSolutionImpl(solver, expectedSolution, true);  // Test multi-threaded
        TestUniqueSolutionImpl(solver, expectedSolution, false); // Test single-threaded
    }

    private static void TestInvalidSolutionImpl(this Solver solver, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        long solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.AreEqual(0, solutionCount, $"Expected 0 solutions but found {solutionCount} solutions for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsFalse(solver2.FindSolution(multiThread: multiThread), $"Found solution for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        long[] solutionCounts = solver3.TrueCandidates(multiThread: multiThread);
        Assert.IsFalse(HasSolutions(solutionCounts), $"Found true candidates for invalid puzzle: {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
    }

    public static void TestInvalidSolution(this Solver solver)
    {
        TestInvalidSolutionImpl(solver, true);  // Test multi-threaded
        TestInvalidSolutionImpl(solver, false); // Test single-threaded
    }

    private static void TestMultipleSolutionImpl(this Solver solver, bool multiThread)
    {
        Solver solver1 = solver.Clone(willRunNonSinglesLogic: false);
        long solutionCount = solver1.CountSolutions(multiThread: multiThread);
        Assert.IsTrue(solutionCount > 1, $"Expected multiple solutions but found {solutionCount} solution(s) for puzzle:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver2 = solver.Clone(willRunNonSinglesLogic: false);
        Assert.IsTrue(solver2.FindSolution(multiThread: multiThread), $"Failed to find any solution for puzzle with multiple solutions:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");

        Solver solver3 = solver.Clone(willRunNonSinglesLogic: false);
        long[] solutionCounts = solver3.TrueCandidates(multiThread: multiThread);
        Assert.IsTrue(HasSolutions(solutionCounts), $"Failed to find true candidates for puzzle with multiple solutions:  {solver.Title} by {solver.Author} ({solver.ToGivenString()})");
        Assert.IsFalse(IsUniqueSolution(solutionCounts), $"True candidates found unique solution for multi-solution puzzle: {solver.Title}  by  {solver.Author}  ( {solver.ToGivenString()} )");
    }

    public static void TestMultipleSolution(this Solver solver)
    {
        TestMultipleSolutionImpl(solver, true);  // Test multi-threaded
        TestMultipleSolutionImpl(solver, false); // Test single-threaded
    }
}
