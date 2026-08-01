namespace SudokuSolver;

/// <summary>
/// Stores reusable sum terms and relations for constraints that can be expressed as sums over fixed cells.
/// </summary>
internal sealed class SumConstraintRegistry
{
    private readonly Solver solver;
    private readonly List<SumTerm> terms = [];
    private readonly List<SumDifferenceRelation> relations = [];
    private readonly List<SumEqualityRelation> equalityRelations = [];

    /// <summary>
    /// Initializes a new registry for a solver model.
    /// </summary>
    /// <param name="solver">The solver that owns the registered sum model.</param>
    internal SumConstraintRegistry(Solver solver)
    {
        this.solver = solver;
    }

    /// <summary>
    /// Gets the fixed-cell sum terms registered during solver setup.
    /// </summary>
    internal IReadOnlyList<SumTerm> Terms => terms;

    /// <summary>
    /// Gets the registered difference relations between sum terms.
    /// </summary>
    internal IReadOnlyList<SumDifferenceRelation> Relations => relations;

    /// <summary>
    /// Gets the registered equality relations between sum terms.
    /// </summary>
    internal IReadOnlyList<SumEqualityRelation> EqualityRelations => equalityRelations;

    /// <summary>
    /// Registers a sum term with a fixed set of allowed totals.
    /// </summary>
    /// <param name="source">The constraint that owns the user-facing semantics.</param>
    /// <param name="cells">The cells participating in the sum.</param>
    /// <param name="allowedSums">The allowed total sums for the cells.</param>
    /// <returns>The registered sum term.</returns>
    internal SumTerm RegisterFixedSum(Constraint source, IReadOnlyList<(int, int)> cells, IEnumerable<int> allowedSums)
    {
        int[] sortedSums = allowedSums.Distinct().ToArray();
        Array.Sort(sortedSums);
        if (sortedSums.Length == 0)
        {
            throw new ArgumentException("At least one allowed sum is required.", nameof(allowedSums));
        }

        SumTerm term = new(source, solver, cells, sortedSums);
        terms.Add(term);
        return term;
    }

    /// <summary>
    /// Registers a sum term whose total is governed by a relation rather than a local fixed target.
    /// </summary>
    /// <param name="source">The constraint that owns the user-facing semantics.</param>
    /// <param name="cells">The cells participating in the sum.</param>
    /// <returns>The registered sum term.</returns>
    internal SumTerm RegisterOpenSum(Constraint source, IReadOnlyList<(int, int)> cells)
    {
        SumTerm term = new(source, solver, cells, []);
        terms.Add(term);
        return term;
    }

    /// <summary>
    /// Registers a relation requiring <paramref name="positive"/> minus <paramref name="negative"/> to equal <paramref name="targetDiff"/>.
    /// </summary>
    /// <param name="source">The constraint that owns the relation.</param>
    /// <param name="positive">The positive sum term.</param>
    /// <param name="negative">The negative sum term.</param>
    /// <param name="targetDiff">The required difference between the terms.</param>
    /// <returns>The registered difference relation.</returns>
    internal SumDifferenceRelation RegisterDifference(Constraint source, SumTerm positive, SumTerm negative, int targetDiff)
    {
        SumDifferenceRelation relation = new(source, positive, negative, targetDiff);
        relations.Add(relation);
        return relation;
    }

    /// <summary>
    /// Registers a relation requiring all supplied terms to have the same total.
    /// </summary>
    /// <param name="source">The constraint that owns the relation.</param>
    /// <param name="equalTerms">The sum terms that must have equal totals.</param>
    /// <returns>The registered equality relation.</returns>
    internal SumEqualityRelation RegisterEquality(Constraint source, IReadOnlyList<SumTerm> equalTerms)
    {
        SumEqualityRelation relation = new(source, equalTerms);
        equalityRelations.Add(relation);
        return relation;
    }
}

/// <summary>
/// Represents a sum over a fixed set of cells and exposes shared setup, enforcement, and brute-force propagation.
/// </summary>
internal sealed class SumTerm
{
    private SumCellsHelper helper;
    private readonly (int, int)[] cells;
    private readonly int[] cellIndices;
    private readonly int[] fixedSums;
    private readonly bool canUseFixedSumsMask;
    private readonly ulong fixedSumsMask;
    private readonly bool canUseSumsMask;

