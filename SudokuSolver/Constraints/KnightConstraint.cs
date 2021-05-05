using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Anti-Knight", ConsoleName = "knight", FPuzzlesName = "antiknight")]
    public class KnightConstraint : Constraint
    {
        public KnightConstraint(Solver sudokuSolver, string options) : base(sudokuSolver) { }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => EnforceConstraintBasedOnSeenCells(sudokuSolver, i, j, val);

        public override IEnumerable<(int, int)> SeenCells((int, int) cell)
        {
            var (i, j) = cell;
            if (i - 2 >= 0 && j - 1 >= 0)
            {
                yield return (i - 2, j - 1);
            }
            if (i - 2 >= 0 && j + 1 < WIDTH)
            {
                yield return (i - 2, j + 1);
            }
            if (i - 1 >= 0 && j - 2 >= 0)
            {
                yield return (i - 1, j - 2);
            }
            if (i - 1 >= 0 && j + 2 < WIDTH)
            {
                yield return (i - 1, j + 2);
            }
            if (i + 2 < HEIGHT && j - 1 >= 0)
            {
                yield return (i + 2, j - 1);
            }
            if (i + 2 < HEIGHT && j + 1 < WIDTH)
            {
                yield return (i + 2, j + 1);
            }
            if (i + 1 < HEIGHT && j - 2 >= 0)
            {
                yield return (i + 1, j - 2);
            }
            if (i + 1 < HEIGHT && j + 2 < WIDTH)
            {
                yield return (i + 1, j + 2);
            }
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
    }
}
