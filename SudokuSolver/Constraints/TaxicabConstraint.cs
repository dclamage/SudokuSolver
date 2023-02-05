namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Taxicab", ConsoleName = "taxi")]
public class TaxicabConstraint : Constraint
{
    private readonly int distance;
    private readonly List<(int, int)> offsets = new();

    public TaxicabConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        distance = int.Parse(options);

        for (int i1 = -distance; i1 <= distance; i1++)
        {
            if (i1 == 0)
            {
                continue;
            }
            for (int j1 = -distance; j1 <= distance; j1++)
            {
                if (j1 == 0)
                {
                    continue;
                }

                if (Math.Abs(i1) + Math.Abs(j1) == distance)
                {
                    offsets.Add((i1, j1));
                }
            }
        }
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => EnforceConstraintBasedOnSeenCells(sudokuSolver, i, j, val);

    public override IEnumerable<(int, int)> SeenCells((int, int) cell)
    {
        var (i0, j0) = cell;
        foreach (var (offseti, offsetj) in offsets)
        {
            int i1 = i0 + offseti;
            int j1 = j0 + offsetj;
            if (i1 >= 0 && i1 < WIDTH && j1 >= 0 && j1 < WIDTH)
            {
                yield return (i1, j1);
            }
        }
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
}