    /// <summary>
    /// Initializes a reusable fixed-cell sum term.
    /// </summary>
    /// <param name="source">The constraint that registered this term.</param>
    /// <param name="solver">The solver used for setup-time cell and group metadata.</param>
    /// <param name="cells">The cells participating in the sum.</param>
    /// <param name="fixedSums">The sorted fixed totals for this term, or an empty array for an open term.</param>
    internal SumTerm(Constraint source, Solver solver, IReadOnlyList<(int, int)> cells, int[] fixedSums)
    {
        Source = source;
        this.cells = cells.ToArray();
        this.fixedSums = fixedSums;

        cellIndices = new int[this.cells.Length];
        for (int index = 0; index < this.cells.Length; index++)
        {
            var (row, col) = this.cells[index];
            cellIndices[index] = row * solver.WIDTH + col;
        }
        Array.Sort(cellIndices);

        canUseFixedSumsMask = TryBuildSumsMask(fixedSums, out fixedSumsMask);
        canUseSumsMask = this.cells.Length * solver.MAX_VALUE <= 63;
        helper = new SumCellsHelper(solver, [.. this.cells]);
    }

    /// <summary>
    /// Gets the constraint that registered this term.
    /// </summary>
    internal Constraint Source { get; }

    /// <summary>
    /// Gets the cells participating in this term.
    /// </summary>
    internal IReadOnlyList<(int, int)> Cells => cells;

    /// <summary>
    /// Gets the flattened cell indices participating in this term.
    /// </summary>
    internal IReadOnlyList<int> CellIndices => cellIndices;

    /// <summary>
    /// Gets whether this term has a fixed local set of allowed sums.
    /// </summary>
    internal bool HasFixedSums => fixedSums.Length > 0;

    /// <summary>
    /// Gets whether this term can safely represent all possible sums in a 64-bit mask.
    /// </summary>
    internal bool CanUseSumsMask => canUseSumsMask;

    /// <summary>
    /// Applies setup-time candidate restrictions for a fixed-sum term.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult InitCandidates(Solver solver)
    {
        RefreshHelper(solver);
        return HasFixedSums ? helper.Init(solver, fixedSums) : LogicResult.None;
    }

    /// <summary>
    /// Applies setup-time candidate restrictions for a caller-supplied set of possible sums.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="possibleSums">The possible sums to preserve.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult InitCandidates(Solver solver, IReadOnlyList<int> possibleSums)
    {
        RefreshHelper(solver);
        return helper.Init(solver, possibleSums);
    }

    /// <summary>
    /// Rebuilds setup-derived helper state from the solver's current topology.
    /// </summary>
    /// <param name="solver">The solver whose current groups and links define the helper split.</param>
    internal void RefreshHelper(Solver solver)
    {
        helper = new SumCellsHelper(solver, [.. cells]);
    }

    /// <summary>
    /// Runs shared sum propagation for this fixed-sum term.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="logicalStepDescription">Optional logical-step description builder.</param>
    /// <param name="isBruteForcing">Whether this is running on the brute-force hot path.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (!HasFixedSums)
        {
            return LogicResult.None;
        }

        if (isBruteForcing && logicalStepDescription == null && canUseFixedSumsMask)
        {
            return RestrictSumsMask(solver, fixedSumsMask);
        }

