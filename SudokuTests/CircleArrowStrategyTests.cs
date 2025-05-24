using SudokuSolver.Constraints.Strategies;

namespace SudokuTests;

[TestClass]
public class CircleArrowStrategyTests
{
    private Solver CreateSolver(int size = 3, int maxValue = 3)
    {
        var solver = new Solver(size, size, maxValue);
        solver.FinalizeConstraints(); // Important for SumCellsHelper internal state
        return solver;
    }

    private (List<(int, int)>, List<(int, int)>, SumCellsHelper, HashSet<(int, int)>) SetupStrategyParams(
        Solver solver, (int, int) circleCell, params (int, int)[] arrowCellCoords)
    {
        var circleCells = new List<(int, int)> { circleCell };
        var arrowCells = arrowCellCoords.ToList();
        var sumHelper = new SumCellsHelper(solver, arrowCells);
        var allCells = new HashSet<(int, int)>(circleCells.Concat(arrowCells));
        return (circleCells, arrowCells, sumHelper, allCells);
    }

    [TestMethod]
    public void InitCandidates_Circle1Arrow1_SyncsCandidates()
    {
        var solver = CreateSolver(3, 3);
        var circleCell = (0, 0);
        var arrowCell = (0, 1);
        var (cs, asCells, sh, ac) = SetupStrategyParams(solver, circleCell, arrowCell);
        var strategy = new CircleArrowStrategy(solver);

        // R1C1 = {1,2}, R1C2 = {2,3}
        solver.SetMask(0, 0, new int[] { 1, 2 });
        solver.SetMask(0, 1, new int[] { 2, 3 });

        var result = strategy.InitCandidates(solver, cs, asCells, sh);
        Assert.AreEqual(LogicResult.Changed, result);
        // Both should become {2}
        Assert.AreEqual(ValueMask(2), solver.Board[0, 0] & ~valueSetMask);
        Assert.AreEqual(ValueMask(2), solver.Board[0, 1] & ~valueSetMask);
    }

    [TestMethod]
    public void InitCandidates_CircleMax3_Arrow2Cells_RestrictsCircle()
    {
        var solver = CreateSolver(3, 3); // Max val 3
        var circleCell = (0, 0); // R1C1 = {1,2,3}
        var arrowCell1 = (1, 0); // R2C1 = {1}
        var arrowCell2 = (1, 1); // R2C2 = {1}
                                 // Min sum for arrow = 1+1=2. Max sum for arrow = 1+1=2. Sum must be 2.
        solver.SetMask(1, 0, new int[] { 1 });
        solver.SetMask(1, 1, new int[] { 1 });

        var (cs, asCells, sh, ac) = SetupStrategyParams(solver, circleCell, arrowCell1, arrowCell2);
        var strategy = new CircleArrowStrategy(solver);

        var result = strategy.InitCandidates(solver, cs, asCells, sh);
        Assert.AreEqual(LogicResult.Changed, result);
        // Circle R1C1 must be 2
        Assert.AreEqual(ValueMask(2), solver.Board[0, 0] & ~valueSetMask);
    }

    [TestMethod]
    public void StepLogic_CircleSet_ArrowGetsRestricted()
    {
        var solver = CreateSolver(3, 3);
        var circleCell = (0, 0);
        var arrow1 = (1, 0); var arrow2 = (1, 1); // R2C1={1,2,3}, R2C2={1,2,3}
        var (cs, asCells, sh, ac) = SetupStrategyParams(solver, circleCell, arrow1, arrow2);
        var strategy = new CircleArrowStrategy(solver);

        solver.SetValue(0, 0, 3); // Circle is 3. Arrow must sum to 3.
                                  // Min values (1,1) sum to 2. Max (3,3) sum to 6.
                                  // Possible arrow combos for sum 3: (1,2), (2,1)

        var result = strategy.StepLogic(solver, cs, asCells, sh, null, false);
        Assert.AreEqual(LogicResult.Changed, result);
        // Arrow cells should be restricted. e.g., R2C1 cannot be 3 (because R2C2 would need to be 0)
        // R2C1 can be {1,2}, R2C2 can be {1,2}
        uint expectedArrowMask = ValueMask(1) | ValueMask(2);
        Assert.AreEqual(expectedArrowMask, solver.Board[1, 0] & ~valueSetMask);
        Assert.AreEqual(expectedArrowMask, solver.Board[1, 1] & ~valueSetMask);
    }

    [TestMethod]
    public void Enforce_CircleSet_ArrowViolates_ReturnsFalse()
    {
        var solver = CreateSolver(3, 3);
        var circleCell = (0, 0);
        var arrow1 = (1, 0); var arrow2 = (1, 1);
        var (cs, asCells, sh, ac) = SetupStrategyParams(solver, circleCell, arrow1, arrow2);
        var strategy = new CircleArrowStrategy(solver);

        solver.SetValue(0, 0, 5); // Circle is 5 (invalid for MAX_VALUE=3, but test logic)
                                  // Let's use a bigger solver for this test.
        solver = CreateSolver(9, 9);
        (cs, asCells, sh, ac) = SetupStrategyParams(solver, circleCell, arrow1, arrow2);
        strategy = new CircleArrowStrategy(solver);
        solver.SetValue(0, 0, 5); // Circle = 5

        solver.SetValue(1, 0, 1); // Arrow cell 1 = 1

        // Pretend arrow cell 2 is being set to 2 (sum = 1+2=3, != 5)
        bool isValid = strategy.EnforceConstraint(solver, cs, asCells, sh, ac, 1, 1, 2);
        // This setup is tricky because Enforce is called AFTER solver.SetValue
        // A better test:
        solver.SetValue(1, 1, 2); // Now arrow is (1,2) sum 3. Circle is 5.
        // If Enforce is called due to setting (1,1) to 2:
        isValid = strategy.EnforceConstraint(solver, cs, asCells, sh, ac, 1, 1, 2);
        Assert.IsFalse(isValid); // 3 != 5
    }
}
