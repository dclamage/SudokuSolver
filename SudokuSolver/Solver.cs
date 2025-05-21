//#define PROFILING
//#define INFINITE_LOOP_CHECK

using System;
using System.Buffers;
using System.Numerics;

namespace SudokuSolver;

public class Solver
{
#if PROFILING
    public static readonly Dictionary<string, Stopwatch> timers = new();
    public static void PrintTimers()
    {
        foreach (var timer in timers.OrderByDescending(timer => timer.Value.Elapsed))
        {
            //if (timer.Key != "Global")
            {
                Console.WriteLine($"{timer.Key}: {timer.Value.Elapsed.TotalMilliseconds}ms");
            }
        }
    }
#endif

    public readonly int WIDTH;
    public readonly int HEIGHT;
    public readonly int MAX_VALUE;
    public readonly uint ALL_VALUES_MASK;
    public readonly int NUM_CELLS;
    public readonly int NUM_CANDIDATES;
    public readonly int[][][] combinations;

    public string Title { get; init; }
    public string Author { get; init; }
    public string Rules { get; init; }
    public bool DisableTuples { get; set; } = false;
    public bool DisablePointing { get; set; } = false;
    public bool DisableFishes { get; set; } = false;
    public bool DisableWings { get; set; } = false;
    public bool DisableAIC { get; set; } = false;
    public bool DisableContradictions { get; set; } = false;
    public bool DisableFindShortestContradiction { get; set; } = false;
    public uint DisabledLogicFlags
    {
        get
        {
            uint disabledLogicFlags = 0;
            if (DisableTuples)
            {
                disabledLogicFlags |= (1u << 0);
            }
            if (DisablePointing)
            {
                disabledLogicFlags |= (1u << 1);
            }
            if (DisableFishes)
            {
                disabledLogicFlags |= (1u << 2);
            }
            if (DisableWings)
            {
                disabledLogicFlags |= (1u << 3);
            }
            if (DisableAIC)
            {
                disabledLogicFlags |= (1u << 4);
            }
            if (DisableContradictions)
            {
                disabledLogicFlags |= (1u << 5);
            }
            return disabledLogicFlags;
        }
    }
    public void SetToBasicsOnly()
    {
        DisableTuples = false;
        DisablePointing = false;
        DisableFishes = true;
        DisableWings = true;
        DisableAIC = true;
        DisableContradictions = true;
    }

    private uint[] board;
    private int[,] regions = null;
    private (int, int)[] candidateToCellAndValueLookup;
    private (int, int, int)[] candidateToCoordValueLookup;
    private List<int>[] weakLinks;
    private int totalWeakLinks = 0;
    private List<int>[] CloneWeakLinks()
    {
        List<int>[] newWeakLinks = new List<int>[NUM_CANDIDATES];
        for (int ci = 0; ci < NUM_CANDIDATES; ci++)
        {
            newWeakLinks[ci] = new(weakLinks[ci]);
        }
        return newWeakLinks;
    }

    // Store "memos" (for memoization)
    // This is useful for remembering things that are specific to board state which won't
    // change as the puzzle is solved.
    // The memos are shallow copied when the solver is cloned, so keys should be descriptive enough
    // about the board state such that the input guarantees a specific output.
    private readonly Dictionary<string, object> memos;
    private readonly object memosLock;

    // Interface for storing/retrieving memos
    public T GetMemo<T>(string key) where T : class
    {
        lock (memosLock)
        {
            return memos.TryGetValue(key, out var val) ? val as T : null;
        }
    }

    public void StoreMemo(string key, object val)
    {
        lock (memosLock)
        {
            memos[key] = val;
        }
    }

    public BoardView Board => new(board, WIDTH, HEIGHT);
    public uint[] BoardClone
    {
        get
        {
            uint[] boardClone = new uint[board.Length];
            board.AsSpan().CopyTo(boardClone);
            return boardClone;
        }
    }

    public int[,] Regions => regions;
    public List<int>[] WeakLinks => weakLinks;
    public Dictionary<string, object> customInfo;
    // Returns whether two cells cannot be the same value for a specific value
    // i0, j0, i1, j0, value or 0 for any value
    private bool[,,,,] seenMap;
    private bool isInSetValue = false;
    public IReadOnlyList<uint> FlatBoard => board;

    public uint this[int row, int col]
    {
        get
        {
            if ((uint)row >= (uint)HEIGHT || (uint)col >= (uint)WIDTH)
                throw new IndexOutOfRangeException();
            return board[row * WIDTH + col];
        }
        private set
        {
            if ((uint)row >= (uint)HEIGHT || (uint)col >= (uint)WIDTH)
                throw new IndexOutOfRangeException();
            board[row * WIDTH + col] = value;
        }
    }

    public string GivenString
    {
        get
        {
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            string nonGiven = new('0', digitWidth);
            StringBuilder stringBuilder = new(board.Length);
            foreach (uint mask in board)
            {
                if (IsValueSet(mask))
                {
                    int v = GetValue(mask);
                    if (digitWidth == 2 && v <= 9)
                    {
                        stringBuilder.Append('0');
                    }
                    stringBuilder.Append(v);
                }
                else
                {
                    stringBuilder.Append(nonGiven);
                }
            }
            return stringBuilder.ToString();
        }
    }

    public string CandidateString
    {
        get
        {
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            StringBuilder stringBuilder = new(board.Length * digitWidth);
            foreach (uint mask in board)
            {
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if ((mask & ValueMask(v)) != 0)
                    {
                        if (digitWidth == 2 && v <= 9)
                        {
                            stringBuilder.Append('0');
                        }
                        stringBuilder.Append(v);
                    }
                    else
                    {
                        stringBuilder.Append('.', digitWidth);
                    }
                }
            }
            return stringBuilder.ToString();
        }
    }

    public string DistinguishedCandidateString
    {
        get
        {
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            StringBuilder stringBuilder = new(board.Length * digitWidth);
            foreach (uint mask in board)
            {
                if (IsValueSet(mask))
                {
                    int setValue = GetValue(mask);
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (digitWidth == 2 && setValue <= 9)
                        {
                            stringBuilder.Append('0');
                        }
                        stringBuilder.Append(setValue);
                    }
                }
                else
                {
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if ((mask & ValueMask(v)) != 0)
                        {
                            if (digitWidth == 2 && v <= 9)
                            {
                                stringBuilder.Append('0');
                            }
                            stringBuilder.Append(v);
                        }
                        else
                        {
                            stringBuilder.Append('.', digitWidth);
                        }
                    }
                }
            }
            return stringBuilder.ToString();
        }
    }

    public string OutputString => IsComplete ? GivenString : CandidateString;

    private readonly List<Constraint> constraints;

    /// <summary>
    /// Groups which cannot contain more than one of the same digit.
    /// This will at least contain all rows, columns, and boxes.
    /// Will also contain any groups from constraints (such as killer cages).
    /// </summary>
    public List<SudokuGroup> Groups { get; }
    private List<SudokuGroup> smallGroupsBySize = null;

    /// <summary>
    /// Maps a cell to the list of groups which contain that cell.
    /// </summary>
    public Dictionary<int, List<SudokuGroup>> CellToGroupMap { get; }

    /// <summary>
    /// Determines if the board has all values set.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                if (!IsValueSet(cellIndex))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public IEnumerable<T> Constraints<T>() where T : Constraint => constraints.Select(c => c as T).Where(c => c != null);

    private bool isBruteForcing = false;

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
        memos = new();
        memosLock = new();
        InitCombinations();

        board = new uint[NUM_CELLS];
        board.AsSpan().Fill(ALL_VALUES_MASK);

        constraints = new();

        Groups = new();
        CellToGroupMap = new();

        weakLinks = new List<int>[NUM_CANDIDATES];
        for (int ci = 0; ci < NUM_CANDIDATES; ci++)
        {
            weakLinks[ci] = new();
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

        customInfo = new();
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
        Groups = other.Groups;
        smallGroupsBySize = other.smallGroupsBySize;
        CellToGroupMap = other.CellToGroupMap;
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

        // Memos are also shallow copies.
        // The "key" for memos need to be descriptive enough such that their input has a guaranteed output.
        // Key/data in memos should never be dependent on the current board state.
        // If board state is an input, it should be encoded into the key.
        // It is ok for memos to assume that the board size and all constraints are the same.
        memos = other.memos;
        memosLock = other.memosLock;
    }

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
                    if (regions[i, j] == region)
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
        foreach (var pair in group.Cells)
        {
            if (CellToGroupMap.TryGetValue(pair, out var value))
            {
                value.Add(group);
            }
            else
            {
                // Reserve 3 entries: row, col, and box
                CellToGroupMap[pair] = new(3) { group };
            }
        }
    }

    /// <summary>
    /// Set custom regions for the board
    /// Each region is indexed starting with 0
    /// </summary>
    /// <param name="regions"></param>
    public void SetRegions(int[,] regions)
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
#if PROFILING
        string constraintName = constraint.GetType().FullName;
        if (!timers.ContainsKey(constraintName))
        {
            timers[constraintName] = new();
        }
