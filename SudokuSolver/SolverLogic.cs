namespace SudokuSolver;

public partial class Solver
{
    public void SetToBasicsOnly()
    {
        DisableTuples = false;
        DisablePointing = false;
        DisableFishes = true;
        DisableWings = true;
        DisableAIC = true;
        DisableContradictions = true;
    }

    /// <summary>
    /// Perform a logical solve until either the board is solved or there are no logical steps found.
    /// </summary>
    /// <param name="stepsDescription">Get a full description of all logical steps taken.</param>
    /// <returns></returns>
    public LogicResult ConsolidateBoard(List<LogicalStepDesc> logicalStepDescs = null, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        bool changed = false;
        LogicResult result;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!isBruteForcing && !IsBoardValid(logicalStepDescs))
            {
                result = LogicResult.Invalid;
            }
            else
            {
                result = StepLogic(logicalStepDescs);
            }
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    public LogicResult ApplySingles()
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        bool changed = false;
        LogicResult result;
        do
        {
            result = FindSingles();
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    private LogicResult FindSingles()
    {
        LogicResult result = FindNakedSingles(null);
        if (result == LogicResult.None)
        {
            result = FindHiddenSingle(null);
        }
        return result;
    }

    /// <summary>
    /// Perform one step of a logical solve and fill a description of the step taken.
    /// The description will contain the reason the board is invalid if that is what is returned.
    /// </summary>
    /// <param name="stepDescription"></param>
    /// <returns></returns>
    public LogicResult StepLogic(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        LogicResult result = LogicResult.None;

        result = FindNakedSingles(logicalStepDescs);
        if (result != LogicResult.None)
        {
            return result;
        }

        result = FindHiddenSingle(logicalStepDescs);
        if (result != LogicResult.None)
        {
            return result;
        }

        foreach (var constraint in constraints)
        {
            result = constraint.StepLogic(this, logicalStepDescs, isBruteForcing);
            if (result != LogicResult.None)
            {
                if (logicalStepDescs != null)
                {
                    if (logicalStepDescs.Count > 0)
                    {
                        logicalStepDescs[^1] = logicalStepDescs[^1].WithPrefix($"[{constraint.SpecificName}] ");
                    }
                    else
                    {
                        logicalStepDescs.Add(new(
                            $"{constraint.SpecificName} reported the board was invalid without specifying why. This is a bug: please report it!",
                            Enumerable.Empty<int>(),
                            Enumerable.Empty<int>()
                        ));
                    }
                }

                return result;
            }
        }

        if (isBruteForcing)
        {
            return LogicResult.None;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Re-evaluate weak links
        foreach (var constraint in constraints)
        {
            var logicResult = constraint.InitLinks(this, logicalStepDescs, false);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!DisableTuples || !DisablePointing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = FindNakedTuplesAndPointing(!DisableTuples, !DisablePointing, logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisablePointing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = FindDirectCellForcing(logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableFishes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = FindFishes(logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            result = FindFinnedFishes(logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableWings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = FindWings(logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableAIC)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = FindAIC(logicalStepDescs, cancellationToken);
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FindSimpleContradictions(logicalStepDescs, cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        return LogicResult.None;
    }

    private bool IsBoardValid(List<LogicalStepDesc> logicalStepDescs)
    {
        // Check for empty cells
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            if ((board[cellIndex] & ~valueSetMask) == 0)
            {
                if (logicalStepDescs != null)
                {
                    var (i, j) = CellIndexToCoord(cellIndex);
                    logicalStepDescs?.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                }
                return false;
            }
        }

        // Check for values that groups must contain but don't
        foreach (var group in Groups)
        {
            var groupCells = group.Cells;
            int numCells = group.Cells.Count;
            if (numCells != MAX_VALUE)
            {
                continue;
            }

            uint atLeastOnce = 0;
            for (int groupindex = 0; groupindex < numCells; groupindex++)
            {
                int cellIndex = groupCells[groupindex];
                atLeastOnce |= board[cellIndex];
            }
            atLeastOnce &= ~valueSetMask;

            if (atLeastOnce != ALL_VALUES_MASK && numCells == MAX_VALUE)
            {
                logicalStepDescs?.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~atLeastOnce)}.", group.Cells.Select(CellIndexToCoord)));
                return false;
            }
        }

        if (isBruteForcing)
        {
            return true;
        }

        // Check for groups which contain too many cells with a tuple that is too small
        List<int> unsetCells = new(MAX_VALUE);
        foreach (var group in Groups)
        {
            for (int tupleSize = 2; tupleSize < MAX_VALUE; tupleSize++)
            {
                unsetCells.Clear();
                foreach (int cellIndex in group.Cells)
                {
                    uint cellMask = board[cellIndex];
                    if (!IsValueSet(cellMask))
                    {
                        unsetCells.Add(cellIndex);
                    }
                }

                if (unsetCells.Count < tupleSize)
                {
                    continue;
                }

                foreach (var tupleCells in unsetCells.Combinations(tupleSize))
                {
                    uint tupleMask = CandidateMask(tupleCells);
                    if (ValueCount(tupleMask) < tupleSize)
                    {
                        logicalStepDescs?.Add(new($"{CompactName(tupleCells)} in {group} are {tupleCells.Count} cells with only {ValueCount(tupleMask)} candidates available ({MaskToString(tupleMask)}).", tupleCells.Select(CellIndexToCoord)));
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private LogicResult FindNakedSingles(List<LogicalStepDesc> logicalStepDescs)
    {
        if (isInvalid)
        {
            return LogicResult.Invalid;
        }

        if (unsetCellsCount == 0)
        {
            return LogicResult.PuzzleComplete;
        }

        if (logicalStepDescs == null)
        {
            bool changed = false;
            while (pendingNakedSingles.Count > 0)
            {
                int cellIndex = pendingNakedSingles[^1];
                pendingNakedSingles.RemoveAt(pendingNakedSingles.Count - 1);

                uint mask = board[cellIndex];
                if ((mask & ~valueSetMask) == 0)
                {
                    return LogicResult.Invalid;
                }

                if (!IsValueSet(mask))
                {
                    if (ValueCount(mask) == 1)
                    {
                        int value = GetValue(mask);
                        if (!SetValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                return LogicResult.Changed;
            }
        }
        else
        {
            while (pendingNakedSingles.Count > 0)
            {
                int cellIndex = pendingNakedSingles[^1];
                pendingNakedSingles.RemoveAt(pendingNakedSingles.Count - 1);

                var (i, j) = CellIndexToCoord(cellIndex);

                uint mask = board[cellIndex];
                if ((mask & ~valueSetMask) == 0)
                {
                    logicalStepDescs.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                    return LogicResult.Invalid;
                }

                if (!IsValueSet(mask))
                {
                    int value = GetValue(mask);
                    if (!SetValue(i, j, value))
                    {
                        logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)} cannot be set to {value}.", (i, j)));
                        return LogicResult.Invalid;
                    }
                    logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)}={value}", CandidateIndex((i, j), value).ToEnumerable(), null, isSingle: true));
                    return LogicResult.Changed;
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FindHiddenSingle(List<LogicalStepDesc> logicalStepDescs)
    {
        foreach (var group in Groups)
        {
            var groupCells = group.Cells;
            int numCells = group.Cells.Count;
            if (numCells != MAX_VALUE && (isBruteForcing || group.FromConstraint == null))
            {
                continue;
            }

            uint atLeastOnce = 0;
            uint moreThanOnce = 0;
            uint setMask = 0;
            for (int groupIndex = 0; groupIndex < numCells; groupIndex++)
            {
                int cellIndex = groupCells[groupIndex];
                uint mask = board[cellIndex];
                if (IsValueSet(mask))
                {
                    setMask |= mask;
                }
                else
                {
                    moreThanOnce |= atLeastOnce & mask;
                    atLeastOnce |= mask;
                }
            }
            setMask &= ~valueSetMask;
            if (numCells == MAX_VALUE && (atLeastOnce | setMask) != ALL_VALUES_MASK)
            {
                if (logicalStepDescs != null)
                {
                    logicalStepDescs.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~(atLeastOnce | setMask))}.", group.Cells.Select(CellIndexToCoord)));
                }
                return LogicResult.Invalid;
            }

            uint exactlyOnce = atLeastOnce & ~moreThanOnce;
            if (exactlyOnce != 0)
            {
                int val = 0;
                int valCellIndex = -1;
                if (numCells == MAX_VALUE)
                {
                    val = MinValue(exactlyOnce);
                    uint valMask = ValueMask(val);
                    foreach (int cellIndex in group.Cells)
                    {
                        if ((board[cellIndex] & valMask) != 0)
                        {
                            valCellIndex = cellIndex;
                            break;
                        }
                    }
                }
                else
                {
                    int minValue = MinValue(exactlyOnce);
                    int maxValue = MaxValue(exactlyOnce);
                    for (int v = minValue; v <= maxValue; v++)
                    {
                        if ((exactlyOnce & v) != 0)
                        {
                            List<(int, int)> cellsMustContain = group.FromConstraint?.CellsMustContain(this, val);
                            if (cellsMustContain != null && cellsMustContain.Count == 1)
                            {
                                val = v;
                                valCellIndex = CellIndex(cellsMustContain[0]);
                                break;
                            }
                        }
                    }
                }

                if (valCellIndex >= 0)
                {
                    if (!SetValue(valCellIndex, val))
                    {
                        if (logicalStepDescs != null)
                        {
                            logicalStepDescs.Add(new($"Hidden Single in {group}: {CellName(CellIndexToCoord(valCellIndex))} cannot be set to {val}.", CellIndexToCoord(valCellIndex)));
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicalStepDescs != null)
                    {
                        logicalStepDescs.Add(new($"Hidden Single in {group}: {CellName(CellIndexToCoord(valCellIndex))}={val}", CandidateIndex(valCellIndex, val).ToEnumerable(), null, isSingle: true));
                    }
                    return LogicResult.Changed;
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FindDirectCellForcing(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        List<int> elims = [];
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                int cellIndex = CellIndex(i, j);
                uint mask = board[cellIndex];
                if (IsValueSet(mask))
                {
                    continue;
                }

                elims.Clear();
                int candBase = cellIndex * MAX_VALUE - 1;
                bool isFirst = true;

                uint remainingMask = mask;
                while (remainingMask != 0)
                {
                    int v = MinValue(remainingMask);
                    remainingMask &= ~ValueMask(v);

                    int candIndex = candBase + v;

                    if (isFirst)
                    {
                        InitIntersectWeakLinks(elims, candIndex);
                        isFirst = false;
                    }
                    else
                    {
                        // Subsequent candidates: keep only common elements
                        IntersectWeakLinks(elims, candIndex);
                    }

                    if (elims.Count == 0)
                    {
                        break;
                    }
                }

                if (elims.Count > 0)
                {
                    if (logicalStepDescs != null)
                    {
                        List<(int, int)> sourceCell = new() { (i, j) };
                        logicalStepDescs.Add(new(
                            desc: $"Direct Cell Forcing: {CompactName(mask, sourceCell)} => {DescribeElims(elims)}",
                            sourceCandidates: CandidateIndexes(mask, sourceCell),
                            elimCandidates: elims
                        ));
                    }
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FindNakedTuplesAndPointing(bool isTuplesEnabled, bool isPointingEnabled, List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        List<int> unsetCells = new(MAX_VALUE);
        for (int numCells = 2; numCells <= MAX_VALUE; numCells++)
        {
            if (isTuplesEnabled && numCells < MAX_VALUE)
            {
                foreach (var group in Groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (group.Cells.Count < numCells)
                    {
                        continue;
                    }

                    // Make a list of cells which aren't already set and contain fewer candidates than the tuple size
                    unsetCells.Clear();
                    foreach (int cellIndex in group.Cells)
                    {
                        uint cellMask = board[cellIndex];
                        if (!IsValueSet(cellMask) && ValueCount(cellMask) <= numCells)
                        {
                            unsetCells.Add(cellIndex);
                        }
                    }

                    if (unsetCells.Count < numCells)
                    {
                        continue;
                    }

                    foreach (var tupleCells in unsetCells.Combinations(numCells))
                    {
                        uint tupleMask = CandidateMask(tupleCells);
                        if (ValueCount(tupleMask) == numCells)
                        {
                            var elims = CalcElims(tupleMask, tupleCells);
                            if (elims.Count > 0)
                            {
                                if (logicalStepDescs != null)
                                {
                                    logicalStepDescs.Add(new(
                                        desc: $"Tuple: {CompactName(tupleMask, tupleCells)} in {group} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(tupleMask, tupleCells.Select(CellIndexToCoord)),
                                        elimCandidates: elims
                                    ));
                                }
                                if (!ClearCandidates(elims))
                                {
                                    return LogicResult.Invalid;
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            if (isPointingEnabled)
            {
                // Look for "pointing" but limit to the same number of cells as the tuple size
                // This is a heuristic ordering which avoids finding something like a pair as pointing,
                // but prefers 2 cells pointing over a triple.
                foreach (var group in Groups.Where(g => g.Cells.Count == MAX_VALUE || g.FromConstraint != null))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint setValuesMask = 0;
                    uint unsetValuesMask = 0;
                    foreach (int cellIndex in group.Cells)
                    {
                        uint mask = board[cellIndex] & ~valueSetMask;
                        if (ValueCount(mask) == 1)
                        {
                            setValuesMask |= mask;
                        }
                        else
                        {
                            unsetValuesMask |= mask;
                        }
                    }
                    unsetValuesMask &= ~setValuesMask;
                    if (unsetValuesMask == 0)
                    {
                        continue;
                    }

                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (!HasValue(unsetValuesMask, v))
                        {
                            continue;
                        }

                        List<(int, int)> cellsMustContain = group.CellsMustContain(this, v);
                        if (cellsMustContain != null && cellsMustContain.Count == numCells)
                        {
                            var logicResult = HandleMustContain(group.ToString(), v, cellsMustContain, logicalStepDescs);
                            if (logicResult != LogicResult.None)
                            {
                                return logicResult;
                            }
                        }
                    }
                }

                // Check constraints as well
                foreach (var constraint in constraints)
                {
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        List<(int, int)> cellsMustContain = constraint.CellsMustContain(this, v);
                        if (cellsMustContain != null && cellsMustContain.Count == numCells)
                        {
                            var logicResult = HandleMustContain(constraint.SpecificName, v, cellsMustContain, logicalStepDescs);
                            if (logicResult != LogicResult.None)
                            {
                                return logicResult;
                            }
                        }
                    }
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult HandleMustContain(string name, int v, List<(int, int)> cellsMustContain, List<LogicalStepDesc> logicalStepDescs)
    {
        if (cellsMustContain == null || cellsMustContain.Count == 0)
        {
            return LogicResult.None;
        }

        if (cellsMustContain.Count == 1)
        {
            var (i, j) = cellsMustContain[0];
            if (logicalStepDescs != null)
            {
                logicalStepDescs.Add(new($"Hidden Single in {name}: {CellName(i, j)}={v}", CandidateIndex((i, j), v).ToEnumerable(), null, isSingle: true));
            }
            if (!SetValue(i, j, v))
            {
                return LogicResult.Invalid;
            }
            return LogicResult.Changed;
        }

        uint valueMask = ValueMask(v);
        var elims = CalcElims(valueMask, cellsMustContain);
        if (elims == null || elims.Count == 0)
        {
            return LogicResult.None;
        }

        if (logicalStepDescs != null)
        {
            logicalStepDescs.Add(new(
                        desc: $"Pointing: {v}{CompactName(cellsMustContain)} in {name} => {DescribeElims(elims)}",
                        sourceCandidates: CandidateIndexes(valueMask, cellsMustContain),
                        elimCandidates: elims
                    ));
        }
        if (!ClearCandidates(elims))
        {
            return LogicResult.Invalid;
        }
        return LogicResult.Changed;
    }

    private LogicResult FindUnorthodoxTuple(List<LogicalStepDesc> logicalStepDescs, int tupleSize, CancellationToken cancellationToken)
    {
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                int valueCount = ValueCount(mask);
                if (!IsValueSet(mask) && valueCount > 1 && valueCount <= tupleSize)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        int numCandidateCells = candidateCells.Count;
        if (numCandidateCells < tupleSize)
        {
            return LogicResult.None;
        }

        bool IsPotentiallyValidTuple(uint valuesMask, List<(int, int)> tupleCells)
        {
            int valuesMaskCount = ValueCount(valuesMask);
            if (valuesMaskCount > tupleSize)
            {
                return false;
            }

            if (tupleCells.Count <= 1)
            {
                return true;
            }

            if (tupleCells.Count == tupleSize && valuesMaskCount != tupleSize)
            {
                return false;
            }

            int minValue = MinValue(valuesMask);
            int maxValue = MaxValue(valuesMask);
            for (int v = minValue; v <= maxValue; v++)
            {
                uint valueMask = ValueMask(v);
                if ((valuesMask & valueMask) != 0)
                {
                    int numWithCandidate = 0;
                    for (int k = 0; k < tupleCells.Count; k++)
                    {
                        var (i, j) = tupleCells[k];
                        if ((this[i, j] & valueMask) != 0)
                        {
                            numWithCandidate++;
                        }
                    }

                    if (numWithCandidate > 1 && !IsGroup(tupleCells, v))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        List<(int, int)> tupleCells = new(tupleSize);
        (int index, (int i, int j) coord, uint accumMask, bool isValid)[] tupleInfoArray = new (int, (int, int), uint, bool)[tupleSize];

        // Initialize the wing info array with the first combination even if it's invalid
        {
            uint accumMask = 0;
            for (int k = 0; k < tupleSize; k++)
            {
                var coord = candidateCells[k];
                accumMask |= this[coord.Item1, coord.Item2];
                tupleCells.Add(coord);
                bool isValid = IsPotentiallyValidTuple(accumMask, tupleCells);
                tupleInfoArray[k] = (k, coord, accumMask, isValid);
            }
        }

        while (true)
        {
            // Check if this wing info is valid and performs eliminations
            var (_, _, accumMask, isValid) = tupleInfoArray[tupleSize - 1];
            if (isValid && ValueCount(accumMask) == tupleSize)
            {
                // All candidates in this tuple must be present, so we can eliminate based on each value
                var elims = CalcElims(accumMask, tupleCells);
                if (elims != null && elims.Count > 0)
                {
                    if (logicalStepDescs != null)
                    {
                        StringBuilder wingName = new();
                        for (int k = tupleSize - 1; k >= 0; k--)
                        {
                            wingName.Append((char)('Z' - k));
                        }
                        logicalStepDescs?.Add(new(
                                desc: $"Unorthodox Tuple ({tupleSize}): {MaskToString(accumMask)} in {CompactName(tupleCells)} => {DescribeElims(elims)}",
                                sourceCandidates: CandidateIndexes(accumMask, tupleCells),
                                elimCandidates: elims
                            ));
                    }
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }

            // Find the first invalid index. This is the minimum index to increment.
            int firstInvalid = tupleSize;
            for (int k = 0; k < tupleSize; k++)
            {
                var tupleinfo = tupleInfoArray[k];
                if (!tupleinfo.isValid)
                {
                    firstInvalid = k;
                    break;
                }
            }

            // Find the last index which can be incremented
            int lastCanIncrement = -1;
            for (int k = tupleSize - 1; k >= 0; k--)
            {
                var tupleInfo = tupleInfoArray[k];
                if (tupleInfo.index + 1 < numCandidateCells - (tupleSize - k))
                {
                    lastCanIncrement = k;
                    break;
                }
            }

            // Check if done
            if (lastCanIncrement == -1)
            {
                break;
            }

            // Increment the smaller of the first invalid one or the last that can increment.
            int k0 = Math.Min(lastCanIncrement, firstInvalid);

            tupleCells.Clear();
            for (int k = 0; k < k0; k++)
            {
                tupleCells.Add(tupleInfoArray[k].coord);
            }

            // Increment to the next combination
            int nextIndex = tupleInfoArray[k0].index + 1;
            var nextCoord = candidateCells[nextIndex];
            uint nextAccumMask = this[nextCoord.Item1, nextCoord.Item2];
            if (k0 > 0)
            {
                nextAccumMask |= tupleInfoArray[k0 - 1].accumMask;
            }
            tupleCells.Add(nextCoord);
            tupleInfoArray[k0] = (nextIndex, nextCoord, nextAccumMask, IsPotentiallyValidTuple(nextAccumMask, tupleCells));

            for (int k1 = k0 + 1; k1 < tupleSize; k1++)
            {
                nextIndex = tupleInfoArray[k1 - 1].index + 1;
                nextCoord = candidateCells[nextIndex];
                nextAccumMask |= this[nextCoord.Item1, nextCoord.Item2];
                tupleCells.Add(nextCoord);
                tupleInfoArray[k1] = (nextIndex, nextCoord, nextAccumMask, IsPotentiallyValidTuple(nextAccumMask, tupleCells));
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindFishes(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        if (WIDTH != MAX_VALUE || HEIGHT != MAX_VALUE)
        {
            return LogicResult.None;
        }
        // Since these are all guaranteed equal at this point, only MAX_VALUE will be used for all three purposes.

        // Construct a transformed lookup of which values are in which rows/cols
        uint[][,] rowcolIndexByValue = new uint[2][,];
        rowcolIndexByValue[0] = new uint[MAX_VALUE, MAX_VALUE];
        rowcolIndexByValue[1] = new uint[MAX_VALUE, MAX_VALUE];
        for (int i = 0; i < MAX_VALUE; i++)
        {
            for (int j = 0; j < MAX_VALUE; j++)
            {
                uint mask = this[i, j] & ~valueSetMask;
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        rowcolIndexByValue[0][v - 1, j] |= (1u << i);
                        rowcolIndexByValue[1][v - 1, i] |= (1u << j);
                    }
                }
            }
        }
        List<int> unsetRowOrCols = new(MAX_VALUE);

        // Look for standard fishes
        for (int tupleSize = 2; tupleSize <= MAX_VALUE / 2; tupleSize++)
        {
            for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint[,] indexByValue = rowcolIndexByValue[rowOrCol];

                for (int valueIndex = 0; valueIndex < MAX_VALUE; valueIndex++)
                {
                    int value = valueIndex + 1;

                    // Make a list of pairs for the row/col which aren't already filled
                    unsetRowOrCols.Clear();
                    for (int j = 0; j < MAX_VALUE; j++)
                    {
                        uint cellMask = indexByValue[valueIndex, j];
                        int valueCount = ValueCount(cellMask);
                        if (valueCount > 1 && valueCount <= tupleSize)
                        {
                            unsetRowOrCols.Add(j);
                        }
                    }
                    if (unsetRowOrCols.Count < tupleSize)
                    {
                        continue;
                    }

                    foreach (var tupleRowOrCols in unsetRowOrCols.Combinations(tupleSize))
                    {
                        uint tupleMask = 0;
                        foreach (int j in tupleRowOrCols)
                        {
                            tupleMask |= indexByValue[valueIndex, j];
                        }

                        if (ValueCount(tupleMask) == tupleSize)
                        {
                            List<int> elims = null;
                            for (int j = 0; j < MAX_VALUE; j++)
                            {
                                if (tupleRowOrCols.Contains(j))
                                {
                                    continue;
                                }

                                uint mask = indexByValue[valueIndex, j];
                                uint elimMask = mask & tupleMask;
                                if (elimMask != 0)
                                {
                                    for (int i = 0; i < MAX_VALUE; i++)
                                    {
                                        if ((elimMask & (1u << i)) != 0)
                                        {
                                            elims ??= new();
                                            elims.Add(CandidateIndex(rowOrCol == 0 ? (i, j) : (j, i), value));
                                        }
                                    }
                                }
                            }

                            if (elims != null && elims.Count > 0)
                            {
                                if (logicalStepDescs != null)
                                {
                                    string techniqueName = tupleSize switch
                                    {
                                        2 => "X-Wing",
                                        3 => "Swordfish",
                                        4 => "Jellyfish",
                                        _ => $"{tupleSize}-Fish",
                                    };

                                    List<(int, int)> fishCells = new();
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j];
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((mask & (1u << i)) != 0)
                                            {
                                                fishCells.Add(rowOrCol == 0 ? (i, j) : (j, i));
                                            }
                                        }
                                    }

                                    logicalStepDescs.Add(new(
                                        desc: $"{techniqueName}: {value} {CompactName(fishCells)} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(ValueMask(value), fishCells),
                                        elimCandidates: elims
                                    ));
                                }
                                if (!ClearCandidates(elims))
                                {
                                    return LogicResult.Invalid;
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindFinnedFishes(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        if (WIDTH != MAX_VALUE || HEIGHT != MAX_VALUE)
        {
            return LogicResult.None;
        }
        // Since these are all guaranteed equal at this point, only MAX_VALUE will be used for all three purposes.

        // Construct a transformed lookup of which values are in which rows/cols
        uint[][,] rowcolIndexByValue = new uint[2][,];
        rowcolIndexByValue[0] = new uint[MAX_VALUE, MAX_VALUE];
        rowcolIndexByValue[1] = new uint[MAX_VALUE, MAX_VALUE];
        for (int i = 0; i < MAX_VALUE; i++)
        {
            for (int j = 0; j < MAX_VALUE; j++)
            {
                uint mask = this[i, j] & ~valueSetMask;
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        rowcolIndexByValue[0][v - 1, j] |= (1u << i);
                        rowcolIndexByValue[1][v - 1, i] |= (1u << j);
                    }
                }
            }
        }
        List<int> unsetRowOrCols = new(MAX_VALUE);

        // Look for finned fishes
        for (int tupleSize = 2; tupleSize <= MAX_VALUE / 2; tupleSize++)
        {
            for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint[,] indexByValue = rowcolIndexByValue[rowOrCol];

                for (int valueIndex = 0; valueIndex < MAX_VALUE; valueIndex++)
                {
                    int value = valueIndex + 1;

                    // Make a list of pairs for the row/col which aren't already filled
                    unsetRowOrCols.Clear();
                    for (int j = 0; j < MAX_VALUE; j++)
                    {
                        uint cellMask = indexByValue[valueIndex, j];
                        int valueCount = ValueCount(cellMask);
                        if (valueCount > 1)
                        {
                            unsetRowOrCols.Add(j);
                        }
                    }
                    if (unsetRowOrCols.Count < tupleSize)
                    {
                        continue;
                    }

                    foreach (var tupleRowOrCols in unsetRowOrCols.Combinations(tupleSize))
                    {
                        uint tupleMask = 0;
                        foreach (int j in tupleRowOrCols)
                        {
                            tupleMask |= indexByValue[valueIndex, j];
                        }

                        List<int> nonTupleRowOrCols = new();
                        for (int j = 0; j < MAX_VALUE; j++)
                        {
                            if (!tupleRowOrCols.Contains(j))
                            {
                                nonTupleRowOrCols.Add(j);
                            }
                        }

                        int numPositions = ValueCount(tupleMask);
                        if (numPositions > tupleSize)
                        {
                            List<int> positions = new(numPositions);
                            for (int j = 0; j < MAX_VALUE; j++)
                            {
                                if ((tupleMask & (1u << j)) != 0)
                                {
                                    positions.Add(j);
                                }
                            }
                            foreach (var positionCombo in positions.Combinations(tupleSize))
                            {
                                uint positionMask = 0;
                                foreach (int j in positionCombo)
                                {
                                    positionMask |= (1u << j);
                                }

                                // Calculate the eliminations from the fish formed by these positions
                                HashSet<int> elims = null;
                                foreach (int j in nonTupleRowOrCols)
                                {
                                    uint mask = indexByValue[valueIndex, j];
                                    uint elimMask = mask & positionMask;
                                    if (elimMask != 0)
                                    {
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((elimMask & (1u << i)) != 0)
                                            {
                                                elims ??= new();
                                                elims.Add(CandidateIndex(rowOrCol == 0 ? (i, j) : (j, i), value));
                                            }
                                        }
                                    }
                                }

                                if (elims != null)
                                {
                                    // Calcuate the eliminations from individual candidates not in the fish
                                    uint notPostionMask = tupleMask & ~positionMask;
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j] & notPostionMask;
                                        if (mask != 0)
                                        {
                                            for (int i = 0; i < MAX_VALUE; i++)
                                            {
                                                if ((mask & (1u << i)) != 0)
                                                {
                                                    var finCell = rowOrCol == 0 ? (i, j) : (j, i);
                                                    elims.IntersectWith(weakLinks[CandidateIndex(finCell, value)]);
                                                }
                                            }
                                        }
                                        if (elims.Count == 0)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (elims != null && elims.Count > 0)
                                {
                                    string techniqueName = tupleSize switch
                                    {
                                        2 => "X-Wing",
                                        3 => "Swordfish",
                                        4 => "Jellyfish",
                                        _ => $"{tupleSize}-Fish",
                                    };

                                    List<(int, int)> fishCells = new();
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j];
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((mask & (1u << i)) != 0)
                                            {
                                                fishCells.Add(rowOrCol == 0 ? (i, j) : (j, i));
                                            }
                                        }
                                    }

                                    logicalStepDescs?.Add(new(
                                        desc: $"Finned {techniqueName}: {value} {CompactName(fishCells)} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(ValueMask(value), fishCells),
                                        elimCandidates: elims
                                    ));
                                    if (!ClearCandidates(elims))
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    return LogicResult.Changed;
                                }
                            }
                        }
                    }
                }
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindWings(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        if (isBruteForcing)
        {
            return LogicResult.None;
        }

        // A y-wing always involves three bivalue cells.
        // The three cells have 3 candidates between them, and one cell called the "pivot" sees the other two "pincers".
        // A strong link is formed between the common candidate between the two "pincer" cells.
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                if (!IsValueSet(mask) && ValueCount(mask) == 2)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        // Look for Y-Wings
        for (int c0 = 0; c0 < candidateCells.Count - 2; c0++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (i0, j0) = candidateCells[c0];
            uint mask0 = this[i0, j0];
            for (int c1 = c0 + 1; c1 < candidateCells.Count - 1; c1++)
            {
                var (i1, j1) = candidateCells[c1];
                uint mask1 = this[i1, j1];
                if (mask0 == mask1 || ValueCount(mask0 | mask1) != 3)
                {
                    continue;
                }

                for (int c2 = c1 + 1; c2 < candidateCells.Count; c2++)
                {
                    var (i2, j2) = candidateCells[c2];
                    uint mask2 = this[i2, j2];
                    if (mask0 == mask2 || mask1 == mask2)
                    {
                        continue;
                    }

                    uint combinedMask = mask0 | mask1 | mask2;
                    if (ValueCount(combinedMask) == 3)
                    {
                        int value01 = GetValue(mask0 & mask1);
                        int value02 = GetValue(mask0 & mask2);
                        int value12 = GetValue(mask1 & mask2);
                        int cand01_0 = CandidateIndex((i0, j0), value01);
                        int cand01_1 = CandidateIndex((i1, j1), value01);
                        int cand02_0 = CandidateIndex((i0, j0), value02);
                        int cand02_2 = CandidateIndex((i2, j2), value02);
                        int cand12_1 = CandidateIndex((i1, j1), value12);
                        int cand12_2 = CandidateIndex((i2, j2), value12);
                        bool weak01 = weakLinks[cand01_0].BinarySearch(cand01_1) >= 0;
                        bool weak02 = weakLinks[cand02_0].BinarySearch(cand02_2) >= 0;
                        bool weak12 = weakLinks[cand12_1].BinarySearch(cand12_2) >= 0;
                        int weakCount = (weak01 ? 1 : 0) + (weak02 ? 1 : 0) + (weak12 ? 1 : 0);
                        if (weakCount != 2)
                        {
                            continue;
                        }

                        List<int> elims;
                        if (weak01 && weak02)
                        {
                            // Pivot is 0, eliminate from the shared 12 value
                            elims = CalcElims(cand12_1, cand12_2).ToList();
                        }
                        else if (weak01 && weak12)
                        {
                            // Pivot is 1, eliminate from the shared 02 value
                            elims = CalcElims(cand02_0, cand02_2).ToList();
                        }
                        else
                        {
                            // Pivot is 2, elimiate from the shared 01 value
                            elims = CalcElims(cand01_0, cand01_1).ToList();
                        }
                        if (elims.Count > 0)
                        {
                            List<(int, int)> cells = new() { (i0, j0), (i1, j1), (i2, j2) };
                            logicalStepDescs?.Add(new(
                                    desc: $"Y-Wing: {MaskToString(combinedMask)} in {CompactName(cells)} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(combinedMask, cells),
                                    elimCandidates: elims
                                ));
                            if (!ClearCandidates(elims))
                            {
                                return LogicResult.Invalid;
                            }
                            return LogicResult.Changed;
                        }
                    }
                }
            }
        }

        // Look for XYZ-wings
        // Look for unorthodox tuples of this size before looking for wings
        LogicResult logicResult = FindUnorthodoxTuple(logicalStepDescs, 3, cancellationToken);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        logicResult = FindNWing(logicalStepDescs, 3, cancellationToken);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        // TODO: W-Wing

        // Look for WXYZ-Wings
        logicResult = FindUnorthodoxTuple(logicalStepDescs, 4, cancellationToken);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        logicResult = FindNWing(logicalStepDescs, 4, cancellationToken);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        // Look for (N)-Wings [Extension of XYZ-Wings and WXYZ-Wings for sizes 5+]
        // An (N)-Wing is N candidates limited to N cells.
        // Looking at each candidate, all but one of them cannot repeat within those cells.
        // This implies that any cell seen by the instances of that last candidate can be eliminated.
        for (int wingSize = 5; wingSize <= MAX_VALUE; wingSize++)
        {
            // Look for unorthodox tuples of this size before looking for wings
            logicResult = FindUnorthodoxTuple(logicalStepDescs, wingSize, cancellationToken);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }

            logicResult = FindNWing(logicalStepDescs, wingSize, cancellationToken);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindNWing(List<LogicalStepDesc> logicalStepDescs, int wingSize, CancellationToken cancellationToken)
    {
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                int valueCount = ValueCount(mask);
                if (!IsValueSet(mask) && valueCount > 1 && valueCount <= wingSize)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        int numCandidateCells = candidateCells.Count;
        if (numCandidateCells < wingSize)
        {
            return LogicResult.None;
        }

        int UngroupedValue(uint valuesMask, List<(int, int)> wingCells, int curUngroupedValue)
        {
            if (curUngroupedValue < 0 || ValueCount(valuesMask) > wingSize)
            {
                return -1;
            }

            if (wingCells.Count <= 1)
            {
                return 0;
            }
            bool checkForSingle = wingCells.Count == wingSize;
            if (checkForSingle && ValueCount(valuesMask) != wingSize)
            {
                return -1;
            }

            valuesMask &= ~ValueMask(curUngroupedValue);

            int ungroupedValue = curUngroupedValue;
            int minValue = MinValue(valuesMask);
            int maxValue = MaxValue(valuesMask);
            for (int v = minValue; v <= maxValue; v++)
            {
                uint valueMask = ValueMask(v);
                if ((valuesMask & valueMask) != 0)
                {
                    int numWithCandidate = 0;
                    for (int k = 0; k < wingCells.Count; k++)
                    {
                        var (i, j) = wingCells[k];
                        if ((this[i, j] & valueMask) != 0)
                        {
                            numWithCandidate++;
                        }
                    }

                    if (checkForSingle && numWithCandidate == 1)
                    {
                        return -1;
                    }

                    if (numWithCandidate > 1 && !IsGroup(wingCells, v))
                    {
                        if (ungroupedValue == 0)
                        {
                            ungroupedValue = v;
                        }
                        else
                        {
                            return -1;
                        }
                    }
                }
            }
            return ungroupedValue;
        }

        List<(int, int)> wingCells = new(wingSize);
        (int index, (int i, int j) coord, uint accumMask, int ungroupedValue)[] wingInfoArray = new (int, (int, int), uint, int)[wingSize];

        // Initialize the wing info array with the first combination even if it's invalid
        {
            uint accumMask = 0;
            for (int k = 0; k < wingSize; k++)
            {
                var coord = candidateCells[k];
                accumMask |= this[coord.Item1, coord.Item2];
                wingCells.Add(coord);

                int ungroupedValue = UngroupedValue(accumMask, wingCells, k == 0 ? 0 : wingInfoArray[k - 1].ungroupedValue);
                wingInfoArray[k] = (k, coord, accumMask, ungroupedValue);
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if this wing info is valid and performs eliminations
            var (_, _, accumMask, ungroupedValue) = wingInfoArray[wingSize - 1];
            if (ValueCount(accumMask) == wingSize && ungroupedValue > 0)
            {
                // At least one of the ungrouped candidates is true, so eliminate any candidates with a weak link to all of them.
                var elims = CalcElims(ValueMask(ungroupedValue), wingCells);
                if (elims != null && elims.Count > 0)
                {
                    if (logicalStepDescs != null)
                    {
                        StringBuilder wingName = new();
                        for (int k = wingSize - 1; k >= 0; k--)
                        {
                            wingName.Append((char)('Z' - k));
                        }
                        logicalStepDescs?.Add(new(
                                desc: $"{wingName}-Wing: {MaskToString(accumMask)} in {CompactName(wingCells)} => {DescribeElims(elims)}",
                                sourceCandidates: CandidateIndexes(accumMask, wingCells),
                                elimCandidates: elims
                            ));
                    }
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }

            // Find the first invalid index. This is the minimum index to increment.
            int firstInvalid = wingSize;
            for (int k = 0; k < wingSize; k++)
            {
                var wingInfo = wingInfoArray[k];
                if (wingInfo.ungroupedValue < 0)
                {
                    firstInvalid = k;
                    break;
                }
            }

            // Find the last index which can be incremented
            int lastCanIncrement = -1;
            for (int k = wingSize - 1; k >= 0; k--)
            {
                var wingInfo = wingInfoArray[k];
                if (wingInfo.index + 1 < numCandidateCells - (wingSize - k))
                {
                    lastCanIncrement = k;
                    break;
                }
            }

            // Check if done
            if (lastCanIncrement == -1)
            {
                break;
            }

            // Increment the smaller of the first invalid one or the last that can increment.
            int k0 = Math.Min(lastCanIncrement, firstInvalid);

            wingCells.Clear();
            for (int k = 0; k < k0; k++)
            {
                wingCells.Add(wingInfoArray[k].coord);
            }

            // Increment to the next combination
            int nextIndex = wingInfoArray[k0].index + 1;
            var nextCoord = candidateCells[nextIndex];
            uint nextAccumMask = this[nextCoord.Item1, nextCoord.Item2];
            if (k0 > 0)
            {
                nextAccumMask |= wingInfoArray[k0 - 1].accumMask;
            }
            wingCells.Add(nextCoord);
            wingInfoArray[k0] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, k0 == 0 ? 0 : wingInfoArray[k0 - 1].ungroupedValue));

            for (int k1 = k0 + 1; k1 < wingSize; k1++)
            {
                nextIndex = wingInfoArray[k1 - 1].index + 1;
                nextCoord = candidateCells[nextIndex];
                nextAccumMask |= this[nextCoord.Item1, nextCoord.Item2];
                wingCells.Add(nextCoord);
                wingInfoArray[k1] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, wingInfoArray[k1 - 1].ungroupedValue));
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindAIC(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken) => new AICSolver(this, logicalStepDescs, cancellationToken).FindAIC();

    private LogicResult FindSimpleContradictions(List<LogicalStepDesc> logicalStepDescs, CancellationToken cancellationToken)
    {
        for (int allowedValueCount = 2; allowedValueCount <= MAX_VALUE; allowedValueCount++)
        {
            ContradictionResult? bestContradiction = null;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint cellMask = this[i, j];
                    if (!IsValueSet(cellMask) && ValueCount(cellMask) == allowedValueCount)
                    {
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            uint valueMask = ValueMask(v);
                            if ((cellMask & valueMask) != 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                Solver boardCopy = Clone(willRunNonSinglesLogic: false);
                                boardCopy.isBruteForcing = true;

                                List<LogicalStepDesc> contradictionSteps = logicalStepDescs != null ? new() : null;

                                string violationString = null;
                                if (boardCopy.EvaluateSetValue(CellIndex(i, j), v, ref violationString) == LogicResult.Invalid)
                                {
                                    logicalStepDescs?.Add(new(
                                        desc: $"If {CellName(i, j)} is set to {v} then {violationString} => -{v}{CellName(i, j)}",
                                        sourceCandidates: Enumerable.Empty<int>(),
                                        elimCandidates: CandidateIndex((i, j), v).ToEnumerable(),
                                        subSteps: null
                                    ));
                                    if (!ClearValue(i, j, v))
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    return LogicResult.Changed;
                                }

                                if (boardCopy.ConsolidateBoard(contradictionSteps) == LogicResult.Invalid)
                                {
                                    bool isTrivial = contradictionSteps != null && contradictionSteps.Count == 0;
                                    if (isTrivial)
                                    {
                                        contradictionSteps.Add(new LogicalStepDesc(
                                            desc: "For unknown reasons. This is a bug: please report this!",
                                            sourceCandidates: Enumerable.Empty<int>(),
                                            elimCandidates: Enumerable.Empty<int>()));
                                    }

                                    // Trivial contradictions will always be as "easy" or "easier" than any other contradiction.
                                    if (isTrivial || DisableFindShortestContradiction || !DisableContradictions && contradictionSteps == null)
                                    {
                                        logicalStepDescs?.Add(new(
                                            desc: $"Setting {CellName(i, j)} to {v} causes a contradiction:",
                                            sourceCandidates: Enumerable.Empty<int>(),
                                            elimCandidates: CandidateIndex((i, j), v).ToEnumerable(),
                                            subSteps: contradictionSteps
                                        ));

                                        if (!ClearValue(i, j, v))
                                        {
                                            return LogicResult.Invalid;
                                        }
                                        return LogicResult.Changed;
                                    }
                                    if (!DisableContradictions)
                                    {
                                        int changes = boardCopy.AmountCellsFilled() - this.AmountCellsFilled();
                                        if (!bestContradiction.HasValue || changes < bestContradiction.Value.Changes)
                                        {
                                            bestContradiction = new ContradictionResult(changes, boardCopy, i, j, v, contradictionSteps);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (bestContradiction.HasValue)
            {
                var contradiction = bestContradiction.Value;

                logicalStepDescs?.Add(new(
                    desc: $"Setting {CellName(contradiction.I, contradiction.J)} to {contradiction.V} causes a contradiction:",
                    sourceCandidates: Enumerable.Empty<int>(),
                    elimCandidates: CandidateIndex((contradiction.I, contradiction.J), contradiction.V).ToEnumerable(),
                    subSteps: contradiction.ContraditionSteps
                ));

                if (!ClearValue(contradiction.I, contradiction.J, contradiction.V))
                {
                    return LogicResult.Invalid;
                }
                return LogicResult.Changed;
            }
        }

        return LogicResult.None;
    }

    private record struct ContradictionResult(
        int Changes,
        Solver BoardCopy,
        int I,
        int J,
        int V,
        List<LogicalStepDesc> ContraditionSteps);
}
