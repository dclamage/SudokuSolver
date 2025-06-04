namespace SudokuSolver;

public class SumGroup
{
    public SumGroup(Solver solver, List<(int, int)> cells, int excludeValue = 0)
    {
        this.cells = cells.OrderBy(cell => cell.Item1 * solver.WIDTH + cell.Item2).ToList();
        if (excludeValue >= 1 && excludeValue <= solver.MAX_VALUE)
        {
            includeMask = solver.ALL_VALUES_MASK & ~ValueMask(excludeValue);
        }
        else
        {
            includeMask = solver.ALL_VALUES_MASK;
        }
    }

    public (int, int) MinMaxSum(Solver solver)
    {
        // Trivial case of max number of cells
        if (cells.Count == solver.MAX_VALUE)
        {
            int sum = (solver.MAX_VALUE * (solver.MAX_VALUE + 1)) / 2;
            return (sum, sum);
        }

        var minMax = CalcMinMaxSum(solver);
        return minMax;
    }

    public (int, int) QuickMinMaxSum(Solver solver)
    {
        var board = solver.Board;

        int min = 0;
        int max = 0;
        foreach (var (i, j) in cells)
        {
            uint mask = board[i, j];
            if ((mask & includeMask & ~valueSetMask) == 0)
            {
                return (0, 0);
            }

            int setVal = GetSetValue(mask);
            if (setVal != 0)
            {
                min += setVal;
                max += setVal;
            }
            else
            {
                uint validMask = mask & includeMask;
                min += MinValue(validMask);
                max += MaxValue(validMask);
            }
        }

        return (min, max);
    }

