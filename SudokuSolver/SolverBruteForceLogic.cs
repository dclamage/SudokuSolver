using System.Threading.Channels;

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
            foreach (var group in smallGroupsBySize)
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
            var (bCellIndex, bVal) = FindBilocalValue();
            if (bVal > 0)
            {
                return (bCellIndex, bVal);
            }
        }

        return (bestCellIndex, 0);
    }

    private (int, int) FindBilocalValue()
    {
        foreach (var group in Groups)
        {
            var groupCells = group.Cells;
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
    private LogicResult StepBruteForceLogic()
    {
        LogicResult curResult = FindNakedSingles(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        curResult = FindHiddenSingle(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        curResult = FastFindNakedTuplesAndPointing();
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        curResult = FindDirectCellForcing(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        foreach (var constraint in constraints)
        {
            curResult = constraint.StepLogic(this, (List<LogicalStepDesc>)null, true);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }
        return LogicResult.None;
    }

    private LogicResult BruteForcePropagate()
    {
        bool changed = false;
        while (true)
        {
            LogicResult curResult = StepBruteForceLogic();
            if (curResult == LogicResult.Invalid || curResult == LogicResult.PuzzleComplete)
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
    private LogicResult DiscoverWeakLinks()
    {
        LogicResult result = LogicResult.None;

        LogicResult innerResult;
        do
        {
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
                    LogicResult curResult = solver.BruteForcePropagate();
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
                                AddWeakLink(setCandidate, elimCandidate);
                            }
                        }
                    }
                }
            }
        } while (innerResult == LogicResult.Changed);

        return result;
    }

    private LogicResult FastFindNakedTuplesAndPointing()
    {
        // --- 1. Naked Pairs ---
        // Reusable list for cells eligible for tuple formation within a group.
        // Capacity can be MAX_VALUE as it's per group.
        List<int> tempUnsetCells = new List<int>(MAX_VALUE);

        foreach (var group in Groups)
        {
            if (group.Cells.Count < 2)
            {
                continue;
            }

            tempUnsetCells.Clear();
            foreach (int cellIndex in group.Cells)
            {
                uint cellMask = board[cellIndex];
                if (!IsValueSet(cellMask) && ValueCount(cellMask) <= 2)
                {
                    tempUnsetCells.Add(cellIndex);
                }
            }

            if (tempUnsetCells.Count < 2)
            {
                // Not enough eligible cells found
                continue;
            }

            for (int i = 0; i < tempUnsetCells.Count; i++)
            {
                for (int j = i + 1; j < tempUnsetCells.Count; j++)
                {
                    int cell1_idx = tempUnsetCells[i];
                    int cell2_idx = tempUnsetCells[j];

                    // Combine candidates from the two cells
                    uint tupleMask = board[cell1_idx] | board[cell2_idx];

                    if (ValueCount(tupleMask) == 2) // Found a Naked Pair
                    {
                        List<int> elims = CalcElims(tupleMask, [cell1_idx, cell2_idx]);
                        if (elims.Count > 0)
                        {
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

        // --- 2. Naked Triples ---
        // tempUnsetCells is reused
        foreach (var group in Groups)
        {
            if (group.Cells.Count < 3)
            {
                // Not enough cells for a triple
                continue;
            }

            tempUnsetCells.Clear();
            foreach (int cellIndex in group.Cells)
            {
                uint cellMask = board[cellIndex];
                if (!IsValueSet(cellMask) && ValueCount(cellMask) <= 3)
                {
                    tempUnsetCells.Add(cellIndex);
                }
            }

            if (tempUnsetCells.Count < 3)
            {
                // Not enough eligible cells
                continue;
            }

            for (int i = 0; i < tempUnsetCells.Count; i++)
            {
                for (int j = i + 1; j < tempUnsetCells.Count; j++)
                {
                    for (int k = j + 1; k < tempUnsetCells.Count; k++)
                    {
                        int cell1_idx = tempUnsetCells[i];
                        int cell2_idx = tempUnsetCells[j];
                        int cell3_idx = tempUnsetCells[k];

                        uint tupleMask = board[cell1_idx] | board[cell2_idx] | board[cell3_idx];

                        if (ValueCount(tupleMask) == 3) // Found a Naked Triple
                        {
                            List<int> elims = CalcElims(tupleMask, [cell1_idx, cell2_idx, cell3_idx]);
                            if (elims.Count > 0)
                            {
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

        // --- 3 & 4. Pointing (Locked Candidates Type 1 / Claiming) ---
        // For 2 or 3 candidates restricted to cells within a group.
        foreach (var group in maxValueGroups)
        {
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
                List<int> pointingCandidates = new(group.Cells.Count);
                foreach (int cellIndex in group.Cells)
                {
                    uint currentCellMask = board[cellIndex];
                    if ((currentCellMask & valueMask) != 0)
                    {
                        pointingCandidates.Add(CandidateIndex(cellIndex, value));
                    }
                }

                if (pointingCandidates.Count == 2 || pointingCandidates.Count == 3)
                {
                    List<int> elims = CalcElims(pointingCandidates);
                    if (elims.Count > 0)
                    {
                        if (!ClearCandidates(elims))
                        {
                            return LogicResult.Invalid;
                        }
                        return LogicResult.Changed;
                    }
                }
            }
        }

        // No changes made by these techniques
        return LogicResult.None;
    }
}