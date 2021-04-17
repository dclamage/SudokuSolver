using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Killer Cage", ConsoleName = "killer", FPuzzlesName = "killercage")]
    public class KillerCageConstraint : Constraint
    {
        private readonly List<(int, int)> cells;
        private readonly int sum;
        private List<List<int>> sumCombinations = null;
        private HashSet<int> possibleValues = null;

        private static readonly Regex optionsRegex = new(@"(\d+);(.*)");

        public KillerCageConstraint(string options)
        {
            var match = optionsRegex.Match(options);
            if (match.Success)
            {
                sum = int.Parse(match.Groups[1].Value);
                options = match.Groups[2].Value;
            }
            else
            {
                // No sum provided
                sum = 0;
            }

            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 1)
            {
                throw new ArgumentException($"Killer cage expects 1 cell group, got {cellGroups.Count} groups.");
            }
            cells = cellGroups[0];
            InitCombinations();
        }

        public override string SpecificName => sum > 0 ? $"Killer Cage {sum} at {CellName(cells[0])}" : $"Killer Cage at {CellName(cells[0])}";

        public static void InitCombinations(int sum, int numCells, out List<List<int>> sumCombinations, out HashSet<int> possibleValues)
        {
            const int allValueSum = (MAX_VALUE * (MAX_VALUE + 1)) / 2;
            if (sum > 0 && sum < allValueSum)
            {
                sumCombinations = new();
                possibleValues = new();
                foreach (var combination in Enumerable.Range(1, MAX_VALUE).Combinations(numCells))
                {
                    if (combination.Sum() == sum)
                    {
                        sumCombinations.Add(combination);
                        foreach (int value in combination)
                        {
                            possibleValues.Add(value);
                        }
                    }
                }
            }
            else
            {
                sumCombinations = null;
                possibleValues = null;
            }
        }

        private void InitCombinations() =>
            InitCombinations(sum, cells.Count, out sumCombinations, out possibleValues);

        public static LogicResult InitCandidates(Solver sudokuSolver, List<(int, int)> cells, HashSet<int> possibleValues)
        {
            LogicResult result = LogicResult.None;
            if (possibleValues != null && possibleValues.Count < MAX_VALUE)
            {
                var board = sudokuSolver.Board;
                for (int v = 1; v <= 9; v++)
                {
                    if (!possibleValues.Contains(v))
                    {
                        uint valueMask = ValueMask(v);
                        foreach (var cell in cells)
                        {
                            uint cellMask = board[cell.Item1, cell.Item2];
                            if ((cellMask & valueMask) != 0)
                            {
                                if (!sudokuSolver.ClearValue(cell.Item1, cell.Item2, v))
                                {
                                    return LogicResult.Invalid;
                                }
                                result = LogicResult.Changed;
                            }
                        }
                    }
                }
            }
            return result;
        }

        public override LogicResult InitCandidates(Solver sudokuSolver) => InitCandidates(sudokuSolver, cells, possibleValues);

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            // Determine if the sum is now complete
            if (sum != 0 && cells.Contains((i, j)) && cells.All(cell => sudokuSolver.IsValueSet(cell.Item1, cell.Item2)))
            {
                return cells.Select(cell => sudokuSolver.GetValue(cell)).Sum() == sum;
            }
            return true;
        }

        public static LogicResult StepLogic(Solver sudokuSolver, int sum, List<(int, int)> cells, List<List<int>> sumCombinations, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            bool changed = false;
            if (sumCombinations == null || sumCombinations.Count == 0)
            {
                return LogicResult.None;
            }

            // Reduce the remaining cell options
            int numUnset = 0;
            int setSum = 0;
            uint valueUsedMask = 0;
            uint valuePresentMask = 0;
            List<List<int>> validCombinations = sumCombinations.ToList();
            var board = sudokuSolver.Board;
            foreach (var curCell in cells)
            {
                uint cellMask = board[curCell.Item1, curCell.Item2];
                valuePresentMask |= (cellMask & ~valueSetMask);
                if (IsValueSet(cellMask))
                {
                    int curValue = GetValue(cellMask);
                    setSum += curValue;
                    validCombinations.RemoveAll(list => !list.Contains(curValue));
                    valueUsedMask |= (cellMask & ~valueSetMask);
                }
                else
                {
                    numUnset++;
                }
            }

            // Remove combinations which require a value which isn't present
            validCombinations.RemoveAll(list => list.Any(v => (valuePresentMask & ValueMask(v)) == 0));

            if (validCombinations.Count == 0)
            {
                // Sum is no longer possible
                logicalStepDescription?.Append($"No more valid combinations which sum to {sum}.");
                return LogicResult.Invalid;
            }

            if (numUnset > 0)
            {
                uint valueRemainingMask = 0;
                foreach (var combination in validCombinations)
                {
                    foreach (int v in combination)
                    {
                        valueRemainingMask |= ValueMask(v);
                    }
                }
                valueRemainingMask &= ~valueUsedMask;

                var unsetCells = cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();
                var unsetCombinations = validCombinations.Select(list => list.Where(v => (valueUsedMask & ValueMask(v)) == 0).ToList()).ToList();
                var unsetCellCurMasks = unsetCells.Select(cell => board[cell.Item1, cell.Item2]).ToList();
                uint[] unsetCellNewMasks = new uint[unsetCells.Count];
                foreach (var curCombination in unsetCombinations)
                {
                    foreach (var permutation in curCombination.Permuatations())
                    {
                        bool permutationValid = true;
                        for (int i = 0; i < permutation.Count; i++)
                        {
                            uint cellMask = unsetCellCurMasks[i];
                            uint permValueMask = ValueMask(permutation[i]);
                            if ((cellMask & permValueMask) == 0)
                            {
                                permutationValid = false;
                                break;
                            }
                        }

                        if (permutationValid)
                        {
                            for (int i = 0; i < permutation.Count; i++)
                            {
                                unsetCellNewMasks[i] |= ValueMask(permutation[i]);
                            }
                        }
                    }
                }

                for (int i = 0; i < unsetCells.Count; i++)
                {
                    var curCell = unsetCells[i];
                    uint cellMask = board[curCell.Item1, curCell.Item2];
                    uint newCellMask = unsetCellNewMasks[i];
                    if (newCellMask != cellMask)
                    {
                        changed = true;
                        if (!sudokuSolver.SetMask(curCell.Item1, curCell.Item2, newCellMask))
                        {
                            // Cell has no values remaining
                            logicalStepDescription?.Append($"{CellName(curCell)} has no more remaining values.");
                            return LogicResult.Invalid;
                        }
                        if (logicalStepDescription != null)
                        {
                            if (logicalStepDescription.Length > 0)
                            {
                                logicalStepDescription.Append(", ");
                            }
                            logicalStepDescription.Append($"{CellName(curCell)} reduced to: {MaskToString(newCellMask)}");
                        }
                    }
                }
            }
            else
            {
                // Ensure the sum is correct
                if (setSum != sum)
                {
                    logicalStepDescription?.Append($"Sums to {setSum} instead of {sum}.");
                    return LogicResult.Invalid;
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) =>
            StepLogic(sudokuSolver, sum, cells, sumCombinations, logicalStepDescription, isBruteForcing);

        public override List<(int, int)> Group => cells;
    }
}
