using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Anti-King", ConsoleName = "king", FPuzzlesName = "antiking")]
    public class KingConstraint : Constraint
    {
        public KingConstraint(string _) { }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => EnforceConstraintBasedOnSeenCells(sudokuSolver, i, j, val);

        public override IEnumerable<(int, int)> SeenCells((int, int) cell)
        {
            var (i, j) = cell;
            if (i - 1 >= 0 && j - 1 >= 0)
            {
                yield return (i - 1, j - 1);
            }
            if (i - 1 >= 0 && j + 1 < WIDTH)
            {
                yield return (i - 1, j + 1);
            }
            if (i + 1 < HEIGHT && j - 1 >= 0)
            {
                yield return (i + 1, j - 1);
            }
            if (i + 1 < HEIGHT && j + 1 < WIDTH)
            {
                yield return (i + 1, j + 1);
            }
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            return LogicResult.None;
        }
    }
}
