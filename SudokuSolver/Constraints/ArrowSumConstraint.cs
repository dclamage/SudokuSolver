using SudokuSolver.Constraints.Strategies;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Arrow", ConsoleName = "arrow")]
public class ArrowSumConstraint : Constraint
{
    private readonly IArrowLogicStrategy _logicStrategy;

    // Made public readonly for easier access if needed by external tools/UI, though not strictly necessary for internal logic
    public readonly List<(int, int)> circleCells;
    public readonly List<(int, int)> arrowCells;
    private readonly HashSet<(int, int)> _allCells;
    private SumCellsHelper _arrowSumHelperInstance;
    private readonly Solver _solverInstanceRef; // To pass to strategy methods that might need solver context
    private readonly bool _isDegenerate;
    private readonly bool _isClone;

    public ArrowSumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        _solverInstanceRef = sudokuSolver;
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 2)
        {
            throw new ArgumentException($"Arrow constraint expects 2 cell groups (circle/pill and arrow), got {cellGroups.Count}. Options: '{options}'");
        }

        circleCells = cellGroups[0];
        arrowCells = cellGroups[1];

        _isDegenerate = circleCells.Count == 0 || arrowCells.Count == 0;
        _isClone = circleCells.Count == 1 && arrowCells.Count == 1;

        if (!_isDegenerate)
        {
            _allCells = [.. circleCells.Concat(arrowCells)];

            if (circleCells.Count == 1)
            {
                _logicStrategy = new CircleArrowStrategy(sudokuSolver);
            }
            else
            {
                _logicStrategy = new PillArrowStrategy(sudokuSolver);
            }
        }
    }

    public override string SpecificName => _logicStrategy.GetSpecificName(circleCells, _solverInstanceRef);

    // An arrow constraint does not typically impose its own "distinct digits" group on all its cells
    // like a Killer Cage does. Standard Sudoku rules (row, col, box) and the SumCellsHelper
    // (for the arrow shaft, ensuring digits used in the sum are distinct if they fall in the same Sudoku group)
    // cover distinctness. If this arrow *also* implied all its cells must be distinct,
    // it would either be a different constraint type or require an additional GroupConstraint.
    public override List<(int, int)> Group => null;

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (_isDegenerate || _isClone)
        {
            return LogicResult.None;
        }

        _arrowSumHelperInstance = new SumCellsHelper(sudokuSolver, arrowCells);
        return _logicStrategy.InitCandidates(sudokuSolver, circleCells, arrowCells, _arrowSumHelperInstance);
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (_isDegenerate || _isClone)
        {
            return true;
        }

        return _logicStrategy.EnforceConstraint(sudokuSolver, circleCells, arrowCells, _arrowSumHelperInstance, _allCells, i, j, val);
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        if (_isDegenerate || _isClone)
        {
            return LogicResult.None;
        }

        return _logicStrategy.StepLogic(sudokuSolver, circleCells, arrowCells, _arrowSumHelperInstance, logicalStepDescription, isBruteForcing);
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (_isDegenerate)
        {
            return LogicResult.None;
        }

        if (_isClone)
        {
            if (!isInitializing)
            {
                return LogicResult.None;
            }

            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                int candIndex0 = CandidateIndex(circleCells[0], v0);
                int candIndex1 = CandidateIndex(arrowCells[0], v0);
                sudokuSolver.AddCloneLink(candIndex0, candIndex1);
            }
            return LogicResult.None;
        }

        return InitLinksByRunningLogic(sudokuSolver, _allCells, logicalStepDescription);
    }

    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value)
    {
        if (_isDegenerate)
        {
            return null;
        }

        return CellsMustContainByRunningLogic(sudokuSolver, _allCells, value);
    }
}
