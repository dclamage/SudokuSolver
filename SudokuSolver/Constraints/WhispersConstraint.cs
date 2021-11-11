using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Whispers", ConsoleName = "whispers")]
public class WhispersConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int difference;
    private readonly HashSet<(int, int)> cellsSet;

    private static readonly Regex optionsRegex = new(@"(\d+);(.*)");
    public WhispersConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var match = optionsRegex.Match(options);
        if (match.Success)
        {
            difference = int.Parse(match.Groups[1].Value);
            options = match.Groups[2].Value;
        }
        else
        {
            // No difference provided, use default
            difference = (MAX_VALUE + 1) / 2;
        }

        if (difference < 1 || difference > MAX_VALUE - 1)
        {
            throw new ArgumentException($"Whispers difference must be between 1 and {MAX_VALUE - 1}. Specified difference was: {difference}");
        }

        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Whispers constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    public override string SpecificName => $"Whispers {CellName(cells[0])} - {CellName(cells[^1])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count <= 1)
        {
            return LogicResult.None;
        }

        uint clearMask = 0;
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (v - difference < 1 && v + difference > MAX_VALUE)
            {
                clearMask |= ValueMask(v);
            }
        }

        bool changed = false;
        if (clearMask != 0)
        {
            foreach (var (i, j) in cells)
            {
                var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= clearResult == LogicResult.Changed;
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells.Count <= 1)
        {
            return true;
        }

        if (cellsSet.Contains((i, j)))
        {
            uint adjMask = CalcKeepMask(ValueMask(val));
            if (adjMask == 0)
            {
                return false;
            }

            for (int ti = 0; ti < cells.Count; ti++)
            {
                var curCell = cells[ti];
                if (curCell == (i, j))
                {
                    if (ti - 1 > 0)
                    {
                        var prevCell = cells[ti - 1];
                        if (sudokuSolver.KeepMask(prevCell.Item1, prevCell.Item2, adjMask) == LogicResult.Invalid)
                        {
                            return false;
                        }
                    }
                    if (ti + 1 < cells.Count)
                    {
                        var nextCell = cells[ti + 1];
                        if (sudokuSolver.KeepMask(nextCell.Item1, nextCell.Item2, adjMask) == LogicResult.Invalid)
                        {
                            return false;
                        }
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
        uint[] clearedMasks = null;
        for (int ti = 0; ti < cells.Count; ti++)
        {
            var curCell = cells[ti];
            uint curMask = board[curCell.Item1, curCell.Item2];
            if (IsValueSet(curMask))
            {
                continue;
            }

            var prevCell = ti - 1 >= 0 ? cells[ti - 1] : (-1, -1);
            var nextCell = ti + 1 < cells.Count ? cells[ti + 1] : (-1, -1);
            uint prevMask = prevCell.Item1 != -1 ? board[prevCell.Item1, prevCell.Item2] : ALL_VALUES_MASK;
            uint nextMask = nextCell.Item1 != -1 ? board[nextCell.Item1, nextCell.Item2] : ALL_VALUES_MASK;

            prevMask &= ~valueSetMask;
            curMask &= ~valueSetMask;
            nextMask &= ~valueSetMask;

            uint keepMask = CalcKeepMask(prevMask) & CalcKeepMask(nextMask);
            if (keepMask == 0)
            {
                logicalStepDescription?.Append($"{CellName(curCell)} has no more valid candidates.");
                return LogicResult.Invalid;
            }

            bool changed = false;

            uint clearMask = ~keepMask & ALL_VALUES_MASK & curMask;
            if (clearMask != 0)
            {
                LogicResult clearResult = sudokuSolver.ClearMask(curCell.Item1, curCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Append($"{CellName(curCell)} has no more valid candidates.");
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    if (clearedMasks == null)
                    {
                        clearedMasks = new uint[cells.Count];
                    }
                    clearedMasks[ti] |= clearMask;
                    curMask = board[curCell.Item1, curCell.Item2] & ~valueSetMask;
                    changed = true;
                }
            }

            if (prevCell.Item1 != -1 && nextCell.Item1 != -1)
            {
                int minCurVal = MinValue(curMask);
                int maxCurVal = MaxValue(curMask);
                for (int v = minCurVal; v <= maxCurVal; v++)
                {
                    uint valueMask = ValueMask(v);
                    if ((curMask & valueMask) == 0)
                    {
                        continue;
                    }

                    if (sudokuSolver.IsSeenByValue(prevCell, nextCell, v))
                    {
                        uint adjMask = CalcKeepMask(valueMask);
                        uint keepPrev = prevMask & adjMask;
                        uint keepNext = nextMask & adjMask;
                        if (keepPrev == keepNext && ValueCount(keepPrev) == 1)
                        {
                            if (sudokuSolver.ClearValue(curCell.Item1, curCell.Item2, v))
                            {
                                if (clearedMasks == null)
                                {
                                    clearedMasks = new uint[cells.Count];
                                }
                                clearedMasks[ti] |= valueMask;
                                changed = true;
                            }
                            else
                            {
                                logicalStepDescription?.Append($"{CellName(curCell)} has no more valid candidates.");
                                return LogicResult.Invalid;
                            }
                        }
                    }
                }
            }

            if (changed)
            {
                // Start over
                ti = -1;
            }
        }

        if (clearedMasks != null)
        {
            if (logicalStepDescription != null)
            {
                logicalStepDescription.Append($"Cleared values");
                bool first = true;
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    if (clearedMasks[cellIndex] != 0)
                    {
                        var cell = cells[cellIndex];
                        if (!first)
                        {
                            logicalStepDescription.Append(';');
                        }
                        logicalStepDescription.Append($" {MaskToString(clearedMasks[cellIndex])} from {CellName(cell)}");
                        first = false;
                    }
                }
            }
            return LogicResult.Changed;
        }
        return LogicResult.None;
    }

    private uint CalcKeepMask(uint adjMask)
    {
        int adjValMin = MinValue(adjMask);
        int adjValMax = MaxValue(adjMask);
        int maxSmallVal = adjValMax - difference;
        int minLargeVal = adjValMin + difference;
        uint keepMask = 0;
        if (maxSmallVal >= 1)
        {
            keepMask |= MaskValAndLower(maxSmallVal);
        }
        if (minLargeVal <= MAX_VALUE)
        {
            keepMask |= MaskValAndHigher(minLargeVal);
        }
        return keepMask;
    }
}

