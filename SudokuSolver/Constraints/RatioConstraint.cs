using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Ratio", ConsoleName = "ratio")]
    public class RatioConstraint : OrthogonalValueConstraint
    {
        public RatioConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
        {
        }

        protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 * markerValue == v1 || v1 * markerValue == v0);

        protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
            ((IEnumerable<OrthogonalValueConstraint>)solver.Constraints<DifferenceConstraint>()).Concat(solver.Constraints<AnykropkiConstraint>());

        protected override int DefaultMarkerValue => 2;
    }
}
