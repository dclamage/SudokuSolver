using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Little Killer", ConsoleName = "lk")]
    public class LittleKillerConstraint : Constraint
    {
        public enum Direction
        {
            UpRight,
            UpLeft,
            DownRight,
            DownLeft,
        }

        public readonly (int, int) outerCell;
        public readonly Direction direction;
        public readonly int sum;
        private readonly (int, int) cellStart;
        private readonly HashSet<(int, int)> cells;
        private readonly List<(int, int)> cellsList;
        private bool isGroup = false;
        private List<List<int>> sumCombinations = null;
        private HashSet<int> possibleValues = null;

        public override List<(int, int)> Group => isGroup ? cellsList : null;

        private static readonly Regex optionsRegex = new(@"(\d+);[rR](\d+)[cC](\d+);([UD][LR])");

        public LittleKillerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var match = optionsRegex.Match(options);
            if (!match.Success)
            {
                throw new ArgumentException($"Little Killer options \"{options}\" invalid. Expecting: \"sum;rXcY;UL|UR|DL|DR\"");
            }

            sum = int.Parse(match.Groups[1].Value);

            outerCell = cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

            direction = Direction.UpRight;
            switch (match.Groups[4].Value)
            {
                case "UR":
                    direction = Direction.UpRight;
                    break;
                case "UL":
                    direction = Direction.UpLeft;
                    break;
                case "DR":
                    direction = Direction.DownRight;
                    break;
                case "DL":
                    direction = Direction.DownLeft;
                    break;
            }

            // F-Puzzles starts off the grid, so allow one step to enter the grid if necessary
            if (cellStart.Item1 < 0 || cellStart.Item1 >= HEIGHT || cellStart.Item2 < 0 || cellStart.Item2 >= WIDTH)
            {
                cellStart = NextCell(cellStart);
            }
            else
            {
                outerCell = PrevCell(cellStart);
            }

            // If the cell start is still invalid, then this is an error.
            if (cellStart.Item1 < 0 || cellStart.Item1 >= HEIGHT || cellStart.Item2 < 0 || cellStart.Item2 >= WIDTH)
            {
                throw new ArgumentException($"Little Killer options \"{options}\" invalid. Starting cell is invalid.");
            }

            cells = new HashSet<(int, int)>();
            (int, int) cell = cellStart;
            while (cell.Item1 >= 0 && cell.Item1 < HEIGHT && cell.Item2 >= 0 && cell.Item2 < WIDTH)
            {
                cells.Add(cell);
                cell = NextCell(cell);
            }
            cellsList = new(cells);
        }

        private (int, int) NextCell((int, int) cell)
        {
            switch (direction)
            {
                case Direction.UpRight:
                    cell = (cell.Item1 - 1, cell.Item2 + 1);
                    break;
                case Direction.UpLeft:
                    cell = (cell.Item1 - 1, cell.Item2 - 1);
                    break;
                case Direction.DownRight:
                    cell = (cell.Item1 + 1, cell.Item2 + 1);
                    break;
                case Direction.DownLeft:
                    cell = (cell.Item1 + 1, cell.Item2 - 1);
                    break;
            }
            return cell;
        }

        private (int, int) PrevCell((int, int) cell)
        {
            switch (direction)
            {
                case Direction.UpRight:
                    cell = (cell.Item1 + 1, cell.Item2 - 1);
                    break;
                case Direction.UpLeft:
                    cell = (cell.Item1 + 1, cell.Item2 + 1);
                    break;
                case Direction.DownRight:
                    cell = (cell.Item1 - 1, cell.Item2 - 1);
                    break;
                case Direction.DownLeft:
                    cell = (cell.Item1 - 1, cell.Item2 + 1);
                    break;
            }
            return cell;
        }

        public override string SpecificName => $"Little Killer at {CellName(cellStart)}";

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            bool definitelyNotGroup = false;
            for (int i0 = 0; i0 < cellsList.Count - 1; i0++)
            {
                if (definitelyNotGroup)
                {
                    break;
                }

                var cell0 = cellsList[i0];
                var seen = sudokuSolver.SeenCells(cell0);
                for (int i1 = i0 + 1; i1 < cellsList.Count; i1++)
                {
                    var cell1 = cellsList[i1];
                    if (!seen.Contains(cell1))
                    {
                        definitelyNotGroup = true;
                        break;
                    }
                }
            }
            if (!definitelyNotGroup)
            {
                isGroup = true;
                KillerCageConstraint.InitCombinations(MAX_VALUE, sum, cellsList.Count, out sumCombinations, out possibleValues);
            }

            var board = sudokuSolver.Board;

            if (isGroup && possibleValues != null && possibleValues.Count < MAX_VALUE)
            {
                return KillerCageConstraint.InitCandidates(sudokuSolver, cellsList, possibleValues);
            }

            bool changed = false;
            int maxValue = sum - cells.Count + 1;
            if (maxValue < MAX_VALUE)
            {
                uint maxValueMask = (1u << maxValue) - 1;
                foreach (var cell in cells)
                {
                    uint cellMask = board[cell.Item1, cell.Item2];
                    uint newCellMask = cellMask & maxValueMask;
                    if (newCellMask == 0)
                    {
                        return LogicResult.Invalid;
                    }

                    if (newCellMask != cellMask)
                    {
                        board[cell.Item1, cell.Item2] = newCellMask;
                        changed = true;
                    }
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (!cells.Contains((i, j)))
            {
                return true;
            }

            if (cells.All(cell => sudokuSolver.IsValueSet(cell.Item1, cell.Item2)))
            {
                var board = sudokuSolver.Board;
                int actualSum = cells.Select(cell => GetValue(board[cell.Item1, cell.Item2])).Sum();
                return sum == actualSum;
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            if (isGroup && sumCombinations != null & sumCombinations.Count > 0)
            {
                return KillerCageConstraint.StepLogic(sudokuSolver, sum, cellsList, sumCombinations, logicalStepDescription, isBruteForcing);
            }

            var board = sudokuSolver.Board;
            var cellMasks = cells.Select(cell => board[cell.Item1, cell.Item2]);

            int setValueSum = cellMasks.Where(mask => IsValueSet(mask)).Select(mask => GetValue(mask)).Sum();
            if (setValueSum > sum)
            {
                logicalStepDescription?.Append($"Sum of filled values is too large.");
                return LogicResult.Invalid;
            }

            // Ensure the sum is still possible
            var unsetMasks = cellMasks.Where(mask => !IsValueSet(mask)).ToArray();
            if (unsetMasks.Length == 0)
            {
                if (setValueSum != sum)
                {
                    logicalStepDescription?.Append($"Sum of values is incorrect.");
                    return LogicResult.Invalid;
                }
            }
            else if (unsetMasks.Length == 1)
            {
                int exactCellValue = sum - setValueSum;
                if (exactCellValue <= 0 || exactCellValue > MAX_VALUE || !HasValue(unsetMasks[0], exactCellValue))
                {
                    logicalStepDescription?.Append($"The final cell cannot fulfill the sum.");
                    return LogicResult.Invalid;
                }
            }
            else
            {
                int minSum = setValueSum + unsetMasks.Select(mask => MinValue(mask)).Sum();
                int maxSum = setValueSum + unsetMasks.Select(mask => MaxValue(mask)).Sum();
                if (minSum > sum || maxSum < sum)
                {
                    logicalStepDescription?.Append($"The sum is no longer possible.");
                    return LogicResult.Invalid;
                }
            }

            return LogicResult.None;
        }
    }
}
