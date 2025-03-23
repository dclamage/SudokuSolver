﻿namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Ratio", ConsoleName = "ratio")]
public class RatioConstraint : OrthogonalValueConstraint
{
    public RatioConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
    }

    protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 * markerValue == v1 || v1 * markerValue == v0);

    protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
        solver.Constraints<DifferenceConstraint>();

    protected override int DefaultMarkerValue => 2;
}
