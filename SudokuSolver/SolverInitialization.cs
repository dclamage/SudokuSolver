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

        isInvalid = false;
        unsetCellsCount = NUM_CELLS;
        pendingNakedSingles = [];

        // Hidden single tracking
        _candidateCountsPerGroupValue = null;
        _checkGroupForHiddens = null;

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
        board = new uint[NUM_CELLS];
        other.board.AsSpan().CopyTo(board);
        regions = other.regions;
        candidateToCellAndValueLookup = other.candidateToCellAndValueLookup;
        candidateToCoordValueLookup = other.candidateToCoordValueLookup;
        seenMap = other.seenMap;
        constraints = other.constraints;
        enforceConstraints = other.enforceConstraints;
        isInvalid = other.isInvalid;
        unsetCellsCount = other.unsetCellsCount;
        pendingNakedSingles = [.. other.pendingNakedSingles];

        // Hidden single tracking
        if (other._candidateCountsPerGroupValue != null)
        {
            _candidateCountsPerGroupValue = new int[other._candidateCountsPerGroupValue.Length];
            other._candidateCountsPerGroupValue.AsSpan().CopyTo(_candidateCountsPerGroupValue);

            _checkGroupForHiddens = new bool[other._checkGroupForHiddens.Length];
            other._checkGroupForHiddens.AsSpan().CopyTo(_checkGroupForHiddens);
        }
        else
        {
            _candidateCountsPerGroupValue = null;
            _checkGroupForHiddens = null;
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

    public LogicResult AddWeakLink(int candIndex0, int candIndex1)
    {
        if (candIndex0 == candIndex1)
        {
            return LogicResult.None;
        }

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
        int idx0 = list0.BinarySearch(candIndex1);
        if (idx0 < 0)
        {
            list0.Insert(~idx0, candIndex1);
            totalWeakLinks++;
        }

        // Insert into weakLinks[candIndex1]
        var list1 = weakLinks[candIndex1];
        int idx1 = list1.BinarySearch(candIndex0);
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

        // Initialize hidden single tracking array
        if (Groups.Count > 0)
        {
            _candidateCountsPerGroupValue = new int[Groups.Count * MAX_VALUE];
            _checkGroupForHiddens = new bool[Groups.Count];
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
                    _checkGroupForHiddens[groupIdx] = count <= 1;
                }
            }
        }
        else
        {
            _candidateCountsPerGroupValue = null;
            _checkGroupForHiddens = null;
        }

        return true;
    }
}
