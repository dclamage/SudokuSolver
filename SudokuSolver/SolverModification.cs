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

        uint newCellMask = cellMask & ~valueMask;
        board[cellIndex] = newCellMask;

        if ((newCellMask & ~valueSetMask) == 0)
        {
            isInvalid = true;
            return false;
        }

        if (ValueCount(newCellMask) == 1)
        {
            pendingNakedSingles.Add(cellIndex);
        }

        TrackHiddenSingles(cellIndex, cellMask, newCellMask);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int i, int j, int v)
    {
        return ClearValue(CellIndex(i, j), v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ClearCandidate(int candidate)
    {
        (int cellIndex, int v) = CandIndexToCellAndValue(candidate);
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

    public bool SetValue(int i, int j, int val)
    {
        return SetValue(CellIndex(i, j), val);
    }

    public bool SetValue(int cellIndex, int val)
    {
;       uint prevMask = board[cellIndex];
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
            if (prevMask != valMask)
            {
                board[cellIndex] = valMask;
                pendingNakedSingles.Add(cellIndex);
                TrackHiddenSingles(cellIndex, prevMask, valMask);
            }
            return true;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        unsetCellsCount--;

        TrackHiddenSingles(cellIndex, prevMask, valMask);

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        List<int> curWeakLinks = weakLinks[setCandidateIndex];
        int curWeakLinksCount = curWeakLinks.Count;
        for (int curWeakLinkIndex = 0; curWeakLinkIndex < curWeakLinksCount; curWeakLinkIndex++)
        {
            int elimCandIndex = curWeakLinks[curWeakLinkIndex];
            (int cellIndex1, int v1) = CandIndexToCellAndValue(elimCandIndex);
            if (!ClearValue(cellIndex1, v1))
            {
                isInvalid = true;
                return false;
            }
        }

        // Enforce all constraints
        if (enforceConstraints.Count > 0)
        {
            (int i, int j) = CellIndexToCoord(cellIndex);
            foreach (Constraint constraint in enforceConstraints)
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
;       uint prevMask = board[cellIndex];
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
            if (prevMask != valMask)
            {
                board[cellIndex] = valMask;
                pendingNakedSingles.Add(cellIndex);
                TrackHiddenSingles(cellIndex, prevMask, valMask);
            }
            return LogicResult.Changed;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        unsetCellsCount--;

        TrackHiddenSingles(cellIndex, prevMask, valMask);

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        foreach (int elimCandIndex in weakLinks[setCandidateIndex])
        {
            (int i1, int j1, int v1) = CandIndexToCoord(elimCandIndex);
            if (!ClearValue(i1, j1, v1))
            {
                violationString = $"{CellName(i1, j1)} has no value";
                isInvalid = true;
                return LogicResult.Invalid;
            }
        }

        // Enforce all constraints
        (int i, int j) = CellIndexToCoord(cellIndex);
        foreach (Constraint constraint in constraints)
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
    public bool SetMask(int i, int j, uint mask)
    {
        return SetMask(CellIndex(i, j), mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetMask(int cellIndex, uint mask)
    {
        uint prevMask = board[cellIndex];
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

        TrackHiddenSingles(cellIndex, prevMask, mask);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, params int[] values)
    {
        return SetMask(CellIndex(i, j), values);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int cellIndex, params int[] values)
    {
        uint mask = 0;
        for (int i = 0; i < values.Length; i++)
        {
            mask |= ValueMask(values[i]);
        }
        return SetMask(cellIndex, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, IEnumerable<int> values)
    {
        return SetMask(CellIndex(i, j), [.. values]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int cellIndex, IEnumerable<int> values)
    {
        return SetMask(cellIndex, [.. values]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult KeepMask(int i, int j, uint mask)
    {
        return KeepMask(CellIndex(i, j), mask);
    }

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
    public LogicResult ClearMask(int i, int j, uint mask)
    {
        return ClearMask(CellIndex(i, j), mask);
    }

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

    // Hidden single tracking: update candidate counts
    private void TrackHiddenSingles(int cellIndex, uint oldMask, uint newMask)
    {
        if (_candidateCountsPerGroupValue == null)
        {
            return;
        }

        uint diffMask = oldMask & ~newMask & ~valueSetMask;
        if (diffMask == 0)
        {
            return;
        }

        foreach (SudokuGroup group in CellToGroupsLookup[cellIndex])
        {
            int groupIndex = group.Index;
            uint curDiffMask = diffMask;
            while (curDiffMask != 0)
            {
                int v = MinValue(curDiffMask);
                curDiffMask &= ~ValueMask(v);
                int newCount = --_candidateCountsPerGroupValue[groupIndex * MAX_VALUE + (v - 1)];
                if (newCount <= 1)
                {
                    _checkGroupForHiddens[groupIndex] = true;
                }
            }

            if (!_checkGroupForHiddens[groupIndex] && group.Cells.Count < MAX_VALUE && group.FromConstraint != null)
            {
                // This group has changed, so its idea of whether it may need a value may also have changed
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if (_candidateCountsPerGroupValue[groupIndex * MAX_VALUE + (v - 1)] <= 1)
                    {
                        _checkGroupForHiddens[groupIndex] = true;
                        break;
                    }
                }
            }
        }
    }
}
