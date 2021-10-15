using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver
{
    public class SumGroup
    {
        public SumGroup(Solver solver, List<(int, int)> cells)
        {
            this.cells = cells.OrderBy(cell => cell.Item1 * solver.WIDTH + cell.Item2).ToList();
            cellsString = CellsKey("SumGroup", this.cells);
        }

        record MinMaxMemo(int Min, int Max);

        public (int, int) MinMaxSum(Solver solver)
        {
            // Trivial case of max number of cells
            if (cells.Count == solver.MAX_VALUE)
            {
                int sum = (solver.MAX_VALUE * (solver.MAX_VALUE + 1)) / 2;
                return (sum, sum);
            }

            // Check for a memo
            string memoKey = new StringBuilder()
                .Append(cellsString)
                .Append("|MinMax")
                .AppendCellValueKey(solver, cells)
                .ToString();
            var minMaxMemo = solver.GetMemo<MinMaxMemo>(memoKey);
            if (minMaxMemo != null)
            {
                return (minMaxMemo.Min, minMaxMemo.Max);
            }

            var minMax = CalcMinMaxSum(solver);
            solver.StoreMemo(memoKey, new MinMaxMemo(minMax.Item1, minMax.Item2));
            return minMax;
        }

        private (int, int) CalcMinMaxSum(Solver solver)
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

            // Exactly the correct number of values in the unset cells so its sum is exact
            if (ValueCount(unsetMask) == unsetCells.Count)
            {
                int unsetSum = 0;
                for (int v = 1; v <= solver.MAX_VALUE; v++)
                {
                    if ((unsetMask & ValueMask(v)) != 0)
                    {
                        unsetSum += v;
                    }
                }
                return (setSum + unsetSum, setSum + unsetSum);
            }

            // Only one unset cell, so use its range
            if (unsetCells.Count == 1)
            {
                return (setSum + MinValue(unsetMask), setSum + MaxValue(unsetMask));
            }

            // Determine all possible placeable sums and return that range
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
                else if (curSum < min)
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
                if (curSum > max)
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

        record RestrictSumMemo(uint[] NewUnsetMasks);

        public LogicResult RestrictSum(Solver solver, int minSum, int maxSum)
        {
            var sumsSet = new SortedSet<int>(Enumerable.Range(minSum, maxSum - minSum + 1));
            return RestrictSumHelper(solver, sumsSet);
        }

        public LogicResult RestrictSum(Solver solver, IEnumerable<int> sums)
        {
            var sumsSet = sums as SortedSet<int> ?? new SortedSet<int>(sums);
            return RestrictSumHelper(solver, sumsSet);
        }

        private LogicResult RestrictSumHelper(Solver solver, SortedSet<int> sums)
        {
            if (sums.Count == 0)
            {
                return LogicResult.Invalid;
            }

            int minSum = sums.Min;
            int maxSum = sums.Max;

            var board = solver.Board;

            var unsetCells = cells;
            int setSum = SetSum(solver);
            if (setSum > maxSum)
            {
                return LogicResult.Invalid;
            }

            if (setSum > 0)
            {
                unsetCells = cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();
            }

            int numUnsetCells = unsetCells.Count;
            if (numUnsetCells == 0)
            {
                return sums.Contains(setSum) ? LogicResult.None : LogicResult.Invalid;
            }

            // With one unset cell remaining, its value just needs to conform to the desired sums
            if (numUnsetCells == 1)
            {
                var unsetCell = unsetCells[0];
                uint curMask = board[unsetCell.Item1, unsetCell.Item2];

                uint newMask = 0;
                foreach (int sum in sums)
                {
                    int value = sum - setSum;
                    if (value >= 1 && value <= solver.MAX_VALUE)
                    {
                        newMask |= ValueMask(value);
                    }
                    else if (value > solver.MAX_VALUE)
                    {
                        break;
                    }
                }
                newMask &= curMask;

                if (curMask != newMask)
                {
                    board[unsetCell.Item1, unsetCell.Item2] = newMask;
                    return newMask != 0 ? LogicResult.Changed : LogicResult.Invalid;
                }
                return LogicResult.None;
            }

            uint[] newMasks;

            // Check for a memo
            string memoKey = new StringBuilder()
                .Append(cellsString)
                .Append("|RestrictSum|S")
                .AppendInts(sums)
                .Append("|M")
                .AppendCellValueKey(solver, cells)
                .ToString();
            var memoData = solver.GetMemo<RestrictSumMemo>(memoKey);
            if (memoData != null)
            {
                newMasks = memoData.NewUnsetMasks;
            }
            else
            {
                uint unsetMask = UnsetMask(solver);
                int minValue = MinValue(unsetMask);
                int maxValue = MaxValue(unsetMask);
                List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

                newMasks = new uint[numUnsetCells];
                foreach (var combination in possibleVals.Combinations(unsetCells.Count))
                {
                    int curSum = setSum + combination.Sum();
                    if (sums.Contains(curSum))
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

                solver.StoreMemo(memoKey, new RestrictSumMemo(newMasks));
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

        record PossibleSumsMemo(List<int> sums);

        public List<int> PossibleSums(Solver solver)
        {
            int MAX_VALUE = solver.MAX_VALUE;
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
                return new List<int>() { setSum };
            }

            // With one unset cell remaining, it just contributes its own sum
            if (numUnsetCells == 1)
            {
                List<int> sums = new();
                var unsetCell = unsetCells[0];
                uint curMask = board[unsetCell.Item1, unsetCell.Item2];
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if ((curMask & ValueMask(v)) != 0)
                    {
                        sums.Add(setSum + v);
                    }
                }
                return sums;
            }

            uint[] newMasks;

            // Check for a memo
            string memoKey = new StringBuilder()
                .Append(cellsString)
                .Append("|PossibleSums")
                .AppendCellValueKey(solver, cells)
                .ToString();
            var memoData = solver.GetMemo<PossibleSumsMemo>(memoKey);
            if (memoData != null)
            {
                return memoData.sums.ToList();
            }

            SortedSet<int> sumsSet = new();
            uint unsetMask = UnsetMask(solver);
            int minValue = MinValue(unsetMask);
            int maxValue = MaxValue(unsetMask);
            List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

            newMasks = new uint[numUnsetCells];
            foreach (var combination in possibleVals.Combinations(unsetCells.Count))
            {
                int curSum = setSum + combination.Sum();
                if (!sumsSet.Contains(curSum))
                {
                    // Find if any permutation fits into the cells
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
                            sumsSet.Add(curSum);
                            break;
                        }
                    }
                }
            }

            // Calling ToList twice on purpose so that a different copy is
            // in the memo vs the returned value (which could be changed by the caller).
            solver.StoreMemo(memoKey, new PossibleSumsMemo(sumsSet.ToList()));
            return sumsSet.ToList();
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

        public readonly List<(int, int)> cells;
        private readonly string cellsString;
    }
}