    private (int, int) CalcMinMaxSum(Solver solver)
    {
        var board = solver.Board;

        // Check if the excluded value must be included
        if (cells.Any(cell => (board[cell.Item1, cell.Item2] & includeMask & ~valueSetMask) == 0))
        {
            return (0, 0);
        }

        var unsetCells = cells;
        int setSum = SetSum(solver);
        if (setSum > 0)
        {
            unsetCells = cells.Where(cell => GetSetValue(board[cell.Item1, cell.Item2]) == 0).ToList();
        }

        if (unsetCells.Count == 0)
        {
            return (setSum, setSum);
        }

        uint unsetMask = UnsetMask(solver);
        int numUnsetValues = ValueCount(unsetMask);

        // Check for not enough values to fill all the cells
        if (numUnsetValues < unsetCells.Count)
        {
            return (0, 0);
        }

        // Exactly the correct number of values in the unset cells so its sum is exact
        if (numUnsetValues == unsetCells.Count)
        {
            int unsetSum = 0;
            for (int v = 1; v <= solver.MAX_VALUE; v++)
            {
                if (HasValue(unsetMask, v))
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

    public LogicResult RestrictSum(Solver solver, int minSum, int maxSum)
    {
        var sumsSet = new SortedSet<int>(Enumerable.Range(minSum, maxSum - minSum + 1));
        LogicResult result = RestrictSumHelper(solver, sumsSet, out uint[] resultMasks);
        if (result != LogicResult.None)
        {
            ApplySumResult(solver, resultMasks);
        }
        return result;
    }

    public LogicResult RestrictSumToArray(Solver solver, int sum, out uint[] resultMasks) =>
        RestrictSumToArray(solver, sum.ToEnumerable(), out resultMasks);

    public LogicResult RestrictSumToArray(Solver solver, IEnumerable<int> sums, out uint[] resultMasks)
    {
        var sumsSet = sums as SortedSet<int> ?? new SortedSet<int>(sums);
        return RestrictSumHelper(solver, sumsSet, out resultMasks);
    }

    public LogicResult RestrictSum(Solver solver, int sum) =>
        RestrictSum(solver, sum.ToEnumerable());

    public LogicResult RestrictSum(Solver solver, IEnumerable<int> sums)
    {
        var sumsSet = sums as SortedSet<int> ?? new SortedSet<int>(sums);
        LogicResult result = RestrictSumHelper(solver, sumsSet, out uint[] resultMasks);
        if (result != LogicResult.None)
        {
            ApplySumResult(solver, resultMasks);
        }
        return result;
    }

    private LogicResult RestrictSumHelper(Solver solver, SortedSet<int> sums, out uint[] resultMasks)
    {
        var board = solver.Board;

        resultMasks = cells.Select(cell => board[cell.Item1, cell.Item2]).ToArray();

        // Check if the excluded value must be included
        if (cells.Any(cell => (board[cell.Item1, cell.Item2] & includeMask & ~valueSetMask) == 0))
        {
            return LogicResult.Invalid;
        }

        if (sums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        int minSum = sums.Min;
        int maxSum = sums.Max;

        var unsetCells = cells;
        int setSum = SetSum(solver);
        if (setSum > maxSum)
        {
            return LogicResult.Invalid;
        }

        if (setSum > 0)
        {
            unsetCells = cells.Where(cell => GetSetValue(board[cell.Item1, cell.Item2]) == 0).ToList();
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
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    if (cells[cellIndex] == unsetCell)
                    {
                        resultMasks[cellIndex] = newMask;
                    }
                }
                return newMask != 0 ? LogicResult.Changed : LogicResult.Invalid;
            }
            return LogicResult.None;
        }

        uint[] newMasks;

        uint unsetMask = UnsetMask(solver);

        // Check for not enough values to fill all the cells
        if (ValueCount(unsetMask) < numUnsetCells)
        {
            return LogicResult.Invalid;
        }

        int minValue = MinValue(unsetMask);
        int maxValue = MaxValue(unsetMask);
        List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

        newMasks = new uint[numUnsetCells];
        foreach (var combination in possibleVals.Combinations(unsetCells.Count))
        {
            int curSum = setSum + combination.Sum();
            if (sums.Contains(curSum))
            {
                foreach (var perm in combination.Permutations())
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
        bool invalid = false;
        int unsetIndex = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            uint curMask = board[cell.Item1, cell.Item2];
            if (GetSetValue(curMask) == 0)
            {
                uint newMask = curMask & newMasks[unsetIndex++];
                if (resultMasks[i] != newMask)
                {
                    resultMasks[i] = newMask;
                    changed = true;

                    if (newMask == 0)
                    {
                        invalid = true;
                    }
                }
            }
        }
        return invalid ? LogicResult.Invalid : (changed ? LogicResult.Changed : LogicResult.None);
    }

    private void ApplySumResult(Solver solver, uint[] resultMasks)
    {
        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            solver.KeepMask(i, j, resultMasks[cellIndex]);
        }
    }

    public List<int> PossibleSums(Solver solver)
    {
        int MAX_VALUE = solver.MAX_VALUE;
        var board = solver.Board;

        var unsetCells = cells;
        int setSum = SetSum(solver);
        if (setSum > 0)
        {
            unsetCells = cells.Where(cell => GetSetValue(board[cell.Item1, cell.Item2]) == 0).ToList();
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

        SortedSet<int> sumsSet = new();
        uint unsetMask = UnsetMask(solver);
        if (ValueCount(unsetMask) < unsetCells.Count)
        {
            return new();
        }

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
                foreach (var perm in combination.Permutations())
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

        return sumsSet.ToList();
    }

    public bool IsSumPossible(Solver solver, int sum)
    {
        int MAX_VALUE = solver.MAX_VALUE;
        var board = solver.Board;

        var unsetCells = cells;
        int setSum = SetSum(solver);
        if (setSum > sum)
        {
            return false;
        }

        if (setSum > 0)
        {
            unsetCells = cells.Where(cell => GetSetValue(board[cell.Item1, cell.Item2]) == 0).ToList();
        }

        int numUnsetCells = unsetCells.Count;
        if (numUnsetCells == 0)
        {
            return setSum == sum;
        }

        // With one unset cell remaining, it just contributes its own sum
        if (numUnsetCells == 1)
        {
            var unsetCell = unsetCells[0];
            uint curMask = board[unsetCell.Item1, unsetCell.Item2];
            int valueNeeded = sum - setSum;
            return valueNeeded >= 1 && valueNeeded <= MAX_VALUE && HasValue(curMask, valueNeeded);
        }

        uint[] newMasks;

        uint unsetMask = UnsetMask(solver);
        if (ValueCount(unsetMask) < unsetCells.Count)
        {
            return false;
        }

        int minValue = MinValue(unsetMask);
        int maxValue = MaxValue(unsetMask);
        List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

        newMasks = new uint[numUnsetCells];
        foreach (var combination in possibleVals.Combinations(unsetCells.Count))
        {
            if (setSum + combination.Sum() == sum)
            {
                // Find if any permutation fits into the cells
                foreach (var perm in combination.Permutations())
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
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public uint UnsetMask(Solver solver)
    {
        var board = solver.Board;
        uint combMask = 0;
        foreach (var cell in cells)
        {
            uint mask = board[cell.Item1, cell.Item2];
            if (GetSetValue(mask) == 0)
            {
                combMask |= mask;
            }
        }
        return combMask & includeMask;
    }

    public int SetSum(Solver solver)
    {
        var board = solver.Board;
        int sum = 0;
        foreach (var (i, j) in cells)
        {
            sum += GetSetValue(board[i, j]);
        }
        return sum;
    }

    private int GetSetValue(uint mask)
    {
        if (IsValueSet(mask) || ValueCount(mask) == 1)
        {
            return GetValue(mask);
        }
        if (ValueCount(mask & includeMask) == 1)
        {
            return GetValue(mask & includeMask);
        }
        return 0;
    }

    public IReadOnlyList<(int, int)> Cells => cells;
    private readonly List<(int, int)> cells;
    private readonly uint includeMask;
}
