using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Arrow", ConsoleName = "arrow")]
    public class ArrowSumConstraint : Constraint
    {
        public readonly List<(int, int)> circleCells;
        public readonly List<(int, int)> arrowCells;
        private readonly HashSet<(int, int)> allCells;
        private List<(int, int)> groupCells;
        private bool isArrowGroup = false;
        private bool isCircleGroup = false;
        private bool isAllGrouped = false;

        public ArrowSumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 2)
            {
                throw new ArgumentException($"Arrow constraint expects 2 cell groups, got {cellGroups.Count}.");
            }

            circleCells = cellGroups[0];
            arrowCells = cellGroups[1];
            allCells = new(circleCells.Concat(arrowCells));
        }

        public override IEnumerable<(int, int)> SeenCells((int, int) cell)
        {
            if (arrowCells.Count != 1 && circleCells.Count == 1)
            {
                if (allCells.Contains(cell))
                {
                    if (circleCells.Contains(cell))
                    {
                        return arrowCells;
                    }
                    if (arrowCells.Contains(cell))
                    {
                        return circleCells;
                    }
                }
            }
            return Enumerable.Empty<(int, int)>();
        }

        public override string SpecificName => $"Arrow at {CellName(circleCells[0])}";

        public override List<(int, int)> Group => isArrowGroup ? groupCells : null;

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            bool changed = false;
            var board = sudokuSolver.Board;

            if (arrowCells.Count > 1)
            {
                isAllGrouped = sudokuSolver.IsGroup(allCells.ToList());
                if (isAllGrouped)
                {
                    isArrowGroup = true;
                    isCircleGroup = true;
                    groupCells = allCells.ToList();
                }
                else
                {
                    isArrowGroup = sudokuSolver.IsGroup(arrowCells);
                    if (isArrowGroup)
                    {
                        groupCells = arrowCells.ToList();
                        if (circleCells.Count == 1)
                        {
                            groupCells.Add(circleCells[0]);
                        }
                    }
                    isCircleGroup = sudokuSolver.IsGroup(circleCells);
                }
            }

            if (circleCells.Count == 1)
            {
                int maxValue = MAX_VALUE - arrowCells.Count + 1;
                if (maxValue < MAX_VALUE)
                {
                    uint maxValueMask = (1u << maxValue) - 1;
                    foreach (var cell in arrowCells)
                    {
                        uint cellMask = board[cell.Item1, cell.Item2];
                        if (!IsValueSet(cellMask))
                        {
                            if ((cellMask & maxValueMask) != cellMask)
                            {
                                board[cell.Item1, cell.Item2] &= maxValueMask;
                                changed = true;
                            }
                        }
                    }
                }

                int minSum = arrowCells.Count - 1;
                if (minSum > 0)
                {
                    var sumCell = circleCells[0];
                    uint minValueMask = ~((1u << minSum) - 1);
                    uint cellMask = board[sumCell.Item1, sumCell.Item2];
                    if (!IsValueSet(cellMask))
                    {
                        if ((cellMask & minValueMask) != cellMask)
                        {
                            board[sumCell.Item1, sumCell.Item2] &= minValueMask;
                            changed = true;
                        }
                    }
                }
            }
            else if (circleCells.Count == 2)
            {
                int maxSum = arrowCells.Count * MAX_VALUE;
                if (maxSum <= MAX_VALUE)
                {
                    return LogicResult.Invalid;
                }
                if (maxSum <= 99)
                {
                    int maxSumPrefix = maxSum / 10;
                    if (maxSumPrefix < MAX_VALUE)
                    {
                        var sumCell = circleCells[0];
                        uint maxValueMask = (1u << maxSumPrefix) - 1;
                        uint cellMask = board[sumCell.Item1, sumCell.Item2];
                        if (!IsValueSet(cellMask))
                        {
                            if ((cellMask & maxValueMask) != cellMask)
                            {
                                board[sumCell.Item1, sumCell.Item2] &= maxValueMask;
                                changed = true;
                            }
                        }
                    }
                }
            }
            else if (circleCells.Count == 3)
            {
                int maxSum = arrowCells.Count * MAX_VALUE;
                if (maxSum <= 99)
                {
                    return LogicResult.Invalid;
                }
                if (maxSum <= 999)
                {
                    int maxSumPrefix = maxSum / 100;
                    if (maxSumPrefix < MAX_VALUE)
                    {
                        var sumCell = circleCells[0];
                        uint maxValueMask = (1u << maxSumPrefix) - 1;
                        uint cellMask = board[sumCell.Item1, sumCell.Item2];
                        if (!IsValueSet(cellMask))
                        {
                            if ((cellMask & maxValueMask) != cellMask)
                            {
                                board[sumCell.Item1, sumCell.Item2] &= maxValueMask;
                                changed = true;
                            }
                        }
                    }
                }
                else
                {
                    return LogicResult.Invalid;
                }
            }
            else
            {
                return LogicResult.Invalid;
            }

            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (!allCells.Contains((i, j)))
            {
                return true;
            }

            if (HasCircleValue(sudokuSolver) &&
                HasArrowValue(sudokuSolver) &&
                CircleValue(sudokuSolver) != ArrowValue(sudokuSolver))
            {
                return false;
            }

            if (circleCells.Count == 1 && arrowCells.Count == 1)
            {
                bool isCircleCell = circleCells.Contains((i, j));
                bool isArrowCell = arrowCells.Contains((i, j));

                if (isCircleCell)
                {
                    if (!sudokuSolver.SetValue(arrowCells[0].Item1, arrowCells[0].Item2, val))
                    {
                        return false;
                    }
                }
                else if (isArrowCell)
                {
                    if (!sudokuSolver.SetValue(circleCells[0].Item1, circleCells[0].Item2, val))
                    {
                        return false;
                    }
                }
                return true;
            }

            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var board = sudokuSolver.Board;
            bool changed = false;
            bool circleCellsFilled = HasCircleValue(sudokuSolver);
            bool arrowCellsFilled = HasArrowValue(sudokuSolver);
            if (circleCellsFilled && arrowCellsFilled)
            {
                // Both the sum and arrow cell values are known, so check to ensure the sum in correct
                int circleValue = CircleValue(sudokuSolver);
                int arrowValue = ArrowValue(sudokuSolver);
                if (circleValue != arrowValue)
                {
                    logicalStepDescription?.Append($"Sum of circle {circleValue} and arrow {arrowValue} do not match.");
                    return LogicResult.Invalid;
                }
                return LogicResult.None;
            }

            if (arrowCellsFilled)
            {
                // The arrow sum is known, so the sum cells are forced.
                int arrowSum = ArrowValue(sudokuSolver);
                return FillCircleWithKnownSum(sudokuSolver, arrowSum, logicalStepDescription);
            }
            
            if (circleCellsFilled)
            {
                // The circle sum is known, so adjust the candidates in the arrow cells to match that sum
                int circleValue = CircleValue(sudokuSolver);
                return AdjustArrowCandidatesWithKnownSum(sudokuSolver, circleValue, logicalStepDescription);
            }
            
            // Neither circle nor arrow values filled.
            var possibleArrowSums = PossibleArrowSums(sudokuSolver);
            if (possibleArrowSums.Count == 0)
            {
                logicalStepDescription?.Append($"There are no value sums for the arrow.");
                return LogicResult.Invalid;
            }

            if (possibleArrowSums.Count == 1)
            {
                // Only one circle sum is possible, so fill it
                return FillCircleWithKnownSum(sudokuSolver, possibleArrowSums.First(), logicalStepDescription);
            }

            // Adjust the circle candidates based on the possible sums
            int numCircleCells = circleCells.Count;
            if (numCircleCells == 1)
            {
                uint keepMask = 0;
                foreach (int sum in possibleArrowSums)
                {
                    keepMask |= ValueMask(sum);
                }
                var circleCell = circleCells[0];
                uint circleMask = board[circleCell.Item1, circleCell.Item2] & ~valueSetMask;
                uint clearMask = circleMask & ~keepMask;
                if (clearMask != 0)
                {
                    LogicResult logicResult = sudokuSolver.ClearMask(circleCells[0].Item1, circleCells[0].Item2, clearMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        logicalStepDescription?.Append($"None of the possible sums for arrow can be put in the circle.");
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        logicalStepDescription?.Append($"Removed impossible sums {MaskToString(clearMask)} from circle cell at {CellName(circleCell)}.");
                        return LogicResult.Changed;
                    }
                }
            }
            else
            {
                uint[] keepMasks = new uint[numCircleCells];
                foreach (int sum in possibleArrowSums)
                {
                    int remainingSum = sum;
                    for (int digitIndex = 0; digitIndex < numCircleCells; digitIndex++)
                    {
                        int val = remainingSum % 10;
                        keepMasks[numCircleCells - digitIndex - 1] |= ValueMask(val);
                        remainingSum /= 10;
                    }
                }
                for (int digitIndex = 0; digitIndex < numCircleCells; digitIndex++)
                {
                    var circleCell = circleCells[digitIndex];
                    uint circleMask = board[circleCell.Item1, circleCell.Item2] & ~valueSetMask;
                    uint clearMask = circleMask & ~keepMasks[digitIndex];
                    if (clearMask != 0)
                    {
                        LogicResult logicResult = sudokuSolver.ClearMask(circleCell.Item1, circleCell.Item2, clearMask);
                        if (logicResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"None of the possible sums for arrow can be put in the circle.");
                            return LogicResult.Invalid;
                        }
                        if (logicResult == LogicResult.Changed)
                        {
                            if (!changed)
                            {
                                logicalStepDescription?.Append($"Removed impossible candidates {MaskToString(clearMask)} from circle cell at {CellName(circleCell)}");
                            }
                            else
                            {
                                logicalStepDescription?.Append($", {MaskToString(clearMask)} from {CellName(circleCell)}");
                            }
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    return LogicResult.Changed;
                }
            }

            // Adjust the arrow candidates based on the possible sums
            int numArrowCells = arrowCells.Count;
            uint[] arrowCandidates = new uint[numArrowCells];
            foreach (int sum in possibleArrowSums)
            {
                AppendArrowCandidatesForSums(sudokuSolver, sum, arrowCandidates);
            }
            for (int cellIndex = 0; cellIndex < numArrowCells; cellIndex++)
            {
                var (ai, aj) = arrowCells[cellIndex];
                uint cellMask = board[ai, aj];
                if (IsValueSet(cellMask))
                {
                    continue;
                }

                uint keepMask = arrowCandidates[cellIndex];
                uint clearMask = (cellMask & ~keepMask);
                LogicResult logicResult = sudokuSolver.ClearMask(ai, aj, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Append($"None of the circle sums can be fulfilled by the arrow cells (such as in {CellName(ai, aj)}).");
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    if (!changed)
                    {
                        logicalStepDescription?.Append($"Removed impossible candidates {MaskToString(clearMask)} from arrow cell at {CellName(ai, aj)}.");
                    }
                    else
                    {
                        logicalStepDescription?.Append($", {MaskToString(clearMask)} from {CellName(ai, aj)}.");
                    }
                    changed = true;
                }
            }

            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private HashSet<int> PossibleArrowSums(Solver sudokuSolver)
        {
            HashSet<int> possibleArrowSums = new();

            var board = sudokuSolver.Board;
            if (!isArrowGroup)
            {
                int minSum = 0;
                int maxSum = 0;
                foreach (var (ai, aj) in arrowCells)
                {
                    uint arrowMask = board[ai, aj];
                    if (IsValueSet(arrowMask))
                    {
                        int val = GetValue(arrowMask);
                        minSum += val;
                        maxSum += val;
                    }
                    else
                    {
                        minSum += MinValue(arrowMask);
                        maxSum += MaxValue(arrowMask);
                    }
                }
                for (int sum = minSum; sum <= maxSum; sum++)
                {
                    if (IsPossibleCircleValue(sudokuSolver, sum))
                    {
                        possibleArrowSums.Add(sum);
                    }
                }
            }
            else
            {
                int baseSum = 0;
                List<(int, int)> remainingArrowCells = new();
                uint allArrowMask = 0;
                foreach (var (ai, aj) in arrowCells)
                {
                    uint arrowMask = board[ai, aj];
                    if (IsValueSet(arrowMask))
                    {
                        int val = GetValue(arrowMask);
                        baseSum += val;
                    }
                    else
                    {
                        allArrowMask |= board[ai, aj];
                        remainingArrowCells.Add((ai, aj));
                    }
                }
                if (remainingArrowCells.Count >= 1)
                {
                    int numRemainingArrowCells = remainingArrowCells.Count;
                    int numPossibleValues = ValueCount(allArrowMask);
                    if (numPossibleValues >= numRemainingArrowCells)
                    {
                        List<int> possibleValues = new(ValueCount(allArrowMask));
                        int minValue = MinValue(allArrowMask);
                        int maxValue = MaxValue(allArrowMask);
                        for (int val = minValue; val <= maxValue; val++)
                        {
                            if ((allArrowMask & ValueMask(val)) != 0)
                            {
                                possibleValues.Add(val);
                            }
                        }

                        foreach (var valueCombination in possibleValues.Combinations(numRemainingArrowCells))
                        {
                            int valueComboSum = baseSum + valueCombination.Sum();
                            if (possibleArrowSums.Contains(valueComboSum))
                            {
                                continue;
                            }

                            bool canUseCombination = true;
                            if (isAllGrouped)
                            {
                                int remainingComboSum = valueComboSum;
                                int digitIndex = circleCells.Count - 1;
                                while (remainingComboSum > 0)
                                {
                                    int curDigit = remainingComboSum % 10;
                                    remainingComboSum /= 10;

                                    if (!arrowCells.Contains(circleCells[digitIndex]) && valueCombination.Contains(curDigit))
                                    {
                                        canUseCombination = false;
                                        break;
                                    }
                                    digitIndex--;
                                }
                            }
                            if (!canUseCombination)
                            {
                                continue;
                            }
                            if (!IsPossibleCircleValue(sudokuSolver, valueComboSum))
                            {
                                continue;
                            }

                            bool valueCombinationPossible = false;
                            foreach (var cellPermutation in remainingArrowCells.Permuatations())
                            {
                                bool permutationPossible = true;
                                for (int cellIndex = 0; cellIndex < numRemainingArrowCells; cellIndex++)
                                {
                                    var (i, j) = cellPermutation[cellIndex];
                                    int val = valueCombination[cellIndex];
                                    uint cellMask = board[i, j];
                                    if ((cellMask & ValueMask(val)) == 0)
                                    {
                                        permutationPossible = false;
                                        break;
                                    }
                                }
                                if (permutationPossible)
                                {
                                    valueCombinationPossible = true;
                                    break;
                                }
                            }
                            if (valueCombinationPossible)
                            {
                                possibleArrowSums.Add(valueComboSum);
                            }
                        }
                    }
                }
            }

            return possibleArrowSums;
        }

        private bool IsPossibleCircleValue(Solver sudokuSolver, int circleValue)
        {
            var board = sudokuSolver.Board;
            if (circleCells.Count == 1)
            {
                if (circleValue < 1 || circleValue > MAX_VALUE)
                {
                    return false;
                }

                uint circleCellMask = board[circleCells[0].Item1, circleCells[0].Item2];
                return (circleCellMask & ValueMask(circleValue)) != 0;
            }
            else if (circleCells.Count == 2)
            {
                if (circleValue <= 10 || circleValue >= 100)
                {
                    return false;
                }

                uint circleCellMask0 = board[circleCells[0].Item1, circleCells[0].Item2];
                uint circleCellMask1 = board[circleCells[1].Item1, circleCells[1].Item2];

                int circleValue0 = circleValue / 10;
                int circleValue1 = circleValue % 10;
                if (circleValue0 == 0 || circleValue1 == 0 || circleValue0 == circleValue1 && isCircleGroup)
                {
                    return false;
                }
                return (circleCellMask0 & ValueMask(circleValue0)) != 0 && (circleCellMask1 & ValueMask(circleValue1)) != 0;
            }
            else if (circleCells.Count == 3)
            {
                if (circleValue <= 100 || circleValue >= 1000)
                {
                    return false;
                }

                uint circleCellMask0 = board[circleCells[0].Item1, circleCells[0].Item2];
                uint circleCellMask1 = board[circleCells[1].Item1, circleCells[1].Item2];
                uint circleCellMask2 = board[circleCells[2].Item1, circleCells[2].Item2];

                int circleValue0 = circleValue / 100;
                int circleValue1 = (circleValue / 10) % 10;
                int circleValue2 = circleValue % 10;
                if (circleValue0 == 0 || circleValue1 == 0 || circleValue2 == 0)
                {
                    return false;
                }
                if (isCircleGroup && (circleValue0 == circleValue1 || circleValue0 == circleValue2 || circleValue1 == circleValue2))
                {
                    return false;
                }
                return (circleCellMask0 & ValueMask(circleValue0)) != 0 && (circleCellMask1 & ValueMask(circleValue1)) != 0 && (circleCellMask2 & ValueMask(circleValue2)) != 0;
            }

            return true;
        }

        private LogicResult FillCircleWithKnownSum(Solver sudokuSolver, int arrowSum, StringBuilder logicalStepDescription = null)
        {
            bool changed = false;
            var board = sudokuSolver.Board;
            if (circleCells.Count == 1)
            {
                if (arrowSum <= 0 || arrowSum > MAX_VALUE)
                {
                    logicalStepDescription?.Append($"Sum of arrow is impossible to fill into circle.");
                    return LogicResult.Invalid;
                }
                uint arrowSumMask = ValueMask(arrowSum);
                var sumCell = circleCells[0];
                uint sumCellMask = board[sumCell.Item1, sumCell.Item2];
                if ((sumCellMask & arrowSumMask) == 0)
                {
                    logicalStepDescription?.Append($"Sum of arrow is impossible to fill into circle.");
                    return LogicResult.Invalid;
                }

                if (!sudokuSolver.SetValue(sumCell.Item1, sumCell.Item2, arrowSum))
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSum} into {CellName(sumCell)}");
                    return LogicResult.Invalid;
                }
                logicalStepDescription?.Append($"Circle value at {CellName(sumCell)} set to {arrowSum}");
                changed = true;
            }
            else if (circleCells.Count == 2)
            {
                if (arrowSum <= 9 || arrowSum >= 100)
                {
                    logicalStepDescription?.Append($"Sum of arrow is impossible to fill into pill.");
                    return LogicResult.Invalid;
                }

                int arrowSumTens = arrowSum / 10;
                int arrowSumOnes = arrowSum % 10;
                if (arrowSumOnes == 0)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a 0 in {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }
                if (arrowSumTens == arrowSumOnes)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a {arrowSumTens} in both {CellName(circleCells[0])} and {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }

                uint arrowSumTensMask = ValueMask(arrowSumTens);
                uint arrowSumOnesMask = ValueMask(arrowSumOnes);

                var sumCellTens = circleCells[0];
                var sumCellOnes = circleCells[1];
                uint sumCellTensMask = board[sumCellTens.Item1, sumCellTens.Item2];
                uint sumCellOnesMask = board[sumCellOnes.Item1, sumCellOnes.Item2];
                if ((sumCellTensMask & arrowSumTensMask) == 0)
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSumTens} into {CellName(circleCells[0])}");
                    return LogicResult.Invalid;
                }
                if ((sumCellOnesMask & arrowSumOnesMask) == 0)
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSumOnes} into {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }

                if (!IsValueSet(sumCellTensMask))
                {
                    if (!sudokuSolver.SetValue(sumCellTens.Item1, sumCellTens.Item2, arrowSumTens))
                    {
                        logicalStepDescription?.Append($"Cannot fill {arrowSumTens} into {CellName(sumCellTens)}");
                        return LogicResult.Invalid;
                    }
                }
                if (!IsValueSet(sumCellOnesMask))
                {
                    if (!sudokuSolver.SetValue(sumCellOnes.Item1, sumCellOnes.Item2, arrowSumOnes))
                    {
                        logicalStepDescription?.Append($"Cannot fill {arrowSumOnes} into {CellName(sumCellOnes)}");
                        return LogicResult.Invalid;
                    }
                }
                logicalStepDescription?.Append($"Circle value at {CellName(sumCellTens)}{CellName(sumCellOnes)} set to {arrowSum}");
                changed = true;
            }
            else if (circleCells.Count == 3)
            {
                if (arrowSum <= 99 || arrowSum >= 1000)
                {
                    logicalStepDescription?.Append($"Sum of arrow is impossible to fill into pill.");
                    return LogicResult.Invalid;
                }

                int arrowSumHund = arrowSum / 100;
                int arrowSumTens = (arrowSum / 10) % 10;
                int arrowSumOnes = arrowSum % 10;
                if (arrowSumTens == 0)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a 0 in {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }
                if (arrowSumOnes == 0)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a 0 in {CellName(circleCells[2])}");
                    return LogicResult.Invalid;
                }
                if (arrowSumHund == arrowSumTens)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a {arrowSumTens} in both {CellName(circleCells[0])} and {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }
                if (arrowSumTens == arrowSumOnes)
                {
                    logicalStepDescription?.Append($"Sum of arrow requires a {arrowSumTens} in both {CellName(circleCells[1])} and {CellName(circleCells[2])}");
                    return LogicResult.Invalid;
                }

                uint arrowSumHundMask = ValueMask(arrowSumHund);
                uint arrowSumTensMask = ValueMask(arrowSumTens);
                uint arrowSumOnesMask = ValueMask(arrowSumOnes);

                var sumCellHund = circleCells[0];
                var sumCellTens = circleCells[1];
                var sumCellOnes = circleCells[2];
                uint sumCellHundMask = board[sumCellHund.Item1, sumCellHund.Item2];
                uint sumCellTensMask = board[sumCellTens.Item1, sumCellTens.Item2];
                uint sumCellOnesMask = board[sumCellOnes.Item1, sumCellOnes.Item2];
                if ((sumCellHundMask & arrowSumHundMask) == 0)
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSumHund} into {CellName(circleCells[0])}");
                    return LogicResult.Invalid;
                }
                if ((sumCellTensMask & arrowSumTensMask) == 0)
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSumTens} into {CellName(circleCells[1])}");
                    return LogicResult.Invalid;
                }
                if ((sumCellOnesMask & arrowSumOnesMask) == 0)
                {
                    logicalStepDescription?.Append($"Cannot fill {arrowSumOnes} into {CellName(circleCells[2])}");
                    return LogicResult.Invalid;
                }

                // Let SetValue run again on these as a "naked single" instead of calling SetValue recursively
                if (!IsValueSet(sumCellHundMask))
                {
                    if (!sudokuSolver.SetValue(sumCellHund.Item1, sumCellHund.Item2, arrowSumHund))
                    {
                        logicalStepDescription?.Append($"Cannot fill {arrowSumHund} into {CellName(sumCellHund)}");
                        return LogicResult.Invalid;
                    }
                }
                if (!IsValueSet(sumCellTensMask))
                {
                    if (!sudokuSolver.SetValue(sumCellTens.Item1, sumCellTens.Item2, arrowSumTens))
                    {
                        logicalStepDescription?.Append($"Cannot fill {arrowSumTens} into {CellName(sumCellTens)}");
                        return LogicResult.Invalid;
                    }
                }
                if (!IsValueSet(sumCellOnesMask))
                {
                    if (!sudokuSolver.SetValue(sumCellOnes.Item1, sumCellOnes.Item2, arrowSumOnes))
                    {
                        logicalStepDescription?.Append($"Cannot fill {arrowSumOnes} into {CellName(sumCellOnes)}");
                        return LogicResult.Invalid;
                    }
                }
                logicalStepDescription?.Append($"Circle value at {CellName(sumCellHund)}{CellName(sumCellTens)}{CellName(sumCellOnes)} set to {arrowSum}");
                changed = true;
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private void AppendArrowCandidatesForSums(Solver sudokuSolver, int sum, uint[] arrowCandidates)
        {
            var board = sudokuSolver.Board;
            if (!isArrowGroup)
            {
                int sumRemaining = sum;
                int numRemainingCells = 0;
                for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
                {
                    var (i, j) = arrowCells[cellIndex];
                    uint arrowMask = board[i, j];
                    if (IsValueSet(arrowMask))
                    {
                        sumRemaining -= GetValue(arrowMask);
                    }
                    else
                    {
                        numRemainingCells++;
                    }
                }

                int maxValue = Math.Min(MAX_VALUE, sumRemaining - numRemainingCells + 1);
                uint keepMask = MaskValAndLower(maxValue);
                for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
                {
                    var (i, j) = arrowCells[cellIndex];
                    uint arrowMask = board[i, j];
                    if (!IsValueSet(arrowMask))
                    {
                        arrowCandidates[cellIndex] |= keepMask;
                    }
                }
            }
            else
            {
                int sumRemaining = sum;
                uint allArrowMask = 0;
                List<(int, int, int)> remainingArrowCells = new();
                for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
                {
                    var (i, j) = arrowCells[cellIndex];
                    uint arrowMask = board[i, j];
                    if (IsValueSet(arrowMask))
                    {
                        int arrowVal = GetValue(arrowMask);
                        sumRemaining -= arrowVal;
                    }
                    else
                    {
                        allArrowMask |= arrowMask;
                        remainingArrowCells.Add((i, j, cellIndex));
                    }
                }

                if (sumRemaining < 0)
                {
                    return;
                }

                int remainingArrowCellsCount = remainingArrowCells.Count;
                int numPossibleValues = ValueCount(allArrowMask);
                if (numPossibleValues >= remainingArrowCellsCount)
                {
                    List<int> possibleValues = new(numPossibleValues);
                    int minValue = MinValue(allArrowMask);
                    int maxValue = MaxValue(allArrowMask);
                    for (int curVal = minValue; curVal <= maxValue; curVal++)
                    {
                        if ((allArrowMask & ValueMask(curVal)) != 0)
                        {
                            possibleValues.Add(curVal);
                        }
                    }

                    foreach (var valueCombination in possibleValues.Combinations(remainingArrowCellsCount))
                    {
                        int valueComboSum = valueCombination.Sum();
                        if (valueComboSum != sumRemaining)
                        {
                            continue;
                        }

                        foreach (var cellPermutation in remainingArrowCells.Permuatations())
                        {
                            bool permutationPossible = true;
                            for (int remainingCellIndex = 0; remainingCellIndex < remainingArrowCellsCount; remainingCellIndex++)
                            {
                                var (i, j, _) = cellPermutation[remainingCellIndex];
                                int val = valueCombination[remainingCellIndex];
                                uint cellMask = board[i, j];
                                uint valMask = ValueMask(val);
                                if ((cellMask & valMask) == 0)
                                {
                                    permutationPossible = false;
                                    break;
                                }
                            }
                            if (permutationPossible)
                            {
                                for (int remainingCellIndex = 0; remainingCellIndex < remainingArrowCellsCount; remainingCellIndex++)
                                {
                                    int val = valueCombination[remainingCellIndex];
                                    uint valMask = ValueMask(val);
                                    int cellIndex = cellPermutation[remainingCellIndex].Item3;
                                    arrowCandidates[cellIndex] |= valMask;
                                }
                            }
                        }
                    }
                }
            }
        }

        private LogicResult AdjustArrowCandidatesWithKnownSum(Solver sudokuSolver, int sum, StringBuilder logicalStepDescription = null)
        {
            int numArrowCells = arrowCells.Count;
            uint[] arrowCandidates = new uint[numArrowCells];
            AppendArrowCandidatesForSums(sudokuSolver, sum, arrowCandidates);

            bool changed = false;
            var board = sudokuSolver.Board;
            for (int cellIndex = 0; cellIndex < numArrowCells; cellIndex++)
            {
                var (i, j) = arrowCells[cellIndex];
                uint cellMask = board[i, j];
                if (IsValueSet(cellMask))
                {
                    continue;
                }

                uint clearMask = ALL_VALUES_MASK & ~arrowCandidates[cellIndex];
                uint actuallyCleared = cellMask & clearMask;
                if (actuallyCleared != 0)
                {
                    LogicResult logicResult = sudokuSolver.ClearMask(i, j, actuallyCleared);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"Circle value of {sum} cannot be achieved by arrow cells ({CellName(i, j)} has no candidates).");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        if (logicalStepDescription != null)
                        {
                            if (!changed)
                            {
                                logicalStepDescription.Append($"Circle value of {sum} removes candidates {MaskToString(actuallyCleared)} from {CellName(i, j)}");
                            }
                            else
                            {
                                logicalStepDescription.Append($", {MaskToString(actuallyCleared)} from {CellName(i, j)}");
                            }
                        }
                        changed = true;
                    }
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private static bool HasValue(Solver board, IEnumerable<(int, int)> cells)
        {
            foreach (var cell in cells)
            {
                if (ValueCount(board.Board[cell.Item1, cell.Item2]) != 1)
                {
                    return false;
                }
            }
            return true;
        }

        public bool HasCircleValue(Solver board) => HasValue(board, circleCells);

        public bool HasArrowValue(Solver board) => HasValue(board, arrowCells);

        public int CircleValue(Solver board)
        {
            int sum = 0;
            foreach (int v in circleCells.Select(cell => board.GetValue(cell)))
            {
                sum *= 10;
                sum += v;
            }
            return sum;
        }
        public int ArrowValue(Solver board) => arrowCells.Select(cell => board.GetValue(cell)).Sum();
    }
}
