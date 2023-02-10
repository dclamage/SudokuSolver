namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Sum", ConsoleName = "sum")]
public class SumConstraint : OrthogonalValueConstraint
{
    public SumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
    }

    public SumConstraint(Solver sudokuSolver, int negativeConstraintValue) : base(sudokuSolver, negativeConstraintValue)
    {
    }

    public SumConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2) : base(sudokuSolver, markerValue, cell1, cell2)
    {
    }

    protected override bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1) => (v0 + v1 == markerValue);

    protected override int DefaultMarkerValue => 5;
}
