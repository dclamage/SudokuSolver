namespace SudokuSolver;

public partial class Solver
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int cellIndex, int v)
    {
        uint cellMask = board[cellIndex];
        uint valueMask = ValueMask(v);
        if ((cellMask & valueMask) == 0)
        {
            return true;
        }

        cellMask &= ~valueMask;
        board[cellIndex] = cellMask;

        if ((cellMask & ~valueSetMask) == 0)
        {
            isInvalid = true;
            return false;
        }

        if (ValueCount(cellMask) == 1)
        {
            pendingNakedSingles.Add(cellIndex);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int i, int j, int v) => ClearValue(CellIndex(i, j), v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ClearCandidate(int candidate)
    {
        var (cellIndex, v) = CandIndexToCellAndValue(candidate);
        return ClearValue(cellIndex, v);
    }

    internal bool ClearCandidates(IEnumerable<int> candidates)
    {
        foreach (int c in candidates)
        {
            if (!ClearCandidate(c))
            {
                return false;
            }
        }
        return true;
    }

    public bool SetValue(int i, int j, int val) => SetValue(CellIndex(i, j), val);

    public bool SetValue(int cellIndex, int val)
    {
        uint valMask = ValueMask(val);
        if ((board[cellIndex] & valMask) == 0)
        {
            return false;
        }

        // Check if already set
        if ((board[cellIndex] & valueSetMask) != 0)
        {
            return true;
        }

        if (isInSetValue)
        {
            if (board[cellIndex] != valMask)
            {
                board[cellIndex] = valMask;
                pendingNakedSingles.Add(cellIndex);
            }
            return true;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        unsetCellsCount--;

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        var curWeakLinks = weakLinks[setCandidateIndex];
        int curWeakLinksCount = curWeakLinks.Count;
        for (int curWeakLinkIndex = 0; curWeakLinkIndex < curWeakLinksCount; curWeakLinkIndex++)
        {
            int elimCandIndex = curWeakLinks[curWeakLinkIndex];
            var (cellIndex1, v1) = CandIndexToCellAndValue(elimCandIndex);
            if (!ClearValue(cellIndex1, v1))
            {
                isInvalid = true;
                return false;
            }
        }

        // Enforce all constraints
        if (enforceConstraints.Count > 0)
        {
            var (i, j) = CellIndexToCoord(cellIndex);
            foreach (var constraint in enforceConstraints)
            {
                if (!constraint.EnforceConstraint(this, i, j, val))
                {
                    isInvalid = true;
                    return false;
                }
            }
        }

        isInSetValue = false;

        return true;
    }

    public LogicResult EvaluateSetValue(int cellIndex, int val, ref string violationString)
    {
        uint valMask = ValueMask(val);
        if ((board[cellIndex] & valMask) == 0)
        {
            return LogicResult.None;
        }

        // Check if already set
        if ((board[cellIndex] & valueSetMask) != 0)
        {
            return LogicResult.None;
        }

        if (isInSetValue)
        {
            if (board[cellIndex] != valMask)
            {
                board[cellIndex] = valMask;
                pendingNakedSingles.Add(cellIndex);
            }
            return LogicResult.Changed;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        unsetCellsCount--;

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        foreach (int elimCandIndex in weakLinks[setCandidateIndex])
        {
            var (i1, j1, v1) = CandIndexToCoord(elimCandIndex);
            if (!ClearValue(i1, j1, v1))
            {
                violationString = $"{CellName(i1, j1)} has no value";
                isInvalid = true;
                return LogicResult.Invalid;
            }
        }

        // Enforce all constraints
        var (i, j) = CellIndexToCoord(cellIndex);
        foreach (var constraint in constraints)
        {
            if (!constraint.EnforceConstraint(this, i, j, val))
            {
                violationString = $"{constraint.SpecificName} is violated";
                isInvalid = true;
                return LogicResult.Invalid;
            }
        }

        isInSetValue = false;

        return LogicResult.Changed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, uint mask) => SetMask(CellIndex(i, j), mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetMask(int cellIndex, uint mask)
    {
        board[cellIndex] = mask;
        if ((mask & ~valueSetMask) == 0)
        {
            isInvalid = true;
            return false;
        }

        if (ValueCount(mask) == 1)
        {
            pendingNakedSingles.Add(cellIndex);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, params int[] values) => SetMask(CellIndex(i,j), values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int cellIndex, params int[] values)
    {
        uint mask = 0;
        for (int i = 0; i <  values.Length; i++)
        {
            mask |= ValueMask(values[i]);
        }
        return SetMask(cellIndex, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, IEnumerable<int> values) => SetMask(CellIndex(i, j), [.. values]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int cellIndex, IEnumerable<int> values) => SetMask(cellIndex, [.. values]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult KeepMask(int i, int j, uint mask) => KeepMask(CellIndex(i, j), mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult KeepMask(int cellIndex, uint mask)
    {
        mask &= ALL_VALUES_MASK;
        if (mask == ALL_VALUES_MASK)
        {
            return LogicResult.None;
        }

        LogicResult result = LogicResult.None;
        uint curMask = board[cellIndex] & ~valueSetMask;
        uint newMask = curMask & mask;
        if (newMask != curMask)
        {
            result = SetMask(cellIndex, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult ClearMask(int i, int j, uint mask) => ClearMask(CellIndex(i, j), mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult ClearMask(int cellIndex, uint mask)
    {
        mask &= ALL_VALUES_MASK;
        if (mask == 0)
        {
            return LogicResult.None;
        }

        LogicResult result = LogicResult.None;
        uint curMask = board[cellIndex];
        uint newMask = curMask & ~mask;
        if (newMask != curMask)
        {
            result = SetMask(cellIndex, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        return result;
    }
}