        return helper.StepLogic(solver, fixedSums, logicalStepDescription);
    }

    /// <summary>
    /// Runs shared sum propagation for a caller-supplied set of sums.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="possibleSums">The possible sums to preserve.</param>
    /// <param name="logicalStepDescription">Optional logical-step description builder.</param>
    /// <param name="isBruteForcing">Whether this is running on the brute-force hot path.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult StepLogic(Solver solver, IReadOnlyList<int> possibleSums, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (isBruteForcing && logicalStepDescription == null && TryBuildSumsMask(possibleSums, out ulong sumsMask))
        {
            return RestrictSumsMask(solver, sumsMask);
        }

        return helper.StepLogic(solver, possibleSums, logicalStepDescription);
    }

    /// <summary>
    /// Restricts this term to sums represented by a bit mask.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="sumsMask">A mask where bit s means total sum s is allowed.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult RestrictSumsMask(Solver solver, ulong sumsMask)
    {
        return helper.StepLogicBF(solver, sumsMask, out _);
    }

    /// <summary>
    /// Gets the currently possible total sums as a bit mask.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <returns>A bit mask where bit s means total sum s is possible.</returns>
    internal ulong PossibleSumsMask(Solver solver) => helper.PossibleSumsMask(solver);

    /// <summary>
    /// Gets the currently possible total sums as a list.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <returns>The possible total sums.</returns>
    internal List<int> PossibleSums(Solver solver) => helper.PossibleSums(solver);

    /// <summary>
    /// Gets the current minimum and maximum possible total.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <returns>The possible sum range, or zeros if no sum is possible.</returns>
    internal (int Min, int Max) SumRange(Solver solver) => helper.SumRange(solver);

    /// <summary>
    /// Checks the fixed-sum target after all cells in this term have values.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <param name="changedCellIndex">The cell that was just set.</param>
    /// <returns>True when the term remains satisfiable.</returns>
    internal bool EnforceComplete(Solver solver, int changedCellIndex)
    {
        if (!HasFixedSums || !ContainsCell(changedCellIndex))
        {
            return true;
        }

        int sum = 0;
        uint[] board = solver.BoardArray;
        for (int index = 0; index < cellIndices.Length; index++)
        {
            uint mask = board[cellIndices[index]];
            if (!Solver.IsValueSet(mask))
            {
                return true;
            }
            sum += Solver.GetValue(mask);
        }

        return IsFixedSumAllowed(sum);
    }

    /// <summary>
    /// Gets the cells that must contain a value for a distinct-cell fixed sum term.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <param name="value">The value to test.</param>
    /// <returns>The candidate cells when the value is required, otherwise <c>null</c>.</returns>
    internal List<(int, int)> CellsMustContain(Solver solver, int value)
    {
        if (!HasFixedSums || value < 1 || value > solver.MAX_VALUE || cells.Length == 0 || cells.Length > solver.MAX_VALUE)
        {
            return null;
        }

        SumData sumData = SumData.Get(solver.MAX_VALUE);
        if (sumData == null)
        {
            return null;
        }

        uint valueMask = ValueMask(value);
        uint[] board = solver.BoardArray;
        Span<uint> cellMasksWithoutValue = stackalloc uint[cells.Length];
        int candidateCellCount = 0;
        uint availableValuesMask = 0;

        for (int cellOffset = 0; cellOffset < cells.Length; cellOffset++)
        {
            var (row, col) = cells[cellOffset];
            int cellIndex = row * solver.WIDTH + col;
            uint cellMask = board[cellIndex];
            uint valueBits = cellMask & solver.ALL_VALUES_MASK;
            int valueCount = ValueCount(valueBits);
            if (IsValueSet(cellMask))
            {
                if ((valueBits & valueMask) != 0)
                {
                    return null;
                }

                cellMasksWithoutValue[cellOffset] = valueBits;
                availableValuesMask |= valueBits;
                continue;
            }

            if (valueCount <= 1)
            {
                if ((valueBits & valueMask) != 0)
                {
                    return null;
                }

                cellMasksWithoutValue[cellOffset] = valueBits;
                availableValuesMask |= valueBits;
                continue;
            }

            if ((valueBits & valueMask) != 0)
            {
                candidateCellCount++;
            }

            uint maskWithoutValue = valueBits & ~valueMask;
            if (maskWithoutValue == 0)
            {
                return BuildCellsWithCandidate(solver, valueMask, board);
            }

            cellMasksWithoutValue[cellOffset] = maskWithoutValue;
            availableValuesMask |= maskWithoutValue;
        }

        if (candidateCellCount == 0)
        {
            return null;
        }

        foreach (int fixedSum in fixedSums)
        {
            uint[][] sumsForSize = sumData.KillerCageSums[cells.Length];
            if ((uint)fixedSum >= (uint)sumsForSize.Length)
            {
                continue;
            }

            foreach (uint combinationMask in sumsForSize[fixedSum])
            {
                if ((combinationMask & ~availableValuesMask) != 0)
                {
                    continue;
                }

                if ((combinationMask & valueMask) != 0)
                {
                    continue;
                }

                if (CanAssignCombination(cellMasksWithoutValue, 0, combinationMask))
                {
                    return null;
                }
            }
        }

        return BuildCellsWithCandidate(solver, valueMask, board);
    }

    /// <summary>
    /// Determines whether this term contains a flattened cell index.
    /// </summary>
    /// <param name="cellIndex">The flattened cell index.</param>
    /// <returns>True if the cell participates in this term.</returns>
    internal bool ContainsCell(int cellIndex) => Array.BinarySearch(cellIndices, cellIndex) >= 0;

    private List<(int, int)> BuildCellsWithCandidate(Solver solver, uint valueMask, uint[] board)
    {
        List<(int, int)> result = null;
        for (int cellOffset = 0; cellOffset < cells.Length; cellOffset++)
        {
            var (row, col) = cells[cellOffset];
            int cellIndex = row * solver.WIDTH + col;
            uint cellMask = board[cellIndex];
            if (!IsValueSet(cellMask) && ValueCount(cellMask) > 1 && (cellMask & valueMask) != 0)
            {
                result ??= [];
                result.Add(cells[cellOffset]);
            }
        }
        return result;
    }

    private static bool CanAssignCombination(ReadOnlySpan<uint> cellMasks, int cellOffset, uint remainingValues)
    {
        if (cellOffset == cellMasks.Length)
        {
            return remainingValues == 0;
        }

        uint options = cellMasks[cellOffset] & remainingValues;
        while (options != 0)
        {
            uint valueBit = options & (0u - options);
            if (CanAssignCombination(cellMasks, cellOffset + 1, remainingValues & ~valueBit))
            {
                return true;
            }
            options &= options - 1;
        }

        return false;
    }

    private bool IsFixedSumAllowed(int sum)
    {
        if (canUseFixedSumsMask && (uint)sum < 64)
        {
            return (fixedSumsMask & (1UL << sum)) != 0;
        }

        return Array.BinarySearch(fixedSums, sum) >= 0;
    }

    private static bool TryBuildSumsMask(IReadOnlyList<int> sums, out ulong sumsMask)
    {
        sumsMask = 0;
        for (int index = 0; index < sums.Count; index++)
        {
            int sum = sums[index];
            if ((uint)sum >= 64)
            {
                sumsMask = 0;
                return false;
            }
            sumsMask |= 1UL << sum;
        }
        return sums.Count > 0;
    }
}

