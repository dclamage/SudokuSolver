namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Skyscraper", ConsoleName = "skyscraper")]
public class SkyscraperConstraint : Constraint
{
    public readonly int clue;
    public readonly (int, int) cellStart;
    private readonly List<(int, int)> cells;
    private readonly int[] cellIndices;
    private readonly HashSet<(int, int)> cellsLookup;
    private readonly string specificName;
    private bool needsLogic = true;

    public override string SpecificName => specificName;

    private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
    public SkyscraperConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"Skyscraper options \"{options}\" invalid. Expecting: \"SrXcY\"");
        }

        clue = int.Parse(match.Groups[1].Value);
        cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

        bool isCol = cellStart.Item1 < 0 || cellStart.Item1 >= MAX_VALUE;
        bool isRow = cellStart.Item2 < 0 || cellStart.Item2 >= MAX_VALUE;

        if (isRow && isCol || !isRow && !isCol)
        {
            throw new ArgumentException($"Skyscraper options \"{options}\" has invalid location.");
        }

        cells = new();
        if (isRow)
        {
            int i = cellStart.Item1;
            for (int j = 0; j < WIDTH; j++)
            {
                cells.Add((i, j));
            }
            if (cellStart.Item2 >= WIDTH)
            {
                cells.Reverse();
            }
        }
        else
        {
            int j = cellStart.Item2;
            for (int i = 0; i < HEIGHT; i++)
            {
                cells.Add((i, j));
            }
            if (cellStart.Item1 >= HEIGHT)
            {
                cells.Reverse();
            }
        }
        cellsLookup = new(cells);
        cellIndices = [.. cells.Select(sudokuSolver.CellIndex)];

        specificName = $"Skyscraper {clue} at {CellName(cellStart)}";
    }

    // Watch only this line's cells so brute-force propagation re-runs this constraint when one of
    // them changes, instead of on every step (it has no Group, so it would otherwise always run).
    public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;

    // The support search asks, for every candidate it considers, whether it is weakly linked to any
    // candidate already assigned on the line -- up to MAX_VALUE - 1 of them, re-asked at every node
    // of a backtracking search. Answering that with one binary search per assigned candidate made
    // this constraint 99.8% of all IsWeakLink calls in the benchmark corpus and essentially the
    // whole runtime of skyscraper-search. See docs/weak-link-bitmatrix-exploration.md.
    public override bool WantsWeakLinkMatrix => true;

    public override LogicResult InitCandidates(Solver solver)
    {
        var board = solver.Board;
        if (!needsLogic)
        {
            return LogicResult.None;
        }

        // A clue of 1 means the max value must be in the first cell
        if (clue == 1)
        {
            needsLogic = false;

            var (i, j) = cells[0];
            uint keepMask = ValueMask(MAX_VALUE);
            return solver.KeepMask(i, j, keepMask);
        }

        // A clue of MAX_VALUE means that all digits must be in strict order
        bool changed = false;
        if (clue == MAX_VALUE)
        {
            needsLogic = false;

            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint keepMask = ValueMask(v);
                var (i, j) = cells[v - 1];
                var logicResult = solver.KeepMask(i, j, keepMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    changed = true;
                }
            }
        }
        else
        {
            // Restrict high digits
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                int maxVal = MAX_VALUE - clue + 1 + cellIndex;
                if (maxVal < MAX_VALUE)
                {
                    uint keepMask = MaskValAndLower(maxVal);
                    var logicResult = solver.KeepMask(i, j, keepMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        changed = true;
                    }
                }
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>Measured: 1.20 ms / 16.5% fire, corpus.json; see docs/pathological-outliers.md.</summary>

    public override int BruteForcePropagationCost => 7293000;


    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        if (!needsLogic || !cellsLookup.Contains((i, j)))
        {
            return true;
        }

        var board = solver.Board;
        int numSeen = 0;
        int minValueSeen = 0;
        bool haveUnset = false;
        foreach (var (i1, j1) in cells)
        {
            uint curMask = board[i1, j1];
            if (!IsValueSet(curMask) && ValueCount(curMask) > 1)
            {
                haveUnset = true;
                break;
            }
            else
            {
                int curVal = GetValue(curMask);
                if (curVal > minValueSeen)
                {
                    numSeen++;
                    minValueSeen = curVal;
                }
            }
        }

        return haveUnset && numSeen <= clue || !haveUnset && numSeen == clue;
    }

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (!needsLogic)
        {
            return LogicResult.None;
        }

        int n = cellIndices.Length;
        uint[] board = solver.BoardArray;

        // Allocation-free scratch (n <= MAX_VALUE <= 31).
        Span<uint> candidateMasks = stackalloc uint[n];
        Span<uint> supportMasks = stackalloc uint[n];
        Span<int> assigned = stackalloc int[n];
        supportMasks.Clear();

        for (int pos = 0; pos < n; pos++)
        {
            uint mask = board[cellIndices[pos]] & ALL_VALUES_MASK;
            if (mask == 0)
            {
                return LogicResult.Invalid;
            }
            candidateMasks[pos] = mask;
        }

        // Depth-first support search: find full-line assignments that are all-different, respect the
        // solver's existing weak links, and have exactly `clue` visible buildings, accumulating the
        // supported value at each position. This preserves the old permutation-filter semantics
        // (which called CanPlaceDigits) without allocating or enumerating every permutation.
        // One bit per candidate assigned so far, when the solver has the matrix to test against.
        Span<ulong> assignedCandidates = solver.HasWeakLinkMatrix
            ? stackalloc ulong[solver.WeakLinkMatrixWords]
            : default;
        SkyscraperSearch(solver, candidateMasks, supportMasks, assigned, 0, 0u, 0, 0, assignedCandidates);

        bool changed = false;
        List<int> elims = null;
        for (int pos = 0; pos < n; pos++)
        {
            int cellIndex = cellIndices[pos];
            uint cur = board[cellIndex] & ALL_VALUES_MASK;
            uint support = supportMasks[pos];

            if ((cur & support) == 0)
            {
                logicalStepDescription?.Append($"Clue value {clue} is impossible.");
                return LogicResult.Invalid;
            }

            if (IsValueSet(board[cellIndex]))
            {
                continue;
            }

            uint elimMask = cur & ~support;
            if (elimMask == 0)
            {
                continue;
            }

            var (i, j) = cells[pos];
            var logicResult = solver.KeepMask(i, j, support);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (logicResult == LogicResult.Changed)
            {
                if (logicalStepDescription != null)
                {
                    elims ??= new();
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (HasValue(elimMask, v))
                        {
                            elims.Add(CandidateIndex(cellIndex, v));
                        }
                    }
                }
                changed = true;
            }
        }

        if (logicalStepDescription != null && elims != null)
        {
            logicalStepDescription.Append($"Re-evaluated clue {clue} => {solver.DescribeElims(elims)}");
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Recursively assigns a value to each cell of the line in order, tracking the used values, the
    /// running maximum height, and how many are visible. On reaching a complete assignment with
    /// exactly <c>clue</c> visible, records each position's value into <paramref name="supportMasks"/>.
    /// Prunes on visibility bounds and existing weak links, and returns true once every candidate is
    /// supported (saturation) so the caller stops. Allocation-free.
    /// </summary>
    private bool SkyscraperSearch(Solver solver, ReadOnlySpan<uint> candidateMasks, Span<uint> supportMasks, Span<int> assigned, int pos, uint usedMask, int runningMax, int visible, Span<ulong> assignedCandidates)
    {
        int n = candidateMasks.Length;
        if (pos == n)
        {
            if (visible != clue)
            {
                return false;
            }

            bool saturated = true;
            for (int i = 0; i < n; i++)
            {
                supportMasks[i] |= ValueMask(assigned[i]);
                if ((candidateMasks[i] & ~supportMasks[i]) != 0)
                {
                    saturated = false;
                }
            }
            return saturated;
        }

        // Prune: exactly `clue` visible must still be reachable.
        int remaining = n - pos;
        if (visible > clue || visible + remaining < clue)
        {
            return false;
        }

        int cellIndex = cellIndices[pos];
        uint options = candidateMasks[pos];
        while (options != 0)
        {
            int v = MinValue(options);
            uint vMask = ValueMask(v);
            options &= ~vMask;

            if ((usedMask & vMask) != 0)
            {
                continue; // the line is a house: values are all-different
            }

            bool isVisible = v > runningMax;
            int newVisible = isVisible ? visible + 1 : visible;
            if (newVisible > clue)
            {
                continue;
            }

            // Reject if this candidate is weak-linked to any already-assigned candidate (this is what
            // CanPlaceDigits checked, cross-constraint weak links included).
            int candIndex = CandidateIndex(cellIndex, v);
            bool blocked;
            if (!assignedCandidates.IsEmpty)
            {
                blocked = solver.IsWeakLinkToAny(candIndex, assignedCandidates);
            }
            else
            {
                blocked = false;
                for (int k = 0; k < pos; k++)
                {
                    if (solver.IsWeakLink(candIndex, CandidateIndex(cellIndices[k], assigned[k])))
                    {
                        blocked = true;
                        break;
                    }
                }
            }
            if (blocked)
            {
                continue;
            }

            assigned[pos] = v;
            if (!assignedCandidates.IsEmpty)
            {
                assignedCandidates[candIndex >> 6] |= 1UL << (candIndex & 63);
            }
            bool saturated = SkyscraperSearch(solver, candidateMasks, supportMasks, assigned, pos + 1, usedMask | vMask, isVisible ? v : runningMax, newVisible, assignedCandidates);
            if (!assignedCandidates.IsEmpty)
            {
                assignedCandidates[candIndex >> 6] &= ~(1UL << (candIndex & 63));
            }
            if (saturated)
            {
                return true; // saturated: every candidate is supported, no need to search further
            }
        }

        return false;
    }
}
