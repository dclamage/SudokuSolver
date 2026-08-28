namespace SudokuSolver;

public partial class Solver
{

    public Solver(int width, int height, int maxValue)
    {
        if (maxValue <= 0 || maxValue > 31)
        {
            throw new ArgumentException($"Unsupported max value of: {maxValue}");
        }

        WIDTH = width;
        HEIGHT = height;
        MAX_VALUE = maxValue;
        ALL_VALUES_MASK = (1u << MAX_VALUE) - 1;
        NUM_CELLS = width * height;
        NUM_CANDIDATES = NUM_CELLS * MAX_VALUE;
        combinations = new int[MAX_VALUE][][];
        InitCombinations();

        board = new uint[NUM_CELLS];
        board.AsSpan().Fill(ALL_VALUES_MASK);

        constraints = [];
        enforceConstraints = [];
        sumConstraints = new(this);

        isInvalid = false;
        unsetCellsCount = NUM_CELLS;
        pendingNakedSingles = [];
        pendingCellForcing = [];

        // Hidden single tracking
        _candidateCountsPerGroupValue = null;
        _checkGroupForHiddens = Array.Empty<ulong>();

        // Real contents are built by FinalizeConstraints; the drain is guarded on _constraintQueued
        // being non-empty, but keep the slot map non-null so a stray deref cannot NRE.
        _propagationSlotToConstraint = Array.Empty<int>();

        Groups = [];
        CellToGroupsLookup = new List<SudokuGroup>[NUM_CELLS];
        for (int ci = 0; ci < NUM_CELLS; ci++)
        {
            CellToGroupsLookup[ci] = [];
        }

        weakLinks = new List<int>[NUM_CANDIDATES];
        for (int ci = 0; ci < NUM_CANDIDATES; ci++)
        {
            weakLinks[ci] = [];
        }

        candidateToCellAndValueLookup = new (int, int)[NUM_CANDIDATES];
        candidateToCoordValueLookup = new (int, int, int)[NUM_CANDIDATES];
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    int candidateIndex = CandidateIndex(i, j, v);
                    candidateToCellAndValueLookup[candidateIndex] = (CellIndex(i, j), v);
                    candidateToCoordValueLookup[candidateIndex] = (i, j, v);
                }
            }
        }

        customInfo = [];
    }

    public Solver(Solver other, bool willRunNonSinglesLogic)
    {
        WIDTH = other.WIDTH;
        HEIGHT = other.HEIGHT;
        MAX_VALUE = other.MAX_VALUE;
        ALL_VALUES_MASK = other.ALL_VALUES_MASK;
        NUM_CELLS = other.NUM_CELLS;
        NUM_CANDIDATES = other.NUM_CANDIDATES;
        combinations = other.combinations;
        Title = other.Title;
        Author = other.Author;
        Rules = other.Rules;
        DisableTuples = other.DisableTuples;
        DisablePointing = other.DisablePointing;
        DisableFishes = other.DisableFishes;
        DisableWings = other.DisableWings;
        DisableAIC = other.DisableAIC;
        DisableContradictions = other.DisableContradictions;
        DisableFindShortestContradiction = other.DisableFindShortestContradiction;
        WeakLinkDiscovery = other.WeakLinkDiscovery;
        WeakLinkDiscoveryNodeThreshold = other.WeakLinkDiscoveryNodeThreshold;
        TrueCandidatesStallLimit = other.TrueCandidatesStallLimit;
        board = new uint[NUM_CELLS];
        other.board.AsSpan().CopyTo(board);
        regions = other.regions;
        candidateToCellAndValueLookup = other.candidateToCellAndValueLookup;
        candidateToCoordValueLookup = other.candidateToCoordValueLookup;
        seenMap = other.seenMap;
        constraints = other.constraints;
        enforceConstraints = other.enforceConstraints;
        sumConstraints = other.sumConstraints;
        isInvalid = other.isInvalid;
        unsetCellsCount = other.unsetCellsCount;
        pendingNakedSingles = [.. other.pendingNakedSingles];
        pendingCellForcing = [.. other.pendingCellForcing];

        // Hidden single tracking
        if (other._candidateCountsPerGroupValue != null)
        {
            _candidateCountsPerGroupValue = new int[other._candidateCountsPerGroupValue.Length];
            other._candidateCountsPerGroupValue.AsSpan().CopyTo(_candidateCountsPerGroupValue);

            _checkGroupForHiddens = new ulong[other._checkGroupForHiddens.Length];
            other._checkGroupForHiddens.AsSpan().CopyTo(_checkGroupForHiddens);
        }
        else
        {
            _candidateCountsPerGroupValue = null;
            _checkGroupForHiddens = Array.Empty<ulong>();
        }

        Groups = other.Groups;
        smallGroupsBySize = other.smallGroupsBySize;
        maxValueGroups = other.maxValueGroups;
        CellToGroupsLookup = other.CellToGroupsLookup;
        customInfo = other.customInfo;

        // Brute force is far too slow if the weak links are copied every clone.
        if (willRunNonSinglesLogic)
        {
            weakLinks = other.CloneWeakLinks();
        }
        else
        {
            weakLinks = other.weakLinks;
        }
        totalWeakLinks = other.totalWeakLinks;

        // Inherit the grouped weak-link table by reference, even when the lists themselves were
        // copied: CloneWeakLinks copies contents, so the table still describes them. Any later
        // mutation goes through AddWeakLink, which nulls the table on whichever instance mutated —
        // and because a copied list diverges only on that instance, the parent's table stays
        // correct for the parent. The invariant is simply "non-null implies matches these lists".
        wlGroupedOffsets = other.wlGroupedOffsets;
        wlGroupedCells = other.wlGroupedCells;
        wlGroupedMasks = other.wlGroupedMasks;
        cfOffsets = other.cfOffsets;
        cfTargets = other.cfTargets;
        cfMasks = other.cfMasks;
        cfCanFire = other.cfCanFire;
        cfCanFireWords = other.cfCanFireWords;
        cfNewlyFires = other.cfNewlyFires;

        // Share conflict scores and decay state by reference so all clones update the same arrays.
        conflictScores = other.conflictScores;
        conflictDecayState = other.conflictDecayState;
        branchCellIndex = -1;
        searchDepth = other.searchDepth;

        // Share the read-only propagation-queue maps; allocate a fresh queued-flags array.
        cellToConstraintMask = other.cellToConstraintMask;
        _alwaysRunConstraintBits = other._alwaysRunConstraintBits;
        // Both may still be unset: Constraint.InitLinksByRunningLogic clones mid-FinalizeConstraints,
        // before the slot map exists. Such a clone gets an empty queue, which is correct — it runs
        // logic, not brute force, and the brute-force drain is guarded on a non-empty queue.
        _propagationSlotToConstraint = other._propagationSlotToConstraint ?? Array.Empty<int>();
        _constraintQueued = other._constraintQueued is { Length: > 0 }
            ? new ulong[other._constraintQueued.Length]
            : Array.Empty<ulong>();
        _lastContradictionCellIndex = -1;
    }

    /// <summary>
    /// Creates a copy of the board, including all constraints, set values, and candidates.
    /// </summary>
    /// <returns></returns>
    public Solver Clone(bool willRunNonSinglesLogic) => new(this, willRunNonSinglesLogic);
    private void InitCombinations()
    {
        for (int n = 1; n <= combinations.Length; n++)
        {
            combinations[n - 1] = new int[n][];
            for (int k = 1; k <= n; k++)
            {
                int numCombinations = BinomialCoeff(n, k);
                combinations[n - 1][k - 1] = new int[numCombinations * k];
                FillCombinations(combinations[n - 1][k - 1], n, k);
            }
        }
    }

    private void InitStandardGroups()
    {
        for (int i = 0; i < HEIGHT; i++)
        {
            List<int> cells = new(WIDTH);
            for (int j = 0; j < WIDTH; j++)
            {
                cells.Add(CellIndex(i, j));
            }
            SudokuGroup group = new(GroupType.Row, $"Row {i + 1}", cells, null, Groups.Count);
            Groups.Add(group);
            InitMapForGroup(group);
        }

        // Add col groups
        for (int j = 0; j < WIDTH; j++)
        {
            List<int> cells = new(HEIGHT);
            for (int i = 0; i < HEIGHT; i++)
            {
                cells.Add(CellIndex(i, j));
            }
            SudokuGroup group = new(GroupType.Column, $"Column {j + 1}", cells, null, Groups.Count);
            Groups.Add(group);
            InitMapForGroup(group);
        }

        // Add regions
        for (int region = 0; region < WIDTH; region++)
        {
            List<int> cells = new(WIDTH);
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = CellIndex(i, j);
                    if (regions[cellIndex] == region)
                    {
                        cells.Add(CellIndex(i, j));
                    }
                }
            }
            SudokuGroup group = new(GroupType.Region, $"Region {region + 1}", cells, null, Groups.Count);
            Groups.Add(group);
            InitMapForGroup(group);
        }
    }

    private void InitMapForGroup(SudokuGroup group)
    {
        foreach (int cellIndex in group.Cells)
        {
            CellToGroupsLookup[cellIndex].Add(group);
        }

        for (int groupIndex0 = 0; groupIndex0 < group.Cells.Count - 1; groupIndex0++)
        {
            int cellIndex0 = group.Cells[groupIndex0];
            for (int groupIndex1 = groupIndex0 + 1; groupIndex1 < group.Cells.Count; groupIndex1++)
            {
                int cellIndex1 = group.Cells[groupIndex1];
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    AddWeakLink(CandidateIndex(cellIndex0, v), CandidateIndex(cellIndex1, v));
                }
            }
        }
    }

    /// <summary>
    /// Set custom regions for the board
    /// Each region is indexed starting with 0
    /// </summary>
    /// <param name="regions"></param>
    public void SetRegions(int[] regions)
    {
        if (Groups.Count != 0)
        {
            throw new InvalidOperationException("SetRegions can only be called before FinalizeConstraints");
        }
        this.regions = regions;
    }

    /// <summary>
    /// Adds a new constraint to the board.
    /// Only call this before any values have been set onto the board.
    /// </summary>
    /// <param name="constraint"></param>
    public void AddConstraint(Constraint constraint)
    {
        constraints.Add(constraint);
        if (constraint.NeedsEnforceConstraint)
        {
            enforceConstraints.Add(constraint);
        }
    }

    /// <summary>
    /// Builds the grouped (cell, mask) form of <see cref="weakLinks"/> that <see cref="SetValue"/>
    /// uses during brute force, collapsing all targets that share a cell into one masked clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safe to compile once and share because weak links are frozen for the duration of a
    /// brute-force search: the only mutator reachable from a search is <c>DiscoverWeakLinks</c>,
    /// which runs at the root before the search loop starts, and <c>StepLogic</c>'s "re-evaluate
    /// weak links" block returns early when <c>isBruteForcing</c>. <see cref="AddWeakLink"/>
    /// invalidates the table anyway, so a stale table cannot outlive a mutation on this instance.
    /// </para>
    /// <para>
    /// The sorted candidate lists are still the authoritative representation — AIC, the fish and
    /// wing searches and <c>IsWeakLink</c> all need candidate-to-candidate adjacency with binary
    /// search and set intersection, which this form cannot answer.
    /// </para>
    /// </remarks>
    internal void CompileGroupedWeakLinks()
    {
        // Already valid: the table is nulled by every mutation, so non-null implies it still matches
        // weakLinks. Without this the estimators rebuilt it once per sample — they run a nested
        // CountSolutions per descent, which cost 400 rebuilds and doubled their allocation.
        if (weakLinks == null || wlGroupedOffsets != null)
        {
            return;
        }

        int numCandidates = weakLinks.Length;
        int[] offsets = new int[numCandidates + 1];

        // Pass 1: count distinct target cells per candidate. weakLinks lists are sorted by candidate
        // index, and candIndex = cell * MAX_VALUE + (v - 1), so entries sharing a cell are adjacent.
        int total = 0;
        for (int c = 0; c < numCandidates; c++)
        {
            offsets[c] = total;
            List<int> targets = weakLinks[c];
            int prevCell = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                int cell = targets[i] / MAX_VALUE;
                if (cell != prevCell)
                {
                    total++;
                    prevCell = cell;
                }
            }
        }
        offsets[numCandidates] = total;

        int[] cells = new int[total];
        uint[] masks = new uint[total];

        // Pass 2: fill, OR-ing every value that targets the same cell into one mask.
        int w = 0;
        for (int c = 0; c < numCandidates; c++)
        {
            List<int> targets = weakLinks[c];
            int prevCell = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                int target = targets[i];
                int cell = target / MAX_VALUE;
                uint valueMask = ValueMask(target - cell * MAX_VALUE + 1);
                if (cell != prevCell)
                {
                    cells[w] = cell;
                    masks[w] = valueMask;
                    w++;
                    prevCell = cell;
                }
                else
                {
                    masks[w - 1] |= valueMask;
                }
            }
        }

        wlGroupedCells = cells;
        wlGroupedMasks = masks;
        wlGroupedOffsets = offsets;

        CompileCellForcingTable();
    }

    /// <summary>
    /// Builds the cell-forcing table: per source cell, the targets that any two or more of its
    /// values weakly link to, each with the mask of exactly which values do.
    /// </summary>
    /// <remarks>
    /// Cell forcing eliminates target X from cell A when every remaining candidate of A is weakly
    /// linked to X, i.e. when <c>cand(A)</c> is a subset of <c>S(A,X)</c>. Storing S per target
    /// turns that from a k-way merge-join of sorted candidate lists into one mask test per row.
    ///
    /// Rows with fewer than two bits are dropped, and the prune is exact rather than heuristic: an
    /// unset cell always has at least two candidates, so such a row could never fire. It happens to
    /// remove every ordinary house link -- A=v is weakly linked to peer=v and nothing else, giving a
    /// one-bit mask -- which is why the table is a small fraction of the full link set.
    /// </remarks>
    internal void CompileCellForcingTable()
    {
        if (weakLinks == null || cfOffsets != null)
        {
            return;
        }

        uint[] scratch = new uint[weakLinks.Length];
        List<int> touched = [];
        int[] offsets = new int[NUM_CELLS + 1];
        List<int> rowTargets = [];
        List<uint> rowMasks = [];

        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            offsets[cellIndex] = rowTargets.Count;
            touched.Clear();

            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint valueMask = ValueMask(v);
                List<int> links = weakLinks[cellIndex * MAX_VALUE + v - 1];
                for (int i = 0; i < links.Count; i++)
                {
                    int target = links[i];
                    if (scratch[target] == 0)
                    {
                        touched.Add(target);
                    }
                    scratch[target] |= valueMask;
                }
            }

            // Sorted so rows sharing a target cell are adjacent: candIndex = cell * MAX_VALUE +
            // (v - 1), so ordering by candidate index groups by cell. That lets the search apply a
            // whole run of eliminations with one masked clear per target cell.
            touched.Sort();

            for (int i = 0; i < touched.Count; i++)
            {
                int target = touched[i];
                uint valuesLinking = scratch[target];
                scratch[target] = 0;
                if (ValueCount(valuesLinking) >= 2)
                {
                    rowTargets.Add(target);
                    rowMasks.Add(valuesLinking);
                }
            }
        }

        offsets[NUM_CELLS] = rowTargets.Count;
        cfTargets = [.. rowTargets];
        cfMasks = [.. rowMasks];
        cfOffsets = offsets;

        BuildCellForcingFilter();
    }

    /// <summary>
    /// Number of values above which the cell-forcing enqueue filter is not tabulated. The table is
    /// 2^MAX_VALUE bits per cell, so it stays negligible at 9 (64 bytes per cell, ~5 KB per puzzle)
    /// and becomes unreasonable well before MAX_VALUE's ceiling of 31.
    /// </summary>
    private const int CellForcingFilterMaxValue = 12;

    /// <summary>
    /// Builds <c>cfCanFire</c>: for each cell, the set of candidate masks that could fire at least
    /// one of its cell-forcing rows.
    /// </summary>
    /// <remarks>
    /// A row <c>(X, S)</c> fires for cell A exactly when <c>cand(A)</c> is a subset of S, so the
    /// masks that can fire *something* are the union of the subset-closures of every row's S. That
    /// is a downward zeta transform: mark each S, then for each value bit propagate a marked mask
    /// to the mask with that bit cleared. It costs <c>2^MAX_VALUE * MAX_VALUE</c> per cell and is
    /// independent of the row count — enumerating each row's subsets instead would be exponential
    /// in the wrong thing, since a 9-bit S has 512 subsets and a cell can have hundreds of rows.
    /// </remarks>
    private void BuildCellForcingFilter()
    {
        // Nothing consults the filters unless cell forcing runs in-search, and building them is
        // ~2^MAX_VALUE * MAX_VALUE per cell per level -- real setup cost to pay for a feature that
        // is off by default.
        if (!CellForcingRunsInSearch || !CellForcingFilterEnabled || MAX_VALUE > CellForcingFilterMaxValue)
        {
            cfCanFire = null;
            cfNewlyFires = null;
            cfCanFireWords = 0;
            return;
        }

        int maskCount = 1 << MAX_VALUE;
        cfCanFireWords = Math.Max(1, maskCount >> 6);
        ulong[] canFire = new ulong[NUM_CELLS * cfCanFireWords];

        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            int block = cellIndex * cfCanFireWords;
            for (int row = cfOffsets[cellIndex]; row < cfOffsets[cellIndex + 1]; row++)
            {
                uint s = cfMasks[row];
                canFire[block + (int)(s >> 6)] |= 1UL << (int)(s & 63);
            }

            for (int bit = 0; bit < MAX_VALUE; bit++)
            {
                uint bitMask = 1u << bit;
                for (uint m = 0; m < maskCount; m++)
                {
                    if ((m & bitMask) == 0)
                    {
                        continue;
                    }
                    if ((canFire[block + (int)(m >> 6)] & (1UL << (int)(m & 63))) != 0)
                    {
                        uint subset = m & ~bitMask;
                        canFire[block + (int)(subset >> 6)] |= 1UL << (int)(subset & 63);
                    }
                }
            }
        }

        cfCanFire = canFire;

        if (CellForcingFilterLevel < 2)
        {
            cfNewlyFires = null;
            return;
        }

        // Same transform, but per removed value and over only the rows that exclude that value.
        ulong[] newlyFires = new ulong[NUM_CELLS * MAX_VALUE * cfCanFireWords];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            int rowStart = cfOffsets[cellIndex];
            int rowEnd = cfOffsets[cellIndex + 1];
            if (rowStart == rowEnd)
            {
                continue;
            }

            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint valueMask = ValueMask(v);
                int block = (cellIndex * MAX_VALUE + v - 1) * cfCanFireWords;

                for (int row = rowStart; row < rowEnd; row++)
                {
                    uint s = cfMasks[row];
                    if ((s & valueMask) != 0)
                    {
                        // S contains v, so this row already fired against the pre-write mask.
                        continue;
                    }
                    newlyFires[block + (int)(s >> 6)] |= 1UL << (int)(s & 63);
                }

                for (int bit = 0; bit < MAX_VALUE; bit++)
                {
                    uint bitMask = 1u << bit;
                    for (uint m = 0; m < maskCount; m++)
                    {
                        if ((m & bitMask) == 0)
                        {
                            continue;
                        }
                        if ((newlyFires[block + (int)(m >> 6)] & (1UL << (int)(m & 63))) != 0)
                        {
                            uint subset = m & ~bitMask;
                            newlyFires[block + (int)(subset >> 6)] |= 1UL << (int)(subset & 63);
                        }
                    }
                }
            }
        }

        cfNewlyFires = newlyFires;
    }

    public LogicResult AddWeakLink(int candIndex0, int candIndex1)
    {
        if (candIndex0 == candIndex1)
        {
            return LogicResult.None;
        }

        // Any mutation invalidates the grouped table; it is rebuilt before the next search.
        wlGroupedOffsets = null;
        cfOffsets = null;
        cfCanFire = null;
        cfNewlyFires = null;
        wlGroupedCells = null;
        wlGroupedMasks = null;

        var (cell0, v0) = CandIndexToCellAndValue(candIndex0);
        var (cell1, v1) = CandIndexToCellAndValue(candIndex1);

        uint cell0Mask = board[cell0];
        uint cell1Mask = board[cell1];

        if (!HasValue(cell0Mask, v0) || !HasValue(cell1Mask, v1))
        {
            return LogicResult.None;
        }

        int cell0Count = ValueCount(cell0Mask);
        int cell1Count = ValueCount(cell1Mask);

        if (cell0Count == 1 && cell1Count == 1)
        {
            return LogicResult.None;
        }

        if (cell0Count == 1)
        {
            // Candidate 0 is already true, so candidate 1 can be set untrue right away
            if (!ClearValue(cell1, v1))
            {
                return LogicResult.Invalid;
            }
            return LogicResult.Changed;
        }

        if (cell1Count == 1)
        {
            // Candidate 1 is already true, so candidate 0 can be set untrue right away
            if (!ClearValue(cell0, v0))
            {
                return LogicResult.Invalid;
            }
            return LogicResult.Changed;
        }

        // Insert into weakLinks[candIndex0]
        var list0 = weakLinks[candIndex0];
        int idx0 = WeakLinkSearch(list0, candIndex1);
        if (idx0 < 0)
        {
            list0.Insert(~idx0, candIndex1);
            totalWeakLinks++;
        }

        // Insert into weakLinks[candIndex1]
        var list1 = weakLinks[candIndex1];
        int idx1 = WeakLinkSearch(list1, candIndex0);
        if (idx1 < 0)
        {
            list1.Insert(~idx1, candIndex0);
            totalWeakLinks++;
        }

        return LogicResult.None;
    }

    // Helper function that says candidate_0 <-> candidate_1
    // This actually adds weak links for all the other candidates in their cells
    public LogicResult AddCloneLink(int candIndex0, int candIndex1)
    {
        if (candIndex0 == candIndex1)
        {
            return LogicResult.None;
        }

        LogicResult result = LogicResult.None;

        var (cell0, value0) = CandIndexToCellAndValue(candIndex1);
        for (int v0 = 1; v0 <= MAX_VALUE; v0++)
        {
            if (v0 != value0)
            {
                int curCandIndex0 = CandidateIndex(cell0, v0);
                LogicResult curResult = AddWeakLink(candIndex1, curCandIndex0);
                if (curResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (curResult == LogicResult.Changed)
                {
                    result = LogicResult.Changed;
                }
            }
        }

        var (cell1, value1) = CandIndexToCellAndValue(candIndex1);
        for (int v1 = 1; v1 <= MAX_VALUE; v1++)
        {
            if (v1 != value1)
            {
                int curCandIndex1 = CandidateIndex(cell1, v1);
                LogicResult curResult = AddWeakLink(candIndex0, curCandIndex1);
                if (curResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (curResult == LogicResult.Changed)
                {
                    result = LogicResult.Changed;
                }
            }
        }

        return result;
    }

    private List<int> SeenCells(int cellIndex)
    {
        List<int> result = null;

        for (int v = 1; v <= MAX_VALUE; v++)
        {
            int candidateIndex = CandidateIndex(cellIndex, v);
            List<int> curWeakLinks = weakLinks[candidateIndex];

            if (result == null)
            {
                // First pass (v == 1): initialize result to all oCell with oValue == 1
                result = new(curWeakLinks.Count);

                foreach (int otherCandidateIndex in curWeakLinks)
                {
                    var (oCell, oValue) = CandIndexToCellAndValue(otherCandidateIndex);
                    if (oValue == v)
                    {
                        result.Add(oCell);
                    }
                }

                if (result.Count == 0)
                {
                    return [];
                }
            }
            else
            {
                // Subsequent passes: do an in-place intersection with cellsSeenThisV (sorted)
                int writeIdx = 0;
                int rIdx = 0;

                int candidatePtr = 0;
                int currentCellSeen = -1;
                bool hasNext = false;

                // advanceCell() lands the next (sorted) oCell where oValue == v into currentCellSeen
                void advanceCell()
                {
                    hasNext = false;
                    while (candidatePtr < curWeakLinks.Count)
                    {
                        int ocand = curWeakLinks[candidatePtr++];
                        var (oCell, oValue) = CandIndexToCellAndValue(ocand);
                        if (oValue == v)
                        {
                            currentCellSeen = oCell;
                            hasNext = true;
                            return;
                        }
                    }
                }

                // prime the first cellSeenThisV
                advanceCell();

                // merge “result” (sorted list of cells) with “cellsSeenThisV” (on-the-fly) 
                while (rIdx < result.Count && hasNext)
                {
                    int rc = result[rIdx];
                    if (rc == currentCellSeen)
                    {
                        // match ⇒ keep it
                        result[writeIdx++] = rc;
                        rIdx++;
                        advanceCell(); // get the next cellSeenThisV
                    }
                    else if (rc < currentCellSeen)
                    {
                        // result[rIdx] is too small ⇒ skip it
                        rIdx++;
                    }
                    else
                    {
                        // currentCellSeen < result[rIdx], so advance in curWeakLinks to catch up
                        advanceCell();
                    }
                }

                // Trim off everything after writeIdx
                if (writeIdx < result.Count)
                {
                    result.RemoveRange(writeIdx, result.Count - writeIdx);
                }

                if (result.Count == 0)
                {
                    return [];
                }
            }
        }

        return result ?? [];
    }

    private void InitSeenMap()
    {
        // Create the seen map
        seenMap = new bool[NUM_CELLS * NUM_CELLS];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            foreach (int seenCellIndex in SeenCells(cellIndex))
            {
                seenMap[cellIndex * NUM_CELLS + seenCellIndex] = true;
            }
        }
    }

    /// <summary>
    /// Call this once after all constraints are set, and before setting any values.
    /// </summary>
    /// <returns>True if the board is still valid. False if the constraints cause there to be trivially no solutions.</returns>
    public bool FinalizeConstraints()
    {
        if (regions == null)
        {
            regions = DefaultRegions(WIDTH);
        }

        InitStandardGroups();

        // Create an initial seen map based on the standard groups only
        InitSeenMap();

        // Derive innie/outie sum constraints from killer cage / house overlaps
        AddInnieCageConstraints();

        int prevNumLinks = totalWeakLinks;

        // Do a single pass on intializing constraints.
        foreach (var constraint in constraints)
        {
            LogicResult result = constraint.InitCandidates(this);
            if (result == LogicResult.Invalid)
            {
                return false;
            }
        }

        // Get the groups from the constraints
        foreach (var constraint in constraints)
        {
            var cells = constraint.Group;
            if (cells != null)
            {
                SudokuGroup group = new(GroupType.Constraint, constraint.SpecificName, cells.Select(CellIndex).ToList(), constraint, Groups.Count);
                Groups.Add(group);
                InitMapForGroup(group);
            }
        }

        // Add any weak links from constraints
        foreach (var constraint in constraints)
        {
            constraint.InitLinks(this, null, true);
        }

        if (prevNumLinks < totalWeakLinks)
        {
            // Re-initialize the seen map based on these updated groups / weak links.
            InitSeenMap();
        }

        // Initialize the constraints again in a loop until there are no more changes
        bool haveChange;
        do
        {
            haveChange = false;
            foreach (var constraint in constraints)
            {
                LogicResult result = constraint.InitCandidates(this);
                if (result == LogicResult.Invalid)
                {
                    return false;
                }

                if (result == LogicResult.Changed)
                {
                    haveChange = true;
                }

                prevNumLinks = totalWeakLinks;
                constraint.InitLinks(this, null, true);
                if (prevNumLinks < totalWeakLinks)
                {
                    InitSeenMap();
                    haveChange = true;
                }
            }
        } while (haveChange);

        smallGroupsBySize = Groups.Where(g => g.Cells.Count < MAX_VALUE).OrderBy(g => g.Cells.Count).ToList();
        if (smallGroupsBySize.Count == 0)
        {
            smallGroupsBySize = null;
        }

        maxValueGroups = Groups.Where(g => g.Cells.Count == MAX_VALUE).ToList();

        // Allocate conflict decay state (shared by reference with all search-tree clones).
        conflictDecayState = new long[1]; // [0] = total increments since epoch

        // Allocate conflict scores seeded with structural priority.
        // Cells in smaller constraint groups are tried first during cold-start search.
        conflictScores = new int[NUM_CELLS];

        // Seed every cell. The worklist's wake-up condition covers cells that *newly* force, which
        // presumes a complete first pass has already established the fixpoint; a cell whose
        // opportunity exists from the start and which never loses a candidate would otherwise never
        // be examined.
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            pendingCellForcing.Add(cellIndex);
        }


        // Uniform baseline so every cell participates in score/count ratio comparison.
        // Keep this close to the arrow benchmark's scoring scale so learned conflicts
        // can affect branch selection early in the search.
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            conflictScores[cellIndex] = MAX_VALUE * 3;

        foreach (var group in Groups)
        {
            int count = group.Cells.Count;
            if (count < MAX_VALUE)
            {
                int priority = MAX_VALUE - count;
                foreach (int cellIdx in group.Cells)
                {
                    conflictScores[cellIdx] += priority;
                }
            }
        }

        // Let constraints contribute their own structural priority (e.g. arrow circle cells).
        // This ensures pure-constraint puzzles with no killer cages also get useful
        // cold-start cell ordering even before any conflict data accumulates.
        foreach (var constraint in constraints)
        {
            constraint.SeedConflictPriority(conflictScores);
        }

        // Build the propagation-queue reverse map: cellToConstraintMask[cell] = the constraint bits
        // to queue when that cell changes, from the cells each constraint declared via
        // CellIndicesForPropagationQueue. Constraints that declare none go into
        // _alwaysRunConstraintBits (run every step).
        //
        // The queue is indexed by *slot*, not by constraint index. Slots are assigned in order of
        // BruteForcePropagationCost, so the drain's ascending bit walk runs the cheapest queued
        // constraint first and an expensive one is skipped entirely whenever a cheaper one ends the
        // step. Ties keep declaration order, so equal costs reproduce the old behavior exactly.
        // Slots also cover only the constraints that actually queue, so a constraint with
        // WantsBruteForcePropagation == false no longer occupies a bit.
        {
            List<int> slotOrder = new(constraints.Count);
            for (int ci = 0; ci < constraints.Count; ci++)
            {
                // Constraints whose StepLogic is a no-op during brute force never need to be queued.
                if (constraints[ci].WantsBruteForcePropagation)
                {
                    slotOrder.Add(ci);
                }
            }
            // Stable ordering on (cost, declaration index): List.Sort is unstable, so the index is
            // part of the key rather than relied upon implicitly.
            slotOrder.Sort((leftIndex, rightIndex) =>
            {
                int costDelta = constraints[leftIndex].BruteForcePropagationCost
                              - constraints[rightIndex].BruteForcePropagationCost;
                return costDelta != 0 ? costDelta : leftIndex - rightIndex;
            });

            _propagationSlotToConstraint = slotOrder.Count > 0 ? slotOrder.ToArray() : Array.Empty<int>();
            int numQueueWords = _propagationSlotToConstraint.Length > 0
                ? BitsetWords(_propagationSlotToConstraint.Length)
                : 0;
            _constraintQueued = numQueueWords > 0 ? new ulong[numQueueWords] : Array.Empty<ulong>();
            cellToConstraintMask = new ulong[NUM_CELLS * numQueueWords];
            ulong[] alwaysRunBits = null;

            for (int slot = 0; slot < _propagationSlotToConstraint.Length; slot++)
            {
                var cells = constraints[_propagationSlotToConstraint[slot]].CellIndicesForPropagationQueue;
                if (cells == null || cells.Count == 0)
                {
                    alwaysRunBits ??= new ulong[numQueueWords];
                    BitsetSet(alwaysRunBits, slot);
                }
                else
                {
                    ulong slotBit = 1UL << (slot & 63);
                    int wordOffset = slot >> 6;
                    foreach (int cell in cells)
                        if ((uint)cell < (uint)NUM_CELLS)
                            cellToConstraintMask[cell * numQueueWords + wordOffset] |= slotBit;
                }
            }
            _alwaysRunConstraintBits = alwaysRunBits;
        }

        // Initialize hidden single tracking array
        if (Groups.Count > 0)
        {
            _candidateCountsPerGroupValue = new int[Groups.Count * MAX_VALUE];
            _checkGroupForHiddens = new ulong[BitsetWords(Groups.Count)];
            for (int groupIdx = 0; groupIdx < Groups.Count; groupIdx++)
            {
                var group = Groups[groupIdx];
                foreach (int v in Enumerable.Range(1, MAX_VALUE))
                {
                    int count = 0;
                    uint valMask = ValueMask(v);
                    foreach (int cell in group.Cells)
                    {
                        if ((board[cell] & valMask) != 0)
                        {
                            count++;
                        }
                    }
                    _candidateCountsPerGroupValue[groupIdx * MAX_VALUE + (v - 1)] = count;
                    // Any one value with nowhere left to go is reason to check the whole group, so
                    // this accumulates across values and never clears — the bitset starts zeroed.
                    if (count <= 1)
                    {
                        BitsetSet(_checkGroupForHiddens, groupIdx);
                    }
                }
            }
        }
        else
        {
            _candidateCountsPerGroupValue = null;
            _checkGroupForHiddens = Array.Empty<ulong>();
        }

        return true;
    }
}
