namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Even", ConsoleName = "even")]
public class EvenConstraint : Constraint
{
    public readonly List<(int, int)> cells;

    public EvenConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Even constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells == null)
        {
            return LogicResult.None;
        }

        bool changed = false;
        foreach (var (i, j) in cells)
        {
            uint clearMask = 0;
            for (int v = 1; v <= MAX_VALUE; v += 2)
            {
                clearMask |= ValueMask(v);
            }
            var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
            if (clearResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= clearResult == LogicResult.Changed;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool NeedsEnforceConstraint => false;
    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
}
