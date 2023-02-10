using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Thermometer", ConsoleName = "thermo")]
public class ThermometerConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsSet;

    public ThermometerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Thermometer constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    public ThermometerConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells) : base(sudokuSolver, cells.CellNames(""))
    {
        this.cells = cells.ToList();
        cellsSet = new(cells);
    }

    public override string SpecificName => $"Thermometer {CellName(cells[0])} - {CellName(cells[^1])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        bool changed = false;
        var (firsti, firstj) = cells[0];
        var (lasti, lastj) = cells[^1];
        uint firstMask = sudokuSolver.Board[firsti, firstj];
        uint lastMask = sudokuSolver.Board[lasti, lastj];
        int minVal = MinValue(firstMask & ~valueSetMask);
        int maxVal = MaxValue(lastMask & ~valueSetMask) - cells.Count + 1;
        uint clearMask = ALL_VALUES_MASK;
        for (int val = minVal; val <= maxVal; val++)
        {
            clearMask &= ~ValueMask(val);
        }
        foreach (var (i, j) in cells)
        {
            var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
            if (clearResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= clearResult == LogicResult.Changed;
            clearMask = (clearMask << 1) | 1u;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells.Count == 0)
        {
            return true;
        }

        if (cellsSet.Contains((i, j)))
        {
            var board = sudokuSolver.Board;
            for (int ti = 0; ti < cells.Count - 1; ti++)
            {
                var curCell = cells[ti];
                var nextCell = cells[ti + 1];
                uint curMask = board[curCell.Item1, curCell.Item2];
                uint nextMask = board[nextCell.Item1, nextCell.Item2];
                bool curValueSet = IsValueSet(curMask);
                bool nextValueSet = IsValueSet(nextMask);

                int clearNextValStart = curValueSet ? GetValue(curMask) : MinValue(curMask);
                for (int clearVal = clearNextValStart; clearVal > 0; clearVal--)
                {
                    if (!sudokuSolver.ClearValue(nextCell.Item1, nextCell.Item2, clearVal))
                    {
                        return false;
                    }
                }

                int clearCurValStart = nextValueSet ? GetValue(nextMask) : MaxValue(nextMask);
                for (int clearVal = clearCurValStart; clearVal <= MAX_VALUE; clearVal++)
                {
                    if (!sudokuSolver.ClearValue(curCell.Item1, curCell.Item2, clearVal))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription) => InitLinksByRunningLogic(solver, cells, logicalStepDescription);
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => CellsMustContainByRunningLogic(sudokuSolver, cells, value);

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;
        List<int> elims = null;
        bool hadChange = false;
        bool changed;
        do
        {
            changed = false;
            for (int ti = 0; ti < cells.Count - 1; ti++)
            {
                var curCell = cells[ti];
                var nextCell = cells[ti + 1];
                uint curMask = board[curCell.Item1, curCell.Item2];
                uint nextMask = board[nextCell.Item1, nextCell.Item2];
                bool curValueSet = IsValueSet(curMask);
                bool nextValueSet = IsValueSet(nextMask);

                int clearNextValStart = curValueSet ? GetValue(curMask) : MinValue(curMask);
                uint clearMask = board[nextCell.Item1, nextCell.Item2] & MaskValAndLower(clearNextValStart);
                LogicResult clearResult = sudokuSolver.ClearMask(nextCell.Item1, nextCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Append($"{CellName(nextCell)} has no more valid candidates.");
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        elims ??= new();
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (HasValue(clearMask, v))
                            {
                                elims.Add(CandidateIndex(nextCell, v));
                            }
                        }
                    }
                    changed = true;
                    hadChange = true;
                }

                int clearCurValStart = nextValueSet ? GetValue(nextMask) : MaxValue(nextMask);
                clearMask = board[curCell.Item1, curCell.Item2] & MaskValAndHigher(clearCurValStart);
                clearResult = sudokuSolver.ClearMask(curCell.Item1, curCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        elims ??= new();
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (HasValue(clearMask, v))
                            {
                                elims.Add(CandidateIndex(curCell, v));
                            }
                        }
                    }
                    changed = true;
                    hadChange = true;
                }
            }
        } while (changed);

        if (logicalStepDescription != null && elims != null && elims.Count > 0)
        {
            logicalStepDescription.Append($"Re-evaluated => {sudokuSolver.DescribeElims(elims)}");
        }

        return hadChange ? LogicResult.Changed : LogicResult.None;
    }

    public override List<(int, int)> Group => cells;

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        List<ThermometerConstraint> constraints = new(cells.Count - 1);
        for (int i = 0; i < cells.Count - 1; i++)
        {
            constraints.Add(new(sudokuSolver, new (int, int)[] { cells[i], cells[i + 1] }));
        }
        return constraints;
    }
}

