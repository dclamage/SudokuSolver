namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Palindrome", ConsoleName = "palindrome")]
public class PalindromeConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly Dictionary<(int, int), (int, int)> cellToClone;

    public PalindromeConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Palindrome constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellToClone = new(cells.Count);
        for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
        {
            var cell0 = cells[cellIndex];
            var cell1 = cells[^(cellIndex + 1)];
            cellToClone[cell0] = cell1;
            cellToClone[cell1] = cell0;
        }
    }

    public override string SpecificName => $"Palindrome at {CellName(cells[0])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;
        bool changed = false;
        for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
        {
            var (i0, j0) = cells[cellIndex];
            var (i1, j1) = cells[^(cellIndex + 1)];
            if (sudokuSolver.SeenCells((i0, j0)).Contains((i1, j1)))
            {
                return LogicResult.Invalid;
            }

            uint cellMask0 = board[i0, j0];
            uint cellMask1 = board[i1, j1];
            if (cellMask0 != cellMask1)
            {
                bool cellSet0 = IsValueSet(cellMask0);
                bool cellSet1 = IsValueSet(cellMask1);
                if (cellSet0 && cellSet1)
                {
                    return LogicResult.Invalid;
                }
                if (cellSet0)
                {
                    if (!sudokuSolver.SetValue(i1, j1, GetValue(cellMask0)))
                    {
                        return LogicResult.Invalid;
                    }
                }
                else if (cellSet1)
                {
                    if (!sudokuSolver.SetValue(i0, j0, GetValue(cellMask1)))
                    {
                        return LogicResult.Invalid;
                    }
                }
                else
                {
                    uint combinedMask = cellMask0 & cellMask1;
                    if (combinedMask == 0)
                    {
                        return LogicResult.Invalid;
                    }
                    if (!sudokuSolver.SetMask(i0, j0, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }
                    if (!sudokuSolver.SetMask(i1, j1, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }

                }

                changed = true;
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells.Count == 0)
        {
            return true;
        }

        (int, int) cloneCell;
        if (cellToClone.TryGetValue((i, j), out cloneCell))
        {
            uint clearMask = ALL_VALUES_MASK & ~ValueMask(val);
            var clearResult = sudokuSolver.ClearMask(cloneCell.Item1, cloneCell.Item2, clearMask);
            if (clearResult == LogicResult.Invalid)
            {
                return false;
            }
        }
        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;
        for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
        {
            var (i0, j0) = cells[cellIndex];
            var (i1, j1) = cells[^(cellIndex + 1)];
            uint cellMask0 = board[i0, j0];
            uint cellMask1 = board[i1, j1];
            if (cellMask0 == cellMask1)
            {
                continue;
            }

            bool cellSet0 = IsValueSet(cellMask0);
            bool cellSet1 = IsValueSet(cellMask1);
            if (cellSet0 && cellSet1)
            {
                logicalStepDescription?.Append($"{CellName(i0, j0)} has value {GetValue(cellMask0)} but its clone at {CellName(i1, j1)} has value {GetValue(cellMask1)}");
                return LogicResult.Invalid;
            }

            if (cellSet0)
            {
                if (!sudokuSolver.SetValue(i1, j1, GetValue(cellMask0)))
                {
                    logicalStepDescription?.Append($"{CellName(i0, j0)} has value {GetValue(cellMask0)} but its clone at {CellName(i1, j1)} cannot have this value.");
                    return LogicResult.Invalid;
                }
                logicalStepDescription?.Append($"{CellName(i0, j0)} with value {GetValue(cellMask0)} is cloned into {CellName(i1, j1)}");
                return LogicResult.Changed;
            }
            else if (cellSet1)
            {
                if (!sudokuSolver.SetValue(i0, j0, GetValue(cellMask1)))
                {
                    logicalStepDescription?.Append($"{CellName(i1, j1)} has value {GetValue(cellMask1)} but its clone at {CellName(i0, j0)} cannot have this value.");
                    return LogicResult.Invalid;
                }
                logicalStepDescription?.Append($"{CellName(i1, j1)} with value {GetValue(cellMask1)} is cloned into {CellName(i0, j0)}");
                return LogicResult.Changed;
            }
            else
            {
                uint combinedMask = cellMask0 & cellMask1;
                if (combinedMask == 0)
                {
                    logicalStepDescription?.Append($"No value can go into both {CellName(i0, j0)} with candidates {MaskToString(cellMask0)} and its clone at {CellName(i1, j1)} with candidates {MaskToString(cellMask1)}.");
                    return LogicResult.Invalid;
                }

                uint removed0 = (cellMask0 & ~combinedMask);
                uint removed1 = (cellMask1 & ~combinedMask);
                if (removed0 != 0 || removed1 != 0)
                {
                    if (logicalStepDescription != null)
                    {
                        if (removed0 != 0)
                        {
                            logicalStepDescription.Append($"Candidate {MaskToString(removed0)} removed from {CellName(i0, j0)} (not in {CellName(i1, j1)})");
                        }
                        if (removed1 != 0)
                        {
                            if (removed0 == 0)
                            {
                                logicalStepDescription.Append($"Candidate ");
                            }
                            else
                            {
                                logicalStepDescription.Append($"; ");
                            }
                            logicalStepDescription.Append($"{MaskToString(removed1)} removed from {CellName(i1, j1)} (not in {CellName(i0, j0)})");
                        }
                    }
                    if (!sudokuSolver.SetMask(i0, j0, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }
                    if (!sudokuSolver.SetMask(i1, j1, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }
        }
        return LogicResult.None;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription)
    {
        foreach (var (cell0, cell1) in cellToClone)
        {
            int cellIndex0 = FlatIndex(cell0);
            int cellIndex1 = FlatIndex(cell1);
            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                int candIndex0 = cellIndex0 * MAX_VALUE + v0 - 1;
                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                {
                    if (v0 != v1)
                    {
                        int candIndex1 = cellIndex1 * MAX_VALUE + v1 - 1;
                        sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                    }
                }
            }
        }
        return LogicResult.None;
    }
}
