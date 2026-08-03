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

    // Brute force solver options
    /// <summary>When brute-force operations pay for dynamic weak-link discovery.</summary>
    public WeakLinkDiscoveryMode WeakLinkDiscovery { get; set; } = DefaultWeakLinkDiscovery;
    /// <summary>
    /// Node count after which <see cref="WeakLinkDiscoveryMode.Deferred"/> gives up on the
    /// discovery-free search and restarts with discovery. Ignored by the other modes.
    /// </summary>
    public long WeakLinkDiscoveryNodeThreshold { get; set; } = DefaultWeakLinkDiscoveryNodeThreshold;
    /// <summary>
    /// How many consecutive solutions <see cref="TrueCandidates"/> may find without covering a new
    /// candidate before it abandons the undirected search and hunts the stragglers directly.
    /// Zero switches to the directed phase immediately.
    /// </summary>
    public long TrueCandidatesStallLimit { get; set; } = DefaultTrueCandidatesStallLimit;

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
    /// Snapshots the conflict-score heuristic so that an abandoned
    /// <see cref="WeakLinkDiscoveryMode.Deferred"/> attempt can be rolled back.
    /// </summary>
    /// <remarks>
    /// Search-tree clones share these arrays with this solver by reference, so an abandoned
    /// attempt's branch-ordering learning would otherwise leak into the retry. That is not a
    /// correctness problem — the scores only decide which cell to branch on next — but it makes the
    /// retry explore a different tree than an undeferred search would. Rolling back is what buys
    /// the property that a deferred retry is exactly an undeferred search, so the only cost of
    /// deferral is its bounded wasted prefix. Measured on <c>variant-orbit</c>, leaking the scores
    /// cost 5x more than the prefix itself.
    /// </remarks>
    private (int[] scores, long[] decay) SnapshotConflictState()
        => ((int[])conflictScores?.Clone(), (long[])conflictDecayState?.Clone());

    /// <summary>
    /// Restores a <see cref="SnapshotConflictState"/> result. Copies into the existing arrays rather
    /// than replacing them, because live clones hold the same references.
    /// </summary>
    private void RestoreConflictState((int[] scores, long[] decay) snapshot)
    {
        snapshot.scores?.AsSpan().CopyTo(conflictScores);
        snapshot.decay?.AsSpan().CopyTo(conflictDecayState);
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
