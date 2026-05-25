namespace SudokuSolver;

/// <summary>
/// Stores reusable sum terms and relations for constraints that can be expressed as sums over fixed cells.
/// </summary>
internal sealed class SumConstraintRegistry
{
    private readonly Solver solver;
    private readonly List<SumTerm> terms = [];
    private readonly List<SumDifferenceRelation> relations = [];

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
    /// Determines whether this term contains a flattened cell index.
    /// </summary>
    /// <param name="cellIndex">The flattened cell index.</param>
    /// <returns>True if the cell participates in this term.</returns>
    internal bool ContainsCell(int cellIndex) => Array.BinarySearch(cellIndices, cellIndex) >= 0;

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