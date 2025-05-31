namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Thermometer", ConsoleName = "thermo")]
public class ThermometerConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsSet;

    public ThermometerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        List<List<(int, int)>> cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Thermometer constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = [.. cells];
    }

    public ThermometerConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells) : base(sudokuSolver, cells.CellNames(""))
    {
        this.cells = cells.ToList();
        cellsSet = [.. cells];
    }

    public override string SpecificName => $"Thermometer {CellName(cells[0])} - {CellName(cells[^1])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        bool changed = false;
        (int firsti, int firstj) = cells[0];
        (int lasti, int lastj) = cells[^1];
        uint firstMask = sudokuSolver.Board[firsti, firstj];
        uint lastMask = sudokuSolver.Board[lasti, lastj];
        int minVal = MinValue(firstMask & ~valueSetMask);
        int maxVal = MaxValue(lastMask & ~valueSetMask) - cells.Count + 1;
        uint clearMask = ALL_VALUES_MASK;
        for (int val = minVal; val <= maxVal; val++)
        {
            clearMask &= ~ValueMask(val);
        }
        foreach ((int i, int j) in cells)
        {
            LogicResult clearResult = sudokuSolver.ClearMask(i, j, clearMask);
            if (clearResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= clearResult == LogicResult.Changed;
            clearMask = (clearMask << 1) | 1u;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    // This property indicates to the solver that EnforceConstraint does not need to be called.
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

        // Iterate over all distinct pairs of cells (cellA_coords, cellB_coords) in the thermometer
        // such that cellA_coords appears before cellB_coords.
        for (int idxA = 0; idxA < cells.Count; idxA++)
        {
            var cellA_coords = cells[idxA]; // (row, col)

            for (int idxB = idxA + 1; idxB < cells.Count; idxB++)
            {
                var cellB_coords = cells[idxB]; // (row, col)
                // delta is the minimum difference in value between cellA and cellB.
                // (e.g. if idxB = idxA + 1, delta = 1, so valB must be at least valA + 1)
                int delta = idxB - idxA;

                uint maskA = solver.Board[cellA_coords.Item1, cellA_coords.Item2];
                uint maskB = solver.Board[cellB_coords.Item1, cellB_coords.Item2];

                // Iterate through all possible values for cellA_coords.
                // valA can be at most MAX_VALUE - delta, because cellB_coords's value must be at least valA + delta.
                for (int valA = 1; valA <= MAX_VALUE - delta; valA++)
                {
                    // Check if valA is a candidate for cellA_coords.
                    if (!HasValue(maskA, valA))
                    {
                        continue;
                    }
                    int candA_idx = solver.CandidateIndex(cellA_coords, valA);

                    // If cellA_coords is valA, then cellB_coords must be >= valA + delta.
                    // Therefore, (cellA_coords, valA) has a weak link with (cellB_coords, valB)
                    // for all valB < valA + delta.
                    for (int valB = 1; valB < valA + delta; valB++)
                    {
                        // Check if valB is a candidate for cellB_coords.
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

    public override List<(int, int)> Group => cells;

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        List<ThermometerConstraint> constraints = new(cells.Count - 1);
        for (int i = 0; i < cells.Count - 1; i++)
        {
            constraints.Add(new(sudokuSolver, new (int, int)[] { cells[i], cells[i + 1] }));
        }
        return constraints;
    }
}