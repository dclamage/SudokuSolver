using SudokuSolver.Constraints.Helpers;

namespace SudokuSolver.Constraints.Strategies;

public class PillArrowStrategy : IArrowLogicStrategy
{
    public PillArrowStrategy(Solver solver) { /* Constructor for consistency, solver passed to methods */ }

    public string GetSpecificName(List<(int, int)> circleCells, Solver solver) => $"Arrow from pill {solver.CompactName(circleCells)}";

    public LogicResult InitCandidates(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper)
    {
        bool changed = false;

        var possiblePillTotals = ArrowConstraintHelpers.GeneratePillTotalsFromCandidates(solver, circleCells);
        // If any pill cell is empty, GeneratePillTotalsFromCandidates returns empty.
        // This would be caught by solver's basic propagation if a cell becomes truly empty.
        // Here, it means no combination of current candidates forms a valid pill number.
        if (!possiblePillTotals.Any() && circleCells.Any(cc => (solver.Board[cc.Item1, cc.Item2] & ~valueSetMask) != 0))
        {
            // Only invalid if pill cells have candidates but cannot form any total.
            // If a pill cell is already genuinely empty, solver propagation should have caught it.
            return LogicResult.Invalid;
        }
        // If possiblePillTotals is empty because a pill cell is empty, that's fine, Init can proceed if arrow has sum 0.

        var arrowInitResult = arrowSumHelper.Init(solver, possiblePillTotals);
        if (arrowInitResult == LogicResult.Invalid) return LogicResult.Invalid;
        changed |= arrowInitResult == LogicResult.Changed;

        List<int> currentArrowSums;
        if (arrowCells.Count == 0) currentArrowSums = new List<int> { 0 }; // Arrow sum is 0 if no arrow cells
        else currentArrowSums = arrowSumHelper.PossibleSums(solver);

        if (currentArrowSums == null || !currentArrowSums.Any())
        {
            // If arrowCells is not empty, an empty/null currentArrowSums is an invalid state
            if (arrowCells.Any()) return LogicResult.Invalid;
        }

        // It's possible currentArrowSums is empty here if possiblePillTotals was empty and arrowSumHelper.Init resulted in no possible sums
        // (e.g., arrow shaft requires sum > 0, but pill is empty so possiblePillTotals = {0} if it was implemented that way)
        // However, our GeneratePillTotalsFromCandidates does not yield 0 for empty candidates.
        // So if currentArrowSums is empty, it's an issue.
        if (!currentArrowSums.Any() && arrowCells.Any()) return LogicResult.Invalid;


        var pillRestrictResult = ArrowConstraintHelpers.RestrictPillCandidatesBySumSet(solver, circleCells, currentArrowSums.ToHashSet(), null);
        if (pillRestrictResult == LogicResult.Invalid) return LogicResult.Invalid;
        changed |= pillRestrictResult == LogicResult.Changed;

        // The original code had a check based on maxPossibleArrowSum.ToString().Length vs circleCells.Count.
        // This is complex because the "smallest" number a pill can make depends on candidates (e.g., "10" vs "20").
        // `RestrictPillCandidatesBySumSet` is more robust as it considers all actual candidate combinations.
        // If after restriction, a pill cell is empty, it will be caught.

        // Example: If arrowCells can sum to max 17 (2 cells, MAX_VALUE=9). Pill has 3 cells.
        // Smallest 3-digit number is 111. 111 > 17. This should be caught by RestrictPillCandidatesBySumSet
        // because no sum in currentArrowSums (e.g., {1..17}) would have a valid 3-digit pill arrangement.
        // So, the PossiblePillArrangements for those sums would yield nothing, leading to empty keepMasks for pill cells.

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public bool EnforceConstraint(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, HashSet<(int, int)> allCells, int r, int c, int val)
    {
        if (!allCells.Contains((r, c))) return true;
        var boardView = solver.Board; // Current state

        bool pillFullyValued = circleCells.All(cc => IsValueSet(boardView[cc.Item1, cc.Item2]));
        bool arrowFullyValued = arrowCells.All(ac => IsValueSet(boardView[ac.Item1, ac.Item2]));

        if (pillFullyValued)
        {
            int pillVal = ArrowConstraintHelpers.CalculatePillValueFromValues(circleCells.Select(cc => GetValue(boardView[cc.Item1, cc.Item2])));
            var possibleSumsForArrow = arrowSumHelper.PossibleSums(solver); // Reflects current board state
            if (arrowCells.Count == 0) possibleSumsForArrow = new List<int> { 0 };

            if (possibleSumsForArrow == null || !possibleSumsForArrow.Contains(pillVal)) return false;

            if (arrowFullyValued)
            {
                int arrowSum = arrowCells.Select(ac => GetValue(boardView[ac.Item1, ac.Item2])).Sum();
                if (pillVal != arrowSum) return false;
            }
        }

        if (arrowFullyValued) // This implies all arrow cells are set, including potentially (r,c)
        {
            int currentArrowSum = arrowCells.Select(ac => GetValue(boardView[ac.Item1, ac.Item2])).Sum();
            if (!pillFullyValued)
            {
                // Pill not set. Check if it *can* be set to currentArrowSum.
                var currentPillTotals = ArrowConstraintHelpers.GeneratePillTotalsFromCandidates(solver, circleCells);
                if (!currentPillTotals.Contains(currentArrowSum)) return false;
                // If it can be, StepLogic would handle setting it if unique. Enforce is just for validity.
            }
            // If pillFullyValued, it's already checked by the block above.
        }
        // If neither is fully valued, or only one part is partially valued, rely on StepLogic.
        // A quick check could be:
        else if (!pillFullyValued && !arrowFullyValued)
        {
            var possiblePillTotals = ArrowConstraintHelpers.GeneratePillTotalsFromCandidates(solver, circleCells);
            var possibleArrowSums = arrowSumHelper.PossibleSums(solver);
            if (arrowCells.Count == 0) possibleArrowSums = new List<int> { 0 };


            if (!possiblePillTotals.Any() && circleCells.Any(cc => (solver.Board[cc.Item1, cc.Item2] & ~valueSetMask) != 0)) return false; // Pill has candidates but forms no totals
            if (!possibleArrowSums.Any() && arrowCells.Any()) return false; // Arrow has cells but forms no sums

            if (possiblePillTotals.Any() && possibleArrowSums.Any() && !possiblePillTotals.Overlaps(possibleArrowSums))
            {
                return false; // No common sum possible
            }
        }


        return true;
    }

    public LogicResult StepLogic(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells, SumCellsHelper arrowSumHelper, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        bool changed = false;

        var possiblePillTotals = ArrowConstraintHelpers.GeneratePillTotalsFromCandidates(solver, circleCells);
        if (!possiblePillTotals.Any() && circleCells.Any(cc => (solver.Board[cc.Item1, cc.Item2] & ~valueSetMask) != 0))
        {
            logicalStepDescription?.Add(new($"Pill {solver.CompactName(circleCells)} has candidates but cannot form any valid number.", circleCells));
            return LogicResult.Invalid;
        }
        // If possiblePillTotals is empty because a pill cell is genuinely empty, other logic should catch it.

        var arrowStepResult = arrowSumHelper.StepLogic(solver, possiblePillTotals, logicalStepDescription, isBruteForcing);
        if (arrowStepResult == LogicResult.Invalid) return LogicResult.Invalid;
        changed |= arrowStepResult == LogicResult.Changed;

        // Re-evaluate possibleArrowSums as arrowStepResult might have changed arrow cells
        var possibleArrowSums = arrowSumHelper.PossibleSums(solver);
        if (arrowCells.Count == 0) possibleArrowSums = new List<int> { 0 };

        if (possibleArrowSums == null || !possibleArrowSums.Any())
        {
            if (arrowCells.Any())
            { // Only an issue if arrow cells exist but cannot form a sum
                logicalStepDescription?.Add(new($"Arrow cells {solver.CompactName(arrowCells)} cannot form any valid sum after interaction with pill.", arrowCells));
                return LogicResult.Invalid;
            }
            // If arrowCells is empty, possibleArrowSums should be {0}, which is valid.
        }

        // Now, restrict pill candidates based on the (potentially updated) possibleArrowSums
        // Re-generate possiblePillTotals could be done here if we expect arrow changes to drastically alter pill sum feasibility.
        // However, RestrictPillCandidatesBySumSet uses the possibleArrowSums to filter pill arrangements.
        var pillRestrictResult = ArrowConstraintHelpers.RestrictPillCandidatesBySumSet(solver, circleCells, possibleArrowSums.ToHashSet(), logicalStepDescription);
        if (pillRestrictResult == LogicResult.Invalid) return LogicResult.Invalid;
        changed |= pillRestrictResult == LogicResult.Changed;

        return changed ? LogicResult.Changed : LogicResult.None;
    }
}
