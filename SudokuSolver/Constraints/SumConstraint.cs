using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Sum", ConsoleName = "sum")]
    public class SumConstraint : OrthogonalValueConstraint
    {
        public SumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
        {
        }

        protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 + v1 == markerValue);

        protected override int DefaultMarkerValue => 5;
    }
}
