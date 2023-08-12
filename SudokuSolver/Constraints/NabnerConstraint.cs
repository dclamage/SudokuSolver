using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Nabner", ConsoleName = "nabner")]
public class NabnerConstraint : Constraint
{
    public readonly List<(int, int)> cells;

    public NabnerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Nabner constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];

        if (cells.Count > MAX_VALUE)
        {
            throw new ArgumentException($"Nabner can only contain up to {MAX_VALUE} cells, but {cells.Count} were provided.");
        }
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Fully enforced by weak links
        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription)
    {
        for (int cellIndex0 = 0; cellIndex0 < cells.Count; cellIndex0++)
        {
            int cellCandidateBase0 = CandidateIndex(cells[cellIndex0], 1);
            for (int cellIndex1 = cellIndex0 + 1; cellIndex1 < cells.Count; cellIndex1++)
            {
                int cellCandidateBase1 = CandidateIndex(cells[cellIndex1], 1);

                for (int value0 = 1; value0 <= MAX_VALUE; value0++)
                {
                    int cellCandidate0 = cellCandidateBase0 + value0 - 1;

                    int value1min = Math.Max(value0 - 1, 1);
                    int value1max = Math.Min(value0 + 1, MAX_VALUE);
                    for (int value1 = value1min; value1 <= value1max; value1++)
                    {
                        int cellCandidate1 = cellCandidateBase1 + value1 - 1;
                        solver.AddWeakLink(cellCandidate0, cellCandidate1);
                    }
                }
            }
        }

        return LogicResult.None;
    }
}
