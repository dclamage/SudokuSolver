namespace SudokuSolver;

public class SumGroup
{
    public SumGroup(Solver solver, List<(int, int)> cells, int excludeValue = 0)
    {
        this.cells = cells.OrderBy(cell => cell.Item1 * solver.WIDTH + cell.Item2).ToList();
        numValues = solver.MAX_VALUE;
        if (excludeValue >= 1 && excludeValue <= numValues)
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
        if (cells.Count == numValues)
        {
            int sum = (numValues * (numValues + 1)) / 2;
            return (sum, sum);
        }

        var minMax = CalcMinMaxSum(solver);
        return minMax;
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
            for (int v = 1; v <= numValues; v++)
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

        // K>=2: KillerCageSums fast path
        int numUnset = unsetCells.Count;
        SumData sumData = SumData.Get(numValues);
        if (sumData != null && numUnset <= numValues)
        {
            uint[] unsetCellMasks = new uint[numUnset];
            for (int i = 0; i < numUnset; i++)
            {
                var (r, c) = unsetCells[i];
                unsetCellMasks[i] = board[r, c] & includeMask & ~valueSetMask;
            }

            var kcRow = sumData.KillerCageSums[numUnset];
            int minTotal = int.MaxValue, maxTotal = 0;

            for (int rs = 1; rs < kcRow.Length; rs++)
            {
                uint[] opts = kcRow[rs];
                if (opts.Length == 0) continue;
                for (int oi = 0; oi < opts.Length; oi++)
                {
                    uint o = opts[oi];
                    if ((o & ~unsetMask) != 0) continue;
                    bool valid = true;
                    for (int i = 0; i < numUnset; i++)
                        if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                    if (!valid) continue;
                    int total = setSum + rs;
                    if (minTotal == int.MaxValue) minTotal = total;
                    maxTotal = total;
                    break; // one valid combo per rs is enough
                }
            }

            return minTotal == int.MaxValue ? (0, 0) : (minTotal, maxTotal);
        }

        // Fallback: combination-based approach
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
                if (value >= 1 && value <= numValues)
                {
                    newMask |= ValueMask(value);
                }
                else if (value > numValues)
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

        uint unsetMask = UnsetMask(solver);

        // Check for not enough values to fill all the cells
        if (ValueCount(unsetMask) < numUnsetCells)
        {
            return LogicResult.Invalid;
        }

        // K>=2: KillerCageSums fast path
        SumData sumData = SumData.Get(numValues);
        if (sumData != null && numUnsetCells <= numValues)
        {
            uint[] unsetCellMasks = new uint[numUnsetCells];
            for (int i = 0; i < numUnsetCells; i++)
            {
                var (r, c) = unsetCells[i];
                unsetCellMasks[i] = board[r, c] & includeMask & ~valueSetMask;
            }

            uint[] cellSupported = new uint[numUnsetCells];
            var kcRow = sumData.KillerCageSums[numUnsetCells];

            foreach (int s in sums)
            {
                int rs = s - setSum;
                if (rs <= 0 || rs >= kcRow.Length) continue;
                uint[] opts = kcRow[rs];
                for (int oi = 0; oi < opts.Length; oi++)
                {
                    uint o = opts[oi];
                    if ((o & ~unsetMask) != 0) continue;
                    bool valid = true;
                    for (int i = 0; i < numUnsetCells; i++)
                        if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                    if (!valid) continue;
                    for (int i = 0; i < numUnsetCells; i++)
                        cellSupported[i] |= unsetCellMasks[i] & o;
                }
            }

            bool changed = false, invalid = false;
            int unsetIndex = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                uint curMask = board[cell.Item1, cell.Item2];
                if (GetSetValue(curMask) == 0)
                {
                    uint newMask = curMask & cellSupported[unsetIndex];
                    if (resultMasks[i] != newMask)
                    {
                        resultMasks[i] = newMask;
                        changed = true;
                        if (newMask == 0) invalid = true;
                    }
                    unsetIndex++;
                }
            }
            return invalid ? LogicResult.Invalid : (changed ? LogicResult.Changed : LogicResult.None);
        }

        // Fallback: combination/permutation approach
        int minValue = MinValue(unsetMask);
        int maxValue = MaxValue(unsetMask);
        List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

        uint[] newMasks = new uint[numUnsetCells];
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

