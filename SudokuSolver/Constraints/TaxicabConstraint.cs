namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Taxicab", ConsoleName = "taxi")]
public class TaxicabConstraint : Constraint
{
    private readonly int distance;
    private readonly List<(int, int)> offsets = [];

    public TaxicabConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        if (!int.TryParse(options, out distance) || distance < 1)
        {
            throw new ArgumentException($"Taxicab distance must be a positive integer. Specified options: {options}");
        }

        // Pre-calculate valid offsets.
        // An offset (dx, dy) is part of the taxicab distance if |dx| + |dy| == distance.
        for (int i_offset = -distance; i_offset <= distance; i_offset++)
        {
            for (int j_offset = -distance; j_offset <= distance; j_offset++)
            {
                if (i_offset == 0 && j_offset == 0)
                {
                    continue; // Skip no offset
                }

                if (Math.Abs(i_offset) + Math.Abs(j_offset) == distance)
                {
                    offsets.Add((i_offset, j_offset));
                }
            }
        }
    }

    public override string SpecificName => $"Taxicab Distance {distance}";

    public override bool NeedsEnforceConstraint => false;

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // This logic is now handled by weak links established in InitLinks
        // and the solver's general propagation mechanisms.
        return true;
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
                uint maskA = solver.Board[cellA_idx];
                if (IsValueSet(maskA))
                {
                    continue;
                }

                foreach ((int offsetR, int offsetC) in offsets)
                {
                    int rB = rA + offsetR;
                    int cB = cA + offsetC;

                    if (rB >= 0 && rB < HEIGHT && cB >= 0 && cB < WIDTH)
                    {
                        // Ensure we only process each pair once (e.g., A-B, not B-A again)
                        // A simple way is to only link if cellA_idx < cellB_idx
                        int cellB_idx = solver.CellIndex(rB, cB);
                        if (cellA_idx >= cellB_idx)
                        {
                            continue;
                        }

                        (int rB, int cB) cellB_coords = (rB, cB);
                        uint maskB = solver.Board[cellB_idx];
                        if (IsValueSet(maskB))
                        {
                            continue;
                        }

                        uint commonCandidates = maskA & maskB; // Candidates present in both cells

                        if (commonCandidates != 0)
                        {
                            for (int val = 1; val <= MAX_VALUE; val++)
                            {
                                if (HasValue(commonCandidates, val))
                                {
                                    int candA_idx = solver.CandidateIndex(cellA_coords, val);
                                    int candB_idx = solver.CandidateIndex(cellB_coords, val);

                                    LogicResult linkResult = solver.AddWeakLink(candA_idx, candB_idx);
                                    if (linkResult == LogicResult.Invalid)
                                    {
                                        logicalStepDescription?.Add(new LogicalStepDesc(
                                            desc: $"Taxicab: Adding weak link between {solver.CandIndexDesc(candA_idx)} and {solver.CandIndexDesc(candB_idx)} made board invalid.",
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
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override IEnumerable<(int, int)> SeenCells((int, int) cell)
    {
        (int i0, int j0) = cell;
        foreach ((int offseti, int offsetj) in offsets)
        {
            int i1 = i0 + offseti;
            int j1 = j0 + offsetj;
            if (i1 >= 0 && i1 < WIDTH && j1 >= 0 && j1 < HEIGHT) // Corrected WIDTH to HEIGHT for j1 boundary
            {
                yield return (i1, j1);
            }
        }
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        // The primary logic is handled by weak links and general solver mechanisms.
        return LogicResult.None;
    }
}