#endif
        constraints.Add(constraint);
    }

    public void AddWeakLink(int candIndex0, int candIndex1)
    {
        if (candIndex0 == candIndex1)
        {
            return;
        }

        var (cell0, v0) = CandIndexToCellAndValue(candIndex0);
        var (cell1, v1) = CandIndexToCellAndValue(candIndex1);

        if (!HasValue(board[cell0], v0) || !HasValue(board[cell1], v1))
        {
            return;
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
#if PROFILING
        if (!timers.ContainsKey("Global"))
        {
            timers["Global"] = Stopwatch.StartNew();

            timers["FindNakedSingles"] = new();
            timers["FindHiddenSingle"] = new();
            timers["AddNewWeakLinks"] = new();
            timers["FindDirectCellForcing"] = new();
            timers["FindNakedTuples"] = new();
            timers["FindPointingTuples"] = new();
            timers["FindFishes"] = new();
            timers["FindWings"] = new();
            timers["FindAIC"] = new();
            timers["FindSimpleContradictions"] = new();
        }
#endif
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
            constraint.InitLinks(this, null);
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
                constraint.InitLinks(this, null);
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
        return true;
    }

    /// <summary>
    /// Creates a copy of the board, including all constraints, set values, and candidates.
    /// </summary>
    /// <returns></returns>
    public Solver Clone(bool willRunNonSinglesLogic) => new(this, willRunNonSinglesLogic);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetValue((int, int) cell)
    {
        return SolverUtility.GetValue(board[CellIndex(cell.Item1, cell.Item2)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetValue(uint mask)
    {
        return SolverUtility.GetValue(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValueSet(int cellIndex)
    {
        return SolverUtility.IsValueSet(board[cellIndex]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValueSet(int i, int j) => IsValueSet(CellIndex(i, j));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValueSet(uint mask)
    {
        return SolverUtility.IsValueSet(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskStrictlyHigher(int v) => ALL_VALUES_MASK & ~((1u << v) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskValAndHigher(int v) => ALL_VALUES_MASK & ~((1u << (v - 1)) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskBetweenInclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskStrictlyLower(v0) | MaskStrictlyHigher(v1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskBetweenExclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskValAndLower(v0) | MaskValAndHigher(v1));

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
            if (!CellToGroupMap.TryGetValue(CellIndex(cell), out var groupList) || groupList.Count == 0)
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
            if (!CellToGroupMap.TryGetValue(CellIndex(cell), out var groupList) || groupList.Count == 0)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int cellIndex, int v)
    {
        board[cellIndex] &= ~ValueMask(v);
        return (board[cellIndex] & ~valueSetMask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ClearValue(int i, int j, int v)
    {
        int cellIndex = CellIndex(i, j);
        return ClearValue(cellIndex, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ClearCandidate(int candidate)
    {
        var (cellIndex, v) = CandIndexToCellAndValue(candidate);
        return ClearValue(cellIndex, v);
    }

    internal bool ClearCandidates(IEnumerable<int> candidates)
    {
        bool valid = true;
        foreach (int c in candidates)
        {
            if (!ClearCandidate(c))
            {
                valid = false;
            }
        }
        return valid;
    }

    public bool SetValue(int i, int j, int val) => SetValue(CellIndex(i, j), val);

    public bool SetValue(int cellIndex, int val)
    {
        uint valMask = ValueMask(val);
        if ((board[cellIndex] & valMask) == 0)
        {
            return false;
        }

        // Check if already set
        if ((board[cellIndex] & valueSetMask) != 0)
        {
            return true;
        }

        if (isInSetValue)
        {
            board[cellIndex] = valMask;
            return true;
        }

        isInSetValue = true;

        board[cellIndex] = valueSetMask | valMask;

        // Apply all weak links
        int setCandidateIndex = CandidateIndex(cellIndex, val);
        var curWeakLinks = weakLinks[setCandidateIndex];
        int curWeakLinksCount = curWeakLinks.Count;
        for (int curWeakLinkIndex = 0; curWeakLinkIndex < curWeakLinksCount; curWeakLinkIndex++)
        {
            int elimCandIndex = curWeakLinks[curWeakLinkIndex];
            var (cellIndex1, v1) = CandIndexToCellAndValue(elimCandIndex);
            if (!ClearValue(cellIndex1, v1))
            {
                return false;
            }
        }

        // Enforce all constraints
        if (constraints.Count > 0)
        {
            var (i, j) = CellIndexToCoord(cellIndex);
            foreach (var constraint in constraints)
            {
                if (!constraint.EnforceConstraint(this, i, j, val))
                {
                    return false;
                }
            }
        }

        isInSetValue = false;

        return true;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, uint mask)
    {
        if ((mask & ~valueSetMask) == 0)
        {
            return false;
        }

        this[i, j] = mask;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, params int[] values)
    {
        uint mask = 0;
        foreach (int v in values)
        {
            mask |= ValueMask(v);
        }
        return SetMask(i, j, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetMask(int i, int j, IEnumerable<int> values)
    {
        return SetMask(i, j, values.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult KeepMask(int i, int j, uint mask)
    {
        mask &= ALL_VALUES_MASK;
        if (mask == ALL_VALUES_MASK)
        {
            return LogicResult.None;
        }

        isInSetValue = true;

        LogicResult result = LogicResult.None;
        uint curMask = this[i, j] & ~valueSetMask;
        uint newMask = curMask & mask;
        if (newMask != curMask)
        {
            result = SetMask(i, j, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        isInSetValue = false;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogicResult ClearMask(int i, int j, uint mask)
    {
        mask &= ALL_VALUES_MASK;
        if (mask == 0)
        {
            return LogicResult.None;
        }
        isInSetValue = true;

        LogicResult result = LogicResult.None;
        uint curMask = this[i, j];
        uint newMask = curMask & ~mask;
        if (newMask != curMask)
        {
            result = SetMask(i, j, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        isInSetValue = false;
        return result;
    }

    private (int, int) GetLeastCandidateCell(bool[] ignoreCell = null)
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
                    if (!IsValueSet(cellMask) && (ignoreCell == null || !ignoreCell[cellIndex]))
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

        if (ignoreCell == null)
        {
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
        }
        else
        {
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint cellMask = board[cellIndex];
                if (!IsValueSet(cellMask) && !ignoreCell[cellIndex])
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
        }

        if (numCandidates > 3)
        {
            var (bCellIndex, bVal) = FindBilocalValue();
            if (bVal > 0)
            {
                return (bCellIndex, bVal);
            }
        }

        return (bestCellIndex, 0);
    }

    private int CellPriority(int cellIndex)
    {
        if (IsValueSet(cellIndex))
        {
            return -1;
        }

        int numCandidates = ValueCount(board[cellIndex]);
        int invNumCandidates = MAX_VALUE - numCandidates + 1;
        int priority = invNumCandidates;
        if (CellToGroupMap.TryGetValue(cellIndex, out var groups))
        {
            try
            {
                int smallestGroupSize = groups.Select(g => g.Cells.Count).Where(c => c > 1).Min();
                int groupPriority = MAX_VALUE - smallestGroupSize + 1;
                priority += MAX_VALUE * groupPriority;
            }
            catch (Exception) { }
        }

        // Within the same priority level, sort by cell order
        priority = priority * NUM_CELLS + (NUM_CELLS - cellIndex - 1);
        return priority;
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
    /// Finds a single solution to the board. This may not be the only solution.
    /// For the exact same board inputs, the solution will always be the same.
    /// The board itself is modified to have the solution as its board values.
    /// If no solution is found, the board is left in an invalid state.
    /// </summary>
    /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
    /// <returns>True if a solution is found, otherwise false.</returns>
    public bool FindSolution(bool multiThread = false, CancellationToken cancellationToken = default, bool isRandom = false)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        Solver solver = Clone(willRunNonSinglesLogic: false);
        solver.isBruteForcing = true;

        using FindSolutionState state = new(isRandom, multiThread, cancellationToken);
        if (multiThread)
        {
            if (!state.PushSolver(solver, true))
            {
                throw new Exception("Initial PushSolver failed!");
            }
            state.Wait();
        }
        else
        {
            FindSolutionInternal(solver, state);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        if (state.result != null)
        {
            board = state.result;
        }
        return state.result != null;
    }

    private class FindSolutionState : IDisposable
    {
        public CountdownEvent countdownEvent = new(1);
        public uint[] result = null;
        public CancellationToken cancellationToken;
        public bool isRandom = false;
        public bool isMultiThreaded = false;

        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;

        public FindSolutionState(bool isRandom, bool isMultiThreaded, CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            this.isRandom = isRandom;
            this.isMultiThreaded = isMultiThreaded;

            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
        }

        public bool PushSolver(Solver solver, bool isInitialCall = false)
        {
            int newCount = Interlocked.Increment(ref numRunningTasks);
            if (isInitialCall || newCount <= maxRunningTasks)
            {
                // only schedule if we stayed within the limit
                if (!isInitialCall)
                {
                    // countdownEvent cannot start at 0 or it starts already signaled, so the first call to PushSolver
                    // does not increment, since it starts at 1
                    countdownEvent.AddCount();
                }
                Task.Run(() => {
                    try
                    {
                        FindSolutionInternal(solver, this);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref numRunningTasks);
                        countdownEvent.Signal();
                    }
                });
                return true;
            }

            // we overshot: roll back and decline
            Interlocked.Decrement(ref numRunningTasks);
            return false;
        }

        public void Dispose()
        {
            ((IDisposable)countdownEvent).Dispose();
        }

        public void ReportSolution(Solver solver)
        {
            Interlocked.CompareExchange(ref result, solver.board, null);
        }

        public void Wait()
        {
            countdownEvent.Wait(cancellationToken);
        }
    }

    private static void FindSolutionInternal(Solver root, FindSolutionState state)
    {
        var stack = new Stack<Solver>();
        stack.Push(root);

        while (state.result is null && stack.TryPop(out var solver))
        {
            state.cancellationToken.ThrowIfCancellationRequested();
            if (state.result != null)
            {
                continue;
            }

            var logicResult = solver.ConsolidateBoard();
            if (logicResult == LogicResult.PuzzleComplete)
            {
                state.ReportSolution(solver);
                continue;
            }

            if (logicResult == LogicResult.Invalid)
            {
                continue;
            }

            (int cellIndex, int v) = solver.GetLeastCandidateCell();
            if (cellIndex < 0)
            {
                state.ReportSolution(solver);
                continue;
            }

            // Try a possible value for this cell
            int val = v != 0 ? v : state.isRandom ? GetRandomValue(solver.board[cellIndex]) : MinValue(solver.board[cellIndex]);
            uint valMask = ValueMask(val);

            // Create a backup board in case it needs to be restored
            Solver newSolver = solver.Clone(willRunNonSinglesLogic: false);
            newSolver.isBruteForcing = true;
            newSolver.board[cellIndex] &= ~valMask;
            if (newSolver.board[cellIndex] != 0)
            {
                if (!state.isMultiThreaded || !state.PushSolver(newSolver))
                {
                    stack.Push(newSolver);
                }
            }

            // Change the board to only allow this value in the slot
            if (solver.SetValue(cellIndex, val))
            {
                stack.Push(solver);
            }
        }
    }

    /// <summary>
    /// Determine how many solutions the board has.
    /// </summary>
    /// <param name="maxSolutions">The maximum number of solutions to find. Pass 0 for no maximum.</param>
    /// <param name="multiThread">Whether to use multiple threads.</param>
    /// <param name="progressEvent">An event to receive the progress count as solutions are found.</param>
    /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
    /// <returns>The solution count found.</returns>
    public ulong CountSolutions(ulong maxSolutions = 0, bool multiThread = false, Action<ulong> progressEvent = null, Action<Solver> solutionEvent = null, HashSet<string> skipSolutions = null, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        using CountSolutionsState state = new(maxSolutions, multiThread, progressEvent, solutionEvent, skipSolutions, cancellationToken);
        try
        {
            Solver boardCopy = Clone(willRunNonSinglesLogic: false);
            boardCopy.isBruteForcing = true;
            if (state.multiThread)
            {
                if (!state.PushSolver(boardCopy, isInitialCall: true))
                {
                    throw new Exception("Initial PushSolver failed!");
                }
                state.Wait();
            }
            else
            {
                CountSolutionsInternal(boardCopy, state);
            }
        }
        catch (OperationCanceledException) { }

        if (maxSolutions > 0 && state.numSolutions > maxSolutions)
        {
            return maxSolutions;
        }
        return state.numSolutions;
    }

    private class CountSolutionsState : IDisposable
    {
        public ulong numSolutions = 0;
        public readonly bool multiThread;
        public readonly ulong maxSolutions;
        public readonly Action<ulong> progressEvent;
        public readonly Action<Solver> solutionEvent;
        public readonly HashSet<string> skipSolutions;
        public readonly CancellationToken cancellationToken;
        public readonly CountdownEvent countdownEvent;

        private readonly object solutionLock = new();
        private readonly Stopwatch eventTimer;

        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;
        private readonly bool fastIncrement;
        private bool maxSolutionsReached;

        public CountSolutionsState(ulong maxSolutions, bool multiThread, Action<ulong> progressEvent, Action<Solver> solutionEvent, HashSet<string> skipSolutions, CancellationToken cancellationToken)
        {
            this.maxSolutions = maxSolutions;
            this.multiThread = multiThread;
            this.progressEvent = progressEvent;
            this.solutionEvent = solutionEvent;
            this.skipSolutions = skipSolutions;
            this.cancellationToken = cancellationToken;
            eventTimer = Stopwatch.StartNew();
            countdownEvent = multiThread ? new CountdownEvent(1) : null;
            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
            fastIncrement = skipSolutions == null && solutionEvent == null;
            maxSolutionsReached = false;
        }

        public bool MaxSolutionsReached => maxSolutionsReached;

        public void IncrementSolutions(Solver solver)
        {
            bool invokeProgress = false;
            if (fastIncrement)
            {
                ulong newNumSolutions = Interlocked.Increment(ref numSolutions);
                if (maxSolutions > 0 && newNumSolutions >= maxSolutions)
                {
                    maxSolutionsReached = true;
                    return;
                }

                if (eventTimer.ElapsedMilliseconds > 500)
                {
                    lock (solutionLock)
                    {
                        if (eventTimer.ElapsedMilliseconds > 500)
                        {
                            invokeProgress = true;
                            eventTimer.Restart();
                        }
                    }
                }
            }
            else
            {
                if (skipSolutions != null && skipSolutions.Contains(solver.GivenString))
                {
                    return;
                }

                ulong newNumSolutions = Interlocked.Increment(ref numSolutions);
                if (maxSolutions > 0 && newNumSolutions >= maxSolutions)
                {
                    maxSolutionsReached = true;
                    return;
                }

                if (solutionEvent != null || eventTimer.ElapsedMilliseconds > 500)
                {
                    lock (solutionLock)
                    {
                        solutionEvent?.Invoke(solver);
                        if (eventTimer.ElapsedMilliseconds > 500)
                        {
                            invokeProgress = true;
                            eventTimer.Restart();
                        }
                    }
                }
            }
            if (invokeProgress)
            {
                progressEvent?.Invoke(numSolutions);
            }
        }

        public bool PushSolver(Solver solver, bool isInitialCall = false)
        {
            int newNumRunningTasks = Interlocked.Increment(ref numRunningTasks);
            if (isInitialCall || newNumRunningTasks <= maxRunningTasks)
            {
                if (!isInitialCall)
                {
                    // countdownEvent cannot start at 0 or it starts already signaled, so the first call to PushSolver
                    // does not increment, since it starts at 1
                    countdownEvent.AddCount();
                }
                Task.Run(() => {
                    try
                    {
                        CountSolutionsInternal(solver, this);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref numRunningTasks);
                        countdownEvent.Signal();
                    }
                });
                return true;
            }
            else
            {
                Interlocked.Decrement(ref numRunningTasks);
            }

            return false;
        }

        public void Wait()
        {
            countdownEvent.Wait(cancellationToken);
        }

        public void Dispose()
        {
            ((IDisposable)countdownEvent)?.Dispose();
        }
    }

    private static void CountSolutionsInternal(Solver root, CountSolutionsState state)
    {
        bool isMultithreaded = state.multiThread;

        var stack = new Stack<Solver>();
        stack.Push(root);

        while (stack.TryPop(out var solver) && !state.MaxSolutionsReached)
        {
            state.cancellationToken.ThrowIfCancellationRequested();

            var logicResult = solver.ConsolidateBoard();
            if (logicResult == LogicResult.PuzzleComplete)
            {
                state.IncrementSolutions(solver);
                continue;
            }

            if (logicResult == LogicResult.Invalid)
            {
                continue;
            }

            // Start with the cell that has the least possible candidates
            (int cellIndex, int v) = solver.GetLeastCandidateCell();
            if (cellIndex < 0)
            {
                state.IncrementSolutions(solver);
                continue;
            }

            // Try a possible value for this cell
            int val = v != 0 ? v : state.skipSolutions != null ? GetRandomValue(solver.board[cellIndex]) : MinValue(solver.board[cellIndex]);
            uint valMask = ValueMask(val);

            // Create a solver without this value and start a task for it
            Solver newSolver = solver.Clone(willRunNonSinglesLogic: false);
            newSolver.isBruteForcing = true;
            newSolver.board[cellIndex] &= ~valMask;
            if (newSolver.board[cellIndex] != 0)
            {
                if (!isMultithreaded || !state.PushSolver(newSolver))
                {
                    stack.Push(newSolver);
                }
            }

            if (solver.SetValue(cellIndex, val))
            {
                stack.Push(solver);
            }
        }
    }

    private class FillRealCandidatesState
    {
        public readonly uint[] fixedBoard;
        public readonly bool[] candidatesFixed;
        public readonly int[] tasksRemainingPerCell;
        public readonly int[] numSolutions;
        public readonly HashSet<string> solutionsCounted;

        public readonly Action<uint[]> progressEvent;
        public readonly Stopwatch eventTimer = Stopwatch.StartNew();

        public readonly CancellationToken cancellationToken;
        public readonly bool multiThread;
        public bool boardInvalid = false;

        public FillRealCandidatesState(bool multiThread, int numCells, Action<uint[]> progressEvent, int[] numSolutions, CancellationToken cancellationToken)
        {
            fixedBoard = new uint[numCells];
            candidatesFixed = new bool[numCells];
            tasksRemainingPerCell = new int[numCells];
            this.progressEvent = progressEvent;
            this.cancellationToken = cancellationToken;
            this.multiThread = multiThread;
            this.numSolutions = numSolutions;
            solutionsCounted = (numSolutions != null) ? new() : null;
        }

        public void CheckProgressEvent()
        {
            if (progressEvent == null)
            {
                return;
            }

            bool doProgressEvent = false;
            if (eventTimer.ElapsedMilliseconds > 2000)
            {
                doProgressEvent = true;
                eventTimer.Restart();
            }
            if (doProgressEvent)
            {
                progressEvent(fixedBoard);
            }
        }
    }

    /// <summary>
    /// Remove any candidates which do not lead to an actual solution to the board.
    /// </summary>
    /// <param name="multiThread">If true, uses multiple threads to calculate.</param>
    /// <param name="skipConsolidate">If true, the initial consolidate board is skipped.</param>
    /// <param name="progressEvent">Recieve progress notifications. Sends the true candidates currently found.</param>
    /// <param name="numSolutions">Expected to be HEIGHT * WIDTH * MAX_VALUE in size. The number of solutions per candidate, capped to 9.</param>
    /// <param name="cancellationToken">Pass in to support cancelling.</param>
    /// <returns>True if there are solutions and candidates are filled. False if there are no solutions.</returns>
    public bool FillRealCandidates(bool multiThread = false, Action<uint[]> progressEvent = null, int[] numSolutions = null, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        if (numSolutions != null && numSolutions.Length != HEIGHT * WIDTH * MAX_VALUE)
        {
            throw new InvalidOperationException($"numSolutions was incorrect size. Expected {HEIGHT * WIDTH * MAX_VALUE} got {numSolutions.Length}");
        }

        Stopwatch timeSinceCheck = Stopwatch.StartNew();

        LogicResult logicResult = PrepForBruteForce();
        if (logicResult == LogicResult.Invalid)
        {
            return false;
        }

        isBruteForcing = true;
        FillRealCandidatesState state = new(multiThread, NUM_CELLS, progressEvent, numSolutions, cancellationToken);
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint cellMask = board[cellIndex];
            if (IsValueSet(cellMask))
            {
                state.fixedBoard[cellIndex] = cellMask;
                state.candidatesFixed[cellIndex] = true;
            }
        }

        int numUnsetCells = logicResult == LogicResult.PuzzleComplete ? 0 : NUM_CELLS - NumSetValues;
        List<(int, int, int)> cellValuesByPriority = null;
        if (numUnsetCells > 0)
        {
            cellValuesByPriority = new(numUnsetCells);
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                int cellPriority = CellPriority(cellIndex);
                if (cellPriority < 0)
                {
                    continue;
                }

                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    // Don't bother trying the value if it's not a possibility
                    uint valMask = ValueMask(v);
                    if ((board[cellIndex] & valMask) == 0)
                    {
                        continue;
                    }

                    state.tasksRemainingPerCell[cellIndex]++;
                    cellValuesByPriority.Add((cellPriority, cellIndex, v));
                }
            }
        }

        if (cellValuesByPriority == null || cellValuesByPriority.Count == 0)
        {
            if (numSolutions != null)
            {
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        uint mask = Board[i, j];
                        int value = GetValue(mask);
                        numSolutions[(i * WIDTH + j) * MAX_VALUE + (value - 1)] = 1;
                    }
                }
            }
        }
        else
        {
            cellValuesByPriority.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            try
            {
                foreach (var (p, cellIndex, v) in cellValuesByPriority)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FillRealCandidateAction(cellIndex, v, state);
                    if (state.boardInvalid)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                state.boardInvalid = true;
            }

            if (state.boardInvalid)
            {
                isBruteForcing = false;
                return false;
            }
        }

        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint cellMask = board[cellIndex];
            if (IsValueSet(cellMask))
            {
                state.fixedBoard[cellIndex] = cellMask;
            }
        }

        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = state.fixedBoard[i * WIDTH + j];
                if (!IsValueSet(mask) && ValueCount(mask) == 1)
                {
                    SetValue(i, j, GetValue(mask));
                }
                else
                {
                    SetMask(i, j, state.fixedBoard[i * WIDTH + j]);
                }
            }
        }
        isBruteForcing = false;
        return true;
    }

    private void FillRealCandidateAction(int cellIndex, int v, FillRealCandidatesState state)
    {
        int numSolutionsIndex = cellIndex * MAX_VALUE + (v - 1);
        uint valMask = ValueMask(v);

        // Don't bother trying this value if it's already confirmed in the fixed board
        if (!state.boardInvalid && ((state.fixedBoard[cellIndex] & valMask) == 0 || state.numSolutions != null && state.numSolutions[numSolutionsIndex] < 8))
        {
            // Do the solve on a copy of the board
            Solver boardCopy = Clone(willRunNonSinglesLogic: false);
            boardCopy.isBruteForcing = true;

            // Go through all previous cells and set only their real candidates as possibilities
            for (int fi = 0; fi < HEIGHT; fi++)
            {
                for (int fj = 0; fj < WIDTH; fj++)
                {
                    int fixedCellIndex = fi * WIDTH + fj;
                    if (state.candidatesFixed[fixedCellIndex])
                    {
                        boardCopy[fi, fj] = state.fixedBoard[fixedCellIndex];
                    }
                }
            }

            // Set the board to use this candidate's value
            if (boardCopy.SetValue(cellIndex, v))
            {
                if (state.numSolutions == null)
                {
                    if (boardCopy.FindSolution(multiThread: state.multiThread, cancellationToken: state.cancellationToken, isRandom: true))
                    {
                        for (int si = 0; si < HEIGHT; si++)
                        {
                            for (int sj = 0; sj < WIDTH; sj++)
                            {
                                uint solutionValMask = boardCopy[si, sj] & ~valueSetMask;
                                state.fixedBoard[si * WIDTH + sj] |= solutionValMask;
                            }
                        }
                    }
                }
                else
                {
                    int numSolutionsNeeded = 8 - state.numSolutions[numSolutionsIndex];
                    int numSolutions = (int)boardCopy.CountSolutions(
                        maxSolutions: (uint)numSolutionsNeeded,
                        multiThread: state.multiThread,
                        skipSolutions: state.solutionsCounted,
                        cancellationToken: state.cancellationToken,
                        solutionEvent: (curSolutionBoard) =>
                        {
                            state.solutionsCounted.Add(curSolutionBoard.GivenString);

                            for (int si = 0; si < HEIGHT; si++)
                            {
                                for (int sj = 0; sj < WIDTH; sj++)
                                {
                                    uint solutionValMask = curSolutionBoard[si, sj] & ~valueSetMask;
                                    int cellIndex = si * WIDTH + sj;
                                    state.fixedBoard[cellIndex] |= solutionValMask;
                                    state.numSolutions[cellIndex * MAX_VALUE + GetValue(solutionValMask) - 1]++;
                                }
                            }
                        }
                    );
                }
            }
        }

        if (state.tasksRemainingPerCell != null && --state.tasksRemainingPerCell[cellIndex] == 0)
        {
            // If a cell has no possible candidates then there are no solutions and thus all candidates are empty.
            if (state.fixedBoard[cellIndex] == 0)
            {
                state.boardInvalid = true;
            }
            state.candidatesFixed[cellIndex] = true;
        }

        state.CheckProgressEvent();
    }

#if INFINITE_LOOP_CHECK
    public bool IsSame(Solver other)
    {
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                if (other.Board[i, j] != Board[i, j])
                {
                    return false;
                }
            }
        }
        return true;
    }
#endif

    /// <summary>
    /// Perform a logical solve until either the board is solved or there are no logical steps found.
    /// </summary>
    /// <param name="stepsDescription">Get a full description of all logical steps taken.</param>
    /// <returns></returns>
    public LogicResult ConsolidateBoard(List<LogicalStepDesc> logicalStepDescs = null)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        bool changed = false;
        LogicResult result;
        do
        {
#if INFINITE_LOOP_CHECK
            Solver clone = Clone();
#endif
            if (!isBruteForcing && !IsBoardValid(logicalStepDescs))
            {
                result = LogicResult.Invalid;
            }
            else
            {
                result = StepLogic(logicalStepDescs);
            }
#if INFINITE_LOOP_CHECK
            if (result == LogicResult.Changed && IsSame(clone))
            {
                throw new InvalidOperationException("Logic step returned a change, but no changed to candidates occured.");
            }
#endif
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    public LogicResult PrepForBruteForce()
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        SetToBasicsOnly();

        bool changed = false;
        LogicResult result;
        do
        {
#if INFINITE_LOOP_CHECK
            Solver clone = Clone();
#endif
            result = StepLogic(null);
#if INFINITE_LOOP_CHECK
            if (result == LogicResult.Changed && IsSame(clone))
            {
                throw new InvalidOperationException("Logic step returned a change, but no changed to candidates occured.");
            }
#endif
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    public LogicResult ApplySingles()
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        bool changed = false;
        LogicResult result;
        do
        {
            result = FindSingles();
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    public LogicResult ApplyNakedSingles()
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        bool changed = false;
        LogicResult result;
        do
        {
            result = FindNakedSingles(null);
            changed |= result == LogicResult.Changed;
        } while (result == LogicResult.Changed);

        return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
    }

    private LogicResult FindSingles()
    {
        LogicResult result = FindNakedSingles(null);
        if (result == LogicResult.None)
        {
            result = FindHiddenSingle(null);
        }
        return result;
    }

    /// <summary>
    /// Perform one step of a logical solve and fill a description of the step taken.
    /// The description will contain the reason the board is invalid if that is what is returned.
    /// </summary>
    /// <param name="stepDescription"></param>
    /// <returns></returns>
    public LogicResult StepLogic(List<LogicalStepDesc> logicalStepDescs)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        LogicResult result = LogicResult.None;

#if PROFILING
        timers["FindNakedSingles"].Start();
#endif
        result = FindNakedSingles(logicalStepDescs);
#if PROFILING
        timers["FindNakedSingles"].Stop();
#endif
        if (result != LogicResult.None)
        {
            return result;
        }

#if PROFILING
        timers["FindHiddenSingle"].Start();
#endif
        result = FindHiddenSingle(logicalStepDescs);
#if PROFILING
        timers["FindHiddenSingle"].Stop();
#endif
        if (result != LogicResult.None)
        {
            return result;
        }

        foreach (var constraint in constraints)
        {
#if PROFILING
            string constraintName = constraint.GetType().FullName;
            timers[constraintName].Start();
#endif
            result = constraint.StepLogic(this, logicalStepDescs, isBruteForcing);
#if PROFILING
            timers[constraintName].Stop();
#endif
            if (result != LogicResult.None)
            {
                if (logicalStepDescs != null && logicalStepDescs.Count > 0)
                {
                    logicalStepDescs[^1] = logicalStepDescs[^1].WithPrefix($"[{constraint.SpecificName}] ");
                }
                else if (logicalStepDescs != null && logicalStepDescs.Count == 0)
                {
                    logicalStepDescs.Add(new(
                        $"{constraint.SpecificName} reported the board was invalid without specifying why. This is a bug: please report it!",
                        Enumerable.Empty<int>(),
                        Enumerable.Empty<int>()
                    ));
                }
                return result;
            }
        }

        if (isBruteForcing)
        {
            return LogicResult.None;
        }

        // Re-evaluate weak links
#if PROFILING
        timers["AddNewWeakLinks"].Start();
#endif
        foreach (var constraint in constraints)
        {
            var logicResult = constraint.InitLinks(this, logicalStepDescs);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }
        }
#if PROFILING
        timers["AddNewWeakLinks"].Stop();
#endif

        if (!DisableTuples || !DisablePointing)
        {
#if PROFILING
            timers["FindNakedTuples"].Start();
#endif
            result = FindNakedTuplesAndPointing(logicalStepDescs);
#if PROFILING
            timers["FindNakedTuples"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisablePointing)
        {
#if PROFILING
            timers["FindDirectCellForcing"].Start();
#endif
            result = FindDirectCellForcing(logicalStepDescs);
#if PROFILING
            timers["FindDirectCellForcing"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableFishes)
        {
#if PROFILING
            timers["FindFishes"].Start();
#endif
            result = FindFishes(logicalStepDescs);
#if PROFILING
            timers["FindFishes"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableWings)
        {
#if PROFILING
            timers["FindWings"].Start();
#endif
            result = FindWings(logicalStepDescs);
#if PROFILING
            timers["FindWings"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }
        }

        if (!DisableAIC)
        {
#if PROFILING
            timers["FindAIC"].Start();
#endif
            result = FindAIC(logicalStepDescs);
#if PROFILING
            timers["FindAIC"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }
        }

#if PROFILING
        timers["FindSimpleContradictions"].Start();
#endif
        result = FindSimpleContradictions(logicalStepDescs);
#if PROFILING
        timers["FindSimpleContradictions"].Stop();
#endif
        if (result != LogicResult.None)
        {
            return result;
        }

        return LogicResult.None;
    }

    private bool IsBoardValid(List<LogicalStepDesc> logicalStepDescs)
    {
        // Check for empty cells
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            if ((board[cellIndex] & ~valueSetMask) == 0)
            {
                if (logicalStepDescs != null)
                {
                    var (i, j) = CellIndexToCoord(cellIndex);
                    logicalStepDescs?.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                }
                return false;
            }
        }

        // Check for values that groups must contain but don't
        foreach (var group in Groups)
        {
            var groupCells = group.Cells;
            int numCells = group.Cells.Count;
            if (numCells != MAX_VALUE)
            {
                continue;
            }

            uint atLeastOnce = 0;
            for (int groupindex = 0; groupindex < numCells; groupindex++)
            {
                int cellIndex = groupCells[groupindex];
                atLeastOnce |= board[cellIndex];
            }
            atLeastOnce &= ~valueSetMask;

            if (atLeastOnce != ALL_VALUES_MASK && numCells == MAX_VALUE)
            {
                logicalStepDescs?.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~atLeastOnce)}.", group.Cells.Select(CellIndexToCoord)));
                return false;
            }
        }

        if (isBruteForcing)
        {
            return true;
        }

        // Check for groups which contain too many cells with a tuple that is too small
        List<int> unsetCells = new(MAX_VALUE);
        foreach (var group in Groups)
        {
            for (int tupleSize = 2; tupleSize < MAX_VALUE; tupleSize++)
            {
                unsetCells.Clear();
                foreach (int cellIndex in group.Cells)
                {
                    uint cellMask = board[cellIndex];
                    if (!IsValueSet(cellMask))
                    {
                        unsetCells.Add(cellIndex);
                    }
                }

                if (unsetCells.Count < tupleSize)
                {
                    continue;
                }

                foreach (var tupleCells in unsetCells.Combinations(tupleSize))
                {
                    uint tupleMask = CandidateMask(tupleCells);
                    if (ValueCount(tupleMask) < tupleSize)
                    {
                        logicalStepDescs?.Add(new($"{CompactName(tupleCells)} in {group} are {tupleCells.Count} cells with only {ValueCount(tupleMask)} candidates available ({MaskToString(tupleMask)}).", tupleCells.Select(CellIndexToCoord)));
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private LogicResult FindNakedSingles(List<LogicalStepDesc> logicalStepDescs)
    {
        bool hasUnsetCells = false;
        if (logicalStepDescs == null)
        {
            bool changed = false;
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint mask = board[cellIndex];
                if ((mask & ~valueSetMask) == 0)
                {
                    return LogicResult.Invalid;
                }

                if (!IsValueSet(mask))
                {
                    hasUnsetCells = true;

                    if (ValueCount(mask) == 1)
                    {
                        int value = GetValue(mask);
                        if (!SetValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                return LogicResult.Changed;
            }
        }
        else
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = CellIndex(i, j);
                    uint mask = board[cellIndex];
                    if ((mask & ~valueSetMask) == 0)
                    {
                        logicalStepDescs.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                        return LogicResult.Invalid;
                    }

                    if (!IsValueSet(mask))
                    {
                        hasUnsetCells = true;

                        if (ValueCount(mask) == 1)
                        {
                            int value = GetValue(mask);
                            if (!SetValue(i, j, value))
                            {
                                logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)} cannot be set to {value}.", (i, j)));
                                return LogicResult.Invalid;
                            }
                            logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)}={value}", CandidateIndex((i, j), value).ToEnumerable(), null, isSingle: true));
                            return LogicResult.Changed;
                        }
                    }
                }
            }
        }
        return !hasUnsetCells ? LogicResult.PuzzleComplete : LogicResult.None;
    }

    private LogicResult FindHiddenSingle(List<LogicalStepDesc> logicalStepDescs)
    {
        foreach (var group in Groups)
        {
            var groupCells = group.Cells;
            int numCells = group.Cells.Count;
            if (numCells != MAX_VALUE && (isBruteForcing || group.FromConstraint == null))
            {
                continue;
            }

            uint atLeastOnce = 0;
            uint moreThanOnce = 0;
            uint setMask = 0;
            for (int groupIndex = 0; groupIndex < numCells; groupIndex++)
            {
                int cellIndex = groupCells[groupIndex];
                uint mask = board[cellIndex];
                if (IsValueSet(mask))
                {
                    setMask |= mask;
                }
                else
                {
                    moreThanOnce |= atLeastOnce & mask;
                    atLeastOnce |= mask;
                }
            }
            setMask &= ~valueSetMask;
            if (numCells == MAX_VALUE && (atLeastOnce | setMask) != ALL_VALUES_MASK)
            {
                if (logicalStepDescs != null)
                {
                    logicalStepDescs.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~(atLeastOnce | setMask))}.", group.Cells.Select(CellIndexToCoord)));
                }
                return LogicResult.Invalid;
            }

            uint exactlyOnce = atLeastOnce & ~moreThanOnce;
            if (exactlyOnce != 0)
            {
                int val = 0;
                int valCellIndex = -1;
                if (numCells == MAX_VALUE)
                {
                    val = MinValue(exactlyOnce);
                    uint valMask = ValueMask(val);
                    foreach (int cellIndex in group.Cells)
                    {
                        if ((board[cellIndex] & valMask) != 0)
                        {
                            valCellIndex = cellIndex;
                            break;
                        }
                    }
                }
                else
                {
                    int minValue = MinValue(exactlyOnce);
                    int maxValue = MaxValue(exactlyOnce);
                    for (int v = minValue; v <= maxValue; v++)
                    {
                        if ((exactlyOnce & v) != 0)
                        {
                            List<(int, int)> cellsMustContain = group.FromConstraint?.CellsMustContain(this, val);
                            if (cellsMustContain != null && cellsMustContain.Count == 1)
                            {
                                val = v;
                                valCellIndex = CellIndex(cellsMustContain[0]);
                                break;
                            }
                        }
                    }
                }

                if (valCellIndex >= 0)
                {
                    if (!SetValue(valCellIndex, val))
                    {
                        if (logicalStepDescs != null)
                        {
                            logicalStepDescs.Add(new($"Hidden Single in {group}: {CellName(CellIndexToCoord(valCellIndex))} cannot be set to {val}.", CellIndexToCoord(valCellIndex)));
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicalStepDescs != null)
                    {
                        logicalStepDescs.Add(new($"Hidden Single in {group}: {CellName(CellIndexToCoord(valCellIndex))}={val}", CandidateIndex(valCellIndex, val).ToEnumerable(), null, isSingle: true));
                    }
                    return LogicResult.Changed;
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FindDirectCellForcing(List<LogicalStepDesc> logicalStepDescs)
    {
        HashSet<int> elimSet = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                if (IsValueSet(mask))
                {
                    continue;
                }

                bool isFirstElimArray = true;
                elimSet.Clear();
                int candBase = (i * HEIGHT + j) * MAX_VALUE;
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        int candIndex = candBase + v - 1;
                        if (isFirstElimArray)
                        {
                            elimSet.UnionWith(weakLinks[candIndex]);
                            isFirstElimArray = false;
                        }
                        else
                        {
                            elimSet.IntersectWith(weakLinks[candIndex]);
                        }

                        if (elimSet.Count == 0)
                        {
                            break;
                        }
                    }
                }

                if (elimSet.Count > 0)
                {
                    List<int> elims = elimSet.Where(IsCandIndexValid).ToList();
                    if (elims.Count > 0)
                    {
                        List<(int, int)> sourceCell = new() { (i, j) };
                        logicalStepDescs?.Add(new(
                            desc: $"Direct Cell Forcing: {CompactName(mask, sourceCell)} => {DescribeElims(elims)}",
                            sourceCandidates: CandidateIndexes(mask, sourceCell),
                            elimCandidates: elims
                        ));
                        if (!ClearCandidates(elims))
                        {
                            return LogicResult.Invalid;
                        }
                        return LogicResult.Changed;
                    }
                }
            }
        }
        return LogicResult.None;
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

    private LogicResult FindNakedTuplesAndPointing(List<LogicalStepDesc> logicalStepDescs)
    {
        List<int> unsetCells = new(MAX_VALUE);
        for (int numCells = 2; numCells <= MAX_VALUE; numCells++)
        {
            if (!DisableTuples && numCells < MAX_VALUE)
            {
                foreach (var group in Groups)
                {
                    if (group.Cells.Count < numCells)
                    {
                        continue;
                    }

                    // Make a list of cells which aren't already set and contain fewer candidates than the tuple size
                    unsetCells.Clear();
                    foreach (int cellIndex in group.Cells)
                    {
                        uint cellMask = board[cellIndex];
                        if (!IsValueSet(cellMask) && ValueCount(cellMask) <= numCells)
                        {
                            unsetCells.Add(cellIndex);
                        }
                    }

                    if (unsetCells.Count < numCells)
                    {
                        continue;
                    }

                    foreach (var tupleCells in unsetCells.Combinations(numCells))
                    {
                        uint tupleMask = CandidateMask(tupleCells);
                        if (ValueCount(tupleMask) == numCells)
                        {
                            var elims = CalcElims(tupleMask, tupleCells);
                            if (elims.Count > 0)
                            {
                                logicalStepDescs?.Add(new(
                                    desc: $"Tuple: {CompactName(tupleMask, tupleCells)} in {group} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(tupleMask, tupleCells.Select(CellIndexToCoord)),
                                    elimCandidates: elims
                                ));
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

            if (!DisablePointing)
            {
                // Look for "pointing" but limit to the same number of cells as the tuple size
                // This is a heuristic ordering which avoids finding something like a pair as pointing,
                // but prefers 2 cells pointing over a triple.
                foreach (var group in Groups.Where(g => g.Cells.Count == MAX_VALUE || g.FromConstraint != null))
                {
                    uint setValuesMask = 0;
                    uint unsetValuesMask = 0;
                    foreach (int cellIndex in group.Cells)
                    {
                        uint mask = board[cellIndex] & ~valueSetMask;
                        if (ValueCount(mask) == 1)
                        {
                            setValuesMask |= mask;
                        }
                        else
                        {
                            unsetValuesMask |= mask;
                        }
                    }
                    unsetValuesMask &= ~setValuesMask;
                    if (unsetValuesMask == 0)
                    {
                        continue;
                    }

                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (!HasValue(unsetValuesMask, v))
                        {
                            continue;
                        }

                        List<(int, int)> cellsMustContain = group.CellsMustContain(this, v);
                        if (cellsMustContain != null && cellsMustContain.Count == numCells)
                        {
                            var logicResult = HandleMustContain(group.ToString(), v, cellsMustContain, logicalStepDescs);
                            if (logicResult != LogicResult.None)
                            {
                                return logicResult;
                            }
                        }
                    }
                }

                // Check constraints as well
                foreach (var constraint in constraints)
                {
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        List<(int, int)> cellsMustContain = constraint.CellsMustContain(this, v);
                        if (cellsMustContain != null && cellsMustContain.Count == numCells)
                        {
                            var logicResult = HandleMustContain(constraint.SpecificName, v, cellsMustContain, logicalStepDescs);
                            if (logicResult != LogicResult.None)
                            {
                                return logicResult;
                            }
                        }
                    }
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult HandleMustContain(string name, int v, List<(int, int)> cellsMustContain, List<LogicalStepDesc> logicalStepDescs)
    {
        if (cellsMustContain == null || cellsMustContain.Count == 0)
        {
            return LogicResult.None;
        }

        if (cellsMustContain.Count == 1)
        {
            var (i, j) = cellsMustContain[0];
            logicalStepDescs?.Add(new($"Hidden Single in {name}: {CellName(i, j)}={v}", CandidateIndex((i, j), v).ToEnumerable(), null, isSingle: true));
            if (!SetValue(i, j, v))
            {
                return LogicResult.Invalid;
            }
            return LogicResult.Changed;
        }

        uint valueMask = ValueMask(v);
        var elims = CalcElims(valueMask, cellsMustContain);
        if (elims == null || elims.Count == 0)
        {
            return LogicResult.None;
        }

        logicalStepDescs?.Add(new(
                        desc: $"Pointing: {v}{CompactName(cellsMustContain)} in {name} => {DescribeElims(elims)}",
                        sourceCandidates: CandidateIndexes(valueMask, cellsMustContain),
                        elimCandidates: elims
                    ));
        if (!ClearCandidates(elims))
        {
            return LogicResult.Invalid;
        }
        return LogicResult.Changed;
    }

    private LogicResult FindUnorthodoxTuple(List<LogicalStepDesc> logicalStepDescs, int tupleSize)
    {
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                int valueCount = ValueCount(mask);
                if (!IsValueSet(mask) && valueCount > 1 && valueCount <= tupleSize)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        int numCandidateCells = candidateCells.Count;
        if (numCandidateCells < tupleSize)
        {
            return LogicResult.None;
        }

        bool IsPotentiallyValidTuple(uint valuesMask, List<(int, int)> tupleCells)
        {
            int valuesMaskCount = ValueCount(valuesMask);
            if (valuesMaskCount > tupleSize)
            {
                return false;
            }

            if (tupleCells.Count <= 1)
            {
                return true;
            }

            if (tupleCells.Count == tupleSize && valuesMaskCount != tupleSize)
            {
                return false;
            }

            int minValue = MinValue(valuesMask);
            int maxValue = MaxValue(valuesMask);
            for (int v = minValue; v <= maxValue; v++)
            {
                uint valueMask = ValueMask(v);
                if ((valuesMask & valueMask) != 0)
                {
                    int numWithCandidate = 0;
                    for (int k = 0; k < tupleCells.Count; k++)
                    {
                        var (i, j) = tupleCells[k];
                        if ((this[i, j] & valueMask) != 0)
                        {
                            numWithCandidate++;
                        }
                    }

                    if (numWithCandidate > 1 && !IsGroup(tupleCells, v))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        List<(int, int)> tupleCells = new(tupleSize);
        (int index, (int i, int j) coord, uint accumMask, bool isValid)[] tupleInfoArray = new (int, (int, int), uint, bool)[tupleSize];

        // Initialize the wing info array with the first combination even if it's invalid
        {
            uint accumMask = 0;
            for (int k = 0; k < tupleSize; k++)
            {
                var coord = candidateCells[k];
                accumMask |= this[coord.Item1, coord.Item2];
                tupleCells.Add(coord);
                bool isValid = IsPotentiallyValidTuple(accumMask, tupleCells);
                tupleInfoArray[k] = (k, coord, accumMask, isValid);
            }
        }

        while (true)
        {
            // Check if this wing info is valid and performs eliminations
            var (_, _, accumMask, isValid) = tupleInfoArray[tupleSize - 1];
            if (isValid && ValueCount(accumMask) == tupleSize)
            {
                // All candidates in this tuple must be present, so we can eliminate based on each value
                var elims = CalcElims(accumMask, tupleCells);
                if (elims != null && elims.Count > 0)
                {
                    if (logicalStepDescs != null)
                    {
                        StringBuilder wingName = new();
                        for (int k = tupleSize - 1; k >= 0; k--)
                        {
                            wingName.Append((char)('Z' - k));
                        }
                        logicalStepDescs?.Add(new(
                                desc: $"Unorthodox Tuple ({tupleSize}): {MaskToString(accumMask)} in {CompactName(tupleCells)} => {DescribeElims(elims)}",
                                sourceCandidates: CandidateIndexes(accumMask, tupleCells),
                                elimCandidates: elims
                            ));
                    }
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }

            // Find the first invalid index. This is the minimum index to increment.
            int firstInvalid = tupleSize;
            for (int k = 0; k < tupleSize; k++)
            {
                var tupleinfo = tupleInfoArray[k];
                if (!tupleinfo.isValid)
                {
                    firstInvalid = k;
                    break;
                }
            }

            // Find the last index which can be incremented
            int lastCanIncrement = -1;
            for (int k = tupleSize - 1; k >= 0; k--)
            {
                var tupleInfo = tupleInfoArray[k];
                if (tupleInfo.index + 1 < numCandidateCells - (tupleSize - k))
                {
                    lastCanIncrement = k;
                    break;
                }
            }

            // Check if done
            if (lastCanIncrement == -1)
            {
                break;
            }

            // Increment the smaller of the first invalid one or the last that can increment.
            int k0 = Math.Min(lastCanIncrement, firstInvalid);

            tupleCells.Clear();
            for (int k = 0; k < k0; k++)
            {
                tupleCells.Add(tupleInfoArray[k].coord);
            }

            // Increment to the next combination
            int nextIndex = tupleInfoArray[k0].index + 1;
            var nextCoord = candidateCells[nextIndex];
            uint nextAccumMask = this[nextCoord.Item1, nextCoord.Item2];
            if (k0 > 0)
            {
                nextAccumMask |= tupleInfoArray[k0 - 1].accumMask;
            }
            tupleCells.Add(nextCoord);
            tupleInfoArray[k0] = (nextIndex, nextCoord, nextAccumMask, IsPotentiallyValidTuple(nextAccumMask, tupleCells));

            for (int k1 = k0 + 1; k1 < tupleSize; k1++)
            {
                nextIndex = tupleInfoArray[k1 - 1].index + 1;
                nextCoord = candidateCells[nextIndex];
                nextAccumMask |= this[nextCoord.Item1, nextCoord.Item2];
                tupleCells.Add(nextCoord);
                tupleInfoArray[k1] = (nextIndex, nextCoord, nextAccumMask, IsPotentiallyValidTuple(nextAccumMask, tupleCells));
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindFishes(List<LogicalStepDesc> logicalStepDescs)
    {
        if (WIDTH != MAX_VALUE || HEIGHT != MAX_VALUE)
        {
            return LogicResult.None;
        }
        // Since these are all guaranteed equal at this point, only MAX_VALUE will be used for all three purposes.

        // Construct a transformed lookup of which values are in which rows/cols
        uint[][,] rowcolIndexByValue = new uint[2][,];
        rowcolIndexByValue[0] = new uint[MAX_VALUE, MAX_VALUE];
        rowcolIndexByValue[1] = new uint[MAX_VALUE, MAX_VALUE];
        for (int i = 0; i < MAX_VALUE; i++)
        {
            for (int j = 0; j < MAX_VALUE; j++)
            {
                uint mask = this[i, j] & ~valueSetMask;
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        rowcolIndexByValue[0][v - 1, j] |= (1u << i);
                        rowcolIndexByValue[1][v - 1, i] |= (1u << j);
                    }
                }
            }
        }

        // Look for standard fishes
        List<int> unsetRowOrCols = new(MAX_VALUE);
        for (int tupleSize = 2; tupleSize <= MAX_VALUE / 2; tupleSize++)
        {
            for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
            {
                uint[,] indexByValue = rowcolIndexByValue[rowOrCol];

                for (int valueIndex = 0; valueIndex < MAX_VALUE; valueIndex++)
                {
                    int value = valueIndex + 1;

                    // Make a list of pairs for the row/col which aren't already filled
                    unsetRowOrCols.Clear();
                    for (int j = 0; j < MAX_VALUE; j++)
                    {
                        uint cellMask = indexByValue[valueIndex, j];
                        int valueCount = ValueCount(cellMask);
                        if (valueCount > 1 && valueCount <= tupleSize)
                        {
                            unsetRowOrCols.Add(j);
                        }
                    }
                    if (unsetRowOrCols.Count < tupleSize)
                    {
                        continue;
                    }

                    foreach (var tupleRowOrCols in unsetRowOrCols.Combinations(tupleSize))
                    {
                        uint tupleMask = 0;
                        foreach (int j in tupleRowOrCols)
                        {
                            tupleMask |= indexByValue[valueIndex, j];
                        }

                        if (ValueCount(tupleMask) == tupleSize)
                        {
                            List<int> elims = null;
                            for (int j = 0; j < MAX_VALUE; j++)
                            {
                                if (tupleRowOrCols.Contains(j))
                                {
                                    continue;
                                }

                                uint mask = indexByValue[valueIndex, j];
                                uint elimMask = mask & tupleMask;
                                if (elimMask != 0)
                                {
                                    for (int i = 0; i < MAX_VALUE; i++)
                                    {
                                        if ((elimMask & (1u << i)) != 0)
                                        {
                                            elims ??= new();
                                            elims.Add(CandidateIndex(rowOrCol == 0 ? (i, j) : (j, i), value));
                                        }
                                    }
                                }
                            }

                            if (elims != null && elims.Count > 0)
                            {
                                string techniqueName = tupleSize switch
                                {
                                    2 => "X-Wing",
                                    3 => "Swordfish",
                                    4 => "Jellyfish",
                                    _ => $"{tupleSize}-Fish",
                                };

                                List<(int, int)> fishCells = new();
                                foreach (int j in tupleRowOrCols)
                                {
                                    uint mask = indexByValue[valueIndex, j];
                                    for (int i = 0; i < MAX_VALUE; i++)
                                    {
                                        if ((mask & (1u << i)) != 0)
                                        {
                                            fishCells.Add(rowOrCol == 0 ? (i, j) : (j, i));
                                        }
                                    }
                                }

                                logicalStepDescs?.Add(new(
                                    desc: $"{techniqueName}: {value} {CompactName(fishCells)} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(ValueMask(value), fishCells),
                                    elimCandidates: elims
                                ));
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

        // Look for finned fishes
        for (int tupleSize = 2; tupleSize <= MAX_VALUE / 2; tupleSize++)
        {
            for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
            {
                uint[,] indexByValue = rowcolIndexByValue[rowOrCol];

                for (int valueIndex = 0; valueIndex < MAX_VALUE; valueIndex++)
                {
                    int value = valueIndex + 1;

                    // Make a list of pairs for the row/col which aren't already filled
                    unsetRowOrCols.Clear();
                    for (int j = 0; j < MAX_VALUE; j++)
                    {
                        uint cellMask = indexByValue[valueIndex, j];
                        int valueCount = ValueCount(cellMask);
                        if (valueCount > 1)
                        {
                            unsetRowOrCols.Add(j);
                        }
                    }
                    if (unsetRowOrCols.Count < tupleSize)
                    {
                        continue;
                    }

                    foreach (var tupleRowOrCols in unsetRowOrCols.Combinations(tupleSize))
                    {
                        uint tupleMask = 0;
                        foreach (int j in tupleRowOrCols)
                        {
                            tupleMask |= indexByValue[valueIndex, j];
                        }

                        List<int> nonTupleRowOrCols = new();
                        for (int j = 0; j < MAX_VALUE; j++)
                        {
                            if (!tupleRowOrCols.Contains(j))
                            {
                                nonTupleRowOrCols.Add(j);
                            }
                        }

                        int numPositions = ValueCount(tupleMask);
                        if (numPositions > tupleSize)
                        {
                            List<int> positions = new(numPositions);
                            for (int j = 0; j < MAX_VALUE; j++)
                            {
                                if ((tupleMask & (1u << j)) != 0)
                                {
                                    positions.Add(j);
                                }
                            }
                            foreach (var positionCombo in positions.Combinations(tupleSize))
                            {
                                uint positionMask = 0;
                                foreach (int j in positionCombo)
                                {
                                    positionMask |= (1u << j);
                                }

                                // Calculate the eliminations from the fish formed by these positions
                                HashSet<int> elims = null;
                                foreach (int j in nonTupleRowOrCols)
                                {
                                    uint mask = indexByValue[valueIndex, j];
                                    uint elimMask = mask & positionMask;
                                    if (elimMask != 0)
                                    {
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((elimMask & (1u << i)) != 0)
                                            {
                                                elims ??= new();
                                                elims.Add(CandidateIndex(rowOrCol == 0 ? (i, j) : (j, i), value));
                                            }
                                        }
                                    }
                                }

                                if (elims != null)
                                {
                                    // Calcuate the eliminations from individual candidates not in the fish
                                    uint notPostionMask = tupleMask & ~positionMask;
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j] & notPostionMask;
                                        if (mask != 0)
                                        {
                                            for (int i = 0; i < MAX_VALUE; i++)
                                            {
                                                if ((mask & (1u << i)) != 0)
                                                {
                                                    var finCell = rowOrCol == 0 ? (i, j) : (j, i);
                                                    elims.IntersectWith(weakLinks[CandidateIndex(finCell, value)]);
                                                }
                                            }
                                        }
                                        if (elims.Count == 0)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (elims != null && elims.Count > 0)
                                {
                                    string techniqueName = tupleSize switch
                                    {
                                        2 => "X-Wing",
                                        3 => "Swordfish",
                                        4 => "Jellyfish",
                                        _ => $"{tupleSize}-Fish",
                                    };

                                    List<(int, int)> fishCells = new();
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j];
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((mask & (1u << i)) != 0)
                                            {
                                                fishCells.Add(rowOrCol == 0 ? (i, j) : (j, i));
                                            }
                                        }
                                    }

                                    logicalStepDescs?.Add(new(
                                        desc: $"Finned {techniqueName}: {value} {CompactName(fishCells)} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(ValueMask(value), fishCells),
                                        elimCandidates: elims
                                    ));
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
        }

        return LogicResult.None;
    }

    private LogicResult FindWings(List<LogicalStepDesc> logicalStepDescs)
    {
        if (isBruteForcing)
        {
            return LogicResult.None;
        }

        // A y-wing always involves three bivalue cells.
        // The three cells have 3 candidates between them, and one cell called the "pivot" sees the other two "pincers".
        // A strong link is formed between the common candidate between the two "pincer" cells.
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                if (!IsValueSet(mask) && ValueCount(mask) == 2)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        // Look for Y-Wings
        for (int c0 = 0; c0 < candidateCells.Count - 2; c0++)
        {
            var (i0, j0) = candidateCells[c0];
            uint mask0 = this[i0, j0];
            for (int c1 = c0 + 1; c1 < candidateCells.Count - 1; c1++)
            {
                var (i1, j1) = candidateCells[c1];
                uint mask1 = this[i1, j1];
                if (mask0 == mask1 || ValueCount(mask0 | mask1) != 3)
                {
                    continue;
                }

                for (int c2 = c1 + 1; c2 < candidateCells.Count; c2++)
                {
                    var (i2, j2) = candidateCells[c2];
                    uint mask2 = this[i2, j2];
                    if (mask0 == mask2 || mask1 == mask2)
                    {
                        continue;
                    }

                    uint combinedMask = mask0 | mask1 | mask2;
                    if (ValueCount(combinedMask) == 3)
                    {
                        int value01 = GetValue(mask0 & mask1);
                        int value02 = GetValue(mask0 & mask2);
                        int value12 = GetValue(mask1 & mask2);
                        int cand01_0 = CandidateIndex((i0, j0), value01);
                        int cand01_1 = CandidateIndex((i1, j1), value01);
                        int cand02_0 = CandidateIndex((i0, j0), value02);
                        int cand02_2 = CandidateIndex((i2, j2), value02);
                        int cand12_1 = CandidateIndex((i1, j1), value12);
                        int cand12_2 = CandidateIndex((i2, j2), value12);
                        bool weak01 = weakLinks[cand01_0].BinarySearch(cand01_1) >= 0;
                        bool weak02 = weakLinks[cand02_0].BinarySearch(cand02_2) >= 0;
                        bool weak12 = weakLinks[cand12_1].BinarySearch(cand12_2) >= 0;
                        int weakCount = (weak01 ? 1 : 0) + (weak02 ? 1 : 0) + (weak12 ? 1 : 0);
                        if (weakCount != 2)
                        {
                            continue;
                        }

                        List<int> elims;
                        if (weak01 && weak02)
                        {
                            // Pivot is 0, eliminate from the shared 12 value
                            elims = CalcElims(cand12_1, cand12_2).ToList();
                        }
                        else if (weak01 && weak12)
                        {
                            // Pivot is 1, eliminate from the shared 02 value
                            elims = CalcElims(cand02_0, cand02_2).ToList();
                        }
                        else
                        {
                            // Pivot is 2, elimiate from the shared 01 value
                            elims = CalcElims(cand01_0, cand01_1).ToList();
                        }
                        if (elims.Count > 0)
                        {
                            List<(int, int)> cells = new() { (i0, j0), (i1, j1), (i2, j2) };
                            logicalStepDescs?.Add(new(
                                    desc: $"Y-Wing: {MaskToString(combinedMask)} in {CompactName(cells)} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(combinedMask, cells),
                                    elimCandidates: elims
                                ));
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

        // Look for XYZ-wings
        // Look for unorthodox tuples of this size before looking for wings
        LogicResult logicResult = FindUnorthodoxTuple(logicalStepDescs, 3);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        logicResult = FindNWing(logicalStepDescs, 3);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        // TODO: W-Wing

        // Look for WXYZ-Wings
        logicResult = FindUnorthodoxTuple(logicalStepDescs, 4);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        logicResult = FindNWing(logicalStepDescs, 4);
        if (logicResult != LogicResult.None)
        {
            return logicResult;
        }

        // Look for (N)-Wings [Extension of XYZ-Wings and WXYZ-Wings for sizes 5+]
        // An (N)-Wing is N candidates limited to N cells.
        // Looking at each candidate, all but one of them cannot repeat within those cells.
        // This implies that any cell seen by the instances of that last candidate can be eliminated.
        for (int wingSize = 5; wingSize <= MAX_VALUE; wingSize++)
        {
            // Look for unorthodox tuples of this size before looking for wings
            logicResult = FindUnorthodoxTuple(logicalStepDescs, wingSize);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }

            logicResult = FindNWing(logicalStepDescs, wingSize);
            if (logicResult != LogicResult.None)
            {
                return logicResult;
            }
        }

        return LogicResult.None;
    }

    private LogicResult FindNWing(List<LogicalStepDesc> logicalStepDescs, int wingSize)
    {
        List<(int, int)> candidateCells = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = this[i, j];
                int valueCount = ValueCount(mask);
                if (!IsValueSet(mask) && valueCount > 1 && valueCount <= wingSize)
                {
                    candidateCells.Add((i, j));
                }
            }
        }

        int numCandidateCells = candidateCells.Count;
        if (numCandidateCells < wingSize)
        {
            return LogicResult.None;
        }

        int UngroupedValue(uint valuesMask, List<(int, int)> wingCells, int curUngroupedValue)
        {
            if (curUngroupedValue < 0 || ValueCount(valuesMask) > wingSize)
            {
                return -1;
            }

            if (wingCells.Count <= 1)
            {
                return 0;
            }
            bool checkForSingle = wingCells.Count == wingSize;
            if (checkForSingle && ValueCount(valuesMask) != wingSize)
            {
                return -1;
            }

            valuesMask &= ~ValueMask(curUngroupedValue);

            int ungroupedValue = curUngroupedValue;
            int minValue = MinValue(valuesMask);
            int maxValue = MaxValue(valuesMask);
            for (int v = minValue; v <= maxValue; v++)
            {
                uint valueMask = ValueMask(v);
                if ((valuesMask & valueMask) != 0)
                {
                    int numWithCandidate = 0;
                    for (int k = 0; k < wingCells.Count; k++)
                    {
                        var (i, j) = wingCells[k];
                        if ((this[i, j] & valueMask) != 0)
                        {
                            numWithCandidate++;
                        }
                    }

                    if (checkForSingle && numWithCandidate == 1)
                    {
                        return -1;
                    }

                    if (numWithCandidate > 1 && !IsGroup(wingCells, v))
                    {
                        if (ungroupedValue == 0)
                        {
                            ungroupedValue = v;
                        }
                        else
                        {
                            return -1;
                        }
                    }
                }
            }
            return ungroupedValue;
        }

        List<(int, int)> wingCells = new(wingSize);
        (int index, (int i, int j) coord, uint accumMask, int ungroupedValue)[] wingInfoArray = new (int, (int, int), uint, int)[wingSize];

        // Initialize the wing info array with the first combination even if it's invalid
        {
            uint accumMask = 0;
            for (int k = 0; k < wingSize; k++)
            {
                var coord = candidateCells[k];
                accumMask |= this[coord.Item1, coord.Item2];
                wingCells.Add(coord);

                int ungroupedValue = UngroupedValue(accumMask, wingCells, k == 0 ? 0 : wingInfoArray[k - 1].ungroupedValue);
                wingInfoArray[k] = (k, coord, accumMask, ungroupedValue);
            }
        }

        while (true)
        {
            // Check if this wing info is valid and performs eliminations
            var (_, _, accumMask, ungroupedValue) = wingInfoArray[wingSize - 1];
            if (ValueCount(accumMask) == wingSize && ungroupedValue > 0)
            {
                // At least one of the ungrouped candidates is true, so eliminate any candidates with a weak link to all of them.
                var elims = CalcElims(ValueMask(ungroupedValue), wingCells);
                if (elims != null && elims.Count > 0)
                {
                    if (logicalStepDescs != null)
                    {
                        StringBuilder wingName = new();
                        for (int k = wingSize - 1; k >= 0; k--)
                        {
                            wingName.Append((char)('Z' - k));
                        }
                        logicalStepDescs?.Add(new(
                                desc: $"{wingName}-Wing: {MaskToString(accumMask)} in {CompactName(wingCells)} => {DescribeElims(elims)}",
                                sourceCandidates: CandidateIndexes(accumMask, wingCells),
                                elimCandidates: elims
                            ));
                    }
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }

            // Find the first invalid index. This is the minimum index to increment.
            int firstInvalid = wingSize;
            for (int k = 0; k < wingSize; k++)
            {
                var wingInfo = wingInfoArray[k];
                if (wingInfo.ungroupedValue < 0)
                {
                    firstInvalid = k;
                    break;
                }
            }

            // Find the last index which can be incremented
            int lastCanIncrement = -1;
            for (int k = wingSize - 1; k >= 0; k--)
            {
                var wingInfo = wingInfoArray[k];
                if (wingInfo.index + 1 < numCandidateCells - (wingSize - k))
                {
                    lastCanIncrement = k;
                    break;
                }
            }

            // Check if done
            if (lastCanIncrement == -1)
            {
                break;
            }

            // Increment the smaller of the first invalid one or the last that can increment.
            int k0 = Math.Min(lastCanIncrement, firstInvalid);

            wingCells.Clear();
            for (int k = 0; k < k0; k++)
            {
                wingCells.Add(wingInfoArray[k].coord);
            }

            // Increment to the next combination
            int nextIndex = wingInfoArray[k0].index + 1;
            var nextCoord = candidateCells[nextIndex];
            uint nextAccumMask = this[nextCoord.Item1, nextCoord.Item2];
            if (k0 > 0)
            {
                nextAccumMask |= wingInfoArray[k0 - 1].accumMask;
            }
            wingCells.Add(nextCoord);
            wingInfoArray[k0] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, k0 == 0 ? 0 : wingInfoArray[k0 - 1].ungroupedValue));

            for (int k1 = k0 + 1; k1 < wingSize; k1++)
            {
                nextIndex = wingInfoArray[k1 - 1].index + 1;
                nextCoord = candidateCells[nextIndex];
                nextAccumMask |= this[nextCoord.Item1, nextCoord.Item2];
                wingCells.Add(nextCoord);
                wingInfoArray[k1] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, wingInfoArray[k1 - 1].ungroupedValue));
            }
        }

        return LogicResult.None;
    }

    internal string ValueNames(uint mask) =>
        string.Join(MAX_VALUE <= 9 ? "" : ",", Enumerable.Range(1, MAX_VALUE).Where(v => HasValue(mask, v)));

    internal string CompactName(uint mask, IReadOnlyList<int> cells) =>
        ValueNames(mask) + CompactName([.. cells.Select(CellIndexToCoord)]);

    internal string CompactName(uint mask, IReadOnlyList<(int, int)> cells) =>
        ValueNames(mask) + CompactName(cells);

    internal string CompactName(IReadOnlyList<int> cells) =>
        CompactName(cells.Select(cellIndex => CellIndexToCoord(cellIndex)).ToList());

    internal string CompactName(IReadOnlyList<(int, int)> cells)
    {
        string cellSep = MAX_VALUE <= 9 ? string.Empty : ",";
        char groupSep = ',';

        if (cells.Count == 0)
        {
            return "";
        }

        if (cells.Count == 1)
        {
            return CellName(cells[0]);
        }

        if (cells.All(cell => cell.Item1 == cells[0].Item1))
        {
            // All share a row
            return $"r{cells[0].Item1 + 1}c{string.Join(cellSep, cells.Select(cell => cell.Item2 + 1).OrderBy(x => x))}";
        }

        if (cells.All(cell => cell.Item2 == cells[0].Item2))
        {
            // All share a column
            return $"r{string.Join(cellSep, cells.Select(cell => cell.Item1 + 1).OrderBy(x => x))}c{cells[0].Item2 + 1}";
        }

        List<int>[] colsPerRow = new List<int>[HEIGHT];
        for (int i = 0; i < HEIGHT; i++)
        {
            colsPerRow[i] = new();
        }
        foreach (var cell in cells)
        {
            colsPerRow[cell.Item1].Add(cell.Item2 + 1);
        }
        for (int i = 0; i < HEIGHT; i++)
        {
            colsPerRow[i].Sort();
        }

        List<string> groups = new();
        for (int i = 0; i < HEIGHT; i++)
        {
            if (colsPerRow[i].Count == 0)
            {
                continue;
            }

            List<int> rowsInGroup = new() { i + 1 };
            for (int j = i + 1; j < HEIGHT; j++)
            {
                if (colsPerRow[j].SequenceEqual(colsPerRow[i]))
                {
                    rowsInGroup.Add(j + 1);
                    colsPerRow[j].Clear();
                }
            }

            groups.Add($"r{string.Join(cellSep, rowsInGroup)}c{string.Join(cellSep, colsPerRow[i])}");
        }

        return string.Join(groupSep, groups);
    }

    private readonly struct StrongLinkDesc
    {
        public readonly string humanDesc;
        public readonly List<int> alsCells;

        public StrongLinkDesc(string humanDesc, IEnumerable<int> alsCells = null)
        {
            this.humanDesc = humanDesc;
            this.alsCells = alsCells != null ? new(alsCells) : null;
        }

        public static StrongLinkDesc Empty => new(string.Empty, null);
    }

    public uint CandidateMask(IEnumerable<int> cells)
    {
        uint mask = 0;
        foreach (int curCellIndex in cells)
        {
            mask |= board[curCellIndex];
        }
        return mask & ~valueSetMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CellIndex(int i, int j) => (i * WIDTH + j);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CellIndex((int, int) cell) => (cell.Item1 * WIDTH + cell.Item2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int) CellIndexToCoord(int cellIndex) => (cellIndex / WIDTH, cellIndex % WIDTH);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex(int cellIndex, int v) => cellIndex * MAX_VALUE + v - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex(int i, int j, int v) => (i * WIDTH + j) * MAX_VALUE + v - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex((int, int) cell, int v) => (cell.Item1 * WIDTH + cell.Item2) * MAX_VALUE + v - 1;

    internal List<int> CandidateIndexes(uint valueMask, IEnumerable<(int, int)> cells)
    {
        List<int> result = new();
        foreach (var cell in cells)
        {
            uint mask = this[cell.Item1, cell.Item2] & valueMask;
            if (mask != 0)
            {
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        result.Add(CandidateIndex(cell, v));
                    }
                }
            }
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int) CandIndexToCellAndValue(int candIndex)
    {
        return candidateToCellAndValueLookup[candIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int, int) CandIndexToCoord(int candIndex)
    {
        return candidateToCoordValueLookup[candIndex];
    }

    internal string CandIndexDesc(int candIndex)
    {
        var (i, j, v) = CandIndexToCoord(candIndex);
        return $"{v}{CellName(i, j)}";
    }

    internal bool IsCandIndexValid(int candIndex)
    {
        var (i, j, v) = CandIndexToCoord(candIndex);
        uint mask = this[i, j];
        return !IsValueSet(mask) && HasValue(mask, v);
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

    internal IEnumerable<int> CalcElims(IEnumerable<int> candIndexes)
    {
        List<int> result = null;

        foreach (int candIndex in candIndexes)
        {
            // 1) materialize the sorted, filtered weak-link list for this index
            var raw = weakLinks[candIndex];
            var cur = new List<int>(raw.Count);
            for (int k = 0, n = raw.Count; k < n; k++)
            {
                int v = raw[k];
                if (IsCandIndexValid(v))
                {
                    cur.Add(v);
                }
            }

            if (result == null)
            {
                // first list → seed the result
                result = cur;
            }
            else
            {
                // 2) merge-intersect `result` with `cur` into a new list
                var next = new List<int>(Math.Min(result.Count, cur.Count));
                int i = 0, j = 0;
                while (i < result.Count && j < cur.Count)
                {
                    int a = result[i], b = cur[j];
                    if (a < b) i++;
                    else if (b < a) j++;
                    else
                    {
                        // a == b
                        next.Add(a);
                        i++; j++;
                    }
                }

                result = next;
                if (result.Count == 0)
                {
                    // no further intersection possible
                    break;
                }
            }
        }

        // if candIndexes was empty, return an empty sequence
        return result ?? Enumerable.Empty<int>();
    }

    internal HashSet<int> CalcElims(uint clearMask, List<(int, int)> cells) =>
        CalcElims(clearMask, cells.Select(CellIndex).ToList());

    internal HashSet<int> CalcElims(uint clearMask, List<int> cells)
    {
        HashSet<int> elims = null;
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (!HasValue(clearMask, v))
            {
                continue;
            }

            var curElims = CalcElims(cells.Where(cell => HasValue(board[cell], v)).Select(cell => CandidateIndex(cell, v)));
            if (curElims != null)
            {
                if (elims == null)
                {
                    elims = curElims.ToHashSet();
                }
                else
                {
                    elims.UnionWith(curElims);
                }
            }
        }
        return elims;
    }

    internal void CalcElims(HashSet<int> outElims, uint clearMask, List<int> cellIndexes)
    {
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            if (!HasValue(clearMask, v))
            {
                continue;
            }

            var curElims = CalcElims(cellIndexes.Where(cell => HasValue(board[cell], v)).Select(cell => CandidateIndex(cell, v)));
            if (curElims != null)
            {
                outElims.UnionWith(curElims);
            }
        }
    }

    internal string DescribeElims(IEnumerable<int> elims)
    {
        List<(int, int)>[] elimsByVal = new List<(int, int)>[MAX_VALUE];
        foreach (int elimCandIndex in elims)
        {
            var (i, j, v) = CandIndexToCoord(elimCandIndex);
            elimsByVal[v - 1] ??= new();
            elimsByVal[v - 1].Add((i, j));
        }

        List<(List<int>, string)> elimDescs = new();
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            var elimCells = elimsByVal[v - 1];
            if (elimCells != null && elimCells.Count > 0)
            {
                elimCells.Sort();
                elimDescs.Add(([v], CompactName(elimCells)));
            }
        }

        // Check for elim descriptions that differ only by value
        for (int i1 = 0; i1 < elimDescs.Count; i1++)
        {
            for (int i2 = i1 + 1; i2 < elimDescs.Count; i2++)
            {
                if (elimDescs[i1].Item2 == elimDescs[i2].Item2)
                {
                    elimDescs[i1].Item1.AddRange(elimDescs[i2].Item1);
                    elimDescs.RemoveAt(i2);
                    i2--;
                }
            }
        }

        List<string> elimDescsFinal = elimDescs.Select(desc => $"-{string.Join("", desc.Item1)}{desc.Item2}").ToList();
        return string.Join(';', elimDescsFinal);
    }

    internal int NumSetValues
    {
        get
        {
            int numSetValues = 0;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    if (IsValueSet(i, j))
                    {
                        numSetValues++;
                    }
                }
            }
            return numSetValues;
        }
    }

    private LogicResult FindAIC(List<LogicalStepDesc> logicalStepDescs) => new AICSolver(this, logicalStepDescs).FindAIC();

    private LogicResult FindSimpleContradictions(List<LogicalStepDesc> logicalStepDescs)
    {
        for (int allowedValueCount = 2; allowedValueCount <= MAX_VALUE; allowedValueCount++)
        {
            ContradictionResult? bestContradiction = null;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint cellMask = this[i, j];
                    if (!IsValueSet(cellMask) && ValueCount(cellMask) == allowedValueCount)
                    {
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            uint valueMask = ValueMask(v);
                            if ((cellMask & valueMask) != 0)
                            {
                                Solver boardCopy = Clone(willRunNonSinglesLogic: false);
                                boardCopy.isBruteForcing = true;

                                List<LogicalStepDesc> contradictionSteps = logicalStepDescs != null ? new() : null;

                                string violationString = null;
                                if (boardCopy.EvaluateSetValue(i, j, v, ref violationString) == LogicResult.Invalid)
                                {
                                    logicalStepDescs?.Add(new(
                                        desc: $"If {CellName(i, j)} is set to {v} then {violationString} => -{v}{CellName(i, j)}",
                                        sourceCandidates: Enumerable.Empty<int>(),
                                        elimCandidates: CandidateIndex((i, j), v).ToEnumerable(),
                                        subSteps: null
                                    ));
                                    if (!ClearValue(i, j, v))
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    return LogicResult.Changed;
                                }

                                if (boardCopy.ConsolidateBoard(contradictionSteps) == LogicResult.Invalid)
                                {
                                    bool isTrivial = contradictionSteps != null && contradictionSteps.Count == 0;
                                    if (isTrivial)
                                    {
                                        contradictionSteps.Add(new LogicalStepDesc(
                                            desc: "For unknown reasons. This is a bug: please report this!",
                                            sourceCandidates: Enumerable.Empty<int>(),
                                            elimCandidates: Enumerable.Empty<int>()));
                                    }

                                    // Trivial contradictions will always be as "easy" or "easier" than any other contradiction.
                                    if (isTrivial || DisableFindShortestContradiction || !DisableContradictions && contradictionSteps == null)
                                    {
                                        logicalStepDescs?.Add(new(
                                            desc: $"Setting {CellName(i, j)} to {v} causes a contradiction:",
                                            sourceCandidates: Enumerable.Empty<int>(),
                                            elimCandidates: CandidateIndex((i, j), v).ToEnumerable(),
                                            subSteps: contradictionSteps
                                        ));

                                        if (!ClearValue(i, j, v))
                                        {
                                            return LogicResult.Invalid;
                                        }
                                        return LogicResult.Changed;
                                    }
                                    if (!DisableContradictions)
                                    {
                                        int changes = boardCopy.AmountCellsFilled() - this.AmountCellsFilled();
                                        if (!bestContradiction.HasValue || changes < bestContradiction.Value.Changes)
                                        {
                                            bestContradiction = new ContradictionResult(changes, boardCopy, i, j, v, contradictionSteps);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (bestContradiction.HasValue)
            {
                var contradiction = bestContradiction.Value;

                logicalStepDescs?.Add(new(
                    desc: $"Setting {CellName(contradiction.I, contradiction.J)} to {contradiction.V} causes a contradiction:",
                    sourceCandidates: Enumerable.Empty<int>(),
                    elimCandidates: CandidateIndex((contradiction.I, contradiction.J), contradiction.V).ToEnumerable(),
                    subSteps: contradiction.ContraditionSteps
                ));

                if (!ClearValue(contradiction.I, contradiction.J, contradiction.V))
                {
                    return LogicResult.Invalid;
                }
                return LogicResult.Changed;
            }
        }

        return LogicResult.None;
    }

    private int AmountCellsFilled()
    {
        int counter = 0;
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                counter += IsValueSet(i, j) ? 1 : 0;
            }
        }
        return counter;
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

            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    if (regions[i, j] != other.regions[i, j])
                    {
                        return false;
                    }
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

    private record struct ContradictionResult(
        int Changes,
        Solver BoardCopy,
        int I,
        int J,
        int V,
        List<LogicalStepDesc> ContraditionSteps);
}
