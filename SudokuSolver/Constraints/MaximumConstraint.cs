namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Maximum", ConsoleName = "max")]
public class MaximumConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsLookup;
    private readonly Dictionary<(int, int), List<(int, int)>> adjCellLookup = new();
    private readonly Dictionary<(int, int), int> minUniqueValuesLookup = new();

    public MaximumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Maximum constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsLookup = new(cells);

        foreach (var cell0 in cells)
        {
            foreach (var cell1 in AdjacentCells(cell0.Item1, cell0.Item2))
            {
                if (!cellsLookup.Contains(cell1))
                {
                    adjCellLookup.AddToList(cell1, cell0);
                }
            }
        }
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells == null || cells.Count == 0)
        {
            return LogicResult.None;
        }

        bool changed = false;
        foreach (var (i0, j0) in cells)
        {
            minUniqueValuesLookup[(i0, j0)] = sudokuSolver.MinimumUniqueValues(ValidAdjacentCells(i0, j0));

            var logicResult = DoLogic(sudokuSolver, i0, j0, null);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= logicResult == LogicResult.Changed;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells == null || cells.Count == 0)
        {
            return true;
        }

        var cell0 = (i, j);
        if (cellsLookup.Contains(cell0))
        {
            uint clearMask = ALL_VALUES_MASK & ~((1u << (val - 1)) - 1);
            foreach (var cell1 in ValidAdjacentCells(i, j))
            {
                var logicResult = sudokuSolver.ClearMask(cell1.Item1, cell1.Item2, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }
            }
        }
        else if (adjCellLookup.TryGetValue(cell0, out var cellList))
        {
            uint clearMask = (1u << val) - 1;
            if (clearMask != 0)
            {
                foreach (var minCell in cellList)
                {
                    var logicResult = sudokuSolver.ClearMask(minCell.Item1, minCell.Item2, clearMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells == null || cells.Count == 0)
        {
            return LogicResult.None;
        }

        foreach (var (i0, j0) in cells)
        {
            var logicResult = DoLogic(sudokuSolver, i0, j0, logicalStepDescription);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (logicResult == LogicResult.Changed)
            {
                return LogicResult.Changed;
            }
        }
        return LogicResult.None;
    }

    private LogicResult DoLogic(Solver sudokuSolver, int i0, int j0, StringBuilder logicalStepDescription)
    {
        var board = sudokuSolver.Board;
        uint cellMask0 = board[i0, j0] & ~valueSetMask;
        bool changed = false;

        // Clear adjacent cells of the maximum value in this cell and higher
        {
            int maxValue = MaxValue(cellMask0);
            uint clearMask = ALL_VALUES_MASK & ~((1u << (maxValue - 1)) - 1);
            foreach (var (i1, j1) in ValidAdjacentCells(i0, j0))
            {
                var logicResult = sudokuSolver.ClearMask(i1, j1, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"{CellName(i0, j0)} has maximum value {maxValue}, causing {CellName(i1, j1)} to have no valid candidates.");
                    }
                    return LogicResult.Invalid;
                }

                if (logicResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        if (!changed)
                        {
                            logicalStepDescription.Append($"{CellName(i0, j0)} has maximum value {maxValue}, removing {MaskToString(clearMask)} from {CellName(i1, j1)}");
                        }
                        else
                        {
                            logicalStepDescription.Append($", {CellName(i1, j1)}");
                        }
                    }
                    changed = true;
                }
            }

            if (changed && logicalStepDescription != null)
            {
                return LogicResult.Changed;
            }
        }

        // Clear this cell based on the candidates available in the adjacent cells
        {
            uint adjCellsMask = AdjacentCellsMask(sudokuSolver, i0, j0);
            if (adjCellsMask == 0)
            {
                return LogicResult.None;
            }

            int minUniqueValues = minUniqueValuesLookup[(i0, j0)];

            int numRemoved = 0;
            for (int v = 1; numRemoved < minUniqueValues && v <= MAX_VALUE; v++)
            {
                uint valMask = ValueMask(v);
                if ((adjCellsMask & valMask) != 0)
                {
                    adjCellsMask &= ~valMask;
                    numRemoved++;
                }
            }

            if (adjCellsMask == 0)
            {
                if (logicalStepDescription != null)
                {
                    logicalStepDescription.Clear();
                    logicalStepDescription.Append($"{CellName(i0, j0)} has no valid candidates to fulfill maximum.");
                }
                return LogicResult.Invalid;
            }

            int minAdjValue = MinValue(adjCellsMask) - 1;

            uint clearMask = (1u << minAdjValue) - 1;
            clearMask &= cellMask0;

            if (clearMask != 0)
            {
                var clearResult = sudokuSolver.ClearMask(i0, j0, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"{CellName(i0, j0)} has no valid candidates to fulfill maximum.");
                    }
                    return LogicResult.Invalid;
                }

                if (clearResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Append($"Adjacent values to {CellName(i0, j0)} cannot be higher than {minAdjValue}, removing {MaskToString(clearMask)}");
                        return LogicResult.Changed;
                    }
                    changed = true;
                }
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private uint AdjacentCellsMask(Solver sudokuSolver, int i, int j)
    {
        var board = sudokuSolver.Board;
        uint adjValuesMask = board[i, j];
        foreach (var (i1, j1) in ValidAdjacentCells(i, j))
        {
            adjValuesMask |= board[i1, j1];
        }
        return adjValuesMask & ~valueSetMask;
    }

    private IEnumerable<(int, int)> ValidAdjacentCells(int i, int j)
    {
        foreach (var (i1, j1) in AdjacentCells(i, j))
        {
            if (!cellsLookup.Contains((i1, j1)))
            {
                yield return (i1, j1);
            }
        }
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription)
    {
        foreach (var maxCell in cells)
        {
            int maxCellIndex = FlatIndex(maxCell);
            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                int maxCellCandIndex = maxCellIndex * MAX_VALUE + v0 - 1;

                foreach (var minCell in ValidAdjacentCells(maxCell.Item1, maxCell.Item2))
                {
                    int minCellIndex = FlatIndex(minCell);
                    for (int v1 = v0; v1 <= MAX_VALUE; v1++)
                    {
                        int minCellCandIndex = minCellIndex * MAX_VALUE + v1 - 1;
                        sudokuSolver.AddWeakLink(maxCellCandIndex, minCellCandIndex);
                    }
                }
            }
        }
        return LogicResult.None;
    }
}