        bool ch = false, inv = false;
        int ui = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            uint curMask = board[cell.Item1, cell.Item2];
            if (GetSetValue(curMask) == 0)
            {
                uint newMask = curMask & newMasks[ui++];
                if (resultMasks[i] != newMask)
                {
                    resultMasks[i] = newMask;
                    ch = true;
                    if (newMask == 0) inv = true;
                }
            }
        }
        return inv ? LogicResult.Invalid : (ch ? LogicResult.Changed : LogicResult.None);
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
            for (int v = 1; v <= numValues; v++)
            {
                if ((curMask & ValueMask(v)) != 0)
                {
                    sums.Add(setSum + v);
                }
            }
            return sums;
        }

        uint unsetMask = UnsetMask(solver);
        if (ValueCount(unsetMask) < unsetCells.Count)
        {
            return new();
        }

        // K>=2: KillerCageSums fast path
        SumData sumData = SumData.Get(numValues);
        if (sumData != null && numUnsetCells <= numValues)
        {
            uint[] unsetCellMasks = new uint[numUnsetCells];
            for (int i = 0; i < numUnsetCells; i++)
            {
                var (r, c) = unsetCells[i];
                unsetCellMasks[i] = board[r, c] & includeMask & ~valueSetMask;
            }

            SortedSet<int> sumsSet = new();
            var kcRow = sumData.KillerCageSums[numUnsetCells];

            for (int rs = 1; rs < kcRow.Length; rs++)
            {
                uint[] opts = kcRow[rs];
                if (opts.Length == 0) continue;
                for (int oi = 0; oi < opts.Length; oi++)
                {
                    uint o = opts[oi];
                    if ((o & ~unsetMask) != 0) continue;
                    bool valid = true;
                    for (int i = 0; i < numUnsetCells; i++)
                        if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                    if (!valid) continue;
                    sumsSet.Add(setSum + rs);
                    break; // one valid combo per rs is enough
                }
            }

            return sumsSet.ToList();
        }

        // Fallback
        SortedSet<int> sumsSetFallback = new();
        int minValue = MinValue(unsetMask);
        int maxValue = MaxValue(unsetMask);
        List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

        uint[] newMasks = new uint[numUnsetCells];
        foreach (var combination in possibleVals.Combinations(unsetCells.Count))
        {
            int curSum = setSum + combination.Sum();
            if (!sumsSetFallback.Contains(curSum))
            {
                foreach (var perm in combination.Permutations())
                {
                    bool needCheck = false;
                    for (int i = 0; i < numUnsetCells; i++)
                    {
                        uint valueMask = ValueMask(perm[i]);
                        if ((newMasks[i] & valueMask) == 0) { needCheck = true; break; }
                    }

                    if (needCheck && solver.CanPlaceDigits(unsetCells, perm))
                    {
                        sumsSetFallback.Add(curSum);
                        break;
                    }
                }
            }
        }

        return sumsSetFallback.ToList();
    }

    public bool IsSumPossible(Solver solver, int sum)
    {
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
            return valueNeeded >= 1 && valueNeeded <= numValues && HasValue(curMask, valueNeeded);
        }

        uint unsetMask = UnsetMask(solver);
        if (ValueCount(unsetMask) < unsetCells.Count)
        {
            return false;
        }

        // K>=2: KillerCageSums fast path
        SumData sumData = SumData.Get(numValues);
        if (sumData != null && numUnsetCells <= numValues)
        {
            uint[] unsetCellMasks = new uint[numUnsetCells];
            for (int i = 0; i < numUnsetCells; i++)
            {
                var (r, c) = unsetCells[i];
                unsetCellMasks[i] = board[r, c] & includeMask & ~valueSetMask;
            }

            int rs = sum - setSum;
            var kcRow = sumData.KillerCageSums[numUnsetCells];
            if (rs > 0 && rs < kcRow.Length)
            {
                uint[] opts = kcRow[rs];
                for (int oi = 0; oi < opts.Length; oi++)
                {
                    uint o = opts[oi];
                    if ((o & ~unsetMask) != 0) continue;
                    bool valid = true;
                    for (int i = 0; i < numUnsetCells; i++)
                        if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                    if (!valid) continue;
                    return true;
                }
            }
            return false;
        }

        // Fallback
        int minValue = MinValue(unsetMask);
        int maxValue = MaxValue(unsetMask);
        List<int> possibleVals = Enumerable.Range(minValue, maxValue).Where(v => HasValue(unsetMask, v)).ToList();

        uint[] newMasks = new uint[numUnsetCells];
        foreach (var combination in possibleVals.Combinations(unsetCells.Count))
        {
            if (setSum + combination.Sum() == sum)
            {
                foreach (var perm in combination.Permutations())
                {
                    bool needCheck = false;
                    for (int i = 0; i < numUnsetCells; i++)
                    {
                        uint valueMask = ValueMask(perm[i]);
                        if ((newMasks[i] & valueMask) == 0) { needCheck = true; break; }
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

    // Returns a bitmask where bit s is set if sum s is achievable. Uses stackalloc — zero heap allocations.
    public ulong PossibleSumsMask(Solver solver)
    {
        var board = solver.Board;
        int setSum = SetSum(solver);

        int numUnset = 0;
        Span<uint> unsetCellMasks = stackalloc uint[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            var (r, c) = cells[i];
            uint mask = board[r, c];
            if (GetSetValue(mask) == 0)
                unsetCellMasks[numUnset++] = mask & includeMask & ~valueSetMask;
        }

        if (numUnset == 0)
            return (uint)setSum < 64 ? 1uL << setSum : 0;

        uint unsetMask = 0;
        for (int i = 0; i < numUnset; i++)
            unsetMask |= unsetCellMasks[i];

        if (ValueCount(unsetMask) < numUnset)
            return 0;

        if (numUnset == 1)
        {
            ulong result = 0;
            uint m = unsetCellMasks[0];
            for (int v = 1; v <= numValues; v++)
                if ((m & ValueMask(v)) != 0)
                {
                    int s = setSum + v;
                    if ((uint)s < 64) result |= 1uL << s;
                }
            return result;
        }

        SumData sumData = SumData.Get(numValues);
        if (sumData == null || numUnset > numValues)
            return 0;

        ulong resultMask = 0;
        var kcRow = sumData.KillerCageSums[numUnset];
        for (int rs = 1; rs < kcRow.Length; rs++)
        {
            uint[] opts = kcRow[rs];
            if (opts.Length == 0) continue;
            for (int oi = 0; oi < opts.Length; oi++)
            {
                uint o = opts[oi];
                if ((o & ~unsetMask) != 0) continue;
                bool valid = true;
                for (int i = 0; i < numUnset; i++)
                    if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                if (!valid) continue;
                int totalSum = setSum + rs;
                if ((uint)totalSum < 64) resultMask |= 1uL << totalSum;
                break;
            }
        }
        return resultMask;
    }

    // Restricts cells to support only sums set in sumsMask, applying changes directly. Uses stackalloc — zero heap allocations.
    public LogicResult RestrictSumMask(Solver solver, ulong sumsMask)
    {
        if (sumsMask == 0) return LogicResult.Invalid;
        var board = solver.Board;

        for (int i = 0; i < cells.Count; i++)
        {
            var (r, c) = cells[i];
            if ((board[r, c] & includeMask & ~valueSetMask) == 0)
                return LogicResult.Invalid;
        }

        int setSum = SetSum(solver);

        int numUnset = 0;
        Span<int> unsetIdx = stackalloc int[cells.Count];
        Span<uint> unsetCellMasks = stackalloc uint[cells.Count];
        uint unsetMask = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var (r, c) = cells[i];
            uint mask = board[r, c];
            if (GetSetValue(mask) == 0)
            {
                unsetIdx[numUnset] = i;
                uint cm = mask & includeMask & ~valueSetMask;
                unsetCellMasks[numUnset] = cm;
                unsetMask |= cm;
                numUnset++;
            }
        }

        if (numUnset == 0)
            return ((uint)setSum < 64 && (sumsMask & (1uL << setSum)) != 0) ? LogicResult.None : LogicResult.Invalid;

        if (ValueCount(unsetMask) < numUnset)
            return LogicResult.Invalid;

        if (numUnset == 1)
        {
            var (r, c) = cells[unsetIdx[0]];
            uint curMask = unsetCellMasks[0];
            uint newMask = 0;
            ulong rem = sumsMask;
            while (rem != 0)
            {
                int s = BitOperations.TrailingZeroCount(rem);
                rem &= rem - 1;
                int v = s - setSum;
                if (v >= 1 && v <= numValues)
                    newMask |= ValueMask(v);
            }
            newMask &= curMask;
            if (newMask == curMask) return LogicResult.None;
            return solver.KeepMask(r, c, newMask);
        }

        SumData sumData = SumData.Get(numValues);
        if (sumData == null || numUnset > numValues)
            return LogicResult.None;

        Span<uint> cellSupported = stackalloc uint[cells.Count];
        cellSupported[..numUnset].Clear();

        var kcRow = sumData.KillerCageSums[numUnset];
        ulong remaining = sumsMask;
        while (remaining != 0)
        {
            int s = BitOperations.TrailingZeroCount(remaining);
            remaining &= remaining - 1;
            int rs = s - setSum;
            if (rs <= 0 || rs >= kcRow.Length) continue;
            uint[] opts = kcRow[rs];
            for (int oi = 0; oi < opts.Length; oi++)
            {
                uint o = opts[oi];
                if ((o & ~unsetMask) != 0) continue;
                bool valid = true;
                for (int i = 0; i < numUnset; i++)
                    if ((unsetCellMasks[i] & o) == 0) { valid = false; break; }
                if (!valid) continue;
                for (int i = 0; i < numUnset; i++)
                    cellSupported[i] |= unsetCellMasks[i] & o;
            }
        }

        LogicResult result = LogicResult.None;
        for (int i = 0; i < numUnset; i++)
        {
            if (cellSupported[i] == 0) return LogicResult.Invalid;
            var (r, c) = cells[unsetIdx[i]];
            var lr = solver.KeepMask(r, c, cellSupported[i]);
            if (lr == LogicResult.Invalid) return LogicResult.Invalid;
            if (lr == LogicResult.Changed) result = LogicResult.Changed;
        }
        return result;
    }

    public IReadOnlyList<(int, int)> Cells => cells;
    private readonly List<(int, int)> cells;
    private readonly uint includeMask;
    private readonly int numValues;
}
