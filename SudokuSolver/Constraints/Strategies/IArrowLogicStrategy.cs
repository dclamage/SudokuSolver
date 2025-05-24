namespace SudokuSolver.Constraints.Strategies;

public interface IArrowLogicStrategy
{
    string GetSpecificName(List<(int, int)> circleCells, Solver solver);

    LogicResult InitCandidates(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper);

    bool EnforceConstraint(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, HashSet<(int, int)> allCells, int r, int c, int val);

    LogicResult StepLogic(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing);
}
