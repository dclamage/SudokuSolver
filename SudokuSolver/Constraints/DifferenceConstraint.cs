using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Difference", ConsoleName = "difference", FPuzzlesName = "difference")]
    public class DifferenceConstraint : OrthogonalValueConstraint
    {
        public DifferenceConstraint(string options) : base(options)
        {
        }

        protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 + markerValue == v1 || v1 + markerValue == v0);

        protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
            solver.Constraints<RatioConstraint>();

        protected override int DefaultMarkerValue => 1;
    }
}
