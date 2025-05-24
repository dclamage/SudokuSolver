namespace SudokuSolver;

public partial class Solver
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int cellIndex, int v)
    {
        board[cellIndex] &= ~ValueMask(v);
        return (board[cellIndex] & ~valueSetMask) != 0;
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
            board[cellIndex] = valMask;
            return true;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;

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
                return false;
            }
        }

        // Enforce all constraints
        if (constraints.Count > 0)
        {
            var (i, j) = CellIndexToCoord(cellIndex);
            foreach (var constraint in constraints)
            {
                if (!constraint.EnforceConstraint(this, i, j, val))
                {
                    return false;
                }
            }
        }

        isInSetValue = false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, uint mask) => SetMask(CellIndex(i, j), mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int cellIndex, uint mask)
    {
        if ((mask & ~valueSetMask) == 0)
        {
            return false;
        }

        board[cellIndex] = mask;
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

        isInSetValue = true;

        LogicResult result = LogicResult.None;
        uint curMask = board[cellIndex] & ~valueSetMask;
        uint newMask = curMask & mask;
        if (newMask != curMask)
        {
            result = SetMask(cellIndex, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        isInSetValue = false;
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
        isInSetValue = true;

        LogicResult result = LogicResult.None;
        uint curMask = board[cellIndex];
        uint newMask = curMask & ~mask;
        if (newMask != curMask)
        {
            result = SetMask(cellIndex, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        isInSetValue = false;
        return result;
    }
}
