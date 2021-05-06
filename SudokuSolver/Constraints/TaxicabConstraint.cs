using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Taxicab", ConsoleName = "taxi")]
    public class TaxicabConstraint : Constraint
    {
        private readonly int distance;

        public TaxicabConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            distance = int.Parse(options);
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => EnforceConstraintBasedOnSeenCells(sudokuSolver, i, j, val);

        public override IEnumerable<(int, int)> SeenCells((int, int) cell)
        {
            var (i0, j0) = cell;
            for (int i1 = i0 - distance; i1 <= i0 + distance; i1++)
            {
                if (i1 < 0 || i1 >= HEIGHT)
                {
                    continue;
                }
                for (int j1 = j0 - distance; j1 <= j0 + distance; j1++)
                {
                    if (j1 < 0 || j1 >= HEIGHT)
                    {
                        continue;
                    }

                    if (TaxicabDistance(i0, j0, i1, j1) == distance)
                    {
                        yield return (i1, j1);
                    }
                }
            }
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
    }
}
