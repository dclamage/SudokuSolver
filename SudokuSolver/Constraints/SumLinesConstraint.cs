using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Sum Lines", ConsoleName = "sumlines")]
internal class SumLinesConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsSet;
    public readonly int sum;
    private List<List<SumCellsHelper>> splits = null;

    private static readonly Regex optionsRegex = new(@"(\d+);(.*)");

    public SumLinesConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"Sum Lines has invalid syntax. Use this format: \"sum;cell_group\" like \"10;r1c1r1c2r1c3r2c2\"");
        }

        sum = int.Parse(match.Groups[1].Value);
        options = match.Groups[2].Value;

        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Sum Lines expects 1 cell group, got {cellGroups.Count} groups.");
        }
        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    private static List<List<SumCellsHelper>> Split(Solver sudokuSolver, List<(int, int)> remainingCells, int sum)
    {
        int minCells = (sum + sudokuSolver.MAX_VALUE - 1) / sudokuSolver.MAX_VALUE;
        int maxCells = Math.Min(sum, remainingCells.Count);

        List<List<SumCellsHelper>> result = [];
        if (remainingCells.Count < minCells)
        {
            return result;
        }
        
        for (int nextGroupSize = minCells; nextGroupSize <= maxCells; nextGroupSize++)
        {
            List<(int, int)> nextCells = remainingCells[0..nextGroupSize];
            SumCellsHelper nextCellsHelper = new(sudokuSolver, nextCells);
            var (minSum, maxSum) = nextCellsHelper.SumRange(sudokuSolver);
            if (sum >= minSum && sum <= maxSum)
            {
                List<(int, int)> nextRemainingCells = remainingCells[nextGroupSize..];
                if (nextRemainingCells.Count == 0)
                {
                    result.Add([nextCellsHelper]);
                }
                else
                {
                    List<List<SumCellsHelper>> nextResults = Split(sudokuSolver, nextRemainingCells, sum);
                    foreach (var nextResult in nextResults)
                    {
                        result.Add([nextCellsHelper, .. nextResult]);
                    }
                }
            }
            else if (minSum > sum)
            {
                // If the minimum sum is greater than the target sum, then we can stop looking
                // because adding another cell will only increase the sum.
                break;
            }
        }
        return result;
    }

    public override string SpecificName => $"Sum Line ({sum}) from {CellName(cells[0])} - {CellName(cells[^1])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        splits = Split(sudokuSolver, cells, sum);
        if (splits.Count == 0)
        {
            return LogicResult.Invalid;
        }
        return LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (!cellsSet.Contains((i, j)))
        {
            return true;
        }

        var board = sudokuSolver.Board;

        // Go through the cells forward, checking for invalid sums
        int currentSum = 0;
        foreach (var cell in cells)
        {
            uint cellMask = board[cell.Item1, cell.Item2];
            if (IsValueSet(cellMask))
            {
                currentSum += GetValue(cellMask);
                if (currentSum > sum)
                {
                    return false;
                }
                if (currentSum == sum)
                {
                    currentSum = 0;
                }
            }
            else if (currentSum + MinValue(cellMask) > sum)
            {
                return false;
            }
            else
            {
                break;
            }
        }

        // Go through the cells backwards
        currentSum = 0;
        foreach (var cell in cells.AsEnumerable().Reverse())
        {
            uint cellMask = board[cell.Item1, cell.Item2];
            if (IsValueSet(cellMask))
            {
                currentSum += GetValue(cellMask);
                if (currentSum > sum)
                {
                    return false;
                }
                if (currentSum == sum)
                {
                    currentSum = 0;
                }
            }
            else if (currentSum + MinValue(cellMask) > sum)
            {
                return false;
            }
            else
            {
                break;
            }
        }

        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        var board = sudokuSolver.Board;

        // Limit the splits to the ones that are still possible
        List<List<SumCellsHelper>> possibleSplits = splits.Where(split => split.All(sumHelper => SumIsPossible(sudokuSolver, sumHelper, sum))).ToList();
        if (possibleSplits.Count == 0)
        {
            logicalStepDescription?.Add(new LogicalStepDesc(
                    desc: $"[{SpecificName}] can longer be segmented into sums of {sum}",
                    sourceCandidates: Enumerable.Empty<int>(),
                    elimCandidates: null));

            return LogicResult.Invalid;
        }

        List<int> elims = logicalStepDescription != null ? [] : null;
        LogicResult result = LogicResult.None;

        if (possibleSplits.Count == 1)
        {
            // If there is only one possible split, then we can set the values
            result = PerformElims(sudokuSolver, possibleSplits[0], elims);
        }
        else if (!isBruteForcing)
        {
            // With more than one possible split, we take the intersection of all eliminations for each split
            List<int> intersection = null;
            foreach (var split in possibleSplits)
            {
                var cloneSolver = sudokuSolver.Clone(false);

                List<int> curElims = [];
                var curResult = PerformElims(cloneSolver, split, curElims);
                if (curResult == LogicResult.Invalid)
                {
                    continue;
                }

                if (curResult == LogicResult.None)
                {
                    intersection = [];
                    break;
                }

                if (curResult == LogicResult.Changed)
                {
                    if (intersection == null)
                    {
                        intersection = curElims;
                    }
                    else
                    {
                        intersection = intersection.Intersect(curElims).ToList();
                    }
                }
            }

            if (intersection == null)
            {
                result = LogicResult.Invalid;
            }
            else if (intersection.Count == 0)
            {
                result = LogicResult.None;
            }
            else
            {
                elims = intersection;
                
                if (!sudokuSolver.ClearCandidates(elims))
                {
                    result = LogicResult.Invalid;
                }
                result = LogicResult.Changed;
            }
        }

        if (logicalStepDescription != null)
        {
            if (result == LogicResult.Changed)
            {
                logicalStepDescription?.Add(new LogicalStepDesc(
                        desc: $"[{SpecificName}] Re-evaluated => {sudokuSolver.DescribeElims(elims)}",
                        sourceCandidates: Enumerable.Empty<int>(),
                        elimCandidates: elims));
            }
            else if (result == LogicResult.Invalid)
            {
                logicalStepDescription?.Add(new LogicalStepDesc(
                        desc: $"[{SpecificName}] can no longer be segmented into sums of {sum}",
                        sourceCandidates: Enumerable.Empty<int>(),
                        elimCandidates: null));

                return LogicResult.Invalid;
            }
        }

        return LogicResult.None;
    }

    private LogicResult PerformElims(Solver sudokuSolver, List<SumCellsHelper> split, List<int> elims)
    {
        var board = sudokuSolver.Board;

        List<uint> previousCellValues = elims != null ? cells.Select(cell => board[cell.Item1, cell.Item2]).ToList() : null;
        LogicResult result = LogicResult.None;
        foreach (var sumHelper in split)
        {
            var curResult = sumHelper.StepLogic(sudokuSolver, [sum], null);
            if (curResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }

            if (curResult != LogicResult.None)
            {
                result = curResult;
            }
        }

        if (result == LogicResult.Changed && elims != null)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                uint prevCellValue = previousCellValues[i];
                uint curCellValue = board[cells[i].Item1, cells[i].Item2];
                uint eliminatedValues = prevCellValue & ~curCellValue;
                if (prevCellValue != curCellValue)
                {
                    for (int v = 1; v <= sudokuSolver.MAX_VALUE; v++)
                    {
                        if (HasValue(eliminatedValues, v))
                        {
                            elims.Add(CandidateIndex(cells[i], v));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static bool SumIsPossible(Solver sudokuSolver, SumCellsHelper sumHelper, int sum)
    {
        var (minSum, maxSum) = sumHelper.SumRange(sudokuSolver);
        return sum >= minSum && sum <= maxSum;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription) => InitLinksByRunningLogic(sudokuSolver, cells, logicalStepDescription);

    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => CellsMustContainByRunningLogic(sudokuSolver, cells, value);
}
