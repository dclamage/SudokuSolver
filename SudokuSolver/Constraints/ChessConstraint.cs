namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Chess", ConsoleName = "chess")]
public class ChessConstraint : Constraint
{
    private readonly List<(int, int)> offsets;
    private readonly uint values; // Mask of values to which this constraint applies
    private readonly HashSet<(int, int)> cellsLookup; // If not null, constraint applies only involving these cells

    public ChessConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
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

    public override bool NeedsEnforceConstraint => false;
    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return true; // Enforced by weak links
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        return LogicResult.None;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        bool changed = false;

        for (int rA = 0; rA < HEIGHT; rA++)
        {
            for (int cA = 0; cA < WIDTH; cA++)
            {
                (int rA, int cA) cellA_coords = (rA, cA);
                int cellA_idx = solver.CellIndex(rA, cA);
                uint boardMaskA = solver.Board[cellA_idx];

                foreach ((int, int) offset in offsets) // Use this.offsets
                {
                    int rB = rA + offset.Item1;
                    int cB = cA + offset.Item2;

                    if (rB >= 0 && rB < HEIGHT && cB >= 0 && cB < WIDTH)
                    {
                        (int rB, int cB) cellB_coords = (rB, cB);
                        int cellB_idx = solver.CellIndex(rB, cB);

                        if (cellA_idx >= cellB_idx)
                        {
                            continue;
                        }

                        bool constraintAppliesToPair = cellsLookup == null ||
                                                        cellsLookup.Contains(cellA_coords) ||
                                                        cellsLookup.Contains(cellB_coords);
                        if (!constraintAppliesToPair)
                        {
                            continue;
                        }

                        uint boardMaskB = solver.Board[cellB_idx];
                        uint commonCandidates = boardMaskA & boardMaskB;

                        if (commonCandidates == 0)
                        {
                            continue;
                        }

                        for (int val = 1; val <= MAX_VALUE; val++)
                        {
                            uint valueMask = ValueMask(val);
                            if ((commonCandidates & valueMask) != 0 && (values & valueMask) != 0) // Use this.values
                            {
                                int candA_idx = solver.CandidateIndex(cellA_coords, val);
                                int candB_idx = solver.CandidateIndex(cellB_coords, val);

                                LogicResult linkResult = solver.AddWeakLink(candA_idx, candB_idx);
                                if (linkResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription?.Add(new LogicalStepDesc(
                                        desc: $"{SpecificName}: Link between {solver.CandIndexDesc(candA_idx)} and {solver.CandIndexDesc(candB_idx)} invalidates board.",
                                        [candA_idx, candB_idx],
                                        []
                                    ));
                                    return LogicResult.Invalid;
                                }
                                if (linkResult == LogicResult.Changed)
                                {
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

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