namespace SudokuSolver;

public partial class Solver
{
    // Public constants
    public readonly int WIDTH;
    public readonly int HEIGHT;
    public readonly int MAX_VALUE;
    public readonly uint ALL_VALUES_MASK;
    public readonly int NUM_CELLS;
    public readonly int NUM_CANDIDATES;
    public readonly int[][][] combinations;

    // Public metadata
    public string Title { get; init; }
    public string Author { get; init; }
    public string Rules { get; init; }
    public Dictionary<string, object> customInfo;

    // Logical solver options
    public bool DisableTuples { get; set; } = false;
    public bool DisablePointing { get; set; } = false;
    public bool DisableFishes { get; set; } = false;
    public bool DisableWings { get; set; } = false;
    public bool DisableAIC { get; set; } = false;
    public bool DisableContradictions { get; set; } = false;
    public bool DisableFindShortestContradiction { get; set; } = false;

    // Private data
    private uint[] board;
    private int[] regions = null;
    private List<int>[] weakLinks;
    private int totalWeakLinks = 0;
    private readonly List<Constraint> constraints;
    private readonly List<Constraint> enforceConstraints;

    // Hidden single tracking fields
    private int[] _candidateCountsPerGroupValue;
    private bool[] _checkGroupForHiddens;
    private int _numGroupsNeedingHiddenCheck;

    // How many constraints currently have _constraintQueued[i] == true.
    private int _numConstraintsQueued;

    // Private state
    private bool isInSetValue = false;
    private bool isBruteForcing = false;
    // Set when a solver belongs to a brute-force pool. The owner reference — rather than a bare
    // flag — lets a release into the wrong invocation's pool be detected instead of silently
    // corrupting that pool's free list.
    private object pooledBruteForceSolverOwner = null;
    private bool isPooledBruteForceSolverRented = false;
    private bool isInvalid;
    private int unsetCellsCount;
    private readonly List<int> pendingNakedSingles;

    // Private lookups
    private (int, int)[] candidateToCellAndValueLookup;
    private (int, int, int)[] candidateToCoordValueLookup;
    // Returns whether two cells cannot be the same value
    private bool[] seenMap;
    private readonly SumConstraintRegistry sumConstraints;

    // Conflict-score heuristic: shared across all clones in one search tree via reference assignment.
    // Cells that repeatedly cause contradictions get higher scores and are branched on first.
    internal int[] conflictScores;
    // Shared decay counter: [0] = total increments since last decay, [1] = next decay threshold.
    // Halve all conflict scores every CONFLICT_DECAY_INTERVAL increments (VSIDS-style decay).
    internal long[] conflictDecayState;
    private const long CONFLICT_DECAY_INTERVAL = 1 << 14; // 16 384 increments per decay step

    // Index of the cell this solver instance was branched on (-1 = not a branch point).
    internal int branchCellIndex = -1;

    // How many committed branch-point assignments are in this solver's search path.
    internal int searchDepth = 0;

    // Propagation queue: maps cell index → list of constraint indices that watch that cell.
    // Shared by reference across all clones (read-only after FinalizeConstraints).
    internal int[][] cellToConstraintIndices;

    // Per-instance: which constraints are pending re-run due to cell changes.
    private bool[] _constraintQueued;

    // Constraint indices whose CellIndicesForPropagationQueue was null (run every propagation step).
    // Shared by reference across clones (read-only after FinalizeConstraints).
    private int[] _alwaysRunConstraintIndices;

    // Cell index that caused a contradiction in the most-recently-discarded child solver.
    // Set by FindSolutionInternal/CountSolutionsInternal when a branch fails.
    internal int _lastContradictionCellIndex = -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void IncrementConflictScore(int cellIndex)
    {
        Interlocked.Increment(ref conflictScores[cellIndex]);
        if (conflictDecayState != null)
        {
            long n = Interlocked.Increment(ref conflictDecayState[0]);
            // One thread wins the decay when total hits each multiple of the interval.
            if ((n & (CONFLICT_DECAY_INTERVAL - 1)) == 0)
            {
                for (int i = 0; i < NUM_CELLS; i++)
                {
                    // Approximate halving — slight races in multi-threaded mode are acceptable.
                    Volatile.Write(ref conflictScores[i], conflictScores[i] >> 1);
                }
            }
        }
    }

    /// <summary>
    /// Groups which cannot contain more than one of the same digit.
    /// This will at least contain all rows, columns, and boxes.
    /// Will also contain any groups from constraints (such as killer cages).
    /// </summary>
    public List<SudokuGroup> Groups { get; }
    private List<SudokuGroup> maxValueGroups = null;
    private List<SudokuGroup> smallGroupsBySize = null;

    /// <summary>
    /// Maps a cell to the list of groups which contain that cell.
    /// </summary>
    public List<SudokuGroup>[] CellToGroupsLookup { get; }
}
