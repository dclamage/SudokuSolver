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

    // Cell-forcing table, CSR by source cell. Row (cfTargets[r], cfMasks[r]) means "value set
    // cfMasks[r] of this cell are exactly the ones that weakly link to candidate cfTargets[r]".
    // Cell forcing eliminates the target when cand(cell) is a subset of that mask, so rows whose
    // mask has fewer than two bits can never fire and are not stored -- which drops every ordinary
    // house link (A=v kills only peer=v) and keeps the cross-constraint deductions. Null until
    // compiled; invalidated alongside wlGroupedOffsets.
    private int[] cfOffsets;
    private int[] cfTargets;
    private uint[] cfMasks;

    // Enqueue filter for cell forcing: bit m of cell A's block is set iff some row of A has
    // cand-mask m as a subset of its S, i.e. iff a cell whose candidates are exactly m could force
    // anything at all. A board write consults it and skips pushing a cell that provably cannot
    // fire, which is the 55.7% "scanned every row, found nothing" bucket -- the only bucket that
    // pays a full row scan before finding nothing. cfCanFireWords ulongs per cell, indexed
    // [cellIndex * cfCanFireWords + (m >> 6)]. Null when MAX_VALUE is too large to tabulate, in
    // which case every write enqueues as before. Shared by reference across clones; invalidated
    // alongside cfOffsets.
    private ulong[] cfCanFire;
    private int cfCanFireWords;

    // Sharper enqueue filter, indexed by the value the write removed. Bit m of block
    // (cell * MAX_VALUE + v - 1) is set iff some row of that cell has m as a subset of its S *and*
    // does not contain v. Such a row fires now and did not fire before the write, because a row
    // whose S contains v already covered the pre-write mask. cfCanFire alone cannot see this: it
    // is exact about "does a row fire" but a firing row whose target is already eliminated
    // produces no change, which measured as 93.3% of pops. MAX_VALUE times the size of cfCanFire
    // (~47 KB per 9x9 puzzle), shared by reference across clones; null when cfCanFire is.
    private ulong[] cfNewlyFires;
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

    // Cells that lost a candidate and so may newly force. Same shape as pendingNakedSingles: a
    // plain LIFO worklist fed from the board writes, no dedup guard. Re-processing a cell is
    // idempotent, so a duplicate costs one table scan and saves the flag/queue desync that a
    // membership guard invites.
    internal readonly List<int> pendingCellForcing;

    // Index of the cell this solver instance was branched on (-1 = not a branch point).
    internal int branchCellIndex = -1;

    // How many committed branch-point assignments are in this solver's search path.
    internal int searchDepth = 0;

    // Per-cell propagation-queue map, packed: the constraint bits to OR into _constraintQueued
    // when this cell changes, stored at [cellIndex * _constraintQueued.Length]. A bitmask rather
    // than a per-cell index list so an enqueue costs one OR per word instead of a test-and-set per
    // constraint — this sits on the board-write path, the hottest code in the solver. Shared by
    // reference across clones (read-only after FinalizeConstraints); null until then.
    internal ulong[] cellToConstraintMask;

    // Per-instance: bit s set == propagation slot s is pending re-run due to cell changes.
    private ulong[] _constraintQueued;

    // Propagation slot -> index into constraints. Slots are ordered by
    // Constraint.BruteForcePropagationCost so the queue drains cheapest-first, and cover only the
    // constraints that participate in the queue at all. Shared by reference across clones
    // (read-only after FinalizeConstraints).
    private int[] _propagationSlotToConstraint;

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