/// <summary>
/// Represents a relation requiring all sum terms to have the same total.
/// </summary>
internal sealed class SumEqualityRelation
{
    private readonly SumTerm[] terms;
    private readonly int[] cellIndices;

    /// <summary>
    /// Initializes a reusable equality relation between sum terms.
    /// </summary>
    /// <param name="source">The constraint that registered this relation.</param>
    /// <param name="terms">The terms that must have equal totals.</param>
    internal SumEqualityRelation(Constraint source, IReadOnlyList<SumTerm> terms)
    {
        Source = source;
        this.terms = terms.ToArray();
        cellIndices = this.terms
            .SelectMany(term => term.CellIndices)
            .Distinct()
            .Order()
            .ToArray();
        CanUseSumsMask = this.terms.All(term => term.CanUseSumsMask);
    }

    /// <summary>
    /// Gets the constraint that registered this relation.
    /// </summary>
    internal Constraint Source { get; }

    /// <summary>
    /// Gets the sum terms participating in this relation.
    /// </summary>
    internal IReadOnlyList<SumTerm> Terms => terms;

    /// <summary>
    /// Gets the flattened cell indices watched by this relation.
    /// </summary>
    internal IReadOnlyList<int> CellIndices => cellIndices;

    /// <summary>
    /// Gets whether every term in this relation can use 64-bit sum masks.
    /// </summary>
    internal bool CanUseSumsMask { get; }

