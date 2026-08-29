namespace SudokuSolver;

public partial class Solver
{
    /// <summary>
    /// Queues a cell for cell forcing, unless the filter proves it cannot force anything.
    /// </summary>
    /// <remarks>
    /// Sits on the board-write path, so it is one array load and a bit test against the cell's new
    /// candidate mask. The trade it makes is paying that on <em>every</em> write to skip work on
    /// the writes it filters — measured at 55.7% of pops scanning every row and finding nothing.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueCellForcing(int cellIndex, uint oldMask, uint newMask)
    {
        if (pendingCellForcing == null)
        {
            return;
        }

        bool stats = CellForcingStatsEnabled;
        if (stats)
        {
            CellForcingStats.EnqueueOffered++;
        }

        // A cell whose value is now set is skipped by CellForcingForCell outright, so queuing it
        // can only cost a pop.
        if ((newMask & valueSetMask) != 0)
        {
            if (stats)
            {
                CellForcingStats.EnqueueValueSet++;
            }
            return;
        }

        ulong[] canFire = cfCanFire;
        if (canFire != null)
        {
            uint candMask = newMask & ~valueSetMask;
            int maskWord = (int)(candMask >> 6);
            ulong maskBit = 1UL << (int)(candMask & 63);

            ulong[] newlyFires = cfNewlyFires;
            if (newlyFires != null)
            {
                // Only queue when some row fires now that did not fire before this write. A row
                // whose S contains a removed value already covered the wider pre-write mask, so it
                // has been applied and would find its target gone.
                uint removedMask = oldMask & ~newMask & ~valueSetMask;
                int valueBlock = cellIndex * MAX_VALUE * cfCanFireWords + maskWord;
                while (removedMask != 0)
                {
                    int v = MinValue(removedMask);
                    removedMask &= ~ValueMask(v);
                    if ((newlyFires[valueBlock + (v - 1) * cfCanFireWords] & maskBit) != 0)
                    {
                        if (stats)
                        {
                            CellForcingStats.EnqueuePushed++;
                        }
                        pendingCellForcing.Add(cellIndex);
                        return;
                    }
                }
                if (stats)
                {
                    CellForcingStats.EnqueueFiltered++;
                }
                return;
            }

            if ((canFire[cellIndex * cfCanFireWords + maskWord] & maskBit) == 0)
            {
                if (stats)
                {
                    CellForcingStats.EnqueueFiltered++;
                }
                return;
            }
        }

        if (stats)
        {
            CellForcingStats.EnqueuePushed++;
        }
        pendingCellForcing.Add(cellIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueConstraintsForCell(int cellIndex)
    {
        int numWords = _constraintQueued.Length;
        if (numWords == 1)
        {
            _constraintQueued[0] |= cellToConstraintMask[cellIndex];
            return;
        }

        int maskBase = cellIndex * numWords;
        for (int word = 0; word < numWords; word++)
        {
            _constraintQueued[word] |= cellToConstraintMask[maskBase + word];
        }
    }

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
        EnqueueCellForcing(cellIndex, cellMask, newCellMask);

        if ((newCellMask & ~valueSetMask) == 0)
        {
            isInvalid = true;
            return false;
        }

        if (isBruteForcing && cellToConstraintMask != null)
            EnqueueConstraintsForCell(cellIndex);

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

    /// <summary>
    /// Clears every value in <paramref name="clearMask"/> from one cell at once. Equivalent to
    /// calling <see cref="ClearValue(int, int)"/> for each of those values, but pays the per-cell
    /// bookkeeping — the constraint enqueue, the naked-single check and
    /// <see cref="TrackHiddenSingles"/>'s walk over the cell's groups — once instead of once per
    /// value. Used by the grouped weak-link path in <see cref="SetValue"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TrackHiddenSingles"/> is composable here: it iterates the bits of
    /// <c>oldMask &amp; ~newMask</c> and decrements each value's per-group count independently, so
    /// one call with a multi-bit diff lands in the same state as one call per bit.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ClearMaskFromCell(int cellIndex, uint clearMask)
    {
        uint cellMask = board[cellIndex];
        if ((cellMask & clearMask) == 0)
        {
            return true;
        }

        uint newCellMask = cellMask & ~clearMask;
        board[cellIndex] = newCellMask;
        EnqueueCellForcing(cellIndex, cellMask, newCellMask);

        if ((newCellMask & ~valueSetMask) == 0)
        {
            isInvalid = true;
            return false;
        }

        if (isBruteForcing && cellToConstraintMask != null)
            EnqueueConstraintsForCell(cellIndex);

        if (ValueCount(newCellMask) == 1)
        {
            pendingNakedSingles.Add(cellIndex);
        }

        TrackHiddenSingles(cellIndex, cellMask, newCellMask);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ClearCandidate(int candidate)
    {
        (int cellIndex, int v) = CandIndexToCellAndValue(candidate);
        return ClearValue(cellIndex, v);
    }

    /// <summary>
    /// Clears a list of individual candidates, one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never use this while brute forcing, or anywhere else speed matters.</b> It pays the full
    /// per-cell bookkeeping — the constraint enqueue, the naked-single check and
    /// <see cref="TrackHiddenSingles"/>'s walk over the cell's groups — once per <em>candidate</em>
    /// rather than once per cell, and taking <see cref="IEnumerable{T}"/> boxes a
    /// <see cref="List{T}"/>'s struct enumerator on every call.
    /// </para>
    /// <para>
    /// It exists for logical solving, where a step has already reported a specific set of
    /// eliminations and has to apply exactly those. Anything on the per-node path should group its
    /// eliminations by cell and use <c>ClearMaskFromCell</c> instead, which is one masked write per
    /// target cell and early-outs on candidates that are already gone.
    /// </para>
    /// </remarks>
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
        uint prevMask = board[cellIndex];
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
                EnqueueCellForcing(cellIndex, prevMask, valMask);
                pendingNakedSingles.Add(cellIndex);
                TrackHiddenSingles(cellIndex, prevMask, valMask);
                if (isBruteForcing && cellToConstraintMask != null)
                    EnqueueConstraintsForCell(cellIndex);
            }
            return true;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        EnqueueCellForcing(cellIndex, prevMask, valueSetMask | valMask);
        unsetCellsCount--;

        TrackHiddenSingles(cellIndex, prevMask, valMask);

        // Enqueue constraints watching this cell — setting a value changes the cell's
        // state just as much as clearing a candidate, but weak-link ClearValues below
        // only cover the targets, not cellIndex itself.
        if (isBruteForcing && cellToConstraintMask != null)
            EnqueueConstraintsForCell(cellIndex);

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        if (wlGroupedOffsets != null)
        {
            // Grouped path: one masked clear per target *cell*. Most targets are already gone (the
            // measured hit rate is 5-30%), so testing a whole cell with a single mask, and doing the
            // per-cell bookkeeping once rather than once per candidate, is where this pays.
            int[] groupedCells = wlGroupedCells;
            uint[] groupedMasks = wlGroupedMasks;
            int end = wlGroupedOffsets[setCandidateIndex + 1];
            for (int k = wlGroupedOffsets[setCandidateIndex]; k < end; k++)
            {
                if (!ClearMaskFromCell(groupedCells[k], groupedMasks[k]))
                {
                    isInvalid = true;
                    return false;
                }
            }
        }
        else
        {
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
        uint prevMask = board[cellIndex];
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
                EnqueueCellForcing(cellIndex, prevMask, valMask);
                pendingNakedSingles.Add(cellIndex);
                TrackHiddenSingles(cellIndex, prevMask, valMask);
            }
            return LogicResult.Changed;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;
        EnqueueCellForcing(cellIndex, prevMask, valueSetMask | valMask);
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
        EnqueueCellForcing(cellIndex, prevMask, mask);
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

        if (isBruteForcing && cellToConstraintMask != null)
            EnqueueConstraintsForCell(cellIndex);

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
            bool needsHiddenCheck = false;
            uint curDiffMask = diffMask;
            while (curDiffMask != 0)
            {
                int v = MinValue(curDiffMask);
                curDiffMask &= ~ValueMask(v);
                int newCount = --_candidateCountsPerGroupValue[groupIndex * MAX_VALUE + (v - 1)];
                needsHiddenCheck |= newCount <= 1;
            }

            if (!needsHiddenCheck && group.Cells.Count < MAX_VALUE && group.FromConstraint != null
                && !BitsetTest(_checkGroupForHiddens, groupIndex))
            {
                // This group has changed, so its idea of whether it may need a value may also have changed
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if (_candidateCountsPerGroupValue[groupIndex * MAX_VALUE + (v - 1)] <= 1)
                    {
                        needsHiddenCheck = true;
                        break;
                    }
                }
            }

            if (needsHiddenCheck)
            {
                BitsetSet(_checkGroupForHiddens, groupIndex);
            }
        }
    }
}
