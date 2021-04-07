using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Diagonal+", ConsoleName = "dpos", FPuzzlesName = "diagonal+")]
    public class DiagonalPositiveGroupConstraint : Constraint
    {
        public DiagonalPositiveGroupConstraint(string _)
        {
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;

        public override List<(int, int)> Group
        {
            get
            {
                if (_group != null)
                {
                    return _group;
                }

                _group = new(WIDTH);
                for (int i = HEIGHT - 1, j = 0; j < WIDTH; i--, j++)
                {
                    _group.Add((i, j));
                }
                return _group;
            }
        }
        private List<(int, int)> _group = null;
    }
}