    /// <summary>
    /// Applies setup-time restrictions implied by the shared possible sums.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult InitCandidates(Solver solver)
    {
        for (int index = 0; index < terms.Length; index++)
        {
            terms[index].RefreshHelper(solver);
        }

        List<int> possibleSums = PossibleSums(solver);
        if (possibleSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        LogicResult result = LogicResult.None;
        for (int index = 0; index < terms.Length; index++)
        {
            LogicResult termResult = terms[index].InitCandidates(solver, possibleSums);
            if (termResult == LogicResult.Invalid) return LogicResult.Invalid;
            if (termResult == LogicResult.Changed) result = LogicResult.Changed;
        }

        return result;
    }

    /// <summary>
    /// Runs equality propagation.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="logicalStepDescription">Optional logical-step description builder.</param>
    /// <param name="isBruteForcing">Whether this is running on the brute-force hot path.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (isBruteForcing && logicalStepDescription == null && TryGetPossibleSumsMask(solver, out ulong sumsMask))
        {
            return RestrictSumsMask(solver, sumsMask);
        }

        List<int> possibleSums = PossibleSums(solver);
        if (possibleSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        return RestrictSums(solver, possibleSums, logicalStepDescription, isBruteForcing);
    }

    /// <summary>
    /// Gets the currently possible equal totals as a list.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <returns>The possible equal totals.</returns>
    internal List<int> PossibleSums(Solver solver)
    {
        if (TryGetPossibleSumsMask(solver, out ulong sumsMask))
        {
            return SumsMaskToList(sumsMask);
        }

        HashSet<int> possibleSums = null;
        for (int index = 0; index < terms.Length; index++)
        {
            List<int> curPossibleSums = terms[index].PossibleSums(solver);
            if (curPossibleSums == null)
            {
                possibleSums = null;
                break;
            }

            if (possibleSums == null)
            {
                possibleSums = curPossibleSums.ToHashSet();
            }
            else
            {
                possibleSums.IntersectWith(curPossibleSums);
            }
        }

        if (possibleSums == null || possibleSums.Count == 0)
        {
            return [];
        }

        List<int> possibleSumsList = possibleSums.ToList();
        possibleSumsList.Sort();
        return possibleSumsList;
    }

    /// <summary>
    /// Checks whether the equality relation remains possible for a changed cell.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <param name="changedCellIndex">The cell that was just set.</param>
    /// <returns>True when at least one shared sum remains possible.</returns>
    internal bool EnforcePossible(Solver solver, int changedCellIndex)
    {
        if (!ContainsCell(changedCellIndex))
        {
            return true;
        }

        if (TryGetPossibleSumsMask(solver, out ulong sumsMask))
        {
            return sumsMask != 0;
        }

        return PossibleSums(solver).Count != 0;
    }

    private LogicResult RestrictSums(Solver solver, IReadOnlyList<int> possibleSums, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        LogicResult result = LogicResult.None;
        for (int index = 0; index < terms.Length; index++)
        {
            LogicResult termResult = terms[index].StepLogic(solver, possibleSums, logicalStepDescription, isBruteForcing);
            if (termResult == LogicResult.Invalid) return LogicResult.Invalid;
            if (termResult == LogicResult.Changed) result = LogicResult.Changed;
        }

        return result;
    }

    private LogicResult RestrictSumsMask(Solver solver, ulong sumsMask)
    {
        if (sumsMask == 0)
        {
            return LogicResult.Invalid;
        }

        LogicResult result = LogicResult.None;
        for (int index = 0; index < terms.Length; index++)
        {
            LogicResult termResult = terms[index].RestrictSumsMask(solver, sumsMask);
            if (termResult == LogicResult.Invalid) return LogicResult.Invalid;
            if (termResult == LogicResult.Changed) result = LogicResult.Changed;
        }

        return result;
    }

    private bool TryGetPossibleSumsMask(Solver solver, out ulong sumsMask)
    {
        sumsMask = 0;
        if (!CanUseSumsMask || terms.Length == 0)
        {
            return false;
        }

        sumsMask = terms[0].PossibleSumsMask(solver);
        for (int index = 1; index < terms.Length && sumsMask != 0; index++)
        {
            sumsMask &= terms[index].PossibleSumsMask(solver);
        }

        return true;
    }

    private bool ContainsCell(int cellIndex) => Array.BinarySearch(cellIndices, cellIndex) >= 0;

    private static List<int> SumsMaskToList(ulong sumsMask)
    {
        List<int> sums = [];
        while (sumsMask != 0)
        {
            int sum = BitOperations.TrailingZeroCount(sumsMask);
            sumsMask &= sumsMask - 1;
            sums.Add(sum);
        }
        return sums;
    }
}

/// <summary>
/// Represents a relation requiring one sum term minus another sum term to equal a fixed target.
/// </summary>
internal sealed class SumDifferenceRelation
{
    private readonly int[] cellIndices;

