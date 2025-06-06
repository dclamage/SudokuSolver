namespace SudokuTests;

[TestClass]
public class ArrowTests
{
    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void MustSupplyTwoGroups()
    {
        // Only providing one group where two are required
        var arrow = new ArrowSumConstraint(SolverFactory.CreateBlank(9), "r1c1");
    }

    [TestMethod]
    public void PopulatesCellGroups()
    {
        var arrow = WithOptions("r1c1; r1c2r1c3");

        Assert.AreEqual(1, arrow.circleCells.Count);
        Assert.AreEqual(2, arrow.arrowCells.Count);

        Assert.AreEqual((0, 0), arrow.circleCells[0]);
        Assert.AreEqual((0, 1), arrow.arrowCells[0]);
        Assert.AreEqual((0, 2), arrow.arrowCells[1]);
    }

    [TestMethod]
    public void NamedAfterCircleCell()
    {
        var arrow = WithOptions("r1c1; r1c2r1c3");

        Assert.AreEqual("Arrow at r1c1", arrow.SpecificName);
    }

    [TestMethod]
    public void CantHaveMoreThan3CircleCells()
    {
        var solver = SolverFactory.CreateBlank(16);

        // Multi-cell circles must (arrowCells.Count * MAX_VALUE) > 99. So we
        // need at least 7 arrow cells in 16x16.
        var broken_arrow = WithOptions("r1c1r1c2r1c3r1c4; r1c5r1c6r1c7r1c8r1c9r1c10r1c11", solver);
        var working_arrow = WithOptions("r2c1r2c2r2c3; r1c3r1c4r1c5r1c6r1c7r1c8r1c9r1c10r1c11r1c12", solver);

        Assert.AreEqual(LogicResult.Invalid, broken_arrow.InitCandidates(solver));
        Assert.AreEqual(LogicResult.Changed, working_arrow.InitCandidates(solver));
    }

    [TestMethod]
    public void ArrowCellsConstrainedMax()
    {
        var solver = SolverFactory.CreateBlank(9);
        var arrow = WithOptions("r1c1; r1c2r1c3r1c4", solver);

        arrow.InitCandidates(solver);

        // 3 arrow cells, max any one cell can be is 6 (if max is 9)
        Assert.AreEqual(6, MaxValue(solver.Board[0, 1]));
    }

    [TestMethod]
    public void CircleConstrainedMin()
    {
        var solver = SolverFactory.CreateBlank(9);
        var arrow = WithOptions("r1c1; r1c2r1c3r1c4", solver);

        arrow.InitCandidates(solver);

        // 3 arrow cells, min circle can be is 6
        Assert.AreEqual(6, MinValue(solver.Board[0, 0]));
    }

    [TestMethod]
    public void MaxFirstDigitWorksWhen16by16()
    {
        var solver = SolverFactory.CreateBlank(16);

        // Arrow 7 cells long
        var arrow = WithOptions("r1c1r1c2; r16c3r16c4r16c5r16c6r16c7r16c8r16c9r16c10r16c11r16c12r16c13r16c14r16c15r16c16", solver);

        arrow.InitCandidates(solver);

        Assert.AreEqual(13, MaxValue(solver.Board[0, 0]), "Sum(3-16) = 133 so first digit should be <= 13");
    }

    [TestMethod]
    public void CantFitSumInCircle()
    {
        // "Sum of arrow ({arrowSum}) is impossible to fill into circle."
        TestLogic(
            options: "r1c1; r1c2r1c3",
            gridSize: 9,
            expectedResult: LogicResult.Invalid,
            messageContains: "Sum is no longer possible (Between 11 and 11).",
            setup: (solver) =>
            {
                solver.SetValue(0, 1, 5);
                solver.SetValue(0, 2, 6);
            }
        );
    }

