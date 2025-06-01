namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Self Taxicab", ConsoleName = "selftaxi")]
public class SelfTaxicabConstraint : Constraint
{
    public SelfTaxicabConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        // Options are not used for this constraint type, but the base constructor requires it.
    }

    public override string SpecificName => "Self Taxicab";

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

                if (IsValueSet(maskA)) // If cellA is already set, its specific value will be handled by links from its candidates
                {
                    // Or, if we want to be explicit for set values during initialization:
                    // int valA = GetValue(maskA);
                    // ProcessLinksForCellValue(solver, cellA_coords, cellA_idx, valA, logicalStepDescription, ref changed);
                    // However, iterating through candidates covers this when those candidates are processed.
                    continue;
                }

                for (int valA = 1; valA <= MAX_VALUE; valA++)
                {
                    if (HasValue(maskA, valA))
                    {
                        int distance = valA; // The value in cellA determines the taxicab distance
                        if (distance == 0)
                        {
                            continue; // Distance 0 means same cell, no link to add to itself.
                        }

                        int candA_idx = solver.CandidateIndex(cellA_coords, valA);

                        // Iterate through potential offsets that sum to `distance`
                        for (int dR = -distance; dR <= distance; dR++)
                        {
                            int dC_abs = distance - Math.Abs(dR);
                            if (dC_abs < 0)
                            {
                                continue; // Not possible
                            }

                            int[] dC_values = dC_abs == 0 ? [0] : [dC_abs, -dC_abs];
                            foreach (int dC in dC_values)
                            {
                                if (dR == 0 && dC == 0)
                                {
                                    continue; // No offset, same cell
                                }

                                int rB = rA + dR;
                                int cB = cA + dC;

                                if (rB >= 0 && rB < HEIGHT && cB >= 0 && cB < WIDTH)
                                {
                                    int cellB_idx = solver.CellIndex(rB, cB);
                                    if (cellA_idx >= cellB_idx) // Process each pair once
                                    {
                                        continue;
                                    }

                                    (int rB, int cB) cellB_coords = (rB, cB);
                                    uint maskB = solver.Board[cellB_idx];

                                    if (HasValue(maskB, valA)) // If cellB also has valA as a candidate
                                    {
                                        int candB_idx = solver.CandidateIndex(cellB_coords, valA);
                                        LogicResult linkResult = solver.AddWeakLink(candA_idx, candB_idx);
                                        if (linkResult == LogicResult.Invalid)
                                        {
                                            logicalStepDescription?.Add(new LogicalStepDesc(
                                                desc: $"SelfTaxicab: Link between {solver.CandIndexDesc(candA_idx)} and {solver.CandIndexDesc(candB_idx)} (dist {distance}) invalidates board.",
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
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        // The primary logic is handled by weak links and general solver mechanisms.
        return LogicResult.None;
    }
}