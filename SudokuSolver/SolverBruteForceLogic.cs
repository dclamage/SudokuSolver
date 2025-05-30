namespace SudokuSolver;

public partial class Solver
{
    private (int, int) GetLeastCandidateCell(bool allowBilocals = true)
    {
        int bestCellIndex = -1;
        int numCandidates = MAX_VALUE + 1;
        if (smallGroupsBySize != null)
        {
            int lastValidGroupSize = MAX_VALUE + 1;
            foreach (SudokuGroup group in smallGroupsBySize)
            {
                int groupSize = group.Cells.Count;
                if (lastValidGroupSize < groupSize)
                {
                    break;
                }

                foreach (int cellIndex in group.Cells)
                {
                    uint cellMask = board[cellIndex];
                    if (!IsValueSet(cellMask))
                    {
                        int curNumCandidates = ValueCount(cellMask);
                        if (curNumCandidates == 2)
                        {
                            return (cellIndex, 0);
                        }
                        if (curNumCandidates < numCandidates)
                        {
                            lastValidGroupSize = groupSize;
                            numCandidates = curNumCandidates;
                            bestCellIndex = cellIndex;
                        }
                    }
                }
            }
            if (bestCellIndex != -1)
            {
                return (bestCellIndex, 0);
            }
        }

        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint cellMask = board[cellIndex];
            if (!IsValueSet(cellMask))
            {
                int curNumCandidates = ValueCount(cellMask);
                if (curNumCandidates == 2)
                {
                    return (cellIndex, 0);
                }
                if (curNumCandidates < numCandidates)
                {
                    numCandidates = curNumCandidates;
                    bestCellIndex = cellIndex;
                }
            }
        }

        if (numCandidates > 3 && allowBilocals)
        {
            (int bCellIndex, int bVal) = FindBilocalValue();
            if (bVal > 0)
            {
                return (bCellIndex, bVal);
            }
        }

