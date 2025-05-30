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
            SudokuGroup group = new(GroupType.Row, $"Row {i + 1}", cells, null);
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
            SudokuGroup group = new(GroupType.Column, $"Column {j + 1}", cells, null);
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
            SudokuGroup group = new(GroupType.Region, $"Region {region + 1}", cells, null);
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

    private void CleanWeakLinks()
    {
        for (int cand = 0; cand < NUM_CANDIDATES; cand++)
        {
            var links = weakLinks[cand];
            if (links.Count == 0)
            {
                continue;
            }

            int writeIndex = 0;
            for (int readIndex = 0; readIndex < links.Count; readIndex++)
            {
                int otherCand = links[readIndex];
                if (IsCandIndexValid(cand) && IsCandIndexValid(otherCand))
                {
                    if (writeIndex != readIndex)
                    {
                        links[writeIndex] = otherCand;
                    }
                    writeIndex++;
                }
                else
                {
                    totalWeakLinks--; // Decrement for each removed link
                }
            }

            if (writeIndex < links.Count)
            {
                links.RemoveRange(writeIndex, links.Count - writeIndex);
            }
        }
    }

    private void InitSeenMap()
    {
        // Create the seen map
        seenMap = new bool[HEIGHT, WIDTH, HEIGHT, WIDTH, MAX_VALUE + 1];
        for (int i0 = 0; i0 < HEIGHT; i0++)
        {
            for (int j0 = 0; j0 < WIDTH; j0++)
            {
                foreach (var (i1, j1) in SeenCells((i0, j0)))
                {
                    seenMap[i0, j0, i1, j1, 0] = true;
                }
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    uint mask = ValueMask(v);
                    foreach (var (i1, j1) in SeenCellsByValueMask(mask, (i0, j0)))
                    {
                        seenMap[i0, j0, i1, j1, v] = true;
                    }
                }
            }
        }

        // Add the weak links
        for (int i0 = 0; i0 < HEIGHT; i0++)
        {
            for (int j0 = 0; j0 < WIDTH; j0++)
            {
                int cellIndex = i0 * WIDTH + j0;
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    uint mask = ValueMask(v);
                    int candIndex0 = cellIndex * MAX_VALUE + v - 1;

                    // Add weak links to all seen cells
                    foreach (var (i1, j1) in SeenCellsByValueMask(mask, (i0, j0)))
                    {
                        int candIndex1 = CandidateIndex((i1, j1), v);
                        AddWeakLink(candIndex0, candIndex1);
                    }

                    // Add weak links to all other candidates within the same cell
                    for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                    {
                        if (v != v1)
                        {
                            int candIndex1 = CandidateIndex((i0, j0), v1);
                            AddWeakLink(candIndex0, candIndex1);
                        }
                    }
                }
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
        bool addedGroup = false;
        foreach (var constraint in constraints)
        {
            var cells = constraint.Group;
            if (cells != null)
            {
                SudokuGroup group = new(GroupType.Constraint, constraint.SpecificName, cells.Select(CellIndex).ToList(), constraint);
                Groups.Add(group);
                InitMapForGroup(group);
                addedGroup = true;
            }
        }

        // Re-create the seen map if any new groups were added
        if (addedGroup)
        {
            InitSeenMap();
        }

        // Add any weak links from constraints
        foreach (var constraint in constraints)
        {
            constraint.InitLinks(this, null, true);
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

                int prevNumLinks = totalWeakLinks;
                constraint.InitLinks(this, null, true);
                if (prevNumLinks < totalWeakLinks)
                {
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

        // Clean up any weak links that cannot occur
        CleanWeakLinks();

        return true;
    }
}
