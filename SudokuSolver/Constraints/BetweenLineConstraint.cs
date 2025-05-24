using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Between Line", ConsoleName = "betweenline")]
public class BetweenLineConstraint : Constraint
{
    public readonly (int, int) outerCell0;
    public readonly (int, int) outerCell1;
    public readonly List<(int, int)> innerCells;
    private readonly HashSet<(int, int)> innerCellsLookup;
    private readonly List<(int, int)> cells;
    private readonly bool valid;
    private int minUniqueInnerValues;

    public BetweenLineConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Between Line constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        if (cells.Count > 2)
        {
            outerCell0 = cells[0];
            outerCell1 = cells[^1];
            innerCells = new(cells.Count - 2);
            for (int i = 1; i < cells.Count - 1; i++)
            {
                innerCells.Add(cells[i]);
            }
            innerCellsLookup = new(innerCells);
            valid = true;
        }
        else
        {
            valid = false;
        }
    }

    public override string SpecificName => $"Between Line {CellName(outerCell0)}-{CellName(outerCell1)}";

    public override IEnumerable<(int, int)> SeenCells((int, int) cell)
    {
        if (innerCellsLookup.Contains(cell))
        {
            yield return outerCell0;
            yield return outerCell1;
        }
        else if (cell == outerCell0)
        {
            foreach (var innerCell in innerCells)
            {
                yield return innerCell;
            }
            yield return outerCell1;
        }
        else if (cell == outerCell1)
        {
            foreach (var innerCell in innerCells)
            {
                yield return innerCell;
            }
            yield return outerCell0;
        }
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (!valid)
        {
            return LogicResult.None;
        }

        minUniqueInnerValues = sudokuSolver.MinimumUniqueValues(innerCells);
        return DoLogic(sudokuSolver, null);
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (!valid)
        {
            return true;
        }

        var board = sudokuSolver.Board;

        var cell = (i, j);
        if (innerCellsLookup.Contains(cell))
        {
            var (minInnerVal, maxInnerVal) = GetMinMaxInnerValues(sudokuSolver);

            uint belowValMask = MaskStrictlyLower(minInnerVal);
            uint aboveValMask = MaskStrictlyHigher(maxInnerVal);
            uint outerCellMask0 = board[outerCell0.Item1, outerCell0.Item2] & ~valueSetMask;
            uint outerCellMask1 = board[outerCell1.Item1, outerCell1.Item2] & ~valueSetMask;

            bool outerCell0HasHigh = (outerCellMask0 & aboveValMask) != 0;
            bool outerCell0HasLow = (outerCellMask0 & belowValMask) != 0;
            bool outerCell0Flex = outerCell0HasHigh && outerCell0HasLow;
            bool outerCell1HasHigh = (outerCellMask1 & aboveValMask) != 0;
            bool outerCell1HasLow = (outerCellMask1 & belowValMask) != 0;
            bool outerCell1Flex = outerCell1HasHigh && outerCell1HasLow;

            if (outerCell0Flex && outerCell1Flex)
            {
                // Can't determine where min and max are yet, so just remove all values between
                // the known inner min and max from both outer cells
                uint betweenValMask = MaskBetweenInclusive(minInnerVal, maxInnerVal);
                var logicResult = sudokuSolver.ClearMask(outerCell0.Item1, outerCell0.Item2, betweenValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }

                logicResult = sudokuSolver.ClearMask(outerCell1.Item1, outerCell1.Item2, betweenValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }
            }
            else if (outerCell0HasLow && !outerCell0HasHigh || !outerCell1HasLow && outerCell1HasHigh)
            {
                // Second outer cell must be the upper bound

                // Remove all higher values from the first outer cell
                var logicResult = sudokuSolver.ClearMask(outerCell0.Item1, outerCell0.Item2, aboveValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }

                // Remove all lower values from the second outer cell
                logicResult = sudokuSolver.ClearMask(outerCell1.Item1, outerCell1.Item2, belowValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }
            }
            else
            {
                // Second outer cell must be the lower bound

                // Remove all lower values from the first outer cell
                var logicResult = sudokuSolver.ClearMask(outerCell0.Item1, outerCell0.Item2, belowValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }

                // Remove all higher values from the second outer cell
                logicResult = sudokuSolver.ClearMask(outerCell1.Item1, outerCell1.Item2, aboveValMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }
            }
        }
        else if (cell == outerCell0 || cell == outerCell1)
        {
            uint mask0 = board[outerCell0.Item1, outerCell0.Item2];
            uint mask1 = board[outerCell1.Item1, outerCell1.Item2];
            bool valueSet0 = IsValueSet(mask0);
            bool valueSet1 = IsValueSet(mask1);

            uint combinedMask = (mask0 | mask1) & ~valueSetMask;
            int minOuterValue = MinValue(combinedMask);
            int maxOuterValue = MaxValue(combinedMask);

            // The inner values must lie between the outer values
            uint clearMask = ALL_VALUES_MASK & ~MaskBetweenExclusive(minOuterValue, maxOuterValue);
            foreach (var (i1, j1) in innerCells)
            {
                var logicResult = sudokuSolver.ClearMask(i1, j1, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return false;
                }
            }

            var (minInner, maxInner) = GetMinMaxInnerValues(sudokuSolver);
            if (minInner <= maxInner)
            {
                // At least on inner value is known, which makes the min/max of the outers known.

                // The set value cannot be between the min and max of the known inner values
                if (val >= minInner && val <= maxInner)
                {
                    return false;
                }

                if (valueSet0 && valueSet1)
                {
                    // Both outers are known

                    // The outer values must surround the inner values
                    if (minOuterValue >= minInner || maxOuterValue <= maxInner)
                    {
                        return false;
                    }

                    // There must be enough room between the outers to fit the minimum number of unique inner values
                    if (maxOuterValue - minOuterValue <= minUniqueInnerValues)
                    {
                        return false;
                    }
                }
                else
                {
                    // Only the one outer is known (the one just set)
                    var unknownOuterCell = valueSet0 ? outerCell1 : outerCell0;
                    if (val < minInner)
                    {
                        // Known outer is the minimum, so the other outer needs room to be the maximum
                        int smallestMaxValue = Math.Max(val + minUniqueInnerValues + 1, maxInner + 1);
                        if (smallestMaxValue > MAX_VALUE)
                        {
                            return false;
                        }

                        clearMask = MaskStrictlyLower(smallestMaxValue);
                    }
                    else
                    {
                        // Known outer is the maximum, so the other outer needs room to be the minimum
                        int largestMinValue = Math.Min(val - minUniqueInnerValues - 1, minInner - 1);
                        if (largestMinValue < 1)
                        {
                            return false;
                        }

                        clearMask = MaskStrictlyHigher(largestMinValue);
                    }
                    if (sudokuSolver.ClearMask(unknownOuterCell.Item1, unknownOuterCell.Item2, clearMask) == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => valid ? InitLinksByRunningLogic(solver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => valid ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (innerCells == null || innerCells.Count == 0)
        {
            return LogicResult.None;
        }

        return DoLogic(sudokuSolver, logicalStepDescription);
    }

    private LogicResult DoLogic(Solver sudokuSolver, StringBuilder logicalStepDescription)
    {
        var board = sudokuSolver.Board;
        bool changed = false;

        uint outerMask0 = board[outerCell0.Item1, outerCell0.Item2];
        uint outerMask1 = board[outerCell1.Item1, outerCell1.Item2];
        bool outerSet0 = IsValueSet(outerMask0);
        bool outerSet1 = IsValueSet(outerMask1);

        // The logic for inners is always the same - stay within the min and max bounds
        {
            uint combinedMask = (outerMask0 | outerMask1) & ~valueSetMask;
            int minOuterValue = MinValue(combinedMask);
            int maxOuterValue = MaxValue(combinedMask);

            uint clearMask = ALL_VALUES_MASK & ~MaskBetweenExclusive(minOuterValue, maxOuterValue);
            foreach (var (i1, j1) in innerCells)
            {
                var logicResult = sudokuSolver.ClearMask(i1, j1, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"{CellName(i1, j1)} has no candidates which can be strictly between the minimum ({minOuterValue}) and maximum ({maxOuterValue}).");
                    }
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        if (!changed)
                        {
                            logicalStepDescription.Append($"The minimum ({minOuterValue}) and maximum ({maxOuterValue}) values remove candidates {MaskToString(clearMask)} from: {CellName(i1, j1)}");
                        }
                        else
                        {
                            logicalStepDescription.Append($", {CellName(i1, j1)}");
                        }
                    }
                    changed = true;
                }
            }
            if (changed)
            {
                return LogicResult.Changed;
            }
        }

        // If both outers are already set, there's no more logic to do.
        if (outerSet0 && outerSet1)
        {
            return LogicResult.None;
        }

        if (!outerSet0 && !outerSet1)
        {
            // Neither outers are set, so ensure they remain within a minimum distance from each other
            outerMask0 &= ~valueSetMask;
            outerMask1 &= ~valueSetMask;

            uint innerMask = 0;
            foreach (var (i1, j1) in innerCells)
            {
                innerMask |= board[i1, j1];
            }
            innerMask &= ~valueSetMask;

            int minInner = MinValue(innerMask);
            int maxInner = MaxValue(innerMask);

            int outerLargestMin = maxInner - minUniqueInnerValues;
            int outerSmallestMax = minInner + minUniqueInnerValues;
            if (outerSmallestMax - outerLargestMin > 1)
            {
                uint clearMask = MaskBetweenExclusive(outerLargestMin, outerSmallestMax);
                var logicResult = sudokuSolver.ClearMask(outerCell0.Item1, outerCell0.Item2, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"All candidates in {CellName(outerCell0)} ({MaskToString(outerMask0)}) would break the cells on the line.");
                    }
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    logicalStepDescription?.Append($"Candidates {MaskToString(clearMask)} in min or max would break the cells on the line, so they are removed from: {CellName(outerCell0)}");
                    changed = true;
                }

                logicResult = sudokuSolver.ClearMask(outerCell1.Item1, outerCell1.Item2, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"All candidates in {CellName(outerCell1)} ({MaskToString(outerMask1)}) would break the cells on the line.");
                    }
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        if (!changed)
                        {
                            logicalStepDescription.Append($"Candidates {MaskToString(clearMask)} in min or max would break the cells on the line, so they are removed from: {CellName(outerCell1)}");
                        }
                        else
                        {
                            logicalStepDescription.Append($", {CellName(outerCell1)}");
                        }
                    }
                    changed = true;
                }
                if (changed)
                {
                    return LogicResult.Changed;
                }
            }
        }
        else
        {
            int setOuterVal = outerSet0 ? GetValue(outerMask0) : GetValue(outerMask1);
            uint unsetOuterMask = outerSet0 ? outerMask1 : outerMask0;
            var unsetOuterCell = outerSet0 ? outerCell1 : outerCell0;

            // Clear out any impossible outer values (must be minUniqueInnerValues away from the set value)
            {
                int minUnsetValue = setOuterVal - minUniqueInnerValues - 1;
                int maxUnsetValue = setOuterVal + minUniqueInnerValues + 1;
                uint clearUnsetMask = (minUnsetValue < 1) ? MaskValAndLower(setOuterVal) : MaskBetweenInclusive(minUnsetValue + 1, setOuterVal);
                clearUnsetMask |= (maxUnsetValue > MAX_VALUE) ? MaskValAndHigher(setOuterVal) : MaskBetweenInclusive(setOuterVal, maxUnsetValue - 1);
                clearUnsetMask &= board[unsetOuterCell.Item1, unsetOuterCell.Item2];
                if (clearUnsetMask != 0)
                {
                    var logicResult = sudokuSolver.ClearMask(unsetOuterCell.Item1, unsetOuterCell.Item2, clearUnsetMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"All candidates in {CellName(unsetOuterCell)} ({MaskToString(unsetOuterMask)}) would break the cells on the line.");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        logicalStepDescription?.Append($"Candidates {MaskToString(clearUnsetMask)} removed from {CellName(unsetOuterCell)}.");
                        return LogicResult.Changed;
                    }
                }
            }

            var (minInner, maxInner) = GetMinMaxInnerValues(sudokuSolver);
            if (minInner <= maxInner)
            {
                if (setOuterVal < minInner)
                {
                    // The set value is the minimum
                    int smallestMaxValue = Math.Max(setOuterVal + minUniqueInnerValues + 1, maxInner + 1);
                    if (smallestMaxValue > MAX_VALUE)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"All candidates in {CellName(unsetOuterCell)} ({MaskToString(unsetOuterMask)}) as a maximum would break the cells on the line.");
                        }
                        return LogicResult.Invalid;
                    }

                    uint clearMask = MaskStrictlyLower(smallestMaxValue);
                    var logicResult = sudokuSolver.ClearMask(unsetOuterCell.Item1, unsetOuterCell.Item2, clearMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"All candidates in {CellName(unsetOuterCell)} ({MaskToString(unsetOuterMask)}) as a maximum would break the cells on the line.");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        logicalStepDescription?.Append($"Candidates {MaskToString(clearMask)} as a maximum would break the cells on the line, so they are removed from: {CellName(unsetOuterCell)}");
                        return LogicResult.Changed;
                    }
                }
                else
                {
                    // The set value is the maximum
                    int largestMinValue = Math.Max(setOuterVal - minUniqueInnerValues - 1, minInner - 1);
                    if (largestMinValue < 1)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"All candidates in {CellName(unsetOuterCell)} ({MaskToString(unsetOuterMask)}) as a minimum would break the cells on the line.");
                        }
                        return LogicResult.Invalid;
                    }

                    uint clearMask = MaskStrictlyHigher(largestMinValue);
                    var logicResult = sudokuSolver.ClearMask(unsetOuterCell.Item1, unsetOuterCell.Item2, clearMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"All candidates in {CellName(unsetOuterCell)} ({MaskToString(unsetOuterMask)}) as a minimum would break the cells on the line.");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        logicalStepDescription?.Append($"Candidates {MaskToString(clearMask)} as a minimum would break the cells on the line, so they are removed from: {CellName(unsetOuterCell)}");
                        return LogicResult.Changed;
                    }
                }
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private (int, int) GetMinMaxInnerValues(Solver sudokuSolver)
    {
        var board = sudokuSolver.Board;

        int min = MAX_VALUE + 1;
        int max = -1;
        foreach (var (i, j) in innerCells)
        {
            uint mask = board[i, j];
            if (IsValueSet(mask))
            {
                int v = GetValue(mask);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }
        return (min, max);
    }
}