        return (bestCellIndex, 0);
    }

    private (int, int) FindBilocalValue()
    {
        foreach (SudokuGroup group in Groups)
        {
            List<int> groupCells = group.Cells;
            int numCells = group.Cells.Count;
            if (numCells != MAX_VALUE)
            {
                continue;
            }

            uint atLeastOnce = 0;
            uint atLeastTwice = 0;
            uint moreThanTwice = 0;
            for (int groupIndex = 0; groupIndex < numCells; groupIndex++)
            {
                int cellIndex = groupCells[groupIndex];
                uint mask = board[cellIndex];
                moreThanTwice |= atLeastTwice & mask;
                atLeastTwice |= atLeastOnce & mask;
                atLeastOnce |= mask;
            }

            uint exactlyTwice = atLeastTwice & ~moreThanTwice & ~valueSetMask;
            if (exactlyTwice != 0)
            {
                int val = MinValue(exactlyTwice);
                uint valMask = ValueMask(val);
                foreach (int cellIndex in group.Cells)
                {
                    if ((board[cellIndex] & valMask) != 0)
                    {
                        return (cellIndex, val);
                    }
                }
            }
        }
        return (-1, 0);
    }

    /// <summary>
    /// Run some hand-selected logic until nothing changes
    /// </summary>
    /// <returns></returns>
    private LogicResult StepBruteForceLogic(bool doAdvancedStrategies, CancellationToken cancellationToken)
    {
        LogicResult curResult = FindNakedSingles(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        curResult = FindHiddenSingle(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (doAdvancedStrategies)
        {
            curResult = FastAdvancedStrategies(cancellationToken);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }

        foreach (Constraint constraint in constraints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            curResult = constraint.StepLogic(this, (List<LogicalStepDesc>)null, true);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }

        return LogicResult.None;
    }

    private LogicResult BruteForcePropagate(bool doAdvancedStrategies, CancellationToken cancellationToken)
    {
        bool changed = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogicResult curResult = StepBruteForceLogic(doAdvancedStrategies, cancellationToken);
            if (curResult is LogicResult.Invalid or LogicResult.PuzzleComplete)
            {
                return curResult;
            }
            if (curResult == LogicResult.Changed)
            {
                changed = true;
            }
            else
            {
                break;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Discovers non-trivial weak links by setting candidates and seeing what happens.
    /// Only call for brute force methods on deeply cloned grids, otherwise there will
    /// be a lot of "magical" eliminations during logical stepping.
    /// </summary>
    /// <returns></returns>
    private LogicResult DiscoverWeakLinks(CancellationToken cancellationToken)
    {
        // Run logic on the base solver first
        LogicResult result = BruteForcePropagate(true, cancellationToken);
        if (result == LogicResult.PuzzleComplete || result == LogicResult.Invalid)
        {
            return result;
        }

        LogicResult innerResult;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            innerResult = LogicResult.None;

            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint cellMask = board[cellIndex];
                if (IsValueSet(cellMask))
                {
                    continue;
                }

                while (cellMask != 0)
                {
                    int value = MinValue(cellMask);
                    cellMask &= ~ValueMask(value);

                    Solver solver = Clone(willRunNonSinglesLogic: false);
                    solver.isBruteForcing = true;
                    if (!solver.SetValue(cellIndex, value))
                    {
                        // Trivially invalid, we can eliminate it from the host solver
                        if (!ClearValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                    }

                    // Run some hand-selected logic until nothing changes
                    LogicResult curResult = solver.BruteForcePropagate(true, cancellationToken);
                    if (curResult == LogicResult.None)
                    {
                        continue;
                    }

                    if (curResult == LogicResult.Invalid)
                    {
                        // Non-trivially invalid, we can eliminate it from the host solver
                        if (!ClearValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                        result = LogicResult.Changed;
                        innerResult = LogicResult.Changed;
                    }
                    else
                    {
                        int setCandidate = CandidateIndex(cellIndex, value);

                        // Find new eliminations and form the proper weak links
                        for (int curCellIndex = 0; curCellIndex < NUM_CELLS; curCellIndex++)
                        {
                            if (curCellIndex == cellIndex)
                            {
                                continue;
                            }

                            uint oldMask = board[curCellIndex];
                            if (IsValueSet(oldMask))
                            {
                                continue;
                            }

                            uint newMask = solver.board[curCellIndex] & ~valueSetMask;
                            uint elimMask = oldMask & ~newMask;
                            while (elimMask != 0)
                            {
                                int elimValue = MinValue(elimMask);
                                elimMask &= ~ValueMask(elimValue);

                                int elimCandidate = CandidateIndex(curCellIndex, elimValue);
                                _ = AddWeakLink(setCandidate, elimCandidate);
                            }
                        }
                    }
                }
            }
        } while (innerResult == LogicResult.Changed);

        return result;
    }

    private LogicResult FastFindPairs(CancellationToken cancellationToken)
    {
        // Gather a list of all bivalue cells
        List<(int cellIndex, uint mask)> bivalueCells = [];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint mask = board[cellIndex];
            if (ValueCount(mask) == 2)
            {
                bivalueCells.Add((cellIndex, mask));
            }
        }
        bivalueCells.Sort((a, b) =>
        {
            int compareMask = a.mask.CompareTo(b.mask);
            return compareMask != 0 ? compareMask : a.cellIndex.CompareTo(b.cellIndex);
        });

        for (int i0 = 0; i0 < bivalueCells.Count; i0++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int cellIndex0, uint mask0) = bivalueCells[i0];
            List<int> weakLinks0 = weakLinks[cellIndex0];
            int valueA = MinValue(mask0);
            int valueB = MaxValue(mask0);
            int candidate0a = CandidateIndex(cellIndex0, valueA);
            int candidate0b = CandidateIndex(cellIndex0, valueB);
            for (int i1 = i0 + 1; i1 < bivalueCells.Count; i1++)
            {
                (int cellIndex1, uint mask1) = bivalueCells[i1];
                if (mask0 != mask1)
                {
                    break;
                }

                int candidate1a = CandidateIndex(cellIndex1, valueA);
                int candidate1b = CandidateIndex(cellIndex1, valueB);
                if (!IsWeakLink(candidate0a, candidate1a) || !IsWeakLink(candidate0b, candidate1b))
                {
                    continue;
                }

                List<int> elims = CalcElims(mask0, [cellIndex0, cellIndex1]);
                if (elims.Count > 0)
                {
                    return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                }
            }
        }

        return LogicResult.None;
    }

    // Helper struct for FastFindTriples
    private readonly record struct PotentialTripleParticipant(int CellIndex, uint ActualCellMask, uint TargetTripleMask);

    private LogicResult FastFindTriples(CancellationToken cancellationToken)
    {
        List<PotentialTripleParticipant> participants = [];

        // Populate the list of potential triple participants
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint actualMask = board[cellIndex];
            if (IsValueSet(actualMask))
            {
                continue;
            }

            int numActualCandidates = ValueCount(actualMask);

            if (numActualCandidates == 2) // Cell has 2 candidates, e.g., {A,B}
            {
                for (int valX = 1; valX <= MAX_VALUE; valX++)
                {
                    uint maskValX = ValueMask(valX);
                    if ((actualMask & maskValX) != 0)
                    {
                        continue;
                    }
                    uint targetTripleMask = actualMask | maskValX;
                    participants.Add(new PotentialTripleParticipant(cellIndex, actualMask, targetTripleMask));
                }
            }
            else if (numActualCandidates == 3) // Cell has 3 candidates, e.g., {A,B,C}
            {
                participants.Add(new PotentialTripleParticipant(cellIndex, actualMask, actualMask));
            }
        }

        if (participants.Count < 3)
        {
            return LogicResult.None;
        }

        participants.Sort((a, b) =>
        {
            int compareMask = a.TargetTripleMask.CompareTo(b.TargetTripleMask);
            return compareMask != 0 ? compareMask : a.CellIndex.CompareTo(b.CellIndex);
        });

        for (int i = 0; i < participants.Count - 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PotentialTripleParticipant p_i = participants[i];
            int c0 = p_i.CellIndex;

            for (int j = i + 1; j < participants.Count - 1; j++)
            {
                PotentialTripleParticipant p_j = participants[j];
                int c1 = p_j.CellIndex;

                if (p_j.TargetTripleMask != p_i.TargetTripleMask)
                {
                    break;
                }

                if (c1 == c0)
                {
                    continue;
                }

                // Extract values from the target triple mask
                uint tempMask = p_i.TargetTripleMask;
                int vA = MinValue(tempMask);
                tempMask &= ~ValueMask(vA);
                int vB = MinValue(tempMask);
                tempMask &= ~ValueMask(vB);
                int vC = MinValue(tempMask);

                // Check non-repeat property between p_i and p_j for all three values
                bool p_i_j_nonrepeat =
                    (!HasValue(p_i.ActualCellMask, vA) || !HasValue(p_j.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c0, vA), CandidateIndex(c1, vA))) &&
                    (!HasValue(p_i.ActualCellMask, vB) || !HasValue(p_j.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c0, vB), CandidateIndex(c1, vB))) &&
                    (!HasValue(p_i.ActualCellMask, vC) || !HasValue(p_j.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c0, vC), CandidateIndex(c1, vC)));

                if (!p_i_j_nonrepeat)
                {
                    continue; // p_i and p_j can repeat digits, so this cannot form a triple
                }

                for (int k = j + 1; k < participants.Count; k++)
                {
                    PotentialTripleParticipant p_k = participants[k];
                    int c2 = p_k.CellIndex;

                    if (p_k.TargetTripleMask != p_i.TargetTripleMask)
                    {
                        break;
                    }

                    if (c2 == c0 || c2 == c1)
                    {
                        continue;
                    }

                    uint combinedActualMask = p_i.ActualCellMask | p_j.ActualCellMask | p_k.ActualCellMask;

                    if (combinedActualMask == p_i.TargetTripleMask)
                    {
                        // p_i and p_j already confirmed to not repeat digits.
                        // Now check p_k's non-repeat with p_i and p_j.
                        bool p_k_links_nonrepeat =
                            (!HasValue(p_i.ActualCellMask, vA) || !HasValue(p_k.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c0, vA), CandidateIndex(c2, vA))) &&
                            (!HasValue(p_j.ActualCellMask, vA) || !HasValue(p_k.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c1, vA), CandidateIndex(c2, vA))) &&
                            (!HasValue(p_i.ActualCellMask, vB) || !HasValue(p_k.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c0, vB), CandidateIndex(c2, vB))) &&
                            (!HasValue(p_j.ActualCellMask, vB) || !HasValue(p_k.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c1, vB), CandidateIndex(c2, vB))) &&
                            (!HasValue(p_i.ActualCellMask, vC) || !HasValue(p_k.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c0, vC), CandidateIndex(c2, vC))) &&
                            (!HasValue(p_j.ActualCellMask, vC) || !HasValue(p_k.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c1, vC), CandidateIndex(c2, vC)));

                        if (!p_k_links_nonrepeat)
                        {
                            continue;
                        }

                        List<int> tripleCellIndices = [c0, c1, c2];
                        List<int> elims = CalcElims(p_i.TargetTripleMask, tripleCellIndices);
                        if (elims.Count > 0)
                        {
                            return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                        }
                    }
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FastFindPointing(CancellationToken cancellationToken)
    {
        List<int> pointingCandidates = new(4);
        List<int> elims = [];
        foreach (SudokuGroup group in maxValueGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint groupSpecific_SetValuesMask = 0;    // Mask of values already SET within this group
            uint groupSpecific_CandidatePoolMask = 0; // Mask of all candidates in UNSET cells of this group

            foreach (int cellIndex in group.Cells)
            {
                uint currentCellMask = board[cellIndex];
                if (IsValueSet(currentCellMask))
                {
                    groupSpecific_SetValuesMask |= currentCellMask;
                }
                else
                {
                    groupSpecific_CandidatePoolMask |= currentCellMask;
                }
            }

            // Candidates that are in the pool AND NOT already set in the group.
            // These are the candidates we might find pointing logic for.
            uint actualCandidatesForPointing = groupSpecific_CandidatePoolMask & ~groupSpecific_SetValuesMask;

            if (actualCandidatesForPointing == 0) // No candidates left for pointing in this group
            {
                continue;
            }

            while (actualCandidatesForPointing != 0)
            {
                int value = MinValue(actualCandidatesForPointing);
                uint valueMask = ValueMask(value);
                actualCandidatesForPointing &= ~valueMask;

                // Find all cells in 'group' where 'v' is a candidate.
                pointingCandidates.Clear();
                foreach (int cellIndex in group.Cells)
                {
                    uint currentCellMask = board[cellIndex];
                    if ((currentCellMask & valueMask) != 0)
                    {
                        pointingCandidates.Add(CandidateIndex(cellIndex, value));
                        if (pointingCandidates.Count > 3)
                        {
                            break;
                        }
                    }
                }

                if (pointingCandidates.Count is 2 or 3)
                {
                    CalcElims(elims, pointingCandidates);
                    if (elims.Count > 0)
                    {
                        return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                    }
                }
            }
        }

        return LogicResult.None;
    }

    private LogicResult FastFindCellForcing(CancellationToken cancellationToken)
    {
        List<int> elims = [];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint mask = board[cellIndex];
            if (IsValueSet(mask) || ValueCount(mask) > 3)
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
                if (!ClearCandidates(elims))
                {
                    return LogicResult.Invalid;
                }
                return LogicResult.Changed;
            }
        }
        return LogicResult.None;
    }

    private LogicResult FastAdvancedStrategies(CancellationToken cancellationToken)
    {
        LogicResult result;

        result = FastFindPairs(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindPointing(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindCellForcing(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindTriples(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        return LogicResult.None;
    }
}
