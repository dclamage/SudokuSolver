using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Sandwich", ConsoleName = "sandwich", FPuzzlesName = "sandwichsum")]
    public class SandwichConstraint : Constraint
    {
        private readonly int sum;
        private readonly (int, int) cellStart;
        private readonly List<(int, int)> cells;
        private readonly HashSet<(int, int)> cellsLookup;
        private int minFillingLength = 0;
        private int maxFillingLength = 0;
        private readonly uint crustsMask = 0;
        private readonly uint nonCrustsMask = 0;
        private uint fillingMask = 0;
        private string specificName;

        public override string SpecificName => specificName;

        private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
        public SandwichConstraint(string options)
        {
            var match = optionsRegex.Match(options);
            if (!match.Success)
            {
                throw new ArgumentException($"Sandwich options \"{options}\" invalid. Expecting: \"SrXcY\"");
            }

            sum = int.Parse(match.Groups[1].Value);
            cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

            bool isCol = cellStart.Item1 < 0 || cellStart.Item1 >= MAX_VALUE;
            bool isRow = cellStart.Item2 < 0 || cellStart.Item2 >= MAX_VALUE;

            if (isRow && isCol || !isRow && !isCol)
            {
                throw new ArgumentException($"Sandwich options \"{options}\" has invalid location.");
            }

            cells = new();
            if (isRow)
            {
                int i = cellStart.Item1;
                for (int j = 0; j < WIDTH; j++)
                {
                    cells.Add((i, j));
                }
            }
            else
            {
                int j = cellStart.Item2;
                for (int i = 0; i < HEIGHT; i++)
                {
                    cells.Add((i, j));
                }
            }
            cellsLookup = new(cells);

            crustsMask = ValueMask(1) | ValueMask(MAX_VALUE);
            nonCrustsMask = ALL_VALUES_MASK & ~crustsMask;

            specificName = $"Sandwich sum {sum} at {CellName(cellStart)}";
        }

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            bool changed = false;
            int numCells = cells.Count;
            fillingMask = ALL_VALUES_MASK & ~crustsMask;
            minFillingLength = 0;
            maxFillingLength = 0;

            const int allValueSum = (MAX_VALUE * (MAX_VALUE + 1)) / 2 - (1 + MAX_VALUE);
            if (sum < 0 || sum > allValueSum)
            {
                return LogicResult.Invalid;
            }

            fillingMask = 0;
            for (int curFillingLength = 1; curFillingLength < numCells - 2; curFillingLength++)
            {
                try
                {
                    foreach (var combination in Enumerable.Range(2, MAX_VALUE - 2).Combinations(curFillingLength))
                    {
                        if (combination.Sum() != sum)
                        {
                            continue;
                        }

                        if (minFillingLength == 0)
                        {
                            minFillingLength = curFillingLength;
                            maxFillingLength = curFillingLength;
                        }
                        else
                        {
                            maxFillingLength = curFillingLength;
                        }

                        foreach (int value in combination)
                        {
                            fillingMask |= ValueMask(value);
                        }
                    }
                }
                catch (InvalidOperationException) { }
            }

            uint[] keepMasks = new uint[numCells];
            int lastLeftCrust = numCells - minFillingLength - 2;
            for (int leftCrust = 0; leftCrust <= lastLeftCrust; leftCrust++)
            {
                for (int curFillingLength = minFillingLength; curFillingLength <= maxFillingLength; curFillingLength++)
                {
                    int rightCrust = leftCrust + curFillingLength + 1;
                    if (rightCrust >= numCells)
                    {
                        break;
                    }

                    // Mark the crusts
                    keepMasks[leftCrust] |= crustsMask;
                    keepMasks[rightCrust] |= crustsMask;

                    // Mark the left outies
                    for (int cellIndex = 0; cellIndex < leftCrust; cellIndex++)
                    {
                        keepMasks[cellIndex] |= nonCrustsMask;
                    }

                    // Mark the filling
                    for (int cellIndex = leftCrust + 1; cellIndex < rightCrust; cellIndex++)
                    {
                        keepMasks[cellIndex] |= fillingMask;
                    }

                    // Mark the right outies
                    for (int cellIndex = rightCrust + 1; cellIndex < numCells; cellIndex++)
                    {
                        keepMasks[cellIndex] |= nonCrustsMask;
                    }
                }
            }

            for (int i = 0; i < numCells; i++)
            {
                var cell = cells[i];
                uint clearMask = ALL_VALUES_MASK & ~keepMasks[i];
                var logicResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= logicResult == LogicResult.Changed;
            }

            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (!cellsLookup.Contains((i, j)))
            {
                return true;
            }

            var board = sudokuSolver.Board;
            (int crustIndex0, int crustIndex1) = GetCrustIndices(sudokuSolver);

            // Nothing to validate until the crusts are filled
            if (crustIndex1 == -1)
            {
                return true;
            }

            int fillingSize = crustIndex1 - crustIndex0 - 1;
            if (fillingSize == 0)
            {
                return sum == 0;
            }

            uint notCrustMask = ALL_VALUES_MASK & ~crustsMask;
            uint possibleFillingMask = 0;
            for (int cellIndex = crustIndex0 + 1; cellIndex < crustIndex1; cellIndex++)
            {
                var curCell = cells[cellIndex];
                possibleFillingMask |= board[curCell.Item1, curCell.Item2] & notCrustMask;
            }

            if (ValueCount(possibleFillingMask) < fillingSize)
            {
                return false;
            }

            int minSum = 0;
            int numValsSummed = 0;
            for (int curVal = 2; numValsSummed < fillingSize && curVal <= MAX_VALUE - 1; curVal++)
            {
                uint curMask = ValueMask(curVal);
                if ((possibleFillingMask & curMask) == 0)
                {
                    continue;
                }
                minSum += curVal;
                numValsSummed++;
            }
            if (sum < minSum)
            {
                return false;
            }

            int maxSum = 0;
            numValsSummed = 0;
            for (int curVal = MAX_VALUE - 1; numValsSummed < fillingSize && curVal > 1; curVal--)
            {
                uint curMask = ValueMask(curVal);
                if ((possibleFillingMask & curMask) == 0)
                {
                    continue;
                }
                maxSum += curVal;
                numValsSummed++;
            }
            if (sum > maxSum)
            {
                return false;
            }

            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var board = sudokuSolver.Board;
            (int crustIndex0, int crustIndex1) = GetCrustIndices(sudokuSolver);
            if (crustIndex1 != -1)
            {
                // Both crust locations are known
                int fillingSize = crustIndex1 - crustIndex0 - 1;
                if (fillingSize == 0)
                {
                    if (sum == 0)
                    {
                        return LogicResult.None;
                    }

                    logicalStepDescription?.Append($"Sandwich sum does not match (sum is 0, expected {sum})");
                    return LogicResult.Invalid;
                }

                int knownSum = 0;
                List<(int, int)> unsetCells = new();
                for (int cellIndex = crustIndex0 + 1; cellIndex < crustIndex1; cellIndex++)
                {
                    var (i, j) = cells[cellIndex];
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        knownSum += GetValue(mask);
                    }
                    else
                    {
                        unsetCells.Add((i, j));
                    }
                }

                if (unsetCells.Count == 0)
                {
                    if (sum != knownSum)
                    {
                        logicalStepDescription?.Append($"Sandwich sum does not match (sum is {knownSum}, expected {sum})");
                        return LogicResult.Invalid;
                    }
                    return LogicResult.None;
                }

                if (knownSum >= sum)
                {
                    logicalStepDescription?.Append($"Sandwich sum does not match (sum is strictly greater than {knownSum}, expected {sum})");
                    return LogicResult.Invalid;
                }

                int numUnsetCells = unsetCells.Count;
                int remainingSum = sum - knownSum;

                uint possibleValuesMask = 0;
                foreach (var (i, j) in unsetCells)
                {
                    uint mask = board[i, j];
                    possibleValuesMask |= mask;
                }
                possibleValuesMask &= nonCrustsMask;

                List<int> possibleValues = new(ValueCount(possibleValuesMask));
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if ((possibleValuesMask & ValueMask(v)) != 0)
                    {
                        possibleValues.Add(v);
                    }
                }

                if (possibleValues.Count < numUnsetCells)
                {
                    logicalStepDescription?.Append($"Remaining sandwich sum values {MaskToString(possibleValuesMask)} do not fit into {numUnsetCells} remaining cells.");
                    return LogicResult.Invalid;
                }

                uint[] fillingKeepMasks = new uint[fillingSize];
                foreach (var combination in possibleValues.Combinations(numUnsetCells))
                {
                    if (combination.Sum() != remainingSum)
                    {
                        continue;
                    }

                    foreach (var permuatation in combination.Permuatations())
                    {
                        if (sudokuSolver.CanPlaceDigits(unsetCells, permuatation))
                        {
                            for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                            {
                                fillingKeepMasks[cellIndex] |= ValueMask(permuatation[cellIndex]);
                            }
                        }
                    }
                }

                return ApplyKeepMask(sudokuSolver, fillingKeepMasks, unsetCells, logicalStepDescription);
            }

            int numCells = cells.Count;
            uint[] keepMasks = new uint[numCells];
            if (crustIndex0 != -1)
            {
                // Only one crust location is known
                for (int fillingSize = minFillingLength; fillingSize <= maxFillingLength; fillingSize++)
                {
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        int curCrustIndex0 = dir == -1 ? crustIndex0 - fillingSize - 1 : crustIndex0;
                        int curCrustIndex1 = curCrustIndex0 + fillingSize + 1;
                        if (curCrustIndex0 < 0 || curCrustIndex1 >= numCells)
                        {
                            continue;
                        }
                        var crustCell0 = cells[curCrustIndex0];
                        var crustCell1 = cells[curCrustIndex1];
                        uint crustMask0 = board[crustCell0.Item1, crustCell0.Item2];
                        uint crustMask1 = board[crustCell1.Item1, crustCell1.Item2];
                        uint bothCrustsMask = crustMask0 | crustMask1;
                        if ((bothCrustsMask & crustsMask) != crustsMask)
                        {
                            continue;
                        }

                        bool haveValidPlacement = false;
                        if (fillingSize == 0)
                        {
                            haveValidPlacement = true;
                        }
                        else
                        {
                            int knownSum = 0;
                            List<int> unsetCellIndices = new();
                            List<(int, int)> unsetCells = new();
                            for (int cellIndex = curCrustIndex0 + 1; cellIndex < curCrustIndex1; cellIndex++)
                            {
                                var (i, j) = cells[cellIndex];
                                uint mask = board[i, j];
                                if (IsValueSet(mask))
                                {
                                    knownSum += GetValue(mask);
                                    keepMasks[cellIndex] |= mask & ~valueSetMask;
                                }
                                else
                                {
                                    unsetCellIndices.Add(cellIndex);
                                    unsetCells.Add((i, j));
                                }
                            }

                            if (unsetCells.Count == 0)
                            {
                                if (sum == knownSum)
                                {
                                    haveValidPlacement = true;
                                }
                            }
                            else if (knownSum < sum)
                            {
                                int numUnsetCells = unsetCells.Count;
                                int remainingSum = sum - knownSum;

                                uint possibleValuesMask = 0;
                                foreach (var (i, j) in unsetCells)
                                {
                                    uint mask = board[i, j];
                                    possibleValuesMask |= mask;
                                }
                                possibleValuesMask &= nonCrustsMask;

                                List<int> possibleValues = new(ValueCount(possibleValuesMask));
                                for (int v = 1; v <= MAX_VALUE; v++)
                                {
                                    if ((possibleValuesMask & ValueMask(v)) != 0)
                                    {
                                        possibleValues.Add(v);
                                    }
                                }

                                if (possibleValues.Count >= numUnsetCells)
                                {
                                    foreach (var combination in possibleValues.Combinations(numUnsetCells))
                                    {
                                        if (combination.Sum() != remainingSum)
                                        {
                                            continue;
                                        }

                                        foreach (var permuatation in combination.Permuatations())
                                        {
                                            if (sudokuSolver.CanPlaceDigits(unsetCells, permuatation))
                                            {
                                                for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                                                {
                                                    keepMasks[unsetCellIndices[cellIndex]] |= ValueMask(permuatation[cellIndex]);
                                                }
                                                haveValidPlacement = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (haveValidPlacement)
                        {
                            uint nonCrustMask = ALL_VALUES_MASK & ~crustsMask;
                            for (int cellIndex = 0; cellIndex < curCrustIndex0; cellIndex++)
                            {
                                keepMasks[cellIndex] |= nonCrustMask;
                            }
                            for (int cellIndex = curCrustIndex1 + 1; cellIndex < numCells; cellIndex++)
                            {
                                keepMasks[cellIndex] |= nonCrustMask;
                            }
                            keepMasks[curCrustIndex0] |= crustsMask;
                            keepMasks[curCrustIndex1] |= crustsMask;
                        }
                    }
                }
            }
            else
            {
                // Neither crust location is known
                for (int curCrustIndex0 = 0; curCrustIndex0 < numCells - minFillingLength - 1; curCrustIndex0++)
                {
                    var crustCell0 = cells[curCrustIndex0];
                    uint crustMask0 = board[crustCell0.Item1, crustCell0.Item2];
                    if ((crustMask0 & crustsMask) == 0)
                    {
                        continue;
                    }

                    for (int fillingSize = minFillingLength; fillingSize <= maxFillingLength; fillingSize++)
                    {
                        int curCrustIndex1 = curCrustIndex0 + fillingSize + 1;
                        if (curCrustIndex1 >= numCells)
                        {
                            break;
                        }

                        var crustCell1 = cells[curCrustIndex1];
                        uint crustMask1 = board[crustCell1.Item1, crustCell1.Item2];
                        uint bothCrustsMask = crustMask0 | crustMask1;
                        if ((bothCrustsMask & crustsMask) != crustsMask)
                        {
                            continue;
                        }

                        bool haveValidPlacement = false;
                        if (fillingSize == 0)
                        {
                            haveValidPlacement = true;
                        }
                        else
                        {
                            int knownSum = 0;
                            List<int> unsetCellIndices = new();
                            List<(int, int)> unsetCells = new();
                            for (int cellIndex = curCrustIndex0 + 1; cellIndex < curCrustIndex1; cellIndex++)
                            {
                                var (i, j) = cells[cellIndex];
                                uint mask = board[i, j];
                                if (IsValueSet(mask))
                                {
                                    knownSum += GetValue(mask);
                                    keepMasks[cellIndex] |= mask & ~valueSetMask;
                                }
                                else
                                {
                                    unsetCellIndices.Add(cellIndex);
                                    unsetCells.Add((i, j));
                                }
                            }

                            if (unsetCells.Count == 0)
                            {
                                if (sum == knownSum)
                                {
                                    haveValidPlacement = true;
                                }
                            }
                            else if (knownSum < sum)
                            {
                                int numUnsetCells = unsetCells.Count;
                                int remainingSum = sum - knownSum;

                                uint possibleValuesMask = 0;
                                foreach (var (i, j) in unsetCells)
                                {
                                    uint mask = board[i, j];
                                    possibleValuesMask |= mask;
                                }
                                possibleValuesMask &= nonCrustsMask;

                                List<int> possibleValues = new(ValueCount(possibleValuesMask));
                                for (int v = 1; v <= MAX_VALUE; v++)
                                {
                                    if ((possibleValuesMask & ValueMask(v)) != 0)
                                    {
                                        possibleValues.Add(v);
                                    }
                                }

                                if (possibleValues.Count >= numUnsetCells)
                                {
                                    foreach (var combination in possibleValues.Combinations(numUnsetCells))
                                    {
                                        if (combination.Sum() != remainingSum)
                                        {
                                            continue;
                                        }

                                        foreach (var permuatation in combination.Permuatations())
                                        {
                                            if (sudokuSolver.CanPlaceDigits(unsetCells, permuatation))
                                            {
                                                for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                                                {
                                                    keepMasks[unsetCellIndices[cellIndex]] |= ValueMask(permuatation[cellIndex]);
                                                }
                                                haveValidPlacement = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (haveValidPlacement)
                        {
                            uint nonCrustMask = ALL_VALUES_MASK & ~crustsMask;
                            for (int cellIndex = 0; cellIndex < curCrustIndex0; cellIndex++)
                            {
                                keepMasks[cellIndex] |= nonCrustMask;
                            }
                            for (int cellIndex = curCrustIndex1 + 1; cellIndex < numCells; cellIndex++)
                            {
                                keepMasks[cellIndex] |= nonCrustMask;
                            }
                            keepMasks[curCrustIndex0] |= crustsMask;
                            keepMasks[curCrustIndex1] |= crustsMask;
                        }
                    }
                }
            }

            return ApplyKeepMask(sudokuSolver, keepMasks, cells, logicalStepDescription);
        }

        private static LogicResult ApplyKeepMask(Solver sudokuSolver, uint[] keepMasks, List<(int, int)> cells, StringBuilder logicalStepDescription)
        {
            bool changed = false;
            var board = sudokuSolver.Board;
            int numCells = cells.Count;
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                uint clearMask = board[i, j] & ~keepMasks[cellIndex] & ~valueSetMask;
                if (clearMask != 0)
                {
                    LogicResult logicResult = sudokuSolver.ClearMask(i, j, clearMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"The sandwich sum cannot be fulfilled (such as in {CellName(i, j)}).");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        if (logicalStepDescription != null)
                        {
                            if (!changed)
                            {
                                if (sudokuSolver.IsValueSet(i, j))
                                {
                                    logicalStepDescription.Append($"Set {CellName(i, j)} to {sudokuSolver.GetValue((i, j))}");
                                }
                                else
                                {
                                    logicalStepDescription.Append($"Removed {MaskToString(clearMask)} from {CellName(i, j)}");
                                }
                            }
                            else
                            {
                                if (sudokuSolver.IsValueSet(i, j))
                                {
                                    logicalStepDescription.Append($"; set {CellName(i, j)} to {sudokuSolver.GetValue((i, j))}");
                                }
                                else
                                {
                                    logicalStepDescription.Append($"; removed {MaskToString(clearMask)} from {CellName(i, j)}");
                                }
                            }
                        }
                        changed = true;
                    }
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private (int, int) GetCrustIndices(Solver sudokuSolver)
        {
            var board = sudokuSolver.Board;
            int numCells = cells.Count;
            int crustIndex0 = -1;
            int crustIndex1 = -1;
            uint notCrustMask = ALL_VALUES_MASK & ~crustsMask;
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                var curCell = cells[cellIndex];
                uint mask = board[curCell.Item1, curCell.Item2];
                if ((mask & notCrustMask) == 0)
                {
                    if (crustIndex0 == -1)
                    {
                        crustIndex0 = cellIndex;
                    }
                    else
                    {
                        crustIndex1 = cellIndex;
                        break;
                    }
                }
            }
            return (crustIndex0, crustIndex1);
        }
    }
}
