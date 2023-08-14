using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Region Sum Lines", ConsoleName = "rsl")]
public class RegionSumLinesConstraint : EqualSumsConstraint
{
    public override string SpecificName => $"Region Sum Line from {CellName(cells[0])} - {CellName(cells[^1])}";

    public readonly List<(int, int)> lineCells;

    public RegionSumLinesConstraint(Solver solver, string options) : base(solver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Region Sum Lines constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        lineCells = cellGroups[0];
    }

    protected override List<List<(int, int)>> GetCellGroups(Solver solver)
    {
        SudokuGroup lastRegion = null;
        List<List<(int, int)>> cellGroups = new();
        foreach (var cell in lineCells)
        {
            SudokuGroup curRegion = solver.Groups
                .Where(group => group.GroupType == GroupType.Region && group.Cells.Contains(cell))
                .FirstOrDefault();
            if (curRegion == null)
            {
                continue;
            }

            if (lastRegion != curRegion)
            {
                cellGroups.Add(new());
            }
            cellGroups[^1].Add(cell);
            lastRegion = curRegion;
        }

        return cellGroups;
    }
}
