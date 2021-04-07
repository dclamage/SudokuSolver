using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Anti-Camel", ConsoleName = "camel")]
    public class CamelConstraint : Constraint
    {
        public CamelConstraint() { }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => EnforceConstraintBasedOnSeenCells(sudokuSolver, i, j, val);

        public override IEnumerable<(int, int)> SeenCells((int, int) cell)
        {
            var (i, j) = cell;
            if (i - 3 >= 0 && j - 1 >= 0)
            {
                yield return (i - 3, j - 1);
            }
            if (i - 3 >= 0 && j + 1 < WIDTH)
            {
                yield return (i - 3, j + 1);
            }
            if (i - 1 >= 0 && j - 3 >= 0)
            {
                yield return (i - 1, j - 3);
            }
            if (i - 1 >= 0 && j + 3 < WIDTH)
            {
                yield return (i - 1, j + 3);
            }
            if (i + 3 < HEIGHT && j - 1 >= 0)
            {
                yield return (i + 3, j - 1);
            }
            if (i + 3 < HEIGHT && j + 1 < WIDTH)
            {
                yield return (i + 3, j + 1);
            }
            if (i + 1 < HEIGHT && j - 3 >= 0)
            {
                yield return (i + 1, j - 3);
            }
            if (i + 1 < HEIGHT && j + 3 < WIDTH)
            {
                yield return (i + 1, j + 3);
            }
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
    }
}
