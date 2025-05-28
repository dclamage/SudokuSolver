namespace SudokuSolver;

public partial class Solver
{
    /// <summary>
    /// Returns which cells must be distinct from the all the inputted cells.
    /// </summary>
    /// <param name="cells"></param>
    /// <returns></returns>
    public HashSet<(int, int)> SeenCells(params (int, int)[] cells)
    {
        HashSet<(int, int)> result = null;
        foreach (var cell in cells)
        {
            var groupList = CellToGroupsLookup[CellIndex(cell)];
            if (groupList.Count == 0)
            {
                return new HashSet<(int, int)>();
            }

            HashSet<(int, int)> curSeen = new(groupList.First().Cells.Select(CellIndexToCoord));
            foreach (var group in groupList.Skip(1))
            {
                curSeen.UnionWith(group.Cells.Select(CellIndexToCoord));
            }

            foreach (var constraint in constraints)
            {
                curSeen.UnionWith(constraint.SeenCells(cell));
            }

            if (result == null)
            {
                result = curSeen;
            }
            else
            {
                result.IntersectWith(curSeen);
            }
        }
        if (result != null)
        {
            foreach (var cell in cells)
            {
                result.Remove(cell);
            }
        }
        return result ?? new HashSet<(int, int)>();
    }

    /// <summary>
    /// Returns which cells must be distinct from the all the inputted cells for a specific set of values.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="cells"></param>
    /// <returns></returns>
    public HashSet<(int, int)> SeenCellsByValueMask(uint mask, params (int, int)[] cells)
    {
        HashSet<(int, int)> result = null;
        foreach (var cell in cells)
        {
            var groupList = CellToGroupsLookup[CellIndex(cell)];
            if (groupList.Count == 0)
            {
                return new HashSet<(int, int)>();
            }

            HashSet<(int, int)> curSeen = new(groupList.First().Cells.Select(CellIndexToCoord));
            foreach (var group in groupList.Skip(1))
            {
                curSeen.UnionWith(group.Cells.Select(CellIndexToCoord));
            }

            foreach (var constraint in constraints)
            {
                curSeen.UnionWith(constraint.SeenCellsByValueMask(cell, mask));
            }

            if (result == null)
            {
                result = curSeen;
            }
            else
            {
                result.IntersectWith(curSeen);
            }
        }
        if (result != null)
        {
            foreach (var cell in cells)
            {
                result.Remove(cell);
            }
        }
        return result ?? new HashSet<(int, int)>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSeen((int, int) cell0, (int, int) cell1)
    {
        return seenMap[cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2, 0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSeenByValue((int, int) cell0, (int, int) cell1, int value)
    {
        return seenMap[cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2, value];
    }

    public bool IsGroup(List<(int, int)> cells)
    {
        for (int i0 = 0; i0 < cells.Count - 1; i0++)
        {
            var cell0 = cells[i0];
            var seen0 = SeenCells(cell0);
            for (int i1 = i0 + 1; i1 < cells.Count; i1++)
            {
                var cell1 = cells[i1];
                if (cell0 != cell1 && !seen0.Contains(cell1))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool IsGroup(IEnumerable<(int, int)> cells, int value)
    {
        List<int> candIndexes = CandidateIndexes(ValueMask(value), cells);
        if (candIndexes.Count <= 1)
        {
            return true;
        }

        for (int i0 = 0; i0 < candIndexes.Count - 1; i0++)
        {
            int cand0 = candIndexes[i0];
            var weakLinks0 = weakLinks[cand0];
            for (int i1 = i0 + 1; i1 < candIndexes.Count; i1++)
            {
                int cand1 = candIndexes[i1];
                if (cand0 != cand1 && weakLinks0.BinarySearch(cand1) < 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool IsGroupByValueMask(List<(int, int)> cells, uint valueMask)
    {
        for (int i0 = 0; i0 < cells.Count - 1; i0++)
        {
            var cell0 = cells[i0];
            var seen0 = SeenCellsByValueMask(valueMask, cell0);
            for (int i1 = i0 + 1; i1 < cells.Count; i1++)
            {
                var cell1 = cells[i1];
                if (cell0 != cell1 && !seen0.Contains(cell1))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public List<List<(int, int)>> SplitIntoGroups(IEnumerable<(int, int)> cellsEnumerable)
    {
        List<List<(int, int)>> groups = new();

        var cells = cellsEnumerable.ToList();
        if (cells.Count == 0)
        {
            return groups;
        }
        if (cells.Count == 1)
        {
            groups.Add(cells);
            return groups;
        }

        // Find the largest group and remove it from the cells
        int numCells = cells.Count;
        for (int groupSize = numCells; groupSize >= 2; groupSize--)
        {
            foreach (var subCells in cells.Combinations(groupSize))
            {
                if (IsGroup(subCells))
                {
                    groups.Add(subCells.ToList());
                    if (groupSize != numCells)
                    {
                        groups.AddRange(SplitIntoGroups(cells.Where(cell => !subCells.Contains(cell))));
                    }
                    return groups;
                }
            }
        }

        foreach (var cell in cells)
        {
            groups.Add(new List<(int, int)>() { cell });
        }
        return groups;
    }

    public bool CanPlaceDigits(List<(int, int)> cells, List<int> values)
    {
        int numCells = cells.Count;
        if (numCells != values.Count)
        {
            throw new ArgumentException($"CanPlaceDigits: Number of cells ({cells.Count}) must match number of values ({values.Count})");
        }

        // Ensure these values fit into the cell masks at all
        for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            int v = values[cellIndex];
            if (!HasValue(this[i, j], v))
            {
                return false;
            }
        }

        // Convert the cell + values to candidate indexes
        List<int> candidates = new(numCells);
        for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
        {
            var cell = cells[cellIndex];
            int v = values[cellIndex];
            candidates.Add(CandidateIndex(cell, v));
        }

        // Check if there are any weak links between candidates. If so, this isn't placeable.
        for (int c0 = 0; c0 < numCells - 1; c0++)
        {
            var weakLinks0 = weakLinks[candidates[c0]];
            for (int c1 = c0 + 1; c1 < numCells; c1++)
            {
                if (weakLinks0.BinarySearch(candidates[c1]) >= 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool CanPlaceDigitsAnyOrder(List<(int, int)> cells, List<int> values)
    {
        int numCells = cells.Count;
        if (numCells != values.Count)
        {
            throw new ArgumentException($"CanPlaceDigits: Number of cells ({cells.Count}) must match number of values ({values.Count})");
        }

        uint combMask = 0;
        foreach (var cell in cells)
        {
            combMask |= this[cell.Item1, cell.Item2];
        }

        uint needMask = 0;
        foreach (int v in values)
        {
            needMask |= ValueMask(v);
        }

        if ((needMask & combMask) != needMask)
        {
            return false;
        }

        foreach (var perm in values.Permuatations())
        {
            if (CanPlaceDigits(cells, perm))
            {
                return true;
            }
        }

        return false;
    }

    public LogicResult EvaluateSetValue(int i, int j, int val, ref string violationString)
    {
        uint valMask = ValueMask(val);
        if ((this[i, j] & valMask) == 0)
        {
            return LogicResult.None;
        }

        // Check if already set
        if ((this[i, j] & valueSetMask) != 0)
        {
            return LogicResult.None;
        }

        if (isInSetValue)
        {
            this[i, j] = valMask;
            return LogicResult.Changed;
        }

        isInSetValue = true;

        this[i, j] = valueSetMask | valMask;

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(i, j, val);
        foreach (int elimCandIndex in weakLinks[setCandidateIndex])
        {
            var (i1, j1, v1) = CandIndexToCoord(elimCandIndex);
            if (!ClearValue(i1, j1, v1))
            {
                violationString = $"{CellName(i1, j1)} has no value";
                return LogicResult.Invalid;
            }
        }

        // Enforce all constraints
        foreach (var constraint in constraints)
        {
            if (!constraint.EnforceConstraint(this, i, j, val))
            {
                violationString = $"{constraint.SpecificName} is violated";
                return LogicResult.Invalid;
            }
        }

        isInSetValue = false;

        return LogicResult.Changed;
    }

    public int MinimumUniqueValues(IEnumerable<(int, int)> cells)
    {
        var cellList = cells.ToList();
        int numCells = cellList.Count;
        if (numCells == 0)
        {
            return 0;
        }
        if (numCells == 1)
        {
            return 1;
        }

        int[] connectionCount = new int[numCells];
        HashSet<(int, int)> connections = new();
        for (int i0 = 0; i0 < numCells - 1; i0++)
        {
            var curCell = cellList[i0];
            var seen = SeenCells(curCell);
            for (int i1 = i0 + 1; i1 < numCells; i1++)
            {
                var otherCell = cellList[i1];
                if (seen.Contains(otherCell))
                {
                    connections.Add((i0, i1));
                    connectionCount[i0]++;
                    connectionCount[i1]++;
                }
            }
        }

        int maxGroupSize = connectionCount.Max() + 1;
        for (int groupSize = maxGroupSize; groupSize >= 2; groupSize--)
        {
            foreach (var groupCells in Enumerable.Range(0, numCells).Combinations(groupSize))
            {
                bool isFullyConnected = true;
                foreach (var pair in groupCells.Combinations(2))
                {
                    int i0 = pair[0];
                    int i1 = pair[1];
                    if (i0 > i1)
                    {
                        (i0, i1) = (i1, i0);
                    }
                    if (!connections.Contains((i0, i1)))
                    {
                        isFullyConnected = false;
                        break;
                    }
                }
                if (isFullyConnected)
                {
                    return groupSize;
                }
            }
        }
        return 1;
    }

    /// <summary>
    /// Build a map of all constraints by type + hash
    /// </summary>
    private Dictionary<string, Dictionary<string, Constraint>> GetConstraintsIndex()
    {
        // Cache the results on the solver object
        if (customInfo.TryGetValue("constraintsIndex", out object resultCache))
        {
            return resultCache as Dictionary<string, Dictionary<string, Constraint>>;
        }

        Dictionary<string, Dictionary<string, Constraint>> result = new();

        foreach (Constraint constraint in constraints.SelectMany(constraint => constraint.SplitToPrimitives(this)))
        {
            string name = constraint.Name;
            if (!result.ContainsKey(name))
            {
                result.Add(name, new());
            }
            result[name].TryAdd(constraint.GetHash(this), constraint);
        }

        customInfo.Add("constraintsIndex", result);

        return result;
    }

    /// <summary>
    /// Check if the current grid inherits another grid,
    /// i.e. if the current grid has exact copy of all constraints of the other grid, and possibly additional constraints.
    /// </summary>
    public bool IsInheritOf(Solver other)
    {
        // Check that the grids have the same basic parameters
        if (WIDTH != other.WIDTH || HEIGHT != other.HEIGHT || MAX_VALUE != other.MAX_VALUE)
        {
            return false;
        }

        // Check that the grids have the same regions
        if (other.regions != null)
        {
            if (regions == null)
            {
                return false;
            }

            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                if (regions[cellIndex] != other.regions[cellIndex])
                {
                    return false;
                }
            }
        }

        // For each cell, check that the current grid has no candidates that are not present in the other grid.
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                if ((this[i, j] & ~other[i, j] & ALL_VALUES_MASK) != 0)
                {
                    return false;
                }
            }
        }

        // Check that the other grid has no constraints that are not present in the current grid.
        var thisConstraintsIndex = GetConstraintsIndex();
        var otherConstraintsIndex = other.GetConstraintsIndex();
        if (otherConstraintsIndex.Count > thisConstraintsIndex.Count)
        {
            return false;
        }
        foreach (var (constraintType, otherConstraintsOfType) in otherConstraintsIndex)
        {
            if (!thisConstraintsIndex.TryGetValue(constraintType, out var thisConstraintsOfType))
            {
                return false;
            }

            if (otherConstraintsOfType.Count > thisConstraintsOfType.Count)
            {
                return false;
            }

            foreach (var constraintKey in otherConstraintsOfType.Keys)
            {
                if (!thisConstraintsOfType.ContainsKey(constraintKey))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal bool IsWeakLink(int candIndex0, int candIndex1) =>
        weakLinks[candIndex0].BinarySearch(candIndex1) >= 0;

    private List<int> InitIntersectWeakLinks(int candIndex)
    {
        List<int> srcList = weakLinks[candIndex];
        List<int> destList = new(srcList.Count);
        foreach (int weakLink in srcList)
        {
            if (IsCandIndexValid(weakLink))
            {
                destList.Add(weakLink);
            }
        }
        return destList;
    }

    private void InitIntersectWeakLinks(List<int> destList, int candIndex)
    {
        List<int> srcList = weakLinks[candIndex];
        foreach (int weakLink in srcList)
        {
            if (IsCandIndexValid(weakLink))
            {
                destList.Add(weakLink);
            }
        }
    }

    private void IntersectWeakLinks(List<int> destList, int candIndex)
    {
        int i = 0, j = 0;
        int writePos = 0;

        List<int> srcList = weakLinks[candIndex];
        while (i < destList.Count && j < srcList.Count)
        {
            if (destList[i] == srcList[j])
            {
                if (IsCandIndexValid(destList[i]))
                {
                    destList[writePos++] = destList[i];
                }
                i++;
                j++;
            }
            else if (destList[i] < srcList[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        // Resize list to remove unused elements
        if (writePos < destList.Count)
        {
            destList.RemoveRange(writePos, destList.Count - writePos);
        }
    }

    private void UnionWeakLinks(List<int> dstList, List<int> srcList)
    {
        // Handle null lists for robustness.
        if (srcList == null || srcList.Count == 0)
        {
            // If srcList is null or empty, there's nothing to merge from it.
            // dstList remains unchanged (which is correct for union with an empty set).
            return;
        }

        // If dstList is empty, the result is just a copy of srcList.
        // Since srcList has no duplicates and is sorted, this is correct.
        if (dstList.Count == 0)
        {
            dstList.AddRange(srcList); // AddRange is efficient here
            return;
        }

        // Create a new list to hold the merged result.
        // Pre-allocate capacity for efficiency.
        List<int> mergedList = new List<int>(dstList.Count + srcList.Count);

        int dstIndex = 0;
        int srcIndex = 0;

        // Iterate while there are elements in both lists
        while (dstIndex < dstList.Count && srcIndex < srcList.Count)
        {
            if (dstList[dstIndex] < srcList[srcIndex])
            {
                mergedList.Add(dstList[dstIndex]);
                dstIndex++;
            }
            else if (srcList[srcIndex] < dstList[dstIndex])
            {
                mergedList.Add(srcList[srcIndex]);
                srcIndex++;
            }
            else // Elements are equal (dstList[dstIndex] == srcList[srcIndex])
            {
                // Since neither list has duplicates, this element is common to both.
                // Add it once to the merged list for the union.
                mergedList.Add(dstList[dstIndex]);
                dstIndex++;
                srcIndex++; // Move past this element in both lists
            }
        }

        // Add any remaining elements from dstList
        // (These elements are greater than all processed elements from srcList)
        while (dstIndex < dstList.Count)
        {
            mergedList.Add(dstList[dstIndex]);
            dstIndex++;
        }

        // Add any remaining elements from srcList
        // (These elements are greater than all processed elements from dstList)
        while (srcIndex < srcList.Count)
        {
            mergedList.Add(srcList[srcIndex]);
            srcIndex++;
        }

        // Clear dstList and add all elements from the mergedList
        // This updates dstList to contain the sorted union.
        dstList.Clear();
        dstList.AddRange(mergedList);
    }


    internal IEnumerable<int> CalcElims(int candIndex0, int candIndex1)
    {
        var list0 = weakLinks[candIndex0];
        var list1 = weakLinks[candIndex1];
        int i = 0, j = 0;

        while (i < list0.Count && j < list1.Count)
        {
            int v0 = list0[i], v1 = list1[j];

            if (v0 < v1)
            {
                i++;
            }
            else if (v1 < v0)
            {
                j++;
            }
            else
            {
                // v0 == v1
                if (IsCandIndexValid(v0))
                {
                    yield return v0;
                }

                i++;
                j++;
            }
        }
    }

    internal IEnumerable<int> CalcElims(params int[] candIndexes) => CalcElims(candIndexes);

    internal List<int> CalcElims(IEnumerable<int> candIndexes)
    {
        List<int> result = null;
        foreach (int candIndex in candIndexes)
        {
            if (result == null)
            {
                result = InitIntersectWeakLinks(candIndex);
            }
            else
            {
                IntersectWeakLinks(result, candIndex);
            }
        }

        // if candIndexes was empty, return an empty sequence
        return result ?? [];
    }

    internal void CalcElims(List<int> dstElims, IEnumerable<int> candIndexes)
    {
        foreach (int candIndex in candIndexes)
        {
            IntersectWeakLinks(dstElims, candIndex);
        }
    }

    internal List<int> CalcElims(uint clearMask, List<(int, int)> cells) =>
        CalcElims(clearMask, cells.Select(CellIndex).ToList());

    internal List<int> CalcElims(uint clearMask, List<int> cells)
    {
        List<int> elims = null;
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (!HasValue(clearMask, v))
            {
                continue;
            }

            var curElims = CalcElims(cells.Where(cell => HasValue(board[cell], v)).Select(cell => CandidateIndex(cell, v)));
            if (elims == null)
            {
                elims = curElims;
            }
            else
            {
                UnionWeakLinks(elims, curElims);
            }
        }
        return elims;
    }

    internal void CalcElims(List<int> outElims, uint clearMask, List<int> cellIndexes)
    {
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (!HasValue(clearMask, v))
            {
                continue;
            }

            var curElims = CalcElims(cellIndexes.Where(cell => HasValue(board[cell], v)).Select(cell => CandidateIndex(cell, v)));
            UnionWeakLinks(outElims, curElims);
        }
    }

    internal int CountCandidatesForNonGivens()
    {
        int count = 0;
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint mask = board[cellIndex];
            if (!IsValueSet(mask))
            {
                count += ValueCount(mask);
            }
        }
        return count;
    }
}
