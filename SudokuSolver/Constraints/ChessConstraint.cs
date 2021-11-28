namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Chess", ConsoleName = "chess")]
public class ChessConstraint : Constraint
{
    private readonly List<(int, int)> offsets;
    private readonly uint values;
    private readonly SortedSet<(int, int)> cellsLookup;

    public ChessConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        offsets = new();
        values = ALL_VALUES_MASK;
        bool valuesCleared = false;

        string[] split = options.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0)
        {
            throw new ArgumentException("Chess Constraint: At least one symmetric offset is required.");
        }

        List<(int, int)> cells = new();
        HashSet<(int, int)> offsetHash = new();
        foreach (string param in split)
        {
            if (param.Length == 0)
            {
                continue;
            }

            if (param[0] == 'v')
            {
                if (!valuesCleared)
                {
                    values = 0;
                    valuesCleared = true;
                }

                string[] valuesSplit = param[1..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (string valueStr in valuesSplit)
                {
                    if (!int.TryParse(valueStr, out int v))
                    {
                        throw new ArgumentException($"Chess Constraint: Invalid value: {valueStr}");
                    }

                    if (v >= 1 && v <= MAX_VALUE)
                    {
                        values |= ValueMask(v);
                    }
                    else
                    {
                        throw new ArgumentException($"Chess Constraint: Value out of range {valueStr}");
                    }
                }
                continue;
            }

            if (param[0] == 'r' || param[0] == 'R')
            {
                var groups = ParseCells(param);
                foreach (var group in groups)
                {
                    cells.AddRange(group);
                }
                continue;
            }

            string[] offsetSplit = param.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (offsetSplit.Length != 2)
            {
                throw new ArgumentException($"Chess Constraint: Invalid symmetric offset: {param}");
            }
            if (!int.TryParse(offsetSplit[0], out int offset0))
            {
                throw new ArgumentException($"Chess Constraint: Invalid symmetric offset: {param}");
            }
            if (!int.TryParse(offsetSplit[1], out int offset1))
            {
                throw new ArgumentException($"Chess Constraint: Invalid symmetric offset: {param}");
            }
            offset0 = Math.Abs(offset0);
            offset1 = Math.Abs(offset1);
            for (uint i = 0; i < 4; i++)
            {
                int sign0 = (i & 1) == 0 ? -1 : 1;
                int sign1 = (i & 2) == 0 ? -1 : 1;
                offsetHash.Add((offset0 * sign0, offset1 * sign1));
                offsetHash.Add((offset1 * sign1, offset0 * sign0));
            }
        }
        offsets = offsetHash.ToList();

        if (cells.Count > 0)
        {
            cellsLookup = new(cells);
        }
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing) => LogicResult.None;

    public override IEnumerable<(int, int)> SeenCells((int, int) cell)
    {
        if (values == ALL_VALUES_MASK)
        {
            foreach (var offset in offsets)
            {
                int i = cell.Item1 + offset.Item1;
                int j = cell.Item2 + offset.Item2;
                if (i >= 0 && i < HEIGHT && j >= 0 && j < WIDTH)
                {
                    if (cellsLookup == null || cellsLookup.Contains((i, j)) || cellsLookup.Contains(cell))
                    {
                        yield return (i, j);
                    }
                }
            }
        }
    }

    public override IEnumerable<(int, int)> SeenCellsByValueMask((int, int) cell, uint mask)
    {
        if ((values & mask) != 0)
        {
            foreach (var offset in offsets)
            {
                int i = cell.Item1 + offset.Item1;
                int j = cell.Item2 + offset.Item2;
                if (i >= 0 && i < HEIGHT && j >= 0 && j < WIDTH)
                {
                    if (cellsLookup == null || cellsLookup.Contains((i, j)) || cellsLookup.Contains(cell))
                    {
                        yield return (i, j);
                    }
                }
            }
        }
    }
}
