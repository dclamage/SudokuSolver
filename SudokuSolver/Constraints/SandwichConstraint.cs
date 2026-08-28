using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Sandwich", ConsoleName = "sandwich")]
public class SandwichConstraint : Constraint
{
    public readonly int sum;
    public readonly (int, int) cellStart;
    private readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsLookup;
    private int minFillingLength = 0;
    private int maxFillingLength = 0;
    private readonly uint crustsMask = 0;
    private readonly uint nonCrustsMask = 0;
    private uint fillingMask = 0;
    private readonly string specificName;

    public override string SpecificName => specificName;

    private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
    public SandwichConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"Sandwich options \"{options}\" invalid. Expecting: \"SrXcY\"");
        }

        sum = int.Parse(match.Groups[1].Value);
        cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

        bool isCol = cellStart.Item1 < 0 || cellStart.Item1 >= MAX_VALUE;
        bool isRow = cellStart.Item2 < 0 || cellStart.Item2 >= MAX_VALUE;

        if (isRow && isCol || !isRow && !isCol)
        {
            throw new ArgumentException($"Sandwich options \"{options}\" has invalid location.");
        }

        cells = new();
        if (isRow)
        {
            int i = cellStart.Item1;
            for (int j = 0; j < WIDTH; j++)
            {
                cells.Add((i, j));
            }
        }
        else
        {
            int j = cellStart.Item2;
            for (int i = 0; i < HEIGHT; i++)
            {
                cells.Add((i, j));
            }
        }
        cellsLookup = new(cells);

        crustsMask = ValueMask(1) | ValueMask(MAX_VALUE);
        nonCrustsMask = ALL_VALUES_MASK & ~crustsMask;

        specificName = $"Sandwich sum {sum} at {CellName(cellStart)}";
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        bool changed = false;
        int numCells = cells.Count;
        fillingMask = ALL_VALUES_MASK & ~crustsMask;
        minFillingLength = 0;
        maxFillingLength = 0;

        int allValueSum = (MAX_VALUE * (MAX_VALUE + 1)) / 2 - (1 + MAX_VALUE);
        if (sum < 0 || sum > allValueSum)
        {
            return LogicResult.Invalid;
        }

        fillingMask = 0;
        for (int curFillingLength = 1; curFillingLength <= numCells - 2; curFillingLength++)
        {
            try
            {
                foreach (var combination in Enumerable.Range(2, MAX_VALUE - 2).Combinations(curFillingLength))
                {
                    if (SumOf(combination) != sum)
                    {
                        continue;
                    }

                    if (minFillingLength == 0)
                    {
                        minFillingLength = curFillingLength;
                        maxFillingLength = curFillingLength;
                    }
                    else
                    {
                        maxFillingLength = curFillingLength;
                    }

                    foreach (int value in combination)
                    {
                        fillingMask |= ValueMask(value);
                    }
                }
            }
            catch (InvalidOperationException) { }
        }

        uint[] keepMasks = new uint[numCells];
        int lastLeftCrust = numCells - minFillingLength - 2;
        for (int leftCrust = 0; leftCrust <= lastLeftCrust; leftCrust++)
        {
            for (int curFillingLength = minFillingLength; curFillingLength <= maxFillingLength; curFillingLength++)
            {
                int rightCrust = leftCrust + curFillingLength + 1;
                if (rightCrust >= numCells)
                {
                    break;
                }

                // Mark the crusts
                keepMasks[leftCrust] |= crustsMask;
                keepMasks[rightCrust] |= crustsMask;

                // Mark the left outies
                for (int cellIndex = 0; cellIndex < leftCrust; cellIndex++)
                {
                    keepMasks[cellIndex] |= nonCrustsMask;
                }

                // Mark the filling
                for (int cellIndex = leftCrust + 1; cellIndex < rightCrust; cellIndex++)
                {
                    keepMasks[cellIndex] |= fillingMask;
                }

                // Mark the right outies
                for (int cellIndex = rightCrust + 1; cellIndex < numCells; cellIndex++)
                {
                    keepMasks[cellIndex] |= nonCrustsMask;
                }
            }
        }

        for (int i = 0; i < numCells; i++)
        {
            var cell = cells[i];
            uint clearMask = ALL_VALUES_MASK & ~keepMasks[i];
            var logicResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, clearMask);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= logicResult == LogicResult.Changed;
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>Measured: 267 ns / 14.6% fire.</summary>

    public override int BruteForcePropagationCost => 1830;


    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (!cellsLookup.Contains((i, j)))
        {
            return true;
        }

        var board = sudokuSolver.Board;
        (int crustIndex0, int crustIndex1) = GetCrustIndices(sudokuSolver);

        // Nothing to validate until the crusts are filled
        if (crustIndex1 == -1)
        {
            return true;
        }

        int fillingSize = crustIndex1 - crustIndex0 - 1;
        if (fillingSize == 0)
        {
            return sum == 0;
        }

        uint notCrustMask = ALL_VALUES_MASK & ~crustsMask;
        uint possibleFillingMask = 0;
        for (int cellIndex = crustIndex0 + 1; cellIndex < crustIndex1; cellIndex++)
        {
            var curCell = cells[cellIndex];
            possibleFillingMask |= board[curCell.Item1, curCell.Item2] & notCrustMask;
        }

        if (ValueCount(possibleFillingMask) < fillingSize)
        {
            return false;
        }

        int minSum = 0;
        int numValsSummed = 0;
        for (int curVal = 2; numValsSummed < fillingSize && curVal <= MAX_VALUE - 1; curVal++)
        {
            uint curMask = ValueMask(curVal);
            if ((possibleFillingMask & curMask) == 0)
            {
                continue;
            }
            minSum += curVal;
            numValsSummed++;
        }
        if (sum < minSum)
        {
            return false;
        }

        int maxSum = 0;
        numValsSummed = 0;
        for (int curVal = MAX_VALUE - 1; numValsSummed < fillingSize && curVal > 1; curVal--)
        {
            uint curMask = ValueMask(curVal);
            if ((possibleFillingMask & curMask) == 0)
            {
                continue;
            }
            maxSum += curVal;
            numValsSummed++;
        }
        if (sum > maxSum)
        {
            return false;
        }

        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => InitLinksByRunningLogic(solver, cells, logicalStepDescription);
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => CellsMustContainByRunningLogic(sudokuSolver, cells, value);

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (isBruteForcing && DefaultBruteForceArm == BruteForceArm.None)
        {
            // Let EnforceConstraint and the weak links carry it, as IndexerConstraint does.
            return LogicResult.None;
        }
        bool useHeuristic = isBruteForcing && DefaultBruteForceArm == BruteForceArm.Heuristic;

        var board = sudokuSolver.Board;
        int numCells = cells.Count;
        (int crustIndex0, int crustIndex1) = GetCrustIndices(sudokuSolver);

        // One scratch buffer per call for the whole method, both bounded by the line length. Every
        // path below indexes keepMasks by position within `cells`, which is what lets the
        // brute-force arm run without allocating. See docs/pathological-outliers.md.
        Span<uint> keepMasks = stackalloc uint[numCells];
        Span<int> unsetIndices = stackalloc int[numCells];

        if (crustIndex1 != -1)
        {
            // Both crust locations are known
            int fillingSize = crustIndex1 - crustIndex0 - 1;
            if (fillingSize == 0)
            {
                if (sum == 0)
                {
                    return LogicResult.None;
                }

                logicalStepDescription?.Append($"Sandwich sum does not match (sum is 0, expected {sum})");
                return LogicResult.Invalid;
            }

            int knownSum = 0;
            int numUnsetCells = 0;
            for (int cellIndex = crustIndex0 + 1; cellIndex < crustIndex1; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                uint mask = board[i, j];
                if (IsValueSet(mask))
                {
                    knownSum += GetValue(mask);
                }
                else
                {
                    unsetIndices[numUnsetCells++] = cellIndex;
                }
            }

            if (numUnsetCells == 0)
            {
                if (sum != knownSum)
                {
                    logicalStepDescription?.Append($"Sandwich sum does not match (sum is {knownSum}, expected {sum})");
                    return LogicResult.Invalid;
                }
                return LogicResult.None;
            }

            if (knownSum >= sum)
            {
                logicalStepDescription?.Append($"Sandwich sum does not match (sum is strictly greater than {knownSum}, expected {sum})");
                return LogicResult.Invalid;
            }

            ReadOnlySpan<int> unsetIdx = unsetIndices[..numUnsetCells];
            int remainingSum = sum - knownSum;
            uint possibleValuesMask = PossibleValuesMask(board, unsetIdx) & nonCrustsMask;

            if (ValueCount(possibleValuesMask) < numUnsetCells)
            {
                logicalStepDescription?.Append($"Remaining sandwich sum values {MaskToString(possibleValuesMask)} do not fit into {numUnsetCells} remaining cells.");
                return LogicResult.Invalid;
            }

            // This branch concludes something only about the unset filling cells, so every other
            // cell keeps whatever it already had.
            keepMasks.Fill(ALL_VALUES_MASK);
            foreach (int cellIndex in unsetIdx)
            {
                keepMasks[cellIndex] = 0;
            }

            ApplySumCombinations(sudokuSolver, possibleValuesMask, unsetIdx, remainingSum, keepMasks, useHeuristic);

            return ApplyKeepMask(sudokuSolver, keepMasks, logicalStepDescription);
        }

        if (crustIndex0 != -1)
        {
            // Only one crust location is known
            for (int fillingSize = minFillingLength; fillingSize <= maxFillingLength; fillingSize++)
            {
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    int curCrustIndex0 = dir == -1 ? crustIndex0 - fillingSize - 1 : crustIndex0;
                    int curCrustIndex1 = curCrustIndex0 + fillingSize + 1;
                    if (curCrustIndex0 < 0 || curCrustIndex1 >= numCells)
                    {
                        continue;
                    }
                    var crustCell0 = cells[curCrustIndex0];
                    var crustCell1 = cells[curCrustIndex1];
                    uint crustMask0 = board[crustCell0.Item1, crustCell0.Item2];
                    uint crustMask1 = board[crustCell1.Item1, crustCell1.Item2];
                    uint bothCrustsMask = crustMask0 | crustMask1;
                    if ((bothCrustsMask & crustsMask) != crustsMask)
                    {
                        continue;
                    }

                    bool haveValidPlacement = false;
                    if (fillingSize == 0)
                    {
                        haveValidPlacement = true;
                    }
                    else
                    {
                        int knownSum = 0;
                        int numUnsetCells = 0;
                        for (int cellIndex = curCrustIndex0 + 1; cellIndex < curCrustIndex1; cellIndex++)
                        {
                            var (i, j) = cells[cellIndex];
                            uint mask = board[i, j];
                            if (IsValueSet(mask))
                            {
                                knownSum += GetValue(mask);
                                keepMasks[cellIndex] |= mask & ~valueSetMask;
                            }
                            else
                            {
                                unsetIndices[numUnsetCells++] = cellIndex;
                            }
                        }

                        if (numUnsetCells == 0)
                        {
                            if (sum == knownSum)
                            {
                                haveValidPlacement = true;
                            }
                        }
                        else if (knownSum < sum)
                        {
                            ReadOnlySpan<int> unsetIdx = unsetIndices[..numUnsetCells];
                            int remainingSum = sum - knownSum;
                            uint possibleValuesMask = PossibleValuesMask(board, unsetIdx) & nonCrustsMask;

                            if (ValueCount(possibleValuesMask) >= numUnsetCells)
                            {
                                haveValidPlacement |= ApplySumCombinations(
                                    sudokuSolver, possibleValuesMask, unsetIdx, remainingSum, keepMasks, useHeuristic);
                            }
                        }
                    }

                    if (haveValidPlacement)
                    {
                        uint nonCrustMask = ALL_VALUES_MASK & ~crustsMask;
                        for (int cellIndex = 0; cellIndex < curCrustIndex0; cellIndex++)
                        {
                            keepMasks[cellIndex] |= nonCrustMask;
                        }
                        for (int cellIndex = curCrustIndex1 + 1; cellIndex < numCells; cellIndex++)
                        {
                            keepMasks[cellIndex] |= nonCrustMask;
                        }
                        keepMasks[curCrustIndex0] |= crustsMask;
                        keepMasks[curCrustIndex1] |= crustsMask;
                    }
                }
            }
        }
        else
        {
            // Neither crust location is known
            for (int curCrustIndex0 = 0; curCrustIndex0 < numCells - minFillingLength - 1; curCrustIndex0++)
            {
                var crustCell0 = cells[curCrustIndex0];
                uint crustMask0 = board[crustCell0.Item1, crustCell0.Item2];
                if ((crustMask0 & crustsMask) == 0)
                {
                    continue;
                }

                for (int fillingSize = minFillingLength; fillingSize <= maxFillingLength; fillingSize++)
                {
                    int curCrustIndex1 = curCrustIndex0 + fillingSize + 1;
                    if (curCrustIndex1 >= numCells)
                    {
                        break;
                    }

                    var crustCell1 = cells[curCrustIndex1];
                    uint crustMask1 = board[crustCell1.Item1, crustCell1.Item2];
                    uint bothCrustsMask = crustMask0 | crustMask1;
                    if ((bothCrustsMask & crustsMask) != crustsMask)
                    {
                        continue;
                    }

                    bool haveValidPlacement = false;
                    if (fillingSize == 0)
                    {
                        haveValidPlacement = true;
                    }
                    else
                    {
                        int knownSum = 0;
                        int numUnsetCells = 0;
                        for (int cellIndex = curCrustIndex0 + 1; cellIndex < curCrustIndex1; cellIndex++)
                        {
                            var (i, j) = cells[cellIndex];
                            uint mask = board[i, j];
                            if (IsValueSet(mask))
                            {
                                knownSum += GetValue(mask);
                                keepMasks[cellIndex] |= mask & ~valueSetMask;
                            }
                            else
                            {
                                unsetIndices[numUnsetCells++] = cellIndex;
                            }
                        }

                        if (numUnsetCells == 0)
                        {
                            if (sum == knownSum)
                            {
                                haveValidPlacement = true;
                            }
                        }
                        else if (knownSum < sum)
                        {
                            ReadOnlySpan<int> unsetIdx = unsetIndices[..numUnsetCells];
                            int remainingSum = sum - knownSum;
                            uint possibleValuesMask = PossibleValuesMask(board, unsetIdx) & nonCrustsMask;

                            if (ValueCount(possibleValuesMask) >= numUnsetCells)
                            {
                                haveValidPlacement |= ApplySumCombinations(
                                    sudokuSolver, possibleValuesMask, unsetIdx, remainingSum, keepMasks, useHeuristic);
                            }
                        }
                    }

                    if (haveValidPlacement)
                    {
                        uint nonCrustMask = ALL_VALUES_MASK & ~crustsMask;
                        for (int cellIndex = 0; cellIndex < curCrustIndex0; cellIndex++)
                        {
                            keepMasks[cellIndex] |= nonCrustMask;
                        }
                        for (int cellIndex = curCrustIndex1 + 1; cellIndex < numCells; cellIndex++)
                        {
                            keepMasks[cellIndex] |= nonCrustMask;
                        }
                        keepMasks[curCrustIndex0] |= crustsMask;
                        keepMasks[curCrustIndex1] |= crustsMask;
                    }
                }
            }
        }

        return ApplyKeepMask(sudokuSolver, keepMasks, logicalStepDescription);
    }

    /// <summary>ORs together the candidate masks of the cells at <paramref name="cellIndices"/>.</summary>
    private uint PossibleValuesMask(BoardView board, ReadOnlySpan<int> cellIndices)
    {
        uint mask = 0;
        foreach (int cellIndex in cellIndices)
        {
            var (i, j) = cells[cellIndex];
            mask |= board[i, j];
        }
        return mask;
    }

    /// <summary>
    /// How much propagation the sandwich does <b>while brute forcing</b>. Logical solving always uses
    /// <see cref="BruteForceArm.Exact"/>. Overridable with
    /// <c>SUDOKU_SANDWICH_BF_ARM=exact|heuristic|none</c> so the arms can be re-measured without a
    /// rebuild.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Correctness does not depend on this.</b> <see cref="EnforceConstraint"/> validates the sum
    /// once both crusts are placed and this constraint inherits
    /// <c>NeedsEnforceConstraint =&gt; true</c>, so <c>StepLogic</c> is pure propagation: a weaker arm
    /// can cost search nodes but cannot change a solution count. Every arm here concludes a subset of
    /// what <c>Exact</c> concludes.
    /// </para>
    /// <para>
    /// <b>Heuristic is the default because Exact was over-propagating.</b> Exact enumerates every
    /// permutation of every candidate value set and tests placement, which is work the search would
    /// otherwise do lazily and only where needed. Measured over 5 runs, best of:
    /// </para>
    /// <code>
    /// case         arm         nodes        time        alloc
    /// blPgSzctUMg  exact        2,870     630.1 ms    1,195 MB
    ///              heuristic    3,790      26.6 ms       47 MB
    ///              none    55,802,068+   &gt;60,000 ms         -
    /// Wb5YT1b-U9Q  exact          178      20.1 ms       31 MB
    ///              heuristic      174       0.7 ms        1 MB
    ///              none         2,000       5.7 ms         -
    /// blank-sw2    exact       13,672     157.5 ms      275 MB
    ///              heuristic   13,721      12.5 ms        8 MB
    ///              none        82,142      59.6 ms         -
    /// </code>
    /// <para>
    /// The exact placement test bought almost no search reduction — at worst 32% more nodes, and
    /// slightly *fewer* on one case — for 12-29x the time. Dropping propagation altogether
    /// (<c>None</c>, the shape <c>IndexerConstraint</c> uses) is the opposite mistake: it is faster
    /// than <c>Exact</c> on easy boards but explodes to 55M nodes on <c>blPgSzctUMg</c>. The sandwich
    /// needs propagation; it did not need exactness.
    /// </para>
    /// <para>
    /// The allocation column above predates the span rewrite and is kept because it is what motivated
    /// the arms. <see cref="BruteForceArm.Heuristic"/> now allocates <b>nothing</b> while brute
    /// forcing; <see cref="BruteForceArm.Exact"/> still pays for <c>Permutations</c>, and only
    /// logical solving reaches it.
    /// </para>
    /// </remarks>
    internal enum BruteForceArm
    {
        /// <summary>Enumerate placements exactly. What the logical solver uses.</summary>
        Exact,
        /// <summary>Union whole value sets without enumerating placements. Default while brute forcing.</summary>
        Heuristic,
        /// <summary>Propagate nothing; leave it to EnforceConstraint and the weak links.</summary>
        None,
    }

    internal static readonly BruteForceArm DefaultBruteForceArm = ReadBruteForceArm();

    private static BruteForceArm ReadBruteForceArm() =>
        Environment.GetEnvironmentVariable("SUDOKU_SANDWICH_BF_ARM")?.ToLowerInvariant() switch
        {
            "exact" => BruteForceArm.Exact,
            "none" => BruteForceArm.None,
            _ => BruteForceArm.Heuristic,
        };

    /// <summary>
    /// For every set of <paramref name="unsetIdx"/>.Length distinct values that sums to
    /// <paramref name="remainingSum"/> and is available in <paramref name="possibleValuesMask"/>,
    /// records which value can appear in which cell by OR-ing it into <paramref name="keepMasks"/>.
    /// Returns whether any placement was possible at all.
    /// </summary>
    /// <param name="unsetIdx">Positions within <see cref="cells"/> of the cells being filled.</param>
    /// <param name="keepMasks">Indexed by position within <see cref="cells"/>, as <paramref name="unsetIdx"/> is.</param>
    /// <remarks>
    /// The candidate value sets come from <see cref="ValueCombinationTable"/> rather than being
    /// enumerated and sum-filtered per call, which is what made this the most expensive constraint in
    /// the corpus. See docs/pathological-outliers.md.
    /// </remarks>
    private bool ApplySumCombinations(
        Solver sudokuSolver,
        uint possibleValuesMask,
        ReadOnlySpan<int> unsetIdx,
        int remainingSum,
        Span<uint> keepMasks,
        bool useHeuristic)
    {
        // The exact arm needs List-shaped arguments for Permutations and CanPlaceDigits, so build
        // them once per call rather than once per value set. The heuristic arm — the brute-force
        // default — needs neither, and that is what keeps brute forcing free of allocation.
        List<(int, int)> exactCells = null;
        List<int> exactValues = null;
        if (!useHeuristic)
        {
            exactCells = new(unsetIdx.Length);
            foreach (int cellIndex in unsetIdx)
            {
                exactCells.Add(cells[cellIndex]);
            }
            exactValues = new(unsetIdx.Length);
        }

        uint[] tabulated = ValueCombinationTable.MasksFor(MAX_VALUE, unsetIdx.Length, remainingSum);
        if (tabulated == null)
        {
            // Grids above ValueCombinationTable.MAX_TABULATED_VALUE are not tabulated, so walk the
            // value sets directly.
            return EnumerateValueSets(
                sudokuSolver, possibleValuesMask, unsetIdx, keepMasks, exactCells, exactValues,
                1, unsetIdx.Length, remainingSum, 0);
        }

        bool foundAny = false;
        foreach (uint comboMask in tabulated)
        {
            // Needs a value that no remaining cell can take.
            if ((comboMask & ~possibleValuesMask) != 0)
            {
                continue;
            }

            foundAny |= ApplyValueSet(sudokuSolver, comboMask, unsetIdx, keepMasks, exactCells, exactValues);
        }
        return foundAny;
    }

    /// <summary>
    /// Walks every set of <paramref name="remainingCount"/> distinct values, drawn from
    /// <paramref name="firstValue"/> up and available in <paramref name="possibleValuesMask"/>, that
    /// sums to <paramref name="remainingSum"/>, and applies each. Recursion depth is the number of
    /// cells being filled, and it allocates nothing.
    /// </summary>
    /// <remarks>
    /// Only reached for grids above <see cref="ValueCombinationTable.MAX_TABULATED_VALUE"/>, which no
    /// real puzzle uses. It enumerates exactly the sets the table would have returned.
    /// </remarks>
    private bool EnumerateValueSets(
        Solver sudokuSolver,
        uint possibleValuesMask,
        ReadOnlySpan<int> unsetIdx,
        Span<uint> keepMasks,
        List<(int, int)> exactCells,
        List<int> exactValues,
        int firstValue,
        int remainingCount,
        int remainingSum,
        uint comboMask)
    {
        if (remainingCount == 0)
        {
            return remainingSum == 0
                && ApplyValueSet(sudokuSolver, comboMask, unsetIdx, keepMasks, exactCells, exactValues);
        }

        bool foundAny = false;
        for (int v = firstValue; v <= MAX_VALUE && v <= remainingSum; v++)
        {
            if ((possibleValuesMask & ValueMask(v)) == 0)
            {
                continue;
            }

            foundAny |= EnumerateValueSets(
                sudokuSolver, possibleValuesMask, unsetIdx, keepMasks, exactCells, exactValues,
                v + 1, remainingCount - 1, remainingSum - v, comboMask | ValueMask(v));
        }
        return foundAny;
    }

    /// <summary>
    /// Records that the values in <paramref name="comboMask"/> could fill the cells at
    /// <paramref name="unsetIdx"/>, OR-ing what each one may take into <paramref name="keepMasks"/>.
    /// Returns whether the set is placeable at all.
    /// </summary>
    /// <param name="exactCells">
    /// The cells at <paramref name="unsetIdx"/>, in the same order, or <see langword="null"/> to take
    /// the heuristic arm. Only the exact arm needs them.
    /// </param>
    /// <param name="exactValues">Scratch for the exact arm's value list; unused when it is null.</param>
    private bool ApplyValueSet(
        Solver sudokuSolver,
        uint comboMask,
        ReadOnlySpan<int> unsetIdx,
        Span<uint> keepMasks,
        List<(int, int)> exactCells,
        List<int> exactValues)
    {
        if (exactCells == null)
        {
            // Set-level union: if this value set is placeable at all, allow any of its values in any
            // of its cells, skipping the k! placement enumeration. Strictly a superset of what the
            // exact arm concludes, so it only ever propagates less.
            var board = sudokuSolver.Board;

            // Necessary condition for any placement: every cell can take some value of the set.
            foreach (int cellIndex in unsetIdx)
            {
                var (i, j) = cells[cellIndex];
                if ((board[i, j] & comboMask) == 0)
                {
                    return false;
                }
            }

            foreach (int cellIndex in unsetIdx)
            {
                keepMasks[cellIndex] |= comboMask;
            }
            return true;
        }

        exactValues.Clear();
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if ((comboMask & ValueMask(v)) != 0)
            {
                exactValues.Add(v);
            }
        }

        bool foundAny = false;
        foreach (var permutation in exactValues.Permutations())
        {
            if (!sudokuSolver.CanPlaceDigits(exactCells, permutation))
            {
                continue;
            }
            for (int cellIndex = 0; cellIndex < unsetIdx.Length; cellIndex++)
            {
                keepMasks[unsetIdx[cellIndex]] |= ValueMask(permutation[cellIndex]);
            }
            foundAny = true;
        }
        return foundAny;
    }

    /// <summary>
    /// Sums a combination without LINQ. <c>Enumerable.Sum</c> on a <c>List&lt;int&gt;</c> boxes the
    /// list's struct enumerator, and <see cref="InitCandidates"/> runs this once per enumerated
    /// combination. See docs/pathological-outliers.md.
    /// </summary>
    private static int SumOf(List<int> values)
    {
        int sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    /// <summary>
    /// Clears from every cell of the line whatever <paramref name="keepMasks"/> did not vouch for.
    /// Indexed by position within <see cref="cells"/>; a cell the caller concluded nothing about must
    /// be left at <c>ALL_VALUES_MASK</c> rather than zero.
    /// </summary>
    private LogicResult ApplyKeepMask(Solver sudokuSolver, ReadOnlySpan<uint> keepMasks, StringBuilder logicalStepDescription)
    {
        bool changed = false;
        var board = sudokuSolver.Board;
        int numCells = cells.Count;
        for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            uint clearMask = board[i, j] & ~keepMasks[cellIndex] & ~valueSetMask;
            if (clearMask != 0)
            {
                LogicResult logicResult = sudokuSolver.ClearMask(i, j, clearMask);
                if (logicResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"The sandwich sum cannot be fulfilled (such as in {CellName(i, j)}).");
                    }
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        if (!changed)
                        {
                            if (sudokuSolver.IsValueSet(i, j))
                            {
                                logicalStepDescription.Append($"Set {CellName(i, j)} to {sudokuSolver.GetValue((i, j))}");
                            }
                            else
                            {
                                logicalStepDescription.Append($"Removed {MaskToString(clearMask)} from {CellName(i, j)}");
                            }
                        }
                        else
                        {
                            if (sudokuSolver.IsValueSet(i, j))
                            {
                                logicalStepDescription.Append($"; set {CellName(i, j)} to {sudokuSolver.GetValue((i, j))}");
                            }
                            else
                            {
                                logicalStepDescription.Append($"; removed {MaskToString(clearMask)} from {CellName(i, j)}");
                            }
                        }
                    }
                    changed = true;
                }
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private (int, int) GetCrustIndices(Solver sudokuSolver)
    {
        var board = sudokuSolver.Board;
        int numCells = cells.Count;
        int crustIndex0 = -1;
        int crustIndex1 = -1;
        uint notCrustMask = ALL_VALUES_MASK & ~crustsMask;
        for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
        {
            var curCell = cells[cellIndex];
            uint mask = board[curCell.Item1, curCell.Item2];
            if ((mask & notCrustMask) == 0)
            {
                if (crustIndex0 == -1)
                {
                    crustIndex0 = cellIndex;
                }
                else
                {
                    crustIndex1 = cellIndex;
                    break;
                }
            }
        }
        return (crustIndex0, crustIndex1);
    }
}
