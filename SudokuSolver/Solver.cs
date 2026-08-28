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
    /// <summary>
    /// How strongly a bilocal (a value with exactly two positions in a house) may override the
    /// conflict-score branch choice in the <see cref="FindSolution"/> and
    /// <see cref="CountSolutions"/> searches, as a percentage. 0, the default, disables the bilocal
    /// tier there; 50 is the weight ISS uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately 0, and deliberately not public.</b> Enabling this is measurably *worse* on
    /// real puzzles: across the 221 non-trivial <c>iss-tune</c> puzzles it costs 7.7x total nodes at
    /// weight 50, with p90 2.56x and a worst case of 180x. It looks like a win on the 28-case corpus
    /// (0.83x total) — that corpus is too small to separate a branch-ordering rule, which is the
    /// whole reason the ISS corpus exists. See docs/branch-ordering.md.
    /// </para>
    /// <para>
    /// It survives as a knob only so the experiment is one env var away instead of a re-derivation,
    /// and so the <c>bilocalWeightPercent: 0</c> at each search call site reads as a measured
    /// decision rather than an unexplained <c>false</c>. It does not affect
    /// <see cref="TrueCandidates"/>, which has always branched with ISS's weight.
    /// </para>
    /// </remarks>
    internal long BilocalSearchWeightPercent { get; set; } = DefaultBilocalSearchWeightPercent;

    // Private data
    private uint[] board;
    private int[] regions = null;
    private List<int>[] weakLinks;
    private int totalWeakLinks = 0;

    // Grouped form of weakLinks for the brute-force hot path, in CSR layout: the targets of
    // candidate c are wlGroupedCells/wlGroupedMasks over [wlGroupedOffsets[c], wlGroupedOffsets[c+1]),
    // one entry per distinct target *cell* rather than one per target candidate. Null until compiled.
    // See CompileGroupedWeakLinks and docs/weak-link-representation.md.
    private int[] wlGroupedOffsets;
    private int[] wlGroupedCells;
    private uint[] wlGroupedMasks;
    private readonly List<Constraint> constraints;
    private readonly List<Constraint> enforceConstraints;

    // Hidden single tracking fields
    private int[] _candidateCountsPerGroupValue;
    // Bit g set == group g may now hold a hidden single and still needs checking. Packed rather
    // than a bool[] because FindHiddenSingle scans it from the start on every propagation step;
    // testing the words for zero is also the scan's own early-out, so no separate pending count
    // has to be maintained on the board-write path.
    private ulong[] _checkGroupForHiddens;

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
    // Per-cell propagation-queue map, packed: the constraint bits to OR into _constraintQueued
    // when this cell changes, stored at [cellIndex * _constraintQueued.Length]. A bitmask rather
    // than a per-cell index list so an enqueue costs one OR per word instead of a test-and-set per
    // constraint — this sits on the board-write path, the hottest code in the solver. Shared by
    // reference across clones (read-only after FinalizeConstraints); null until then.
    internal ulong[] cellToConstraintMask;

    // Per-instance: bit i set == constraint i is pending re-run due to cell changes.
    private ulong[] _constraintQueued;

    // Bit i set == constraint i's CellIndicesForPropagationQueue was null, so it runs on every
    // propagation step. OR'd into _constraintQueued at the top of the constraint stage, which is
    // cheaper than walking an index list. Shared by reference across clones (read-only after
    // FinalizeConstraints); null when no constraint wants it.
    private ulong[] _alwaysRunConstraintBits;

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
