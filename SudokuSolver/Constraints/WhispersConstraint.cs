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

        cellIndices = [.. cells.Select(sudokuSolver.CellIndex)];
        compatibleMask = BuildCompatibleMasks(MAX_VALUE, difference);
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

        cellIndices = [.. this.cells.Select(sudokuSolver.CellIndex)];
        compatibleMask = BuildCompatibleMasks(MAX_VALUE, difference);
    }

    /// <summary>Cell indices of the line, in order. Immutable after construction.</summary>
    private readonly List<int> cellIndices;

    /// <summary>
    /// <c>compatibleMask[v - 1]</c> is the set of values a neighbour may take when this cell is
    /// <c>v</c>. Immutable after construction, which matters because a constraint instance is shared
    /// by reference across every search-tree clone and every thread.
    /// </summary>
    private readonly uint[] compatibleMask;

    private static uint[] BuildCompatibleMasks(int maxValue, int difference)
    {
        uint[] masks = new uint[maxValue];
        for (int v = 1; v <= maxValue; v++)
        {
            uint mask = 0;
            for (int w = 1; w <= maxValue; w++)
            {
                if (Math.Abs(v - w) >= difference)
                {
                    mask |= ValueMask(w);
                }
            }
            masks[v - 1] = mask;
        }
        return masks;
    }

    /// <summary>
    /// The values a neighbour could take if this cell is restricted to <paramref name="mask"/> —
    /// ISS's <c>tables[i][mask]</c>, computed on the fly because the per-value table is
    /// <see cref="MAX_VALUE"/> entries rather than 2^<see cref="MAX_VALUE"/>.
    /// </summary>
    private uint SupportedByAnyOf(uint mask)
    {
        uint support = 0;
        while (mask != 0)
        {
            int v = MinValue(mask);
            mask &= ~ValueMask(v);
            support |= compatibleMask[v - 1];
        }
        return support;
    }

    /// <summary>
    /// Only re-run when a cell on the line changes, rather than on every propagation step.
    /// <see cref="Group"/> is null here — a whisper does not make its cells distinct — so the base
    /// implementation would put this in the always-run bucket.
    /// </summary>
    public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;

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

    /// <summary>
    /// Pairwise arc consistency, the deduction ISS gets from <c>BinaryConstraint.enforceConsistency</c>:
    /// a value survives in one cell only if some value in the neighbouring cell is compatible with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to return <see cref="LogicResult.None"/> on the grounds that the weak links from
    /// <see cref="InitLinks"/> enforce the constraint. They do enforce it — but **weak links only
    /// fire on <c>SetValue</c>**, so they give nothing at candidate level, and the brute-force
    /// propagation loop runs only singles plus each constraint's <c>StepLogic</c>. A whisper
    /// therefore contributed no propagation at all while brute forcing, which is why whisper puzzles
    /// were the slowest class in the ISS corpus. See docs/renban-required-values.md.
    /// </para>
    /// <para>
    /// One forward sweep then one backward sweep reaches a fixpoint over the whole line in a single
    /// call, which is what ISS's prefix/suffix pass buys. Eliminations are applied as they are found
    /// so later pairs see them.
    /// </para>
    /// </remarks>
    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        int numCells = cellIndices.Count;
        if (numCells < 2)
        {
            return LogicResult.None;
        }

        bool changed = false;

        for (int i = 0; i + 1 < numCells; i++)
        {
            LogicResult result = EnforcePair(sudokuSolver, cellIndices[i], cellIndices[i + 1], logicalStepDescription, ref changed);
            if (result == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
        }

        for (int i = numCells - 2; i >= 0; i--)
        {
            LogicResult result = EnforcePair(sudokuSolver, cellIndices[i], cellIndices[i + 1], logicalStepDescription, ref changed);
            if (result == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private LogicResult EnforcePair(Solver sudokuSolver, int cellIndex0, int cellIndex1, List<LogicalStepDesc> logicalStepDescription, ref bool changed)
    {
        BoardView board = sudokuSolver.Board;
        uint mask0 = board[cellIndex0];
        uint mask1 = board[cellIndex1];
        uint cand0 = mask0 & ~valueSetMask;
        uint cand1 = mask1 & ~valueSetMask;

        uint keep0 = cand0 & SupportedByAnyOf(cand1);
        uint keep1 = cand1 & SupportedByAnyOf(cand0);

        LogicResult result = ApplyKeep(sudokuSolver, cellIndex0, mask0, cand0, keep0, cellIndex1, cand1, logicalStepDescription, ref changed);
        if (result == LogicResult.Invalid)
        {
            return LogicResult.Invalid;
        }

        // keep1 was computed against the pre-elimination cand0, which can only make it a superset of
        // the truth — sound, and the backward sweep or the next propagation step tightens it.
        return ApplyKeep(sudokuSolver, cellIndex1, mask1, cand1, keep1, cellIndex0, cand0, logicalStepDescription, ref changed);
    }

    private LogicResult ApplyKeep(Solver sudokuSolver, int cellIndex, uint mask, uint cand, uint keep, int otherCellIndex, uint otherCand, List<LogicalStepDesc> logicalStepDescription, ref bool changed)
    {
        if (keep == cand)
        {
            return LogicResult.None;
        }

        if (IsValueSet(mask))
        {
            // A safety net rather than a live deduction: SetValue applies this pair's weak links, so
            // a solved cell's neighbour never keeps an unsupported value in the first place.
            logicalStepDescription?.Add(new LogicalStepDesc(
                desc: $"{CellName(sudokuSolver.CellIndexToCoord(cellIndex))}={GetValue(mask)} has no value at least {difference} away in {CellName(sudokuSolver.CellIndexToCoord(otherCellIndex))} ({MaskToString(otherCand)}).",
                highlightCells: [sudokuSolver.CellIndexToCoord(cellIndex), sudokuSolver.CellIndexToCoord(otherCellIndex)]
            ));
            return LogicResult.Invalid;
        }

        LogicResult keepResult = sudokuSolver.KeepMask(cellIndex, keep);
        if (keepResult == LogicResult.Invalid)
        {
            logicalStepDescription?.Add(new LogicalStepDesc(
                desc: $"No value in {CellName(sudokuSolver.CellIndexToCoord(cellIndex))} is at least {difference} away from a value in {CellName(sudokuSolver.CellIndexToCoord(otherCellIndex))} ({MaskToString(otherCand)}).",
                highlightCells: [sudokuSolver.CellIndexToCoord(cellIndex), sudokuSolver.CellIndexToCoord(otherCellIndex)]
            ));
            return LogicResult.Invalid;
        }

        if (keepResult == LogicResult.Changed)
        {
            changed = true;
            logicalStepDescription?.Add(new LogicalStepDesc(
                desc: $"{MaskToString(otherCand)}{CellName(sudokuSolver.CellIndexToCoord(otherCellIndex))} => {sudokuSolver.DescribeElims(sudokuSolver.CandidateIndexes(cand & ~keep, [sudokuSolver.CellIndexToCoord(cellIndex)]).ToList())}",
                sourceCandidates: sudokuSolver.CandidateIndexes(otherCand, [sudokuSolver.CellIndexToCoord(otherCellIndex)]),
                elimCandidates: sudokuSolver.CandidateIndexes(cand & ~keep, [sudokuSolver.CellIndexToCoord(cellIndex)])
            ));
        }

        return keepResult;
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