namespace SudokuSolver;

public class SumCellsHelper
{
    public List<(int, int)> Cells => cells;

    public SumCellsHelper(Solver solver, List<(int, int)> cells)
    {
        groups = solver.SplitIntoGroups(cells).Select(g => new SumGroup(solver, g)).ToList();

        this.cells = cells.OrderBy(cell => cell.Item1 * solver.WIDTH + cell.Item2).ToList();
    }

    public LogicResult Init(Solver solver, IEnumerable<int> possibleSums)
    {
        List<int> sums = possibleSums.ToList();
        if (sums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        sums.Sort();

        int minSum = 0;
        int maxSum = 0;
        List<(SumGroup group, int min, int max)> groupMinMax = new(groups.Count);
        foreach (var curGroup in groups)
        {
            var (curMin, curMax) = curGroup.MinMaxSum(solver);
            if (curMin == 0 || curMax == 0)
            {
                return LogicResult.Invalid;
            }

            minSum += curMin;
            maxSum += curMax;

            groupMinMax.Add((curGroup, curMin, curMax));
        }

        int possibleSumMin = sums.First();
        int possibleSumMax = sums.Last();
        if (minSum > possibleSumMax || maxSum < possibleSumMin)
        {
            return LogicResult.Invalid;
        }

        // Each group can increase from its min by the minDof
        // and decrease from its max by the maxDof
        bool changed = false;
        int minDof = possibleSumMax - minSum;
        int maxDof = maxSum - possibleSumMin;

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
                var logicResult = group.RestrictSum(solver, newGroupMin, newGroupMax);
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

    public LogicResult StepLogic(Solver solver, IEnumerable<int> possibleSums, List<LogicalStepDesc> logicalStepDescription)
    {
        var sums = possibleSums as SortedSet<int> ?? new SortedSet<int>(possibleSums);
        if (sums.Count == 0)
        {
            if (logicalStepDescription != null)
            {
                logicalStepDescription.Add(new LogicalStepDesc("There are no possible sums.", cells));
            }
            return LogicResult.Invalid;
        }

        var board = solver.Board;

        int MAX_VALUE = solver.MAX_VALUE;
        int completedSum = 0;
        int numIncompleteGroups = 0;
        int minSum = 0;
        int maxSum = 0;
        List<(SumGroup group, int min, int max)> groupMinMax = new(groups.Count);
        {
            foreach (var curGroup in groups)
            {
                var (curMin, curMax) = curGroup.MinMaxSum(solver);
                if (curMin == 0 || curMax == 0)
                {
                    if (logicalStepDescription != null)
                    {
                        string desc = $"{solver.CompactName(curGroup.Cells)} has no valid candidate combination.";
                        logicalStepDescription.Add(new LogicalStepDesc(desc, curGroup.Cells));
                    }
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
                    completedSum += curMin;
                }

                groupMinMax.Add((curGroup, curMin, curMax));
            }
        }

        int possibleSumMin = sums.Min;
        int possibleSumMax = sums.Max;

        if (minSum > possibleSumMax || maxSum < possibleSumMin)
        {
            if (logicalStepDescription != null)
            {
                string desc = $"Sum is no longer possible (Between {minSum} and {maxSum}).";
                logicalStepDescription?.Add(new LogicalStepDesc(desc, cells));
            }
            return LogicResult.Invalid;
        }

        if (numIncompleteGroups == 0)
        {
            // All groups complete
            return LogicResult.None;
        }

        if (numIncompleteGroups == 1)
        {
            // One group left means it must exactly sum to whatever sum is remaining
            var (group, min, max) = groupMinMax.First(g => g.min != g.max);
            int numCells = group.Cells.Count;

            // If the logical step description is desired, then track what the cells were before applying the sum range.
            uint[] oldMasks = new uint[numCells];
            for (int i = 0; i < numCells; i++)
            {
                var cell = group.Cells[i];
                oldMasks[i] = board[cell.Item1, cell.Item2];
            }

            // Restrict the sum to desired values
            SortedSet<int> validSums = new(sums.Select(s => s - completedSum).Where(s => s >= min && s <= max));
            var logicResult = group.RestrictSum(solver, validSums);
            if (logicResult == LogicResult.Invalid)
            {
                if (logicalStepDescription != null)
                {
                    string desc = $"{solver.CompactName(group.Cells)} cannot sum to the desired value(s).";
                    logicalStepDescription?.Add(new LogicalStepDesc(desc, group.Cells));
                }
                return LogicResult.Invalid;
            }

            if (logicResult == LogicResult.Changed)
            {
                List<int> elims = new();
                for (int i = 0; i < numCells; i++)
                {
                    var cell = group.Cells[i];
                    uint removedMask = oldMasks[i] & ~board[cell.Item1, cell.Item2];
                    if (removedMask != 0)
                    {
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if ((removedMask & ValueMask(v)) != 0)
                            {
                                elims.Add(solver.CandidateIndex(cell, v));
                            }
                        }
                    }
                }

                if (logicalStepDescription != null)
                {
                    string desc = $"Sum re-evaluated: {solver.DescribeElims(elims)}";
                    logicalStepDescription?.Add(new LogicalStepDesc(desc, Enumerable.Empty<int>(), elims));
                }
                return LogicResult.Changed;
            }
        }
        else
        {
            // Each group can increase from its min by the minDof
            // and decrease from its max by the maxDof
            int minDof = possibleSumMax - minSum;
            int maxDof = maxSum - possibleSumMin;

            List<int> elims = new();
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
                    int numCells = group.Cells.Count;
                    uint[] oldMasks = new uint[numCells];
                    for (int i = 0; i < numCells; i++)
                    {
                        var cell = group.Cells[i];
                        oldMasks[i] = board[cell.Item1, cell.Item2];
                    }

                    var logicResult = group.RestrictSum(solver, newGroupMin, newGroupMax);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            string desc = $"{solver.CompactName(group.Cells)} cannot be restricted between {newGroupMin} and {newGroupMax}.";
                            logicalStepDescription?.Add(new LogicalStepDesc(desc, group.Cells));
                        }
                        return LogicResult.Invalid;
                    }

                    if (logicResult == LogicResult.Changed)
                    {
                        for (int i = 0; i < numCells; i++)
                        {
                            var cell = group.Cells[i];
                            uint removedMask = oldMasks[i] & ~board[cell.Item1, cell.Item2];
                            if (removedMask != 0)
                            {
                                for (int v = 1; v <= MAX_VALUE; v++)
                                {
                                    if ((removedMask & ValueMask(v)) != 0)
                                    {
                                        elims.Add(solver.CandidateIndex(cell, v));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (elims.Count > 0)
            {
                if (logicalStepDescription != null)
                {
                    string desc = $"Sum re-evaluated: {solver.DescribeElims(elims)}";
                    logicalStepDescription?.Add(new LogicalStepDesc(desc, Enumerable.Empty<int>(), elims));
                }
                return LogicResult.Changed;
            }
        }
        return LogicResult.None;
    }

    // Brute-force optimized StepLogic: uses sum bitmasks. Zero heap allocations for single-group helpers.
    // sumsMask: bit s set = sum s is one of the allowed totals.
    // outSumsMask: after propagation, which sums are still achievable.
    public LogicResult StepLogicBF(Solver solver, ulong sumsMask, out ulong outSumsMask)
    {
        outSumsMask = 0;
        if (sumsMask == 0) return LogicResult.Invalid;

        if (groups.Count == 1)
        {
            var group = groups[0];
            var lr = group.RestrictSumMask(solver, sumsMask);
            if (lr == LogicResult.Invalid) return LogicResult.Invalid;
            outSumsMask = group.PossibleSumsMask(solver);
            return lr;
        }

        int groupCount = groups.Count;
        Span<ulong> groupMasks = stackalloc ulong[groupCount];
        Span<int> groupMins = stackalloc int[groupCount];
        Span<int> groupMaxes = stackalloc int[groupCount];

        int completedSum = 0;
        int numIncompleteGroups = 0;
        int incompleteGroupIndex = -1;
        int minSum = 0;
        int maxSum = 0;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            ulong groupMask = groups[groupIndex].PossibleSumsMask(solver);
            if (groupMask == 0) return LogicResult.Invalid;

            int groupMin = BitOperations.TrailingZeroCount(groupMask);
            int groupMax = 63 - BitOperations.LeadingZeroCount(groupMask);
            groupMasks[groupIndex] = groupMask;
            groupMins[groupIndex] = groupMin;
            groupMaxes[groupIndex] = groupMax;
            minSum += groupMin;
            maxSum += groupMax;

            if (groupMin == groupMax)
            {
                completedSum += groupMin;
            }
            else
            {
                numIncompleteGroups++;
                incompleteGroupIndex = groupIndex;
            }
        }

        int possibleSumMin = BitOperations.TrailingZeroCount(sumsMask);
        int possibleSumMax = 63 - BitOperations.LeadingZeroCount(sumsMask);
        if (minSum > possibleSumMax || maxSum < possibleSumMin)
        {
            return LogicResult.Invalid;
        }

        if (numIncompleteGroups == 0)
        {
            outSumsMask = PossibleSumsMask(solver);
            return LogicResult.None;
        }

        LogicResult result = LogicResult.None;
        if (numIncompleteGroups == 1)
        {
            ulong validGroupSums = 0;
            ulong remainingSums = sumsMask;
            while (remainingSums != 0)
            {
                int sum = BitOperations.TrailingZeroCount(remainingSums);
                remainingSums &= remainingSums - 1;
                int groupSum = sum - completedSum;
                if ((uint)groupSum < 64)
                {
                    validGroupSums |= 1uL << groupSum;
                }
            }

            validGroupSums &= groupMasks[incompleteGroupIndex];
            if (validGroupSums == 0) return LogicResult.Invalid;

            result = groups[incompleteGroupIndex].RestrictSumMask(solver, validGroupSums);
            if (result == LogicResult.Invalid) return LogicResult.Invalid;
        }
        else
        {
            int minDof = possibleSumMax - minSum;
            int maxDof = maxSum - possibleSumMin;
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                int groupMin = groupMins[groupIndex];
                int groupMax = groupMaxes[groupIndex];
                if (groupMin == groupMax)
                {
                    continue;
                }

                int newGroupMin = Math.Max(groupMin, groupMax - maxDof);
                int newGroupMax = Math.Min(groupMax, groupMin + minDof);
                if (newGroupMin > groupMin || newGroupMax < groupMax)
                {
                    ulong groupRangeMask = SumRangeMask(newGroupMin, newGroupMax) & groupMasks[groupIndex];
                    if (groupRangeMask == 0) return LogicResult.Invalid;

                    var logicResult = groups[groupIndex].RestrictSumMask(solver, groupRangeMask);
                    if (logicResult == LogicResult.Invalid) return LogicResult.Invalid;
                    if (logicResult == LogicResult.Changed) result = LogicResult.Changed;
                }
            }
        }

        outSumsMask = PossibleSumsMask(solver);
        return result;
    }

    private static ulong SumRangeMask(int minSum, int maxSum)
    {
        ulong mask = 0;
        int first = Math.Max(0, minSum);
        int last = Math.Min(63, maxSum);
        for (int sum = first; sum <= last; sum++)
        {
            mask |= 1uL << sum;
        }
        return mask;
    }

    /// <summary>
    /// Gets the currently possible total sums as a bit mask, where bit <c>s</c> means sum <c>s</c> is achievable.
    /// </summary>
    /// <param name="solver">The solver state used to read current candidates and values.</param>
    /// <returns>A mask of possible sums, or <c>0</c> when no sum is possible.</returns>
    public ulong PossibleSumsMask(Solver solver)
    {
        ulong combinedMask = 1;
        foreach (var group in groups)
        {
            ulong groupMask = group.PossibleSumsMask(solver);
            if (groupMask == 0)
            {
                return 0;
            }

            ulong nextMask = 0;
            ulong remainingCombined = combinedMask;
            while (remainingCombined != 0)
            {
                int sumA = BitOperations.TrailingZeroCount(remainingCombined);
                remainingCombined &= remainingCombined - 1;

                ulong remainingGroup = groupMask;
                while (remainingGroup != 0)
                {
                    int sumB = BitOperations.TrailingZeroCount(remainingGroup);
                    remainingGroup &= remainingGroup - 1;

                    int sum = sumA + sumB;
                    if ((uint)sum < 64)
                    {
                        nextMask |= 1uL << sum;
                    }
                }
            }

            combinedMask = nextMask;
        }

        return combinedMask;
    }

    // Overload for StringBuilder compatibility
    public LogicResult StepLogic(Solver solver, IEnumerable<int> possibleSums, StringBuilder logicalStepDescription)
    {
        List<LogicalStepDesc> stepDescs = logicalStepDescription != null ? new() : null;
        var result = StepLogic(solver, possibleSums, stepDescs);
        if (logicalStepDescription != null && stepDescs != null)
        {
            foreach (var step in stepDescs)
            {
                logicalStepDescription.AppendLine(step.ToString());
            }
        }
        return result;
    }

    public (int, int) SumRange(Solver solver)
    {
        int minSum = 0;
        int maxSum = 0;
        List<(SumGroup group, int min, int max)> groupMinMax = new(groups.Count);
        foreach (var curGroup in groups)
        {
            var (curMin, curMax) = curGroup.MinMaxSum(solver);
            if (curMin == 0 || curMax == 0)
            {
                return (0, 0);
            }
            minSum += curMin;
            maxSum += curMax;
        }
        return (minSum, maxSum);
    }

    public List<int> PossibleSums(Solver solver)
    {
        int completedSum = 0;
        List<List<int>> incompleteGroupSums = [];
        foreach (var curGroup in groups)
        {
            List<int> possibleSums = curGroup.PossibleSums(solver);
            if (possibleSums.Count == 0)
            {
                return null;
            }

            if (possibleSums.Count > 1)
            {
                incompleteGroupSums.Add(possibleSums);
            }
            else
            {
                completedSum += possibleSums[0];
            }
        }

        if (incompleteGroupSums.Count == 0)
        {
            return [completedSum];
        }

        // Limit exact results to 5 incomplete groups
        if (incompleteGroupSums.Count <= 5)
        {
            SortedSet<int> sums = new();
            foreach (int sum in EnumerateSums(incompleteGroupSums))
            {
                sums.Add(completedSum + sum);
            }
            return sums.ToList();
        }

        // Quick and dirty min/max
        int min = completedSum;
        int max = completedSum;
        foreach (var groupSums in incompleteGroupSums)
        {
            min += groupSums.First();
            max += groupSums.Last();
        }
        return Enumerable.Range(min, max - min + 1).ToList();
    }

    private static IEnumerable<int> EnumerateSums(List<List<int>> groups, int groupIndex = 0)
    {
        if (groupIndex == groups.Count)
        {
            yield return 0;
        }

        if (groupIndex < groups.Count)
        {
            var group = groups[groupIndex];
            for (int i = 0; i < group.Count; i++)
            {
                int sum = group[i];
                foreach (int subSum in EnumerateSums(groups, groupIndex + 1))
                {
                    yield return sum + subSum;
                }
            }
        }
    }

    private readonly List<SumGroup> groups;
    private readonly List<(int, int)> cells;
}
