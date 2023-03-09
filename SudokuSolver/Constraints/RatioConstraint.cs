namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Ratio", ConsoleName = "ratio")]
public class RatioConstraint : OrthogonalValueConstraint
{
    public RatioConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
    }

    public RatioConstraint(Solver sudokuSolver, int negativeConstraintValue) : base(sudokuSolver, negativeConstraintValue)
    {
    }

    public RatioConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2) : base(sudokuSolver, markerValue, cell1, cell2)
    {
    }

    protected override OrthogonalValueConstraint createNegativeConstraint(Solver sudokuSolver, int negativeConstraintValue)
    {
        return new RatioConstraint(sudokuSolver, negativeConstraintValue);
    }

    protected override OrthogonalValueConstraint createMarkerConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2)
    {
        return new RatioConstraint(sudokuSolver, markerValue, cell1, cell2);
    }

    protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 * markerValue == v1 || v1 * markerValue == v0);

    protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
        solver.Constraints<DifferenceConstraint>();

    protected override int DefaultMarkerValue => 2;
}
