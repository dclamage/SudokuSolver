using System;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Diagonal Nonconsecutive", ConsoleName = "dnc")]
    public class DiagonalNonconsecutiveConstraint : Constraint
    {
        public DiagonalNonconsecutiveConstraint(string _)
        {
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (val > 1)
            {
                int adjVal = val - 1;
                foreach (var pair in DiagonalCells(i, j))
                {
                    if (!sudokuSolver.ClearValue(pair.Item1, pair.Item2, adjVal))
                    {
                        return false;
                    }
                }
            }
            if (val < MAX_VALUE)
            {
                int adjVal = val + 1;
                foreach (var pair in DiagonalCells(i, j))
                {
                    if (!sudokuSolver.ClearValue(pair.Item1, pair.Item2, adjVal))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var board = sudokuSolver.Board;

            // Look for single cells that can eliminate on its diagonals.
            // Some eliminations can only happen within the same box.
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        continue;
                    }

                    int valueCount = ValueCount(mask);
                    if (valueCount <= 3)
                    {
                        int minValue = MinValue(mask);
                        int maxValue = MaxValue(mask);
                        if (maxValue - minValue == 2)
                        {
                            // Values 2 apart will always remove the center value, but if there are 3 candidates this only applies to the same box
                            bool haveChanges = false;
                            int removeValue = minValue + 1;
                            uint removeValueMask = ValueMask(removeValue);
                            foreach (var cell in DiagonalCells(i, j, valueCount != 2))
                            {
                                LogicResult findResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, removeValueMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} removes the only candidate {removeValue} from {CellName(cell)}");
                                    return LogicResult.Invalid;
                                }

                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} remove {removeValue} from {CellName(cell)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(cell)}");
                                    }
                                }
                            }
                            if (haveChanges)
                            {
                                return LogicResult.Changed;
                            }
                        }
                        else if (maxValue - minValue == 1)
                        {
                            // Values 1 apart will always remove both values, but only for diagonals in the same box
                            bool haveChanges = false;
                            uint removeValueMask = ValueMask(minValue) | ValueMask(maxValue);
                            foreach (var cell in DiagonalCells(i, j, true))
                            {
                                LogicResult findResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, removeValueMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} removes the only candidates {MaskToString(removeValueMask)} from {CellName(cell)}");
                                    return LogicResult.Invalid;
                                }

                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {minValue}{maxValue} remove those values from {CellName(cell)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(cell)}");
                                    }
                                }
                            }
                            if (haveChanges)
                            {
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            // Look for groups where a particular digit is locked to 2, 3, or 4 places
            // Any cell that is diagonal to all of them cannot be either consecutive digit
            var valInstances = new (int, int)[MAX_VALUE];
            foreach (var group in sudokuSolver.Groups)
            {
                if (group.Cells.Count != MAX_VALUE)
                {
                    continue;
                }

                for (int val = 1; val <= MAX_VALUE; val++)
                {
                    uint valMask = 1u << (val - 1);
                    int numValInstances = 0;
                    foreach (var cell in group.Cells)
                    {
                        uint mask = board[cell.Item1, cell.Item2];
                        if (IsValueSet(mask))
                        {
                            if ((mask & valMask) != 0)
                            {
                                numValInstances = 0;
                                break;
                            }
                            continue;
                        }
                        if ((mask & valMask) != 0)
                        {
                            valInstances[numValInstances++] = cell;
                        }
                    }
                    if (numValInstances >= 2 && numValInstances <= 5)
                    {
                        bool tooFar = false;
                        var firstCell = valInstances[0];
                        var minCoord = firstCell;
                        var maxCoord = firstCell;
                        for (int i = 1; i < numValInstances; i++)
                        {
                            var curCell = valInstances[i];
                            int curDist = TaxicabDistance(firstCell.Item1, firstCell.Item2, curCell.Item1, curCell.Item2);
                            if (curDist > 5)
                            {
                                tooFar = true;
                                break;
                            }
                            minCoord = (Math.Min(minCoord.Item1, curCell.Item1), Math.Min(minCoord.Item2, curCell.Item2));
                            maxCoord = (Math.Max(maxCoord.Item1, curCell.Item1), Math.Max(maxCoord.Item2, curCell.Item2));
                        }

                        if (!tooFar)
                        {
                            int consecVal1 = val - 1;
                            int consecVal2 = val + 1;
                            uint consecMask1 = consecVal1 >= 1 && consecVal1 <= MAX_VALUE ? ValueMask(consecVal1) : 0u;
                            uint consecMask2 = consecVal2 >= 1 && consecVal2 <= MAX_VALUE ? ValueMask(consecVal2) : 0u;
                            uint consecMask = consecMask1 | consecMask2;

                            bool changed = false;
                            for (int i = minCoord.Item1 - 1; i <= maxCoord.Item1 + 1; i++)
                            {
                                if (i < 0 || i > 8)
                                {
                                    continue;
                                }

                                for (int j = minCoord.Item2 - 1; j <= maxCoord.Item2 + 1; j++)
                                {
                                    if (j < 0 || j > 8)
                                    {
                                        continue;
                                    }

                                    uint otherMask = board[i, j];
                                    if (IsValueSet(otherMask) || (otherMask & consecMask) == 0)
                                    {
                                        continue;
                                    }

                                    bool allDiagonal = true;
                                    for (int valIndex = 0; valIndex < numValInstances; valIndex++)
                                    {
                                        var curCell = valInstances[valIndex];
                                        if (curCell != (i, j) && !IsDiagonal(i, j, curCell.Item1, curCell.Item2))
                                        {
                                            allDiagonal = false;
                                            break;
                                        }
                                    }
                                    if (allDiagonal)
                                    {
                                        LogicResult clearResult = sudokuSolver.ClearMask(i, j, consecMask);
                                        if (clearResult == LogicResult.Invalid)
                                        {
                                            logicalStepDescription.Clear();
                                            logicalStepDescription.Append($"Value {val} is locked to {numValInstances} cells in {group}, removing all candidates from {CellName(i, j)}");
                                            return LogicResult.Invalid;
                                        }
                                        if (clearResult == LogicResult.Changed)
                                        {
                                            if (!changed)
                                            {
                                                logicalStepDescription.Append($"Value {val} is locked to {numValInstances} cells in {group}, removing {MaskToString(consecMask)} from {CellName(i, j)}");
                                                changed = true;
                                            }
                                            else
                                            {
                                                logicalStepDescription.Append($", {CellName(i, j)}");
                                            }
                                        }
                                    }
                                }
                            }
                            if (changed)
                            {
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            // Look for diagonal squares in the same box with a shared value plus two consecutive values.
            // The shared value must be in one of those two squares, eliminating it from
            // the rest of their shared box.
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    (int, int) cellA = (i, j);
                    uint maskA = board[i, j];
                    if (IsValueSet(maskA) || ValueCount(maskA) > 3)
                    {
                        continue;
                    }
                    foreach (var cellB in DiagonalCells(i, j, true))
                    {
                        uint maskB = board[cellB.Item1, cellB.Item2];
                        if (IsValueSet(maskB))
                        {
                            continue;
                        }

                        uint combinedMask = maskA | maskB;
                        if (ValueCount(combinedMask) != 3)
                        {
                            continue;
                        }
                        int valA = 0;
                        int valB = 0;
                        int valC = 0;
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if ((combinedMask & ValueMask(v)) != 0)
                            {
                                if (valA == 0)
                                {
                                    valA = v;
                                }
                                else if (valB == 0)
                                {
                                    valB = v;
                                }
                                else
                                {
                                    valC = v;
                                    break;
                                }
                            }
                        }
                        int mustHaveVal = 0;
                        if (valA + 1 == valB)
                        {
                            mustHaveVal = valC;
                        }
                        else if (valB + 1 == valC)
                        {
                            mustHaveVal = valA;
                        }
                        bool haveChanges = false;
                        if (mustHaveVal != 0)
                        {
                            uint mustHaveMask = ValueMask(mustHaveVal);
                            foreach (var otherCell in sudokuSolver.SeenCells(cellA, cellB))
                            {
                                uint otherMask = board[otherCell.Item1, otherCell.Item2];
                                if (IsValueSet(otherMask))
                                {
                                    continue;
                                }
                                LogicResult findResult = sudokuSolver.ClearMask(otherCell.Item1, otherCell.Item2, mustHaveMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} and {CellName(cellB)} have combined candidates {MaskToString(combinedMask)}, removing all candidates from {CellName(otherCell)}");
                                    return LogicResult.Invalid;
                                }
                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName(i, j)} and {CellName(cellB)} have combined candidates {MaskToString(combinedMask)}, removing {mustHaveVal} from {CellName(otherCell)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(otherCell)}");
                                    }
                                }
                            }
                        }
                        if (haveChanges)
                        {
                            return LogicResult.Changed;
                        }
                    }
                }
            }
            return LogicResult.None;
        }
    }
}
