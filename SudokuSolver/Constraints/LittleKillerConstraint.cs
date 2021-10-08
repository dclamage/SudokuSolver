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

        private class SumGroup
        {
            public SumGroup(LittleKillerConstraint constraint, List<(int, int)> cells)
            {
                this.constraint = constraint;
                this.cells = cells;
            }

            public (int, int) MinMaxSum(Solver solver)
            {
                var board = solver.Board;

                var unsetCells = cells;
                int setSum = SetSum(solver);
                if (setSum > 0)
                {
                    unsetCells = cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();
                }

                if (unsetCells.Count == 0)
                {
                    return (setSum, setSum);
                }

                uint unsetMask = UnsetMask(solver);
                if (unsetCells.Count == 1)
                {
                    return (setSum + MinValue(unsetMask), setSum + MaxValue(unsetMask));
                }

                int minValue = MinValue(unsetMask);
                int maxValue = MaxValue(unsetMask);
                List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

                int min = 0;
                foreach (var combination in possibleVals.Combinations(unsetCells.Count))
                {
                    int curSum = setSum + combination.Sum();
                    if (min == 0)
                    {
                        if (solver.CanPlaceDigitsAnyOrder(unsetCells, combination))
                        {
                            min = curSum;
                        }
                    }
                    else if (min > curSum)
                    {
                        if (solver.CanPlaceDigitsAnyOrder(unsetCells, combination))
                        {
                            min = curSum;
                        }
                    }
                }
                if (min == 0)
                {
                    return (0, 0);
                }

                int max = min;
                List<(List<int> combination, int sum)> potentialCombinations = new();
                foreach (var combination in possibleVals.Combinations(unsetCells.Count))
                {
                    int curSum = setSum + combination.Sum();
                    if (max < curSum)
                    {
                        potentialCombinations.Add((combination.ToList(), curSum));
                    }
                }
                potentialCombinations.Sort((a, b) => b.sum - a.sum);
                foreach (var (combination, curSum) in potentialCombinations)
                {
                    if (solver.CanPlaceDigitsAnyOrder(unsetCells, combination))
                    {
                        max = curSum;
                        break;
                    }
                }

                return (min, max);
            }

            public LogicResult RestrictSum(Solver solver, int minSum, int maxSum)
            {
                var board = solver.Board;

                var unsetCells = cells;
                int setSum = SetSum(solver);
                if (setSum > 0)
                {
                    unsetCells = cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();
                }

                int numUnsetCells = unsetCells.Count;
                if (numUnsetCells == 0)
                {
                    return setSum >= minSum && setSum <= maxSum ? LogicResult.None : LogicResult.Invalid;
                }

                if (numUnsetCells == 1)
                {
                    var unsetCell = unsetCells[0];
                    uint curMask = board[unsetCell.Item1, unsetCell.Item2];
                    uint newMask = curMask & constraint.MaskBetweenInclusive(minSum - setSum, maxSum - setSum);
                    if (curMask != newMask)
                    {
                        board[unsetCell.Item1, unsetCell.Item2] = newMask;
                        return newMask != 0 ? LogicResult.Changed : LogicResult.Invalid;
                    }
                    return LogicResult.None;
                }

                uint unsetMask = UnsetMask(solver);
                int minValue = MinValue(unsetMask);
                int maxValue = MaxValue(unsetMask);
                List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

                uint[] newMasks = new uint[numUnsetCells];
                foreach (var combination in possibleVals.Combinations(unsetCells.Count))
                {
                    int curSum = setSum + combination.Sum();
                    if (curSum >= minSum && curSum <= maxSum)
                    {
                        foreach (var perm in combination.Permuatations())
                        {
                            bool needCheck = false;
                            for (int i = 0; i < numUnsetCells; i++)
                            {
                                uint valueMask = ValueMask(perm[i]);
                                if ((newMasks[i] & valueMask) == 0)
                                {
                                    needCheck = true;
                                    break;
                                }
                            }

                            if (needCheck && solver.CanPlaceDigits(unsetCells, perm))
                            {
                                for (int i = 0; i < numUnsetCells; i++)
                                {
                                    uint valueMask = ValueMask(perm[i]);
                                    newMasks[i] |= valueMask;
                                }
                            }
                        }
                    }
                }

                bool changed = false;
                for (int i = 0; i < numUnsetCells; i++)
                {
                    var unsetCell = unsetCells[i];
                    uint curMask = board[unsetCell.Item1, unsetCell.Item2];
                    uint newMask = curMask & newMasks[i];
                    if (curMask != newMask)
                    {
                        board[unsetCell.Item1, unsetCell.Item2] = newMask;
                        if (newMask == 0)
                        {
                            return LogicResult.Invalid;
                        }
                        changed = true;
                    }
                }
                return changed ? LogicResult.Changed : LogicResult.None;
            }

            public uint UnsetMask(Solver solver)
            {
                var board = solver.Board;
                uint combMask = 0;
                foreach (var cell in cells)
                {
                    uint mask = board[cell.Item1, cell.Item2];
                    if (!IsValueSet(mask))
                    {
                        combMask |= mask;
                    }
                }
                return combMask;
            }

            public int SetSum(Solver solver)
            {
                var board = solver.Board;
                int sum = 0;
                foreach (var cell in cells)
                {
                    uint mask = board[cell.Item1, cell.Item2];
                    if (IsValueSet(mask))
                    {
                        sum += GetValue(mask);
                    }
                }
                return sum;
            }

            private readonly LittleKillerConstraint constraint;
            public readonly List<(int, int)> cells;
        }

        public readonly (int, int) outerCell;
        public readonly Direction direction;
        public readonly int sum;
        private readonly (int, int) cellStart;
        private readonly HashSet<(int, int)> cells;
        private readonly List<(int, int)> cellsList;
        private List<SumGroup> groups = null;
        private bool isGroup = false;
        private List<List<int>> sumCombinations = null;
        private HashSet<int> possibleValues = null;

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
            if (cellsList.Count == 0)
            {
                return LogicResult.None;
            }

            if (groups == null)
            {
                groups = sudokuSolver.SplitIntoGroups(cellsList).Select(g => new SumGroup(this, g)).ToList();
            }

            if (groups.Count == 1)
            {
                isGroup = true;
            }

            if (isGroup)
            {
                KillerCageConstraint.InitCombinations(MAX_VALUE, sum, cellsList.Count, out sumCombinations, out possibleValues);
            }

            var board = sudokuSolver.Board;

            if (isGroup && possibleValues != null && possibleValues.Count < MAX_VALUE)
            {
                return KillerCageConstraint.InitCandidates(sudokuSolver, cellsList, possibleValues);
            }

            int minSum = 0;
            int maxSum = 0;
            List<(SumGroup group, int min, int max)> groupMinMax = new(groups.Count);
            foreach (var curGroup in groups)
            {
                var (curMin, curMax) = curGroup.MinMaxSum(sudokuSolver);
                if (curMin == 0 || curMax == 0)
                {
                    return LogicResult.Invalid;
                }

                minSum += curMin;
                maxSum += curMax;

                groupMinMax.Add((curGroup, curMin, curMax));
            }

            if (minSum > sum || maxSum < sum)
            {
                return LogicResult.Invalid;
            }

            // Each group can increase from its min by the minDof
            // and decrease from its max by the maxDof
            bool changed = false;
            int minDof = sum - minSum;
            int maxDof = maxSum - sum;
            
            foreach (var (group, groupMin, groupMax) in groupMinMax)
            {
                if (groupMin == groupMax)
                {
                    continue;
                }

                int newGroupMin = Math.Max(groupMin, groupMax - maxDof);
                int newGroupMax = Math.Min(groupMax, groupMin + minDof);

                if (newGroupMin > groupMin || newGroupMax < groupMax)
                {
                    var logicResult = group.RestrictSum(sudokuSolver, newGroupMin, newGroupMax);
                    if (logicResult == LogicResult.Invalid)
                    {
                        return LogicResult.Invalid;
                    }

                    if (logicResult == LogicResult.Changed)
                    {
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

            int incompleteSum = sum;
            int numIncompleteGroups = 0;
            int minSum = 0;
            int maxSum = 0;
            List<(SumGroup group, int min, int max)> groupMinMax = new(groups.Count);
            foreach (var curGroup in groups)
            {
                var (curMin, curMax) = curGroup.MinMaxSum(sudokuSolver);
                if (curMin == 0 || curMax == 0)
                {
                    logicalStepDescription?.Append($"{sudokuSolver.CompactName(curGroup.cells)} has no valid candidate combination.");
                    return LogicResult.Invalid;
                }

                minSum += curMin;
                maxSum += curMax;

                if (curMin != curMax)
                {
                    numIncompleteGroups++;
                }
                else
                {
                    incompleteSum -= curMin;
                }

                groupMinMax.Add((curGroup, curMin, curMax));
            }

            if (minSum > sum || maxSum < sum)
            {
                logicalStepDescription?.Append($"Sum is no longer possible (Between {minSum} and {maxSum}).");
                return LogicResult.Invalid;
            }

            if (numIncompleteGroups == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;

            if (numIncompleteGroups == 1)
            {
                // One group left means it must exactly sum to whatever sum is remaining
                var incompleteGroup = groupMinMax.First(g => g.min != g.max).group;

                int numCells = incompleteGroup.cells.Count;
                uint[] oldMasks = null;
                if (logicalStepDescription != null)
                {
                    oldMasks = new uint[numCells];
                    for (int i = 0; i < numCells; i++)
                    {
                        var cell = incompleteGroup.cells[i];
                        oldMasks[i] = board[cell.Item1, cell.Item2];
                    }
                }

                var logicResult = incompleteGroup.RestrictSum(sudokuSolver, incompleteSum, incompleteSum);
                if (logicResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Append($"{sudokuSolver.CompactName(incompleteGroup.cells)} cannot sum to exactly {incompleteSum}.");
                    return LogicResult.Invalid;
                }

                if (logicResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        List<int> elims = new();
                        for (int i = 0; i < numCells; i++)
                        {
                            var cell = incompleteGroup.cells[i];
                            uint removedMask = oldMasks[i] & ~board[cell.Item1, cell.Item2];
                            if (removedMask != 0)
                            {
                                for (int v = 1; v <= MAX_VALUE; v++)
                                {
                                    if ((removedMask & ValueMask(v)) != 0)
                                    {
                                        elims.Add(sudokuSolver.CandidateIndex(cell, v));
                                    }
                                }
                            }
                        }

                        logicalStepDescription.Append($"{sudokuSolver.CompactName(incompleteGroup.cells)} restricted to sum {incompleteSum}: {sudokuSolver.DescribeElims(elims)}");
                    }
                    return LogicResult.Changed;
                }
            }
            else
            {
                // Each group can increase from its min by the minDof
                // and decrease from its max by the maxDof
                bool changed = false;
                int minDof = sum - minSum;
                int maxDof = maxSum - sum;

                List<int> elims = logicalStepDescription != null ? new() : null;
                foreach (var (group, groupMin, groupMax) in groupMinMax)
                {
                    if (groupMin == groupMax)
                    {
                        continue;
                    }

                    int newGroupMin = Math.Max(groupMin, groupMax - maxDof);
                    int newGroupMax = Math.Min(groupMax, groupMin + minDof);

                    if (newGroupMin > groupMin || newGroupMax < groupMax)
                    {
                        int numCells = group.cells.Count;
                        uint[] oldMasks = null;
                        if (logicalStepDescription != null)
                        {
                            oldMasks = new uint[numCells];
                            for (int i = 0; i < numCells; i++)
                            {
                                var cell = group.cells[i];
                                oldMasks[i] = board[cell.Item1, cell.Item2];
                            }
                        }

                        var logicResult = group.RestrictSum(sudokuSolver, newGroupMin, newGroupMax);
                        if (logicResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"{sudokuSolver.CompactName(group.cells)} cannot be restricted between {newGroupMin} and {newGroupMax}.");
                            return LogicResult.Invalid;
                        }

                        if (logicResult == LogicResult.Changed)
                        {
                            if (logicalStepDescription != null)
                            {
                                for (int i = 0; i < numCells; i++)
                                {
                                    var cell = group.cells[i];
                                    uint removedMask = oldMasks[i] & ~board[cell.Item1, cell.Item2];
                                    if (removedMask != 0)
                                    {
                                        for (int v = 1; v <= MAX_VALUE; v++)
                                        {
                                            if ((removedMask & ValueMask(v)) != 0)
                                            {
                                                elims.Add(sudokuSolver.CandidateIndex(cell, v));
                                            }
                                        }
                                    }
                                }
                            }
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    logicalStepDescription?.Append($"Sum re-evaluated: {sudokuSolver.DescribeElims(elims)}");
                    return LogicResult.Changed;
                }
            }
            return LogicResult.None;
        }
    }
}