    /// <summary>
    /// Initializes a reusable relation between two sum terms.
    /// </summary>
    /// <param name="source">The constraint that registered this relation.</param>
    /// <param name="positive">The positive side of the relation.</param>
    /// <param name="negative">The negative side of the relation.</param>
    /// <param name="targetDiff">The required difference.</param>
    internal SumDifferenceRelation(Constraint source, SumTerm positive, SumTerm negative, int targetDiff)
    {
        Source = source;
        Positive = positive;
        Negative = negative;
        TargetDiff = targetDiff;
        cellIndices = positive.CellIndices.Concat(negative.CellIndices).Distinct().Order().ToArray();
    }

    /// <summary>
    /// Gets the constraint that registered this relation.
    /// </summary>
    internal Constraint Source { get; }

    /// <summary>
    /// Gets the positive sum term.
    /// </summary>
    internal SumTerm Positive { get; }

    /// <summary>
    /// Gets the negative sum term.
    /// </summary>
    internal SumTerm Negative { get; }

    /// <summary>
    /// Gets the required difference between the positive and negative terms.
    /// </summary>
    internal int TargetDiff { get; }

    /// <summary>
    /// Gets the flattened cell indices watched by this relation.
    /// </summary>
    internal IReadOnlyList<int> CellIndices => cellIndices;

    /// <summary>
    /// Applies setup-time range restrictions implied by the relation.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult InitCandidates(Solver solver)
    {
        Positive.RefreshHelper(solver);
        Negative.RefreshHelper(solver);

        var (posMin, posMax) = Positive.SumRange(solver);
        var (negMin, negMax) = Negative.SumRange(solver);

        if (posMin == 0 || posMax == 0 || negMin == 0 || negMax == 0)
        {
            return LogicResult.None;
        }

        int validPosMin = Math.Max(posMin, negMin + TargetDiff);
        int validPosMax = Math.Min(posMax, negMax + TargetDiff);
        int validNegMin = Math.Max(negMin, posMin - TargetDiff);
        int validNegMax = Math.Min(negMax, posMax - TargetDiff);

        if (validPosMin > validPosMax || validNegMin > validNegMax)
        {
            return LogicResult.Invalid;
        }

        LogicResult result = LogicResult.None;
        if (validPosMin > posMin || validPosMax < posMax)
        {
            LogicResult posResult = Positive.InitCandidates(solver, [validPosMin, validPosMax]);
            if (posResult == LogicResult.Invalid) return LogicResult.Invalid;
            if (posResult == LogicResult.Changed) result = LogicResult.Changed;
        }

        if (validNegMin > negMin || validNegMax < negMax)
        {
            LogicResult negResult = Negative.InitCandidates(solver, [validNegMin, validNegMax]);
            if (negResult == LogicResult.Invalid) return LogicResult.Invalid;
            if (negResult == LogicResult.Changed) result = LogicResult.Changed;
        }

        return result;
    }

