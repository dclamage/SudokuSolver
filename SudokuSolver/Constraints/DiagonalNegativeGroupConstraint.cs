namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Diagonal-", ConsoleName = "dneg")]
public class DiagonalNegativeGroupConstraint : Constraint
{
    private readonly List<(int, int)> group;

    public DiagonalNegativeGroupConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        group = new(HEIGHT);
        for (int i = 0, j = 0; i < HEIGHT; i++, j++)
        {
            group.Add((i, j));
        }
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing) => LogicResult.None;

    public override List<(int, int)> Group => group;
}
