namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Diagonal+", ConsoleName = "dpos")]
public class DiagonalPositiveGroupConstraint : Constraint
{
    private readonly List<(int, int)> group;

    public DiagonalPositiveGroupConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        group = new(WIDTH);
        for (int i = HEIGHT - 1, j = 0; j < WIDTH; i--, j++)
        {
            group.Add((i, j));
        }
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;

    public override List<(int, int)> Group => group;
}