    /// <summary>
    /// Runs relation propagation.
    /// </summary>
    /// <param name="solver">The solver state to mutate.</param>
    /// <param name="logicalStepDescription">Optional logical-step description builder.</param>
    /// <param name="isBruteForcing">Whether this is running on the brute-force hot path.</param>
    /// <returns>The resulting logic state.</returns>
    internal LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (isBruteForcing && logicalStepDescription == null)
        {
            return StepLogicBF(solver);
        }

        List<int> posSums = Positive.PossibleSums(solver);
        List<int> negSums = Negative.PossibleSums(solver);
        if (posSums == null || posSums.Count == 0 || negSums == null || negSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        HashSet<int> negSumSet = new(negSums);
        HashSet<int> posSumSet = new(posSums);
        List<int> validPosSums = posSums.Where(posSum => negSumSet.Contains(posSum - TargetDiff)).ToList();
        List<int> validNegSums = negSums.Where(negSum => posSumSet.Contains(negSum + TargetDiff)).ToList();

        if (validPosSums.Count == 0 || validNegSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        LogicResult result = LogicResult.None;
        LogicResult posResult = Positive.StepLogic(solver, validPosSums, logicalStepDescription, false);
        if (posResult == LogicResult.Invalid) return LogicResult.Invalid;
        if (posResult == LogicResult.Changed) result = LogicResult.Changed;

        LogicResult negResult = Negative.StepLogic(solver, validNegSums, logicalStepDescription, false);
        if (negResult == LogicResult.Invalid) return LogicResult.Invalid;
        if (negResult == LogicResult.Changed) result = LogicResult.Changed;

        return result;
    }

    /// <summary>
    /// Checks the relation after all cells in both terms have values.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <param name="changedCellIndex">The cell that was just set.</param>
    /// <returns>True when the relation remains satisfiable.</returns>
    internal bool EnforceComplete(Solver solver, int changedCellIndex)
    {
        if (!ContainsCell(changedCellIndex))
        {
            return true;
        }

        int posSum = 0;
        if (!TryGetCompleteSum(solver, Positive.CellIndices, ref posSum))
        {
            return true;
        }

        int negSum = 0;
        if (!TryGetCompleteSum(solver, Negative.CellIndices, ref negSum))
        {
            return true;
        }

        return posSum - negSum == TargetDiff;
    }

    private LogicResult StepLogicBF(Solver solver)
    {
        ulong posMask = Positive.PossibleSumsMask(solver);
        ulong negMask = Negative.PossibleSumsMask(solver);
        if (posMask == 0 || negMask == 0)
        {
            return LogicResult.Invalid;
        }

        ulong validPosMask = 0;
        ulong validNegMask = 0;
        ulong remainingPos = posMask;
        while (remainingPos != 0)
        {
            int posSum = BitOperations.TrailingZeroCount(remainingPos);
            remainingPos &= remainingPos - 1;
            int negSum = posSum - TargetDiff;
            if ((uint)negSum < 64 && (negMask & (1UL << negSum)) != 0)
            {
                validPosMask |= 1UL << posSum;
                validNegMask |= 1UL << negSum;
            }
        }

        if (validPosMask == 0 || validNegMask == 0)
        {
            return LogicResult.Invalid;
        }

        LogicResult result = LogicResult.None;
        LogicResult posResult = Positive.RestrictSumsMask(solver, validPosMask);
        if (posResult == LogicResult.Invalid) return LogicResult.Invalid;
        if (posResult == LogicResult.Changed) result = LogicResult.Changed;

        LogicResult negResult = Negative.RestrictSumsMask(solver, validNegMask);
        if (negResult == LogicResult.Invalid) return LogicResult.Invalid;
        if (negResult == LogicResult.Changed) result = LogicResult.Changed;

        return result;
    }

    private bool ContainsCell(int cellIndex) => Array.BinarySearch(cellIndices, cellIndex) >= 0;

    private static bool TryGetCompleteSum(Solver solver, IReadOnlyList<int> cells, ref int sum)
    {
        uint[] board = solver.BoardArray;
        for (int index = 0; index < cells.Count; index++)
        {
            uint mask = board[cells[index]];
            if (!Solver.IsValueSet(mask))
            {
                return false;
            }
            sum += Solver.GetValue(mask);
        }
        return true;
    }
}