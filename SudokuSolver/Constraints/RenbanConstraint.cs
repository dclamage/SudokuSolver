namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Renban", ConsoleName = "renban")]
public class RenbanConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly List<int> cellIndices;
    private List<uint> rangeMasks; // Assuming this is mutable during InitCandidates as per prior discussion

    public RenbanConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        List<List<(int, int)>> cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Renban constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellIndices = [.. cells.Select(sudokuSolver.CellIndex)];

        if (cellIndices.Count > MAX_VALUE) // MAX_VALUE is from base Constraint class
        {
            throw new ArgumentException($"Renban can only contain up to {MAX_VALUE} cells, but {cellIndices.Count} were provided.");
        }

        List<uint> initialRangeMasks = [];
        int numCells = cells.Count;
        for (int s = 1; s <= MAX_VALUE - numCells + 1; s++)
        {
            initialRangeMasks.Add(CreateRangeMask(s, s + numCells - 1));
        }
        rangeMasks = initialRangeMasks;
    }

    public override string SpecificName => $"Renban from {CellName(cells[0])} - {CellName(cells[^1])}";

    // Digits cannot repeat on a renban line
    public override List<(int, int)> Group => cells;

    // Helper to create a mask for a range of values, now using SolverUtility.ValueMask
    private uint CreateRangeMask(int startVal, int endVal)
    {
        uint mask = 0;
        for (int v = startVal; v <= endVal; v++)
        {
            if (v >= 1 && v <= MAX_VALUE) // Ensure v is within valid Sudoku range
            {
                mask |= ValueMask(v); // From SolverUtility
            }
        }
        return mask;
    }

    // Helper to get bitmasks for the cells on the renban
    private (uint allCellsCandidateUnion, uint allSetValuesUnion) GetCellMasks(BoardView board)
    {
        uint allCellsCandidateUnion = 0;
        uint allSetValuesUnion = 0;
        foreach (int cellIndex in cellIndices)
        {
            uint cellMask = board[cellIndex];
            if (IsValueSet(cellMask))
            {
                uint valueBit = cellMask & ~valueSetMask;
                allSetValuesUnion |= valueBit;
                allCellsCandidateUnion |= valueBit; // Set value is part of the available "candidates"
            }
            else
            {
                allCellsCandidateUnion |= cellMask; // Add candidates from unset cells
            }
        }
        return (allCellsCandidateUnion, allSetValuesUnion);
    }

    private bool IsRangeMaskValid(uint rangeSequenceMask, uint allCellsCandidateUnion, uint allSetValuesUnion, BoardView board)
    {
        // Overall Check 1: All set values on the line must be part of the current renban sequence.
        if ((allSetValuesUnion & ~rangeSequenceMask) != 0)
        {
            return false;
        }

        // Overall Check 2: The renban sequence must be entirely coverable by the candidates/set values present on the line.
        if ((rangeSequenceMask & ~allCellsCandidateUnion) != 0)
        {
            return false;
        }

        // Per-Cell Check: Each individual cell must be able to accommodate a value from this sequence.
        foreach (int cellIndex in cellIndices)
        {
            uint cellBoardMask = board[cellIndex];
            if (!IsValueSet(cellBoardMask))
            {
                if ((cellBoardMask & rangeSequenceMask) == 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        int numCells = cellIndices.Count;
        if (numCells <= 2)
        {
            return LogicResult.None;
        }

        BoardView board = sudokuSolver.Board;
        (uint allCellsCandidateUnionFromBoard, uint allSetValuesUnionFromBoard) = GetCellMasks(board);

        List<uint> currentlyPossibleRangeMasks = [];
        uint unionOfCurrentlyPossibleRangeMasks = 0;

        foreach (uint theoreticalRangeMask in rangeMasks)
        {
            if (IsRangeMaskValid(theoreticalRangeMask, allCellsCandidateUnionFromBoard, allSetValuesUnionFromBoard, board))
            {
                currentlyPossibleRangeMasks.Add(theoreticalRangeMask);
                unionOfCurrentlyPossibleRangeMasks |= theoreticalRangeMask;
            }
        }

        if (currentlyPossibleRangeMasks.Count == 0 && allCellsCandidateUnionFromBoard != 0)
        {
            for (int i = 0; i < numCells; i++)
            {
                int cellIndex = cellIndices[i];
                if ((board[cellIndex] & ~valueSetMask) != 0)
                {
                    LogicResult emptyResult = sudokuSolver.KeepMask(cellIndex, 0);
                    if (emptyResult == LogicResult.Invalid)
                    {
                        return LogicResult.Invalid;
                    }
                }
            }
            return LogicResult.Invalid;
        }

        bool boardChanged = false;
        for (int i = 0; i < numCells; i++)
        {
            int cellIndex = cellIndices[i];
            uint currentCellCandidates = board[cellIndex] & ~valueSetMask;
            uint newCellCandidates = currentCellCandidates & unionOfCurrentlyPossibleRangeMasks;

            if (newCellCandidates != currentCellCandidates)
            {
                LogicResult keepResult = sudokuSolver.KeepMask(cellIndex, newCellCandidates);
                if (keepResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (keepResult == LogicResult.Changed)
                {
                    boardChanged = true;
                }
            }
        }

        (uint finalAllCellsCandidateUnion, uint finalAllSetValuesUnion) = GetCellMasks(board);

        List<uint> newFilteredRangeMasks = [];
        foreach (uint theoreticalRangeMask in rangeMasks)
        {
            if (IsRangeMaskValid(theoreticalRangeMask, finalAllCellsCandidateUnion, finalAllSetValuesUnion, board))
            {
                newFilteredRangeMasks.Add(theoreticalRangeMask);
            }
        }

        bool constraintStateChanged = false;
        if (newFilteredRangeMasks.Count != rangeMasks.Count)
        {
            rangeMasks = newFilteredRangeMasks;
            constraintStateChanged = true;
        }
        else
        {
            for (int i = 0; i < newFilteredRangeMasks.Count; ++i)
            {
                if (newFilteredRangeMasks[i] != rangeMasks[i])
                {
                    rangeMasks = newFilteredRangeMasks;
                    constraintStateChanged = true;
                    break;
                }
            }
        }

        return boardChanged ? LogicResult.Changed : constraintStateChanged ? LogicResult.Changed : LogicResult.None;
    }

    public override bool NeedsEnforceConstraint => false;
    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        int numCells = cellIndices.Count;
        if (numCells <= 2)
        {
            return LogicResult.None;
        }

        BoardView board = sudokuSolver.Board;
        (uint allCellsCandidateUnion, uint allSetValuesUnion) = GetCellMasks(board);

        uint unionOfCurrentlyValidRangeMasks = 0;
        int validRangesFound = 0;

        foreach (uint currentRangeSequenceMask in rangeMasks)
        {
            if (IsRangeMaskValid(currentRangeSequenceMask, allCellsCandidateUnion, allSetValuesUnion, board))
            {
                unionOfCurrentlyValidRangeMasks |= currentRangeSequenceMask;
                validRangesFound++;
            }
        }

        if (validRangesFound == 0)
        {
            // For "source", we can highlight all cells in the Renban.
            logicalStepDescription?.Add(new LogicalStepDesc(
                desc: "No valid Renban sequence is possible with the current candidates.",
                highlightCells: cells // Use the cells of this Renban as the highlight/source
            ));
            return LogicResult.Invalid;
        }

        bool overallBoardChanged = false;
        List<int> allEliminatedCandidateIndicesThisStep = logicalStepDescription != null ? [] : null;
        List<int> sourceCandidatesForSummary = logicalStepDescription != null ? [] : null;

        if (logicalStepDescription != null)
        {
            // Populate sourceCandidatesForSummary with all candidates in the Renban cells before changes
            foreach (int cellIdx in cellIndices)
            {
                uint currentMask = board[cellIdx]; // Includes set bit if set
                if (IsValueSet(currentMask))
                {
                    sourceCandidatesForSummary.Add(CandidateIndex(cellIdx, GetValue(currentMask)));
                }
                else
                {
                    for (int v = 1; v <= MAX_VALUE; ++v)
                    {
                        if (HasValue(currentMask, v))
                        {
                            sourceCandidatesForSummary.Add(CandidateIndex(cellIdx, v));
                        }
                    }
                }
            }
        }

        for (int i = 0; i < numCells; i++)
        {
            int cellIndex = cellIndices[i];
            uint currentCellBoardMask = board[cellIndex];

            if (IsValueSet(currentCellBoardMask))
            {
                continue;
            }

            uint existingCandidates = currentCellBoardMask;
            uint newPotentialCandidates = existingCandidates & unionOfCurrentlyValidRangeMasks;

            if (newPotentialCandidates != existingCandidates)
            {
                uint eliminatedThisCellMask = existingCandidates & ~newPotentialCandidates;

                LogicResult keepResult = sudokuSolver.KeepMask(cellIndex, newPotentialCandidates);

                if (keepResult == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        List<int> eliminatedInThisCellIndices = [];
                        for (int v_elim = 1; v_elim <= MAX_VALUE; v_elim++)
                        {
                            if (HasValue(eliminatedThisCellMask, v_elim))
                            {
                                eliminatedInThisCellIndices.Add(CandidateIndex(cellIndex, v_elim));
                            }
                        }
                        // If newPotentialCandidates is 0, all existing were eliminated.
                        if (newPotentialCandidates == 0 && existingCandidates != 0)
                        {
                            eliminatedInThisCellIndices.Clear(); // Repopulate with all original candidates of this cell
                            for (int v_elim = 1; v_elim <= MAX_VALUE; v_elim++)
                            {
                                if (HasValue(existingCandidates, v_elim))
                                {
                                    eliminatedInThisCellIndices.Add(CandidateIndex(cellIndex, v_elim));
                                }
                            }
                        }

                        List<int> sourceForThisCell = [];
                        for (int v_source = 1; v_source <= MAX_VALUE; ++v_source)
                        {
                            if (HasValue(existingCandidates, v_source))
                            {
                                sourceForThisCell.Add(CandidateIndex(cellIndex, v_source));
                            }
                        }

                        string desc = newPotentialCandidates == 0 && existingCandidates != 0 ?
                            $"Restricting candidates in {CellName(cells[i])} to fit possible Renban sequences removed all its candidates (eliminated {sudokuSolver.DescribeElims(eliminatedInThisCellIndices)})." :
                            $"Applying Renban union mask to {CellName(cells[i])} (eliminated {sudokuSolver.DescribeElims(eliminatedInThisCellIndices)}) made the board invalid.";

                        logicalStepDescription.Add(new LogicalStepDesc(
                            desc: desc,
                            sourceCandidates: sourceForThisCell,
                            elimCandidates: eliminatedInThisCellIndices
                        ));
                    }
                    return LogicResult.Invalid;
                }

                if (keepResult == LogicResult.Changed)
                {
                    overallBoardChanged = true;
                    if (allEliminatedCandidateIndicesThisStep != null)
                    {
                        for (int v_elim = 1; v_elim <= MAX_VALUE; v_elim++)
                        {
                            if (HasValue(eliminatedThisCellMask, v_elim))
                            {
                                allEliminatedCandidateIndicesThisStep.Add(CandidateIndex(cellIndex, v_elim));
                            }
                        }
                    }
                }
            }
        }

        if (overallBoardChanged)
        {
            logicalStepDescription?.Add(new LogicalStepDesc(
                    desc: $"Candidates restricted in Renban cells to fit only possible sequences => {sudokuSolver.DescribeElims(allEliminatedCandidateIndicesThisStep)}.",
                    sourceCandidates: sourceCandidatesForSummary.Distinct(),
                    elimCandidates: allEliminatedCandidateIndicesThisStep.Distinct()
                ));
            return LogicResult.Changed;
        }

        return LogicResult.None;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        int numCells = cellIndices.Count;
        if (numCells <= 1)
        {
            return LogicResult.None;
        }

        bool changed = false;
        for (int i0 = 0; i0 < numCells; i0++)
        {
            int cell0Index = cellIndices[i0];
            for (int i1 = 0; i1 < numCells; i1++)
            {
                if (i0 == i1)
                {
                    continue;
                }

                int cell1Index = cellIndices[i1];
                for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                {
                    int cand0 = CandidateIndex(cell0Index, v0);
                    for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                    {
                        if (Math.Abs(v0 - v1) >= numCells)
                        {
                            LogicResult linkResult = solver.AddWeakLink(cand0, CandidateIndex(cell1Index, v1));
                            if (linkResult == LogicResult.Invalid)
                            {
                                logicalStepDescription?.Add(new LogicalStepDesc(
                                        $"Linking candidates too far apart for Renban ({CellName(cells[i0])}={v0}, {CellName(cells[i1])}={v1}) caused invalid state.",
                                        [cand0, CandidateIndex(cell1Index, v1)],
                                        []
                                    ));
                                return LogicResult.Invalid;
                            }
                            if (linkResult == LogicResult.Changed)
                            {
                                changed = true;
                            }
                        }
                    }
                }
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int valueToTest)
    {
        int numCells = cellIndices.Count;
        if (numCells <= 2)
        {
            return null;
        }

        BoardView board = sudokuSolver.Board;
        (uint allCellsCandidateUnion, uint allSetValuesUnion) = GetCellMasks(board);

        if ((allSetValuesUnion & ValueMask(valueToTest)) != 0)
        {
            return null;
        }

        uint validStartValuesMask = 0;
        foreach (uint currentRangeSequenceMask in rangeMasks)
        {
            if (IsRangeMaskValid(currentRangeSequenceMask, allCellsCandidateUnion, allSetValuesUnion, board))
            {
                validStartValuesMask |= ValueMask(MinValue(currentRangeSequenceMask));
            }
        }

        if (validStartValuesMask == 0)
        {
            return null;
        }

        int min_s_overall = MinValue(validStartValuesMask);
        int max_s_overall = MaxValue(validStartValuesMask);

        int mustContainLowerBound = max_s_overall;
        int mustContainUpperBound = min_s_overall + numCells - 1;

        if (valueToTest >= mustContainLowerBound && valueToTest <= mustContainUpperBound)
        {
            List<(int, int)> resultCells = [];
            for (int i = 0; i < cells.Count; ++i)
            {
                (int r, int c) cellCoord = cells[i];
                int cellIndex = cellIndices[i];
                uint cellBoardMask = board[cellIndex];

                if (!IsValueSet(cellBoardMask) && HasValue(cellBoardMask, valueToTest))
                {
                    resultCells.Add(cellCoord);
                }
            }
            return resultCells.Count > 0 ? resultCells : null;
        }
        return null;
    }
}