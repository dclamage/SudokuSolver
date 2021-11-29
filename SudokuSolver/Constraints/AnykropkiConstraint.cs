using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;
using System.IO;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Anykropki", ConsoleName = "anykropki")]
    public class AnykropkiConstraint : OrthogonalValueConstraint
    {
        public AnykropkiConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
        {
        }

        protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1)
        {
            switch (markerValue)
            {
                case 1:
                    return v0 + 1 == v1 || v1 + 1 == v0 || v0 * 2 == v1 || v1 * 2 == v0;
                case 2:
                    return v0 * 2 != v1 && v1 * 2 != v0;
                case 3:
                    return v0 + 1 != v1 && v1 + 1 != v0;
            }
            return true;
        }

        protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
            ((IEnumerable<OrthogonalValueConstraint>)solver.Constraints<DifferenceConstraint>()).Concat(solver.Constraints<RatioConstraint>());

        protected override int DefaultMarkerValue => 0;
    }
}
