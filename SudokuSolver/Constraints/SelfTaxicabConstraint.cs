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


    public override IEnumerable<(int, int)> SeenCellsByValueMask((int, int) cell, uint mask)
    {
        // This method defines which cells are "seen" by 'cell' if 'cell' contains a value from 'mask'.
        // For SelfTaxicab, if 'mask' represents a single value 'd', then 'seen' cells are those
        // at taxicab distance 'd'.
        if (ValueCount(mask) == 1)
        {
            int distance = GetValue(mask);
            if (distance == 0)
            {
                yield break;
            }

            (int i0, int j0) = cell;

            for (int dR = -distance; dR <= distance; dR++)
            {
                int dC_abs = distance - Math.Abs(dR);
                if (dC_abs < 0)
                {
                    continue;
                }

                int[] dC_values = dC_abs == 0 ? [0] : [dC_abs, -dC_abs];
                foreach (int dC in dC_values)
                {
                    if (dR == 0 && dC == 0)
                    {
                        continue; // No offset means same cell, not "seen" in this context
                    }

                    int i1 = i0 + dR;
                    int j1 = j0 + dC;

                    if (i1 >= 0 && i1 < HEIGHT && j1 >= 0 && j1 < WIDTH)
                    {
                        // Double check the condition, though the loop structure should ensure it.
                        if (TaxicabDistance(i0, j0, i1, j1) == distance)
                        {
                            yield return (i1, j1);
                        }
                    }
                }
            }
        }
        // If mask has multiple values, the concept of "seen cells" becomes ambiguous
        // for SelfTaxicab as the distance changes with the value.
        // The base implementation or specific handling might be needed if this scenario is critical.
        // For now, it's most clearly defined for a single value in the mask.
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        // The primary logic is handled by weak links and general solver mechanisms.
        return LogicResult.None;
    }
}