using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Region Sum Lines", ConsoleName = "rsl")]
public class RegionSumLinesConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsSet;
    private List<SumGroup> lineSegments;
    private bool isNoop;

    public override string SpecificName => $"Region Sum Line from {CellName(cells[0])} - {CellName(cells[^1])}";

    public RegionSumLinesConstraint(Solver solver, string options) : base(solver)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Region Sum Lines constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    public override LogicResult InitCandidates(Solver solver)
    {
        int curRegion = 0;
        List<(int, int)>[] groupsPerRegion = new List<(int, int)>[solver.Regions.Length];
        foreach (var group in solver.Groups)
        {
            if (group.GroupType != GroupType.Region)
            {
                continue;
            }

            foreach (var cell in cells)
            {
                if (group.Cells.Contains(cell))
                {
                    groupsPerRegion[curRegion] ??= new();
                    groupsPerRegion[curRegion].Add(cell);
                }
            }
            curRegion++;
        }

        int numLineGroups = groupsPerRegion.Count(list => list != null && list.Count > 0);
        int numLineCells = groupsPerRegion.Sum(list => list?.Count ?? 0);
        if (numLineCells != cells.Count)
        {
            throw new ArgumentException($"Region Sum Line contains cells which have no region.");
        }

        lineSegments = new();
        foreach (var cells in groupsPerRegion)
        {
            if (cells == null || cells.Count == 0)
            {
                continue;
            }

            lineSegments.Add(new SumGroup(solver, cells));
        }

        isNoop = lineSegments.Count <= 1;

        if (isNoop)
        {
            return LogicResult.None;
        }

        SortedSet<int> possibleSums = PossibleSums(solver);
        if (possibleSums == null || possibleSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        bool changed = false;
        foreach (var segment in lineSegments)
        {
            var curLogicResult = segment.RestrictSum(solver, possibleSums);
            if (curLogicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }

            if (curLogicResult == LogicResult.Changed)
            {
                changed = true;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        if (isNoop || !cellsSet.Contains((i, j)))
        {
            return true;
        }

        SortedSet<int> possibleSums = PossibleSums(solver);
        if (possibleSums == null || possibleSums.Count == 0)
        {
            return false;
        }

        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription) => InitLinksByRunningLogic(solver, cells, logicalStepDescription);

    public override LogicResult StepLogic(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        if (isNoop)
        {
            return LogicResult.None;
        }
        var board = solver.Board;

        SortedSet<int> possibleSums = PossibleSums(solver);
        if (possibleSums == null || possibleSums.Count == 0)
        {
            logicalStepDescription?.Add(new($"There are no possible sums that all segments can be.", cells));
            return LogicResult.Invalid;
        }

        uint[] origMasks = null;
        if (logicalStepDescription != null)
        {
            origMasks = new uint[cells.Count];
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                origMasks[cellIndex] = board[i, j];
            }
        }

        bool changed = false;
        foreach (var segment in lineSegments)
        {
            var curLogicResult = segment.RestrictSum(solver, possibleSums);
            if (curLogicResult == LogicResult.Invalid)
            {
                logicalStepDescription?.Add(new($"Cells {solver.CompactName(segment.cells)} cannot be restricted to sum{(possibleSums.Count > 1 ? "s" : "")} {string.Join(",", possibleSums)}.", segment.cells));
                return LogicResult.Invalid;
            }
            changed |= curLogicResult == LogicResult.Changed;
        }

        if (changed && logicalStepDescription != null)
        {
            List<int> elims = new();
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                uint origMask = origMasks[cellIndex];
                if (IsValueSet(origMask))
                {
                    continue;
                }

                uint newMask = board[i, j];
                if (origMask != newMask)
                {
                    uint removedMask = origMask & ~newMask;
                    int minValue = MinValue(removedMask);
                    int maxValue = MaxValue(removedMask);
                    for (int v = minValue; v <= maxValue; v++)
                    {
                        if (HasValue(removedMask, v))
                        {
                            elims.Add(solver.CandidateIndex((i, j), v));
                        }
                    }
                }
            }

            logicalStepDescription?.Add(new(
                desc: $"Restricted to sum{(possibleSums.Count > 1 ? "s" : "")} {string.Join(",", possibleSums)} => {solver.DescribeElims(elims)}",
                sourceCandidates: Enumerable.Empty<int>(),
                elimCandidates: elims));
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private SortedSet<int> PossibleSums(Solver solver)
    {
        SortedSet<int> possibleSums = null;
        foreach (var segment in lineSegments)
        {
            var curSums = segment.PossibleSums(solver);
            if (curSums.Count == 0)
            {
                return null;
            }

            if (possibleSums == null)
            {
                possibleSums = new(curSums);
            }
            else
            {
                possibleSums.IntersectWith(curSums);
                if (possibleSums.Count == 0)
                {
                    return null;
                }
            }
        }
        return possibleSums;
    }
}
