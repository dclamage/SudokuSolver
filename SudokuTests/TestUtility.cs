namespace SudokuTests;

static internal class TestUtility
{
    public static string ToGivenString(this Solver solver)
    {
        var flatBoard = solver.FlatBoard;
        StringBuilder stringBuilder = new(flatBoard.Length);
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

    public static void TestUniqueSolution(this Solver solver, string expectedSolution)
    {
        Solver solver1 = solver.Clone();
        Assert.AreEqual(1u, solver1.CountSolutions(multiThread: true));

        Solver solver2 = solver.Clone();
        Assert.IsTrue(solver2.FindSolution(multiThread: true));
        Assert.AreEqual(expectedSolution, solver2.ToGivenString());

        Solver solver3 = solver.Clone();
        Assert.IsTrue(solver3.FillRealCandidates(multiThread: true));
        Assert.IsTrue(solver3.IsComplete);
        Assert.AreEqual(expectedSolution, solver3.ToGivenString());
    }

    public static void TestInvalidSolution(this Solver solver)
    {
        Solver solver1 = solver.Clone();
        Assert.AreEqual(0ul, solver1.CountSolutions(multiThread: true));

        Solver solver2 = solver.Clone();
        Assert.IsFalse(solver2.FindSolution(multiThread: true));

        Solver solver3 = solver.Clone();
        Assert.IsFalse(solver3.FillRealCandidates(multiThread: true));
    }

    public static void TestMultipleSolution(this Solver solver)
    {
        Solver solver1 = solver.Clone();
        Assert.IsTrue(solver1.CountSolutions(multiThread: true) > 1ul);

        Solver solver2 = solver.Clone();
        Assert.IsTrue(solver2.FindSolution(multiThread: true));

        Solver solver3 = solver.Clone();
        Assert.IsTrue(solver3.FillRealCandidates(multiThread: true));
        Assert.IsFalse(solver3.IsComplete);
    }
}
