namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Slow Thermometer", ConsoleName = "slowthermo")]
public class SlowThermometerConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsSet;

    public SlowThermometerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        List<List<(int, int)>> cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Slow Thermometer constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = [.. cells];
    }

    public SlowThermometerConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells) : base(sudokuSolver, cells.CellNames(""))
    {
        this.cells = [.. cells];
        cellsSet = [.. cells];
    }

    public override string SpecificName => $"Slow Thermometer {CellName(cells[0])} - {CellName(cells[^1])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        bool overallChanged = false;
        int n = cells.Count;
        if (n == 0)
        {
            return LogicResult.None;
        }

        // Cache forced increments due to standard Sudoku rules (same house)
        int[] forcedIncrementsCache = new int[n]; // forcedIncrementsCache[i] = 1 if cells[i-1] and cells[i] must be different, 0 otherwise.
        for (int i = 1; i < n; i++)
        {
            forcedIncrementsCache[i] = sudokuSolver.SeenCells(cells[i - 1]).Contains(cells[i]) ? 1 : 0;
        }

        for (int k = 0; k < n; k++)
        {
            int currentForcedIncrementsFromStart = 0;
            for (int i = 1; i <= k; i++)
            {
                currentForcedIncrementsFromStart += forcedIncrementsCache[i];
            }

            int forcedIncrementsToEnd = 0;
            for (int i = k + 1; i < n; i++)
            {
                forcedIncrementsToEnd += forcedIncrementsCache[i];
            }

            int minAllowedForCellK = 1 + currentForcedIncrementsFromStart;
            int maxAllowedForCellK = MAX_VALUE - forcedIncrementsToEnd;

            uint cellClearMask = 0;
            for (int val = 1; val <= MAX_VALUE; val++)
            {
                if (val < minAllowedForCellK || val > maxAllowedForCellK)
                {
                    cellClearMask |= ValueMask(val);
                }
            }

            if (cellClearMask != 0)
            {
                LogicResult clearResult = sudokuSolver.ClearMask(cells[k].Item1, cells[k].Item2, cellClearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    overallChanged = true;
                }
            }
        }
        return overallChanged ? LogicResult.Changed : LogicResult.None;
    }

    public override bool NeedsEnforceConstraint => false;

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Logic is now handled by weak links and general solver mechanisms.
        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (cells.Count < 2)
        {
            return LogicResult.None;
        }

        for (int idxA = 0; idxA < cells.Count; idxA++)
        {
            (int, int) cellA_coords = cells[idxA];
            uint maskA = solver.Board[cellA_coords.Item1, cellA_coords.Item2];

            for (int valA = 1; valA <= MAX_VALUE; valA++)
            {
                if (!HasValue(maskA, valA))
                {
                    continue;
                }
                int candA_idx = solver.CandidateIndex(cellA_coords, valA);

                for (int idxB = idxA + 1; idxB < cells.Count; idxB++)
                {
                    (int, int) cellB_coords = cells[idxB];
                    uint maskB = solver.Board[cellB_coords.Item1, cellB_coords.Item2];

                    int minValCellBMustTake = valA; // Initial minimum for the start of the segment from cellA

                    // Calculate the true minimum value cellB must take if cellA is valA
                    int currentPathMinVal = valA; // This is the minimum value for cells[idxA]
                    for (int k = idxA; k < idxB; k++) // Iterate path from cells[idxA]...cells[idxB-1] to determine min for cells[k+1]
                    {
                        if (currentPathMinVal > MAX_VALUE)
                        {
                            break; // Path already impossible
                        }

                        (int, int) cellK_coords = cells[k];
                        (int, int) cellK_plus_1_coords = cells[k + 1];

                        bool mustIncrement = false;
                        // Check if (cellK, currentPathMinVal) and (cellK+1, currentPathMinVal) have a weak link
                        if (HasValue(solver.Board[cellK_coords.Item1, cellK_coords.Item2], currentPathMinVal) &&
                            HasValue(solver.Board[cellK_plus_1_coords.Item1, cellK_plus_1_coords.Item2], currentPathMinVal))
                        {
                            int candK_val = solver.CandidateIndex(cellK_coords, currentPathMinVal);
                            int candK_plus_1_val = solver.CandidateIndex(cellK_plus_1_coords, currentPathMinVal);
                            if (solver.IsWeakLink(candK_val, candK_plus_1_val))
                            {
                                mustIncrement = true;
                            }
                        }
                        else if (solver.SeenCells(cellK_coords).Contains(cellK_plus_1_coords))
                        {
                            // If they are in the same house, they must be different,
                            // so if cellK is currentPathMinVal, cellK+1 must be > currentPathMinVal
                            mustIncrement = true;
                        }


                        if (mustIncrement)
                        {
                            currentPathMinVal++; // Minimum for cells[k+1] must be one greater
                        }
                        // else: cells[k+1] can be currentPathMinVal (or greater)
                    }
                    minValCellBMustTake = currentPathMinVal; // This is the min value for cells[idxB]

                    if (minValCellBMustTake > MAX_VALUE && valA <= MAX_VALUE)
                    {
                        // If cellA is valA, cellB would need to be > MAX_VALUE, which is impossible.
                        // This implies valA is not possible for cellA if the thermometer extends to cellB
                        // and has these distinctness requirements.
                        // The weak links below will effectively make candA_idx point to an impossible state for cellB.
                    }

                    for (int valB = 1; valB < minValCellBMustTake; valB++)
                    {
                        if (valB > MAX_VALUE)
                        {
                            break;
                        }

                        if (!HasValue(maskB, valB))
                        {
                            continue;
                        }
                        int candB_idx = solver.CandidateIndex(cellB_coords, valB);
                        solver.AddWeakLink(candA_idx, candB_idx);
                    }
                }
            }
        }
        return LogicResult.None;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        // Logic is now handled by weak links and general solver mechanisms (e.g., AICs).
        return LogicResult.None;
    }

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        List<SlowThermometerConstraint> constraints = new(cells.Count - 1);
        for (int i = 0; i < cells.Count - 1; i++)
        {
            constraints.Add(new SlowThermometerConstraint(sudokuSolver, new (int, int)[] { cells[i], cells[i + 1] }));
        }
        return constraints;
    }
}