    [TestMethod]
    public void CircleSetToSum()
    {
        TestLogic(
            options: "r1c1; r1c2r1c3",
            gridSize: 9,
            expectedResult: LogicResult.Changed,
            messageContains: "Circle r1c1 restricted by arrow sum 5",
            setup: (solver) =>
            {
                solver.SetValue(0, 1, 2);
                solver.SetValue(0, 2, 3);
            },
            after: (solver) =>
            {
                Assert.AreEqual(5, solver.GetValue((0, 0)));
            }
        );
    }

    [TestMethod]
    public void CanEnterSumWhenLessThan99()
    {
        TestLogic(
            options: "r1c1r1c2; r1c3r1c4",
            gridSize: 9,
            expectedResult: LogicResult.Changed,
            messageContains: "Impossible pill value",
            setup: (solver) =>
            {
                solver.SetValue(0, 2, 9);
                solver.SetValue(0, 3, 8);
            },
            after: (solver) =>
            {
                Assert.AreEqual(1, solver.GetValue((0, 0)));
                Assert.AreEqual(7, solver.GetValue((0, 1)));
            }
        );
    }

    [TestMethod]
    public void OnlyValidSumsAllowedWhenGrouped()
    {
        TestLogic(
            options: "r1c1; r1c2r1c3",
            gridSize: 9,
            expectedResult: LogicResult.Changed,
            messageContains: "Circle r1c1 restricted by arrow sum 6,7,9",
            setup: (solver) =>
            {
                solver.SetMask(0, 1, 5, 2);
                solver.SetMask(0, 2, 4, 2);
            },
            after: (solver) =>
            {
                Assert.AreEqual(ValuesMask(9, 7, 6), solver.Board[0, 0], "4 should not be allowed due to double 2");
            }
        );
    }

    [TestMethod]
    public void MultiCircleCanHandleSecret()
    {
        TestLogic(
            options: "r1c1r1c2; r2c1r2c2r2c3r2c4r2c5r2c6r2c7r2c8r2c9",
            gridSize: 9,
            expectedResult: LogicResult.None,
            messageContains: "",
            setup: (solver) =>
            {
                // Set arrow to digits 1 - 9
                for (var col = 0; col < 9; col++)
                {
                    solver.SetValue(1, col, col + 1);
                }
            },
            after: (solver) =>
            {
                // Don't tell anyone
                Assert.AreEqual(4, solver.GetValue((0, 0)));
                Assert.AreEqual(5, solver.GetValue((0, 1)));
            }
        );
    }

    [TestMethod]
    public void Bug_CodeAssumesCellLessThan10()
    {
        var solver = SolverFactory.CreateBlank(16);
        var arrow = WithOptions("r1c1;r1c2r1c3", solver);

        arrow.InitCandidates(solver);

        // Was throwing OutOfRangeException
        arrow.StepLogic(solver, new List<LogicalStepDesc>(), false);
    }

    [TestMethod]
    public void Bug_SumLessThanMaxCanFit()
    {
        var solver = SolverFactory.CreateBlank(16);
        var arrow = WithOptions("r1c1r1c2; r1c3", solver);

        Assert.AreEqual(LogicResult.Changed, arrow.InitCandidates(solver));
    }

    private void TestLogic(String options, int gridSize, LogicResult expectedResult, String messageContains, Action<Solver> setup, Action<Solver> after = null)
    {
        var solver = SolverFactory.CreateBlank(gridSize);
        var arrow = WithOptions(options, solver);

        arrow.InitCandidates(solver);

        setup(solver);

        var step = new List<LogicalStepDesc>();

        Assert.AreEqual(expectedResult, arrow.StepLogic(solver, step, false));

        if (!string.IsNullOrEmpty(messageContains))
        {
            Assert.IsTrue(step[0].desc.Contains(messageContains), step[0].desc);
        }

        if (after != null)
        {
            after(solver);
        }
    }

    private ArrowSumConstraint WithOptions(String options, Solver solver = null)
    {
        return new ArrowSumConstraint(solver != null ? solver : SolverFactory.CreateBlank(9), options);
    }
}
