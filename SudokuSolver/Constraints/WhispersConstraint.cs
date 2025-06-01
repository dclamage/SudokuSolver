namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Whispers", ConsoleName = "whispers")]
public class WhispersConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int difference;

    private static readonly Regex optionsRegex = new(@"(\d+);(.*)");

    public WhispersConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        Match match = optionsRegex.Match(options);
        if (match.Success)
        {
            difference = int.Parse(match.Groups[1].Value);
            options = match.Groups[2].Value; // This is the cell string part
        }
        else
        {
            // No difference provided, use default
            difference = (MAX_VALUE + 1) / 2;
            // 'options' already contains the cell string
        }

        if (difference < 1 || difference > MAX_VALUE - 1)
        {
            throw new ArgumentException($"Whispers difference must be between 1 and {MAX_VALUE - 1}. Specified difference was: {difference}");
        }

        List<List<(int, int)>> cellGroups = ParseCells(options); // Parse the cell string part
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Whispers constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        if (cells.Count == 0)
        {
            throw new ArgumentException("Whispers constraint cannot be empty.");
        }
    }

    // Constructor used by SplitToPrimitives - this one is fine as it explicitly gets cells and difference
    private WhispersConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells, int difference) : base(sudokuSolver, $"{difference};{string.Join("", cells.Select(CellName))}")
    {
        this.difference = difference;
        this.cells = [.. cells];
        if (this.cells.Count == 0)
        {
            throw new ArgumentException("Whispers constraint (primitive) cannot be empty.");
        }
    }

    public override string SpecificName => $"Whispers {CellName(cells[0])} - {CellName(cells[^1])} (Diff {difference})";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == 0)
        {
            return LogicResult.None;
        }

        uint initialClearMask = 0;
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (v - difference < 1 && v + difference > MAX_VALUE)
            {
                initialClearMask |= ValueMask(v);
            }
        }

        bool changed = false;
        if (initialClearMask != 0)
        {
            foreach ((int r, int c) in cells)
            {
                LogicResult clearResult = sudokuSolver.ClearMask(r, c, initialClearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= clearResult == LogicResult.Changed;
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool NeedsEnforceConstraint => false;

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (cells.Count < 2)
        {
            return LogicResult.None;
        }

        bool overallChanged = false;

        for (int i = 0; i < cells.Count - 1; i++)
        {
            (int, int) cellA_coords = cells[i];
            (int, int) cellB_coords = cells[i + 1];

            uint maskA = solver.Board[cellA_coords.Item1, cellA_coords.Item2];
            uint maskB = solver.Board[cellB_coords.Item1, cellB_coords.Item2];

            for (int valA = 1; valA <= MAX_VALUE; valA++)
            {
                if (!HasValue(maskA, valA))
                {
                    continue;
                }

                int candA_idx = solver.CandidateIndex(cellA_coords, valA);

                for (int valB = 1; valB <= MAX_VALUE; valB++)
                {
                    if (!HasValue(maskB, valB))
                    {
                        continue;
                    }

                    if (Math.Abs(valA - valB) < difference)
                    {
                        int candB_idx = solver.CandidateIndex(cellB_coords, valB);
                        LogicResult linkResult = solver.AddWeakLink(candA_idx, candB_idx);

                        if (linkResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Add(new LogicalStepDesc(
                                $"Adding weak link for {CellName(cellA_coords)}={valA} and {CellName(cellB_coords)}={valB} (difference |{valA}-{valB}| < {difference}) made board invalid.",
                                [candA_idx, candB_idx],
                                []
                            ));
                            return LogicResult.Invalid;
                        }
                        if (linkResult == LogicResult.Changed)
                        {
                            overallChanged = true;
                        }
                    }
                }
            }
        }
        return overallChanged ? LogicResult.Changed : LogicResult.None;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        // The primary pairwise logic is now handled by weak links established in InitLinks.
        // More complex multi-cell interactions or specific patterns might have been in the old StepLogic.
        // For now, we rely on the solver's general mechanisms (AICs, etc.) acting on these weak links.
        return LogicResult.None;
    }

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        // This method is used by the solver for IsInheritOf logic, not for the constraint's own solving.
        if (cells.Count <= 1)
        {
            return [];
        }

        List<WhispersConstraint> primitives = new(cells.Count - 1);
        for (int i = 0; i < cells.Count - 1; i++)
        {
            // Create a new Whispers constraint for the pair of adjacent cells.
            int cellIndex0 = sudokuSolver.CellIndex(cells[i]);
            int cellIndex1 = sudokuSolver.CellIndex(cells[i + 1]);
            (int, int) cell0 = sudokuSolver.CellIndexToCoord(cellIndex0 < cellIndex1 ? cellIndex0 : cellIndex1);
            (int, int) cell1 = sudokuSolver.CellIndexToCoord(cellIndex0 < cellIndex1 ? cellIndex1 : cellIndex0);
            primitives.Add(new WhispersConstraint(sudokuSolver, [cell0, cell1], difference));
        }
        return primitives;
    }

    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value)
    {
        return CellsMustContainByRunningLogic(sudokuSolver, cells, value);
    }
}