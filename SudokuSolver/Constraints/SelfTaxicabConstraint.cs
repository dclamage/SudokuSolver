namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Self Taxicab", ConsoleName = "selftaxi")]
public class SelfTaxicabConstraint : Constraint
{
    public SelfTaxicabConstraint(Solver sudokuSolver, string _) : base(sudokuSolver)
    {
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        foreach (var cell in SeenCellsByValueMask((i, j), ValueMask(val)))
        {
            if (!sudokuSolver.ClearValue(cell.Item1, cell.Item2, val))
            {
                return false;
            }
        }
        return true;
    }

    public override IEnumerable<(int, int)> SeenCellsByValueMask((int, int) cell, uint mask)
    {
        if (ValueCount(mask) == 1)
        {
            int distance = GetValue(mask);
            var (i0, j0) = cell;
            for (int i1 = i0 - distance; i1 <= i0 + distance; i1++)
            {
                if (i1 < 0 || i1 >= HEIGHT)
                {
                    continue;
                }

                for (int j1 = j0 - distance; j1 <= j0 + distance; j1++)
                {
                    if (j1 < 0 || j1 >= WIDTH)
                    {
                        continue;
                    }

                    if (TaxicabDistance(i0, j0, i1, j1) == distance)
                    {
                        yield return (i1, j1);
                    }
                }
            }
        }
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;
}
