using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Modular Line", ConsoleName = "modularline")]
public class ModularLineConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int mod = 3;
    private readonly HashSet<(int, int)> cellsSet;


    public ModularLineConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Modular line constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    public override string SpecificName => $"Modular Line from {CellName(cells[0])} - {CellName(cells[^1])}";

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription)
    {
        for (int i0 = 0; i0 < cells.Count; i0++)
        {
            var cell0 = cells[i0];
            for (int i1 = i0 + 1; i1 < cells.Count; i1++)
            {
                var cell1 = cells[i1];
                
                for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                {
                    int candIndex0 = CandidateIndex(cell0, v0);

                    if (i0 % mod == i1 % mod)
                    {
                        for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                        {
                            int candIndex1 = CandidateIndex(cell1, v1);
                            if (v0 % mod != v1 % mod)
                            {
                                sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                            }
                        }
                    }
                    else
                    {
                        for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                        {
                            int candIndex1 = CandidateIndex(cell1, v1);
                            if (v0 % mod == v1 % mod)
                            {
                                sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                            }
                        }
                    }
                }
            }
        }

        return LogicResult.None;
    }

}
