using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver
{
    public class SumCellsHelper
    {
        public SumCellsHelper(Solver solver, List<(int, int)> cells)
        {
            groups = solver.SplitIntoGroups(cells).Select(g => new SumGroup(solver, g)).ToList();

            this.cells = cells.OrderBy(cell => cell.Item1 * solver.WIDTH + cell.Item2).ToList();
            cellsString = CellsKey("SumCellsHelper", this.cells);
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

        record StepLogicMemo(List<int> Elims, string LogicalStepDesc, LogicResult Result)
        {
            public StepLogicMemo(string logicalStepDesc, LogicResult result) : this(null, logicalStepDesc, result) { }
            public StepLogicMemo(LogicResult result) : this(null, null, result) { }
        }

        public LogicResult StepLogic(Solver solver, IEnumerable<int> possibleSums, StringBuilder logicalStepDescription)
        {
            var sums = possibleSums as SortedSet<int> ?? new SortedSet<int>(possibleSums);
            if (sums.Count == 0)
            {
                return LogicResult.Invalid;
            }

            var board = solver.Board;

            // Check for a memo
            string memoKey = new StringBuilder(cellsString.Length + 16 + sums.Count * 4 + cells.Count * 10)
                    .Append(cellsString)
                    .Append("|StepLogic|S")
                    .AppendInts(sums)
                    .Append("|M")
                    .AppendCellValueKey(solver, cells)
                    .ToString();
            var memoData = solver.GetMemo<StepLogicMemo>(memoKey);
            if (memoData != null)
            {
                if (memoData.Result == LogicResult.None)
                {
                    return LogicResult.None;
                }
                if (memoData.Result == LogicResult.Invalid)
                {
                    logicalStepDescription?.Append(memoData.LogicalStepDesc);
                    return LogicResult.Invalid;
                }

                solver.ClearCandidates(memoData.Elims);
                logicalStepDescription?.Append(memoData.LogicalStepDesc);
                return LogicResult.Changed;
            }

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
                            string desc = $"{solver.CompactName(curGroup.cells)} has no valid candidate combination.";
                            logicalStepDescription.Append(desc);
                            solver.StoreMemo(memoKey, new StepLogicMemo(desc, LogicResult.Invalid));
                        }
                        else
                        {
                            solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.Invalid));
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
                    logicalStepDescription.Append(desc);
                    solver.StoreMemo(memoKey, new StepLogicMemo(desc, LogicResult.Invalid));
                }
                else
                {
                    solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.Invalid));
                }
                return LogicResult.Invalid;
            }

            if (numIncompleteGroups == 0)
            {
                // All groups complete
                solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.None));
                return LogicResult.None;
            }

            if (numIncompleteGroups == 1)
            {
                // One group left means it must exactly sum to whatever sum is remaining
                var (group, min, max) = groupMinMax.First(g => g.min != g.max);
                int numCells = group.cells.Count;

                // If the logical step description is desired, then track what the cells were before applying the sum range.
                uint[] oldMasks = new uint[numCells];
                for (int i = 0; i < numCells; i++)
                {
                    var cell = group.cells[i];
                    oldMasks[i] = board[cell.Item1, cell.Item2];
                }

                // Restrict the sum to desired values
                SortedSet<int> validSums = new(sums.Select(s => s - completedSum).Where(s => s >= min && s <= max));
                var logicResult = group.RestrictSum(solver, validSums);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        string desc = $"{solver.CompactName(group.cells)} cannot sum to the desired value(s).";
                        logicalStepDescription.Append(desc);
                        solver.StoreMemo(memoKey, new StepLogicMemo(desc, LogicResult.Invalid));
                    }
                    else
                    {
                        solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.Invalid));
                    }
                    return LogicResult.Invalid;
                }

                if (logicResult == LogicResult.Changed)
                {
                    List<int> elims = new();
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
                                    elims.Add(solver.CandidateIndex(cell, v));
                                }
                            }
                        }
                    }

                    if (logicalStepDescription != null)
                    {
                        string desc = $"Sum re-evaluated: {solver.DescribeElims(elims)}";
                        logicalStepDescription.Append(desc);
                        solver.StoreMemo(memoKey, new StepLogicMemo(elims, desc, LogicResult.Changed));
                    }
                    else
                    {
                        solver.StoreMemo(memoKey, new StepLogicMemo(elims, null, LogicResult.Changed));
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
                        int numCells = group.cells.Count;
                        uint[] oldMasks = new uint[numCells];
                        for (int i = 0; i < numCells; i++)
                        {
                            var cell = group.cells[i];
                            oldMasks[i] = board[cell.Item1, cell.Item2];
                        }

                        var logicResult = group.RestrictSum(solver, newGroupMin, newGroupMax);
                        if (logicResult == LogicResult.Invalid)
                        {
                            if (logicalStepDescription != null)
                            {
                                string desc = $"{solver.CompactName(group.cells)} cannot be restricted between {newGroupMin} and {newGroupMax}.";
                                logicalStepDescription.Append(desc);
                                solver.StoreMemo(memoKey, new StepLogicMemo(desc, LogicResult.Invalid));
                            }
                            else
                            {
                                solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.Invalid));
                            }
                            return LogicResult.Invalid;
                        }

                        if (logicResult == LogicResult.Changed)
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
                        logicalStepDescription.Append(desc);
                        solver.StoreMemo(memoKey, new StepLogicMemo(elims, desc, LogicResult.Changed));
                    }
                    else
                    {
                        solver.StoreMemo(memoKey, new StepLogicMemo(elims, null, LogicResult.Changed));
                    }
                    return LogicResult.Changed;
                }
            }
            solver.StoreMemo(memoKey, new StepLogicMemo(LogicResult.None));
            return LogicResult.None;
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
            List<List<int>> incompleteGroupSums = new();
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
                return new List<int>() { completedSum };
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

        private readonly List<SumGroup> groups;
        private readonly List<(int, int)> cells;
        private readonly string cellsString;
    }
}
