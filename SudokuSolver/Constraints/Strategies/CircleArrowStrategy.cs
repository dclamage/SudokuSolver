namespace SudokuSolver.Constraints.Strategies;

public class CircleArrowStrategy : IArrowLogicStrategy
{
    public CircleArrowStrategy(Solver solver) { }

    private (int, int) GetCircleCell(List<(int, int)> circleCells) => circleCells[0];

    public string GetSpecificName(List<(int, int)> circleCells, Solver solver) => $"Arrow at {CellName(GetCircleCell(circleCells))}";

    public LogicResult InitCandidates(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper)
    {
        bool changed = false;
        var boardView = solver.Board;
        var circleCell = GetCircleCell(circleCells);

        List<int> possibleCircleValues = [];
        uint circleMask = boardView[circleCell.Item1, circleCell.Item2];
        for (int v = 1; v <= solver.MAX_VALUE; ++v)
        {
            if (HasValue(circleMask, v))
            {
                possibleCircleValues.Add(v);
            }
        }

        if (possibleCircleValues.Count == 0)
        {
            return LogicResult.Invalid;
        }

        var arrowInitResult = arrowSumHelper.Init(solver, possibleCircleValues);
        if (arrowInitResult == LogicResult.Invalid)
        {
            return LogicResult.Invalid;
        }
        changed |= arrowInitResult == LogicResult.Changed;

        List<int> currentArrowSums = arrowSumHelper.PossibleSums(solver);
        if (currentArrowSums == null || currentArrowSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        uint circleKeepMaskBits = 0;
        foreach (int sum in currentArrowSums)
        {
            // A circle cell must hold a single digit from 1 to MAX_VALUE
            if (sum >= 1 && sum <= solver.MAX_VALUE)
            {
                circleKeepMaskBits |= ValueMask(sum);
            }
        }
        if (circleKeepMaskBits == 0)
        {
            return LogicResult.Invalid;
        }

        LogicResult keepResult = solver.KeepMask(circleCell.Item1, circleCell.Item2, circleKeepMaskBits);
        if (keepResult == LogicResult.Invalid)
        {
            return LogicResult.Invalid;
        }
        if (keepResult == LogicResult.Changed)
        {
            changed = true;
        }

        if (arrowCells.Count == 1)
        {
            var arrowCell = arrowCells[0];
            uint commonMask = solver.Board[circleCell.Item1, circleCell.Item2] & solver.Board[arrowCell.Item1, arrowCell.Item2]; // Re-read board
            LogicResult circleResult = solver.KeepMask(circleCell.Item1, circleCell.Item2, commonMask);
            if (circleResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            LogicResult arrowResult = solver.KeepMask(circleCell.Item1, circleCell.Item2, commonMask);
            if (arrowResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (circleResult == LogicResult.Changed || arrowResult == LogicResult.Changed)
            {
                changed = true;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public bool EnforceConstraint(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, HashSet<(int, int)> allCells, int r, int c, int val)
    {
        if (!allCells.Contains((r, c))) return true;

        var circleCell = GetCircleCell(circleCells);
        var boardView = solver.Board; // Use current board state

        if (IsValueSet(boardView[circleCell.Item1, circleCell.Item2]))
        {
            int circleVal = GetValue(boardView[circleCell.Item1, circleCell.Item2]);
            var possibleSumsForArrow = arrowSumHelper.PossibleSums(solver);
            if (possibleSumsForArrow == null || !possibleSumsForArrow.Contains(circleVal)) return false;

            if (arrowCells.All(ac => IsValueSet(boardView[ac.Item1, ac.Item2])))
            {
                int currentArrowSum = arrowCells.Select(ac => GetValue(boardView[ac.Item1, ac.Item2])).Sum();
                if (currentArrowSum != circleVal) return false;
            }
        }

        if (arrowCells.All(ac => IsValueSet(boardView[ac.Item1, ac.Item2])))
        {
            int currentArrowSum = arrowCells.Select(ac => GetValue(boardView[ac.Item1, ac.Item2])).Sum();
            if (!IsValueSet(boardView[circleCell.Item1, circleCell.Item2]))
            {
                if (!solver.SetValue(circleCell.Item1, circleCell.Item2, currentArrowSum)) return false;
            }
            else
            {
                if (GetValue(boardView[circleCell.Item1, circleCell.Item2]) != currentArrowSum) return false;
            }
        }
        else
        { // Not all arrow cells are set
            var possibleArrowSums = arrowSumHelper.PossibleSums(solver);
            if (arrowCells.Count == 0) possibleArrowSums = [0];

            if (possibleArrowSums == null || possibleArrowSums.Count == 0)
            {
                if (arrowCells.Count != 0) return false; // Arrow has cells but no possible sum
            }
            uint circleKeepMask = 0;
            foreach (var sum_val in possibleArrowSums)
            { // Renamed sum to sum_val to avoid conflict
                if (sum_val >= 1 && sum_val <= solver.MAX_VALUE)
                    circleKeepMask |= ValueMask(sum_val);
            }
            // If circle cell is not set, and its current candidates don't overlap with any possible sum
            if (!IsValueSet(boardView[circleCell.Item1, circleCell.Item2]) &&
                (boardView[circleCell.Item1, circleCell.Item2] & circleKeepMask) == 0 &&
                circleKeepMask != 0) return false;
        }

        if (arrowCells.Count == 1)
        {
            var arrowCell = arrowCells[0];
            // Re-check values after potential SetValue calls
            bool circleHasValue = IsValueSet(solver.Board[circleCell.Item1, circleCell.Item2]);
            bool arrowSingleHasValue = IsValueSet(solver.Board[arrowCell.Item1, arrowCell.Item2]);

            if (circleHasValue && !arrowSingleHasValue)
            {
                if (!solver.SetValue(arrowCell.Item1, arrowCell.Item2, GetValue(solver.Board[circleCell.Item1, circleCell.Item2]))) return false;
            }
            else if (!circleHasValue && arrowSingleHasValue)
            {
                if (!solver.SetValue(circleCell.Item1, circleCell.Item2, GetValue(solver.Board[arrowCell.Item1, arrowCell.Item2]))) return false;
            }
        }
        return true;
    }

    public LogicResult StepLogic(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        bool changed = false;
        var boardView = solver.Board;
        var circleCell = GetCircleCell(circleCells);

        List<int> possibleCircleValues = [];
        uint currentCircleMask = boardView[circleCell.Item1, circleCell.Item2];
        for (int v = 1; v <= solver.MAX_VALUE; ++v) { if (HasValue(currentCircleMask, v)) possibleCircleValues.Add(v); }

        if (possibleCircleValues.Count == 0)
        {
            logicalStepDescription?.Add(new($"Circle cell {CellName(circleCell)} has no candidates.", circleCell.ToEnumerable()));
            return LogicResult.Invalid;
        }

        var arrowStepResult = arrowSumHelper.StepLogic(solver, possibleCircleValues, logicalStepDescription, isBruteForcing);
        if (arrowStepResult == LogicResult.Invalid) return LogicResult.Invalid;
        changed |= arrowStepResult == LogicResult.Changed;

        var possibleArrowSums = arrowSumHelper.PossibleSums(solver);
        if (arrowCells.Count == 0) possibleArrowSums = [0];

        if (possibleArrowSums == null || possibleArrowSums.Count == 0)
        {
            if (arrowCells.Count != 0)
            {
                logicalStepDescription?.Add(new($"Arrow cells {solver.CompactName(arrowCells)} cannot form any valid sum.", arrowCells));
                return LogicResult.Invalid;
            }
            // If arrowCells is empty, possibleArrowSums should be {0}
        }

        uint circleKeepMaskBits = 0;
        foreach (int sum_val in possibleArrowSums) // Renamed sum to sum_val
        {
            if (sum_val >= 1 && sum_val <= solver.MAX_VALUE) // Circle must hold a valid digit
                circleKeepMaskBits |= ValueMask(sum_val);
        }

        uint initialCircleMask = solver.Board[circleCell.Item1, circleCell.Item2] & ~valueSetMask; // Mask before KeepMask
        LogicResult keepResult = solver.KeepMask(circleCell.Item1, circleCell.Item2, circleKeepMaskBits);

        if (keepResult == LogicResult.Changed)
        {
            changed = true;
            if (logicalStepDescription != null)
            {
                // Describe what was kept/removed.
                // We need to find the candidates that were actually removed.
                uint finalCircleMask = solver.Board[circleCell.Item1, circleCell.Item2] & ~valueSetMask;
                uint removedMask = initialCircleMask & ~finalCircleMask;
                if (removedMask != 0)
                { // Only log if something was actually removed
                    logicalStepDescription.Add(new LogicalStepDesc(
                        desc: $"Circle {CellName(circleCell)} restricted by arrow sum {string.Join(",", possibleArrowSums)} => {solver.DescribeElims(solver.CandidateIndexes(removedMask, circleCell.ToEnumerable()))}",
                        sourceCandidates: Enumerable.Empty<int>(), // Source is complex (arrow sums)
                        elimCandidates: solver.CandidateIndexes(removedMask, circleCell.ToEnumerable())
                    ));
                }
            }
        }
        // Check if circle cell became empty AFTER KeepMask
        if ((solver.Board[circleCell.Item1, circleCell.Item2] & ~valueSetMask) == 0 && (initialCircleMask) != 0) // if it became empty and wasn't before
        {
            if (logicalStepDescription != null && keepResult != LogicResult.Changed && initialCircleMask != circleKeepMaskBits)
            {
                // If KeepMask didn't report a change (e.g. new mask was same as old, but old was already invalid)
                // but cell is now empty, log it.
                logicalStepDescription.Add(new LogicalStepDesc(
                    $"Circle {CellName(circleCell)} became empty after restriction by arrow sum {string.Join(",", possibleArrowSums)}.",
                     circleCell.ToEnumerable()
                ));
            }
            return LogicResult.Invalid;
        }


        if (arrowCells.Count == 1)
        {
            var arrowCell = arrowCells[0];
            // Re-read board states as they might have changed
            uint currentCircleCellMask = solver.Board[circleCell.Item1, circleCell.Item2];
            uint currentArrowCellMask = solver.Board[arrowCell.Item1, arrowCell.Item2];
            uint commonMask = currentCircleCellMask & currentArrowCellMask;

            // KeepMask for Circle Cell
            uint initialCCCandidateMask = currentCircleCellMask & ~valueSetMask;
            LogicResult circleKeepCommonResult = solver.KeepMask(circleCell.Item1, circleCell.Item2, commonMask);
            if (circleKeepCommonResult == LogicResult.Changed)
            {
                changed = true;
                if (logicalStepDescription != null)
                {
                    uint finalCCCandidateMask = solver.Board[circleCell.Item1, circleCell.Item2] & ~valueSetMask;
                    uint removedCCCMask = initialCCCandidateMask & ~finalCCCandidateMask;
                    if (removedCCCMask != 0)
                    {
                        logicalStepDescription.Add(new LogicalStepDesc(
                            desc: $"Circle {CellName(circleCell)} synced with arrow {CellName(arrowCell)} => {solver.DescribeElims(solver.CandidateIndexes(removedCCCMask, circleCell.ToEnumerable()))}",
                            sourceCandidates: solver.CandidateIndexes(ValueMask(GetValue(commonMask)), arrowCell.ToEnumerable()), // Source is the arrow cell's common value
                            elimCandidates: solver.CandidateIndexes(removedCCCMask, circleCell.ToEnumerable())
                        ));
                    }
                }
            }
            if ((solver.Board[circleCell.Item1, circleCell.Item2] & ~valueSetMask) == 0 && initialCCCandidateMask != 0) return LogicResult.Invalid;


            // KeepMask for Arrow Cell
            uint initialACCandidateMask = currentArrowCellMask & ~valueSetMask;
            LogicResult arrowKeepCommonResult = solver.KeepMask(arrowCell.Item1, arrowCell.Item2, commonMask);
            if (arrowKeepCommonResult == LogicResult.Changed)
            {
                changed = true;
                if (logicalStepDescription != null)
                {
                    uint finalACCandidateMask = solver.Board[arrowCell.Item1, arrowCell.Item2] & ~valueSetMask;
                    uint removedACCMask = initialACCandidateMask & ~finalACCandidateMask;
                    if (removedACCMask != 0)
                    {
                        logicalStepDescription.Add(new LogicalStepDesc(
                            desc: $"Arrow {CellName(arrowCell)} synced with circle {CellName(circleCell)} => {solver.DescribeElims(solver.CandidateIndexes(removedACCMask, arrowCell.ToEnumerable()))}",
                            sourceCandidates: solver.CandidateIndexes(ValueMask(GetValue(commonMask)), circleCell.ToEnumerable()), // Source is the circle cell's common value
                            elimCandidates: solver.CandidateIndexes(removedACCMask, arrowCell.ToEnumerable())
                        ));
                    }
                }
            }
            if ((solver.Board[arrowCell.Item1, arrowCell.Item2] & ~valueSetMask) == 0 && initialACCandidateMask != 0) return LogicResult.Invalid;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }
}
