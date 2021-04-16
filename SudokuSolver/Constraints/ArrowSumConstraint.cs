using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Arrow", ConsoleName = "arrow", FPuzzlesName = "arrow")]
    public class ArrowSumConstraint : Constraint
    {
        public readonly List<(int, int)> circleCells;
        public readonly List<(int, int)> arrowCells;
        private readonly HashSet<(int, int)> allCells;

        public ArrowSumConstraint(string options)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 2)
            {
                throw new ArgumentException($"Arrow constraint expects 2 cell groups, got {cellGroups.Count}.");
            }

            circleCells = cellGroups[0];
            arrowCells = cellGroups[1];
            allCells = new(this.circleCells.Concat(this.arrowCells));
        }

        public override string SpecificName => $"Arrow at {circleCells[0]}";

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            bool changed = false;
            var board = sudokuSolver.Board;

            if (circleCells.Count == 1)
            {
                int maxValue = MAX_VALUE - arrowCells.Count + 1;
                if (maxValue < MAX_VALUE)
                {
                    uint maxValueMask = (1u << maxValue) - 1;
                    foreach (var cell in arrowCells)
                    {
                        if (!IsValueSet(board[cell.Item1, cell.Item2]))
                        {
                            board[cell.Item1, cell.Item2] &= maxValueMask;
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
            else if (circleCells.Count == 2)
            {
                int maxSum = arrowCells.Count * MAX_VALUE;
                if (maxSum <= 99)
                {
                    return LogicResult.Invalid;
                }
                if (maxSum <= 999)
                {
                    int maxSumPrefix = maxSum / 100;

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

            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (!allCells.Contains((i, j)))
            {
                return true;
            }

            if (HasSumValue(sudokuSolver) &&
                HasArrowValue(sudokuSolver) &&
                SumValue(sudokuSolver) != ArrowValue(sudokuSolver))
            {
                return false;
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var board = sudokuSolver.Board;
            bool changed = false;
            bool sumCellsFilled = HasSumValue(sudokuSolver);
            bool arrowCellsFilled = HasArrowValue(sudokuSolver);
            if (sumCellsFilled && arrowCellsFilled)
            {
                // Both the sum and arrow cell values are known, so check to ensure the sum in correct
                int sumValue = SumValue(sudokuSolver);
                int arrowValue = ArrowValue(sudokuSolver);
                if (sumValue != arrowValue)
                {
                    logicalStepDescription?.Append($"Sum is incorrect.");
                    return LogicResult.Invalid;
                }
            }
            else if (arrowCellsFilled)
            {
                // The arrow sum is known, so the sum cells are forced.
                int arrowSum = ArrowValue(sudokuSolver);
                if (circleCells.Count == 1)
                {
                    if (arrowSum <= 0 || arrowSum > 9)
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

        public bool HasSumValue(Solver board) => HasValue(board, circleCells);

        public bool HasArrowValue(Solver board) => HasValue(board, arrowCells);

        public int SumValue(Solver board)
        {
            int sum = 0;
            foreach (int v in arrowCells.Select(cell => board.GetValue(cell)))
            {
                sum *= 10;
                sum += v;
            }
            return sum;
        }
        public int ArrowValue(Solver board) => arrowCells.Select(cell => board.GetValue(cell)).Sum();
    }
}
