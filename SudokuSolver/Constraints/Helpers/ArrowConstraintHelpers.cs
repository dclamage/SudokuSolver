namespace SudokuSolver.Constraints.Helpers;

public static class ArrowConstraintHelpers
{
    public static IEnumerable<int[]> PossiblePillArrangements(int total, int cells, int maxValue, Solver solver) // maxValue is solver.MAX_VALUE
    {
        if (cells == 0)
        {
            if (total == 0) yield return Array.Empty<int>();
            yield break;
        }

        // If the arrow sum is 0, the pill must represent the numerical value 0.
        // Since cell values are 1-based (1 to MAX_VALUE), a standard pill cannot form 0
        // unless MAX_VALUE itself allows for a digit that is 0, which is not the case here.
        // Thus, if total is 0, no standard pill arrangement is possible.
        if (total == 0)
        {
            // A multi-cell pill like {1,0} (if 0 was allowed) would be "10".
            // A pill {0,0} would be "00" = 0.
            // But since cell values are 1 to MAX_VALUE, a component '0' is impossible.
            // Therefore, a pill cannot form the sum 0 if it has any cells.
            yield break;
        }

        if (cells == 1) // Pill has 1 cell
        {
            // The 'total' must be the value in this single cell.
            // It must be a valid digit (1 to MAX_VALUE).
            if (total >= 1 && total <= maxValue)
            {
                yield return [total];
            }
            yield break;
        }

        // cells > 1 and total != 0
        string sTotal = total.ToString();

        int maxLenFirst = sTotal.Length - (cells - 1);
        if (maxLenFirst <= 0)
        {
            yield break;
        }

        for (var numberDigits = 1; numberDigits <= maxLenFirst; numberDigits++)
        {
            string sFirstString = sTotal.Substring(0, numberDigits);
            // A component of a pill (a digit in a cell) cannot have a leading zero
            // unless it is the digit "0" itself. But "0" is not a valid cell value (1-MAX_VALUE).
            if (sFirstString.Length > 1 && sFirstString[0] == '0')
            {
                continue;
            }

            int firstVal = int.Parse(sFirstString);

            // Each component digit must be between 1 and MAX_VALUE.
            if (firstVal < 1 || firstVal > maxValue)
            {
                break;
            }

            string sRemaining = sTotal.Substring(numberDigits);

            int remainingVal = 0; // Default for recursion if sRemaining is empty
            if (sRemaining.Length > 0)
            {
                // If sRemaining implies a number with a leading zero (e.g., "05"),
                // and it's not just "0", then it's not a valid way to form parts of the total
                // because cell values don't have leading zeros (1-9, or 1-16, etc.).
                if (sRemaining.Length > 1 && sRemaining[0] == '0')
                {
                    continue;
                }
                remainingVal = int.Parse(sRemaining);
            }
            else if (cells - 1 > 0)
            { // sRemaining is empty, but more cells to fill. This is not possible to match sTotal.
                continue;
            }
            // If sRemaining is empty AND cells - 1 == 0, then remainingVal = 0, cells-1 = 0.
            // The recursive call `PossiblePillArrangements(0, 0, ...)` will correctly yield an empty array.

            foreach (var remainingCombination in PossiblePillArrangements(remainingVal, cells - 1, maxValue, solver))
            {
                StringBuilder tempSb = new StringBuilder();
                tempSb.Append(firstVal.ToString());
                foreach (var rem_val in remainingCombination) tempSb.Append(rem_val.ToString());

                if (tempSb.ToString() != sTotal)
                {
                    continue;
                }

                var combination = new int[remainingCombination.Length + 1];
                combination[0] = firstVal;
                remainingCombination.CopyTo(combination, 1);
                yield return combination;
            }
        }
    }

    public static int CalculatePillValueFromValues(IEnumerable<int> values)
    {
        if (!values.Any()) return 0; // Should not happen if cells > 0 and values are 1-based
        StringBuilder sb = new StringBuilder();
        foreach (int value in values) sb.Append(value.ToString());
        return int.Parse(sb.ToString());
    }

