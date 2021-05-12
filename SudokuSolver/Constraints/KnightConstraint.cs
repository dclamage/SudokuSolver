using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Anti-Knight", ConsoleName = "knight")]
    public class KnightConstraint : ChessConstraint
    {
        public KnightConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, "1,2") { }
    }
}
