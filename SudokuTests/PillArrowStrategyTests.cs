using SudokuSolver.Constraints.Strategies;

namespace SudokuTests;

[TestClass]
public class PillArrowStrategyTests
{
    private Solver CreateSolver(int size = 9, int maxValue = 9) // Default to 9x9
    {
        var solver = new Solver(size, size, maxValue);
        solver.FinalizeConstraints(); // Crucial for SumCellsHelper
        return solver;
    }

    private (List<(int, int)>, List<(int, int)>, SumCellsHelper, HashSet<(int, int)>) SetupStrategyParams(
        Solver solver, List<(int, int)> pillCellCoords, params (int, int)[] arrowCellCoords)
    {
        var arrowCells = arrowCellCoords.ToList();
        var sumHelper = new SumCellsHelper(solver, arrowCells); // Solver instance here is key
        var allCells = new HashSet<(int, int)>(pillCellCoords.Concat(arrowCells));
        return (pillCellCoords, arrowCells, sumHelper, allCells);
    }

    [TestMethod]
    public void InitCandidates_PillCanBe12_ArrowSumsTo12()
    {
        var solver = CreateSolver(9, 9);
        var pillCoords = new List<(int, int)> { (0, 0), (0, 1) };
        solver.SetMask(0, 0, new int[] { 1, 2 }); // R1C1 = {1,2}
        solver.SetMask(0, 1, new int[] { 2, 3 }); // R1C2 = {2,3}
        // Possible pill totals: "12"=12, "13"=13, "22"=22, "23"=23

        var arrow1 = (1, 0); solver.SetMask(1, 0, new int[] { 5 });
        var arrow2 = (1, 1); solver.SetMask(1, 1, new int[] { 7 }); // Arrow Sum = 12.

        var (pcs, asCells, sh, ac) = SetupStrategyParams(solver, pillCoords, arrow1, arrow2);
        var strategy = new PillArrowStrategy(solver);

        var result = strategy.InitCandidates(solver, pcs, asCells, sh);

        // Arrow sum helper initialized with {12,13,22,23}. Then StepLogic called with {12}.
        // Pill should be restricted to {1,2} for cells (0,0) and (0,1) respectively.
        Assert.AreEqual(LogicResult.Changed, result, "InitCandidates should report changed.");
        Assert.AreEqual(ValueMask(1), solver.Board[0, 0] & ~valueSetMask, "R1C1 should be 1");
        Assert.AreEqual(ValueMask(2), solver.Board[0, 1] & ~valueSetMask, "R1C2 should be 2");
    }

    [TestMethod]
    public void StepLogic_PillSetTo12_ArrowRestricted()
    {
        var solver = CreateSolver(9, 9);
        var pillCoords = new List<(int, int)> { (0, 0), (0, 1) };
        solver.SetValue(0, 0, 1);
        solver.SetValue(0, 1, 2); // Pill is 12

        var arrow1 = (1, 0); // R2C1 {1..9}
        var arrow2 = (1, 1); // R2C2 {1..9}
                             // These are in the same row, so they must be different.
        var (pcs, asCells, sh, ac) = SetupStrategyParams(solver, pillCoords, arrow1, arrow2);
        var strategy = new PillArrowStrategy(solver);

        var result = strategy.StepLogic(solver, pcs, asCells, sh, null, false);
        Assert.AreEqual(LogicResult.Changed, result);
        // Arrow must sum to 12. Pairs: (3,9), (4,8), (5,7). (6,6) is disallowed.
        // So possible values for each arrow cell are {3,4,5,7,8,9}.
        // Mask for {3,4,5,7,8,9} = (1<<2)|(1<<3)|(1<<4)|(1<<6)|(1<<7)|(1<<8)
        // = 4 | 8 | 16 | 64 | 128 | 256 = 476.
        uint expectedArrowMask = 476;
        Assert.AreEqual(expectedArrowMask, solver.Board[1, 0] & ~valueSetMask, "Mask for R2C1 is incorrect.");
        Assert.AreEqual(expectedArrowMask, solver.Board[1, 1] & ~valueSetMask, "Mask for R2C2 is incorrect.");
    }

    [TestMethod]
    public void Enforce_PillSet_ArrowViolates_ReturnsFalse()
    {
        var solver = CreateSolver(9, 9);
        var pillCoords = new List<(int, int)> { (0, 0), (0, 1) };
        var arrow1 = (1, 0); var arrow2 = (1, 1);
        var (pcs, asCells, sh, ac) = SetupStrategyParams(solver, pillCoords, arrow1, arrow2);
        var strategy = new PillArrowStrategy(solver);

        solver.SetValue(0, 0, 1);
        solver.SetValue(0, 1, 2); // Pill value = 12.

        solver.SetValue(1, 0, 3); // Arrow cell 1 = 3

        // Simulate EnforceConstraint being called because (1,1) was set to 4.
        // Solver would have already set (1,1) to 4 before calling Enforce.
        solver.SetValue(1, 1, 4); // Arrow sum = 3+4=7. Pill is 12. 7 != 12.
        bool isValid = strategy.EnforceConstraint(solver, pcs, asCells, sh, ac, 1, 1, 4); // val=4 is what was just set
        Assert.IsFalse(isValid);
    }
}
