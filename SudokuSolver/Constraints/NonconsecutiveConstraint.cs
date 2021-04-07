using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Nonconsecutive", ConsoleName = "nc", FPuzzlesName = "nonconsecutive")]
    public class NonconsecutiveConstraint : Constraint
    {
        public NonconsecutiveConstraint() { }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (val > 1)
            {
                int adjVal = val - 1;
                foreach (var pair in AdjacentCells(i, j))
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
                foreach (var pair in AdjacentCells(i, j))
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
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        continue;
                    }

                    int maskValueCount = ValueCount(mask);
                    if (maskValueCount == 2)
                    {
                        int val1 = MinValue(mask);
                        int val2 = MaxValue(mask);
                        if (val2 - val1 == 1)
                        {
                            // Two consecutive values in a cell means that all of its adjacent cells cannot be either of those values
                            bool haveChanges = false;
                            foreach (var otherPair in AdjacentCells(i, j))
                            {
                                uint otherMask = board[otherPair.Item1, otherPair.Item2];
                                LogicResult findResult = sudokuSolver.ClearMask(otherPair.Item1, otherPair.Item2, mask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} with values {MaskToString(mask)} removes the only candidates {MaskToString(otherMask)} from {CellName(otherPair)}");
                                    return LogicResult.Invalid;
                                }

                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} removes those values from {CellName(otherPair)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(otherPair)}");
                                    }
                                }
                            }
                            if (haveChanges)
                            {
                                return LogicResult.Changed;
                            }
                        }
                        else if (val2 - val1 == 2)
                        {
                            // Values are 2 apart, which means adjacent cells can't be the value between those two
                            int bannedVal = val1 + 1;
                            uint clearMask = 1u << (bannedVal - 1);
                            bool haveChanges = false;
                            foreach (var otherPair in AdjacentCells(i, j))
                            {
                                uint otherMask = board[otherPair.Item1, otherPair.Item2];
                                LogicResult findResult = sudokuSolver.ClearMask(otherPair.Item1, otherPair.Item2, clearMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} with values {MaskToString(mask)} removes the only candidate {bannedVal} from {CellName(otherPair)}");
                                    return LogicResult.Invalid;
                                }

                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} removes {bannedVal} from {CellName(otherPair)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(otherPair)}");
                                    }
                                }
                            }
                            if (haveChanges)
                            {
                                return LogicResult.Changed;
                            }
                        }
                    }
                    else if (maskValueCount == 3)
                    {
                        int minValue = MinValue(mask);
                        int maxValue = MaxValue(mask);
                        if (maxValue - minValue == 2)
                        { 
                            // Three consecutive values means adjacent cells can't be the middle value
                            int clearValue = minValue + 1;
                            uint clearMask = ValueMask(clearValue);
                            bool changed = false;
                            foreach (var otherPair in AdjacentCells(i, j))
                            {
                                LogicResult findResult = sudokuSolver.ClearMask(otherPair.Item1, otherPair.Item2, clearMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} with values {MaskToString(mask)} removes the only candidate {clearValue} from {CellName(otherPair)}");
                                    return LogicResult.Invalid;
                                }

                                if (findResult == LogicResult.Changed)
                                {
                                    if (!changed)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} removes {clearValue} from {CellName(otherPair)}");
                                        changed = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(otherPair)}");
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

            // Look for groups where a particular digit is locked to 2, 3, or 4 places
            // For the case of 2 places, if they are adjacent then neither can be a consecutive digit
            // For the case of 3 places, if they are all adjacent then the center one cannot be a consecutive digit
            // For all cases, any cell that is adjacent to all of them cannot be a consecutive digit
            // That last one should be a generalized version of the first two if we count a cell as adjacent to itself
            var valInstances = new (int, int)[MAX_VALUE];
            foreach (var group in sudokuSolver.Groups)
            {
                // This logic only works if the value found must be in the group.
                // The only way to currently guarantee this is by only applying it to groups of size 9.
                // In the future, it might be useful to track stuff like "this killer cage must contain a 1"
                // and then apply this logic there.
                if (group.Cells.Count != MAX_VALUE)
                {
                    continue;
                }

                for (int val = 1; val <= MAX_VALUE; val++)
                {
                    uint valMask = 1u << (val - 1);
                    int numValInstances = 0;
                    foreach (var pair in group.Cells)
                    {
                        uint mask = board[pair.Item1, pair.Item2];
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
                            valInstances[numValInstances++] = pair;
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
                            if (curDist > 2)
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
                            uint consecMask1 = consecVal1 >= 1 && consecVal1 <= MAX_VALUE ? 1u << (consecVal1 - 1) : 0u;
                            uint consecMask2 = consecVal2 >= 1 && consecVal2 <= MAX_VALUE ? 1u << (consecVal2 - 1) : 0u;
                            uint consecMask = consecMask1 | consecMask2;

                            bool changed = false;
                            for (int i = minCoord.Item1; i <= maxCoord.Item1; i++)
                            {
                                for (int j = minCoord.Item2; j <= maxCoord.Item2; j++)
                                {
                                    uint otherMask = board[i, j];
                                    if (IsValueSet(otherMask) || (otherMask & consecMask) == 0)
                                    {
                                        continue;
                                    }

                                    bool allAdjacent = true;
                                    for (int valIndex = 0; valIndex < numValInstances; valIndex++)
                                    {
                                        var curCell = valInstances[valIndex];
                                        if (!IsAdjacent(i, j, curCell.Item1, curCell.Item2))
                                        {
                                            allAdjacent = false;
                                            break;
                                        }
                                    }
                                    if (allAdjacent)
                                    {
                                        LogicResult clearResult = sudokuSolver.ClearMask(i, j, consecMask);
                                        if (clearResult == LogicResult.Invalid)
                                        {
                                            logicalStepDescription.Clear();
                                            logicalStepDescription.Append($"{group} has {val} always adjacent to {CellName(i, j)}, but cannot clear values {MaskToString(consecMask)} from that cell.");
                                            return LogicResult.Invalid;
                                        }
                                        if (clearResult == LogicResult.Changed)
                                        {
                                            if (!changed)
                                            {
                                                logicalStepDescription.Append($"{group} has {val} always adjacent to one or more cells, removing {MaskToString(consecMask)} from {CellName(i, j)}");
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

            // Look for adjacent squares with a shared value plus two consecutive values.
            // The shared value must be in one of those two squares, eliminating it from
            // the rest of their shared groups.
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
                    for (int d = 0; d < 2; d++)
                    {
                        if (d == 0 && i == HEIGHT - 1)
                        {
                            continue;
                        }
                        if (d == 1 && j == WIDTH - 1)
                        {
                            continue;
                        }
                        (int, int) cellB = d == 0 ? (i + 1, j) : (i, j + 1);
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
                            if ((combinedMask & (1u << (v - 1))) != 0)
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
                            uint mustHaveMask = 1u << (mustHaveVal - 1);
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
                                    logicalStepDescription.Append($"{CellName(i, j)} with candidates {MaskToString(maskA)} and {CellName(cellB)} with candidates {MaskToString(maskB)} are adjacent, meaning they must contain {mustHaveVal}, but cannot clear that value from {CellName(otherCell)}.");
                                    return LogicResult.Invalid;
                                }
                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName(i, j)} with candidates {MaskToString(maskA)} and {CellName(cellB)} with candidates {MaskToString(maskB)} are adjacent, meaning they must contain {mustHaveVal}, clearing it from {CellName(otherCell)}");
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
