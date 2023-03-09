namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Difference", ConsoleName = "difference")]
public class DifferenceConstraint : OrthogonalValueConstraint
{
    public DifferenceConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
    }

    public DifferenceConstraint(Solver sudokuSolver, int negativeConstraintValue) : base(sudokuSolver, negativeConstraintValue)
    {
    }

    public DifferenceConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2) : base(sudokuSolver, markerValue, cell1, cell2)
    {
    }

    protected override OrthogonalValueConstraint createNegativeConstraint(Solver sudokuSolver, int negativeConstraintValue)
    {
        return new DifferenceConstraint(sudokuSolver, negativeConstraintValue);
    }

    protected override OrthogonalValueConstraint createMarkerConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2)
    {
        return new DifferenceConstraint(sudokuSolver, markerValue, cell1, cell2);
    }

    protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 + markerValue == v1 || v1 + markerValue == v0);

    protected override IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) =>
        solver.Constraints<RatioConstraint>();

    protected override int DefaultMarkerValue => 1;
}