    public static HashSet<int> GeneratePillTotalsFromCandidates(Solver solver, List<(int, int)> pillCells)
    {
        var possibleTotals = new HashSet<int>();
        var boardView = solver.Board;
        var candidateLists = pillCells.Select(c => {
            List<int> vals = new List<int>();
            uint mask = boardView[c.Item1, c.Item2];
            for (int v = 1; v <= solver.MAX_VALUE; ++v)
            {
                if (HasValue(mask, v)) vals.Add(v);
            }
            return vals;
        }).ToList();

        if (candidateLists.Any(cl => cl.Count == 0)) return possibleTotals;

        var currentDigits = new List<int>(pillCells.Count);
        GeneratePillTotalsRecursive(0, currentDigits, candidateLists, possibleTotals);
        return possibleTotals;
    }

    private static void GeneratePillTotalsRecursive(int cellIndex, List<int> currentDigits, List<List<int>> candidateLists, HashSet<int> possibleTotals)
    {
        if (cellIndex == candidateLists.Count)
        {
            possibleTotals.Add(CalculatePillValueFromValues(currentDigits));
            return;
        }
        foreach (int candidateValue in candidateLists[cellIndex])
        {
            currentDigits.Add(candidateValue);
            GeneratePillTotalsRecursive(cellIndex + 1, currentDigits, candidateLists, possibleTotals);
            currentDigits.RemoveAt(currentDigits.Count - 1);
        }
    }

    public static LogicResult RestrictPillCandidatesBySumSet(Solver solver, List<(int, int)> circleCells, HashSet<int> allowedArrowSums, List<LogicalStepDesc> logicalStepDescription)
    {
        var boardView = solver.Board;
        var keepMasks = new uint[circleCells.Count];

        foreach (var sum in allowedArrowSums)
        {
            foreach (var arrangement in PossiblePillArrangements(sum, circleCells.Count, solver.MAX_VALUE, solver))
            {
                bool arrangementPossible = true;
                for (int i = 0; i < circleCells.Count; i++)
                {
                    if ((boardView[circleCells[i].Item1, circleCells[i].Item2] & ValueMask(arrangement[i])) == 0)
                    {
                        arrangementPossible = false;
                        break;
                    }
                }

                if (arrangementPossible)
                {
                    for (var cellIndex = 0; cellIndex < circleCells.Count; cellIndex++)
                    {
                        keepMasks[cellIndex] |= ValueMask(arrangement[cellIndex]);
                    }
                }
            }
        }

        var elims = new List<int>();
        bool changedOnBoard = false;

        for (var cellIndex = 0; cellIndex < circleCells.Count; cellIndex++)
        {
            var pillCell = circleCells[cellIndex];
            uint currentCandMask = boardView[pillCell.Item1, pillCell.Item2] & ~valueSetMask;
            uint effectiveKeepMaskForCell = keepMasks[cellIndex] & solver.ALL_VALUES_MASK;

            uint removeMask = currentCandMask & ~effectiveKeepMaskForCell;
            if (removeMask != 0)
            {
                var currentCellElims = solver.CandidateIndexes(removeMask, pillCell.ToEnumerable());
                elims.AddRange(currentCellElims);
            }

            if ((currentCandMask & effectiveKeepMaskForCell) != currentCandMask)
            {
                if (solver.KeepMask(pillCell.Item1, pillCell.Item2, effectiveKeepMaskForCell) == LogicResult.Changed)
                {
                    changedOnBoard = true;
                }
            }
            if ((solver.Board[pillCell.Item1, pillCell.Item2] & ~valueSetMask) == 0 && currentCandMask != 0) // Check if cell became empty AND it wasn't already
            {
                return LogicResult.Invalid;
            }
        }

        if (elims.Count > 0 && logicalStepDescription != null && changedOnBoard)
        {
            logicalStepDescription.Add(new(
               desc: $"Impossible pill value components based on arrow sum constraints => {solver.DescribeElims(elims)}",
               sourceCandidates: Enumerable.Empty<int>(),
               elimCandidates: elims
           ));
        }
        return changedOnBoard ? LogicResult.Changed : LogicResult.None;
    }
}
