namespace SudokuSolver;

/// <summary>
/// Summary metrics from the most recent brute-force search operation.
/// </summary>
public sealed class BruteForceSolveStats
{
    /// <summary>
    /// Gets the number of real branch points encountered during the search.
    /// </summary>
    public required long Guesses { get; init; }

    /// <summary>
    /// Gets the number of value assignments executed by the search, including forced singles.
    /// </summary>
    public required long ValuesTried { get; init; }

    /// <summary>
    /// Gets the time spent preparing the puzzle for brute-force search.
    /// </summary>
    public required TimeSpan PuzzleSetupTime { get; init; }

    /// <summary>
    /// Gets the time spent in the actual brute-force search after setup completed.
    /// </summary>
    public required TimeSpan Runtime { get; init; }

    /// <summary>
    /// Gets the bytes allocated while preparing the puzzle for brute-force search.
    /// </summary>
    public required long PuzzleSetupAllocatedBytes { get; init; }

    /// <summary>
    /// Gets the bytes allocated during the actual brute-force search after setup completed.
    /// </summary>
    public required long RuntimeAllocatedBytes { get; init; }

    /// <summary>
    /// Gets the time spent initializing per-search setup state.
    /// </summary>
    public TimeSpan SetupStateInitializationTime { get; init; }

    /// <summary>
    /// Gets the time spent cloning the root puzzle before brute-force setup.
    /// </summary>
    public TimeSpan SetupCloneTime { get; init; }

    /// <summary>
    /// Gets the time spent discovering setup weak links.
    /// </summary>
    public TimeSpan SetupWeakLinkDiscoveryTime { get; init; }

    /// <summary>
    /// Gets the time spent running the final setup propagation pass.
    /// </summary>
    public TimeSpan SetupFinalPropagationTime { get; init; }

    /// <summary>
    /// Gets the time spent initializing brute-force branch pools.
    /// </summary>
    public TimeSpan SetupPoolInitializationTime { get; init; }

    /// <summary>
    /// Gets setup time not covered by the explicit setup timing buckets.
    /// </summary>
    public TimeSpan SetupOtherTime
    {
        get
        {
            TimeSpan measured = SetupStateInitializationTime + SetupCloneTime + SetupWeakLinkDiscoveryTime +
                SetupFinalPropagationTime + SetupPoolInitializationTime;
            return PuzzleSetupTime > measured ? PuzzleSetupTime - measured : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets the time spent in the initial propagation run inside weak-link discovery.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryInitialPropagationTime { get; init; }

    /// <summary>
    /// Gets the time spent cloning scratch solvers inside weak-link discovery.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryScratchCloneTime { get; init; }

    /// <summary>
    /// Gets the time spent copying root state into the scratch solver inside weak-link discovery.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryCopyTime { get; init; }

    /// <summary>
    /// Gets the time spent setting probe values inside weak-link discovery.
    /// </summary>
    public TimeSpan WeakLinkDiscoverySetValueTime { get; init; }

    /// <summary>
    /// Gets the time spent propagating probe values inside weak-link discovery.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryProbePropagationTime { get; init; }

    /// <summary>
    /// Gets the time spent scanning probe eliminations and registering weak links.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryLinkScanTime { get; init; }

    /// <summary>
    /// Gets weak-link discovery time not covered by the explicit discovery timing buckets.
    /// </summary>
    public TimeSpan WeakLinkDiscoveryOtherTime
    {
        get
        {
            TimeSpan measured = WeakLinkDiscoveryInitialPropagationTime + WeakLinkDiscoveryScratchCloneTime +
                WeakLinkDiscoveryCopyTime + WeakLinkDiscoverySetValueTime + WeakLinkDiscoveryProbePropagationTime +
                WeakLinkDiscoveryLinkScanTime;
            return SetupWeakLinkDiscoveryTime > measured ? SetupWeakLinkDiscoveryTime - measured : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets the number of passes run by dynamic weak-link discovery.
    /// </summary>
    public long WeakLinkDiscoveryPasses { get; init; }

    /// <summary>
    /// Gets the number of candidate probes run by dynamic weak-link discovery.
    /// </summary>
    public long WeakLinkDiscoveryProbes { get; init; }

    /// <summary>
    /// Gets the number of candidate probes that produced a contradiction.
    /// </summary>
    public long WeakLinkDiscoveryInvalidProbes { get; init; }

    /// <summary>
    /// Gets the number of directional weak links added by dynamic weak-link discovery.
    /// </summary>
    public long WeakLinkDiscoveryLinksAdded { get; init; }
}

internal sealed class BruteForceSolveStatsTracker
{
    private long guesses;
    private long valuesTried;
    private long setupStateInitializationTicks;
    private long setupCloneTicks;
    private long setupWeakLinkDiscoveryTicks;
    private long setupFinalPropagationTicks;
    private long setupPoolInitializationTicks;
    private long weakLinkDiscoveryInitialPropagationTicks;
    private long weakLinkDiscoveryScratchCloneTicks;
    private long weakLinkDiscoveryCopyTicks;
    private long weakLinkDiscoverySetValueTicks;
    private long weakLinkDiscoveryProbePropagationTicks;
    private long weakLinkDiscoveryLinkScanTicks;
    private long weakLinkDiscoveryPasses;
    private long weakLinkDiscoveryProbes;
    private long weakLinkDiscoveryInvalidProbes;
    private long weakLinkDiscoveryLinksAdded;

    public void IncrementGuesses() => Interlocked.Increment(ref guesses);

    public void IncrementValuesTried() => Interlocked.Increment(ref valuesTried);

    public void AddSetupStateInitializationTime(long startTimestamp) => AddElapsedTicks(ref setupStateInitializationTicks, startTimestamp);

    public void AddSetupCloneTime(long startTimestamp) => AddElapsedTicks(ref setupCloneTicks, startTimestamp);

    public void AddSetupWeakLinkDiscoveryTime(long startTimestamp) => AddElapsedTicks(ref setupWeakLinkDiscoveryTicks, startTimestamp);

    public void AddSetupFinalPropagationTime(long startTimestamp) => AddElapsedTicks(ref setupFinalPropagationTicks, startTimestamp);

    public void AddSetupPoolInitializationTime(long startTimestamp) => AddElapsedTicks(ref setupPoolInitializationTicks, startTimestamp);

    public void AddWeakLinkDiscoveryInitialPropagationTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoveryInitialPropagationTicks, startTimestamp);

    public void AddWeakLinkDiscoveryScratchCloneTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoveryScratchCloneTicks, startTimestamp);

    public void AddWeakLinkDiscoveryCopyTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoveryCopyTicks, startTimestamp);

    public void AddWeakLinkDiscoverySetValueTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoverySetValueTicks, startTimestamp);

    public void AddWeakLinkDiscoveryProbePropagationTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoveryProbePropagationTicks, startTimestamp);

    public void AddWeakLinkDiscoveryLinkScanTime(long startTimestamp) => AddElapsedTicks(ref weakLinkDiscoveryLinkScanTicks, startTimestamp);

    public void IncrementWeakLinkDiscoveryPasses() => Interlocked.Increment(ref weakLinkDiscoveryPasses);

    public void IncrementWeakLinkDiscoveryProbes() => Interlocked.Increment(ref weakLinkDiscoveryProbes);

    public void IncrementWeakLinkDiscoveryInvalidProbes() => Interlocked.Increment(ref weakLinkDiscoveryInvalidProbes);

    public void AddWeakLinkDiscoveryLinksAdded(long linksAdded) => Interlocked.Add(ref weakLinkDiscoveryLinksAdded, linksAdded);

    public BruteForceSolveStats Snapshot(TimeSpan puzzleSetupTime, TimeSpan runtime, long puzzleSetupAllocatedBytes, long runtimeAllocatedBytes) => new()
    {
        Guesses = Interlocked.Read(ref guesses),
        ValuesTried = Interlocked.Read(ref valuesTried),
        PuzzleSetupTime = puzzleSetupTime,
        Runtime = runtime,
        PuzzleSetupAllocatedBytes = puzzleSetupAllocatedBytes,
        RuntimeAllocatedBytes = runtimeAllocatedBytes,
        SetupStateInitializationTime = TimestampTicksToTimeSpan(Interlocked.Read(ref setupStateInitializationTicks)),
        SetupCloneTime = TimestampTicksToTimeSpan(Interlocked.Read(ref setupCloneTicks)),
        SetupWeakLinkDiscoveryTime = TimestampTicksToTimeSpan(Interlocked.Read(ref setupWeakLinkDiscoveryTicks)),
        SetupFinalPropagationTime = TimestampTicksToTimeSpan(Interlocked.Read(ref setupFinalPropagationTicks)),
        SetupPoolInitializationTime = TimestampTicksToTimeSpan(Interlocked.Read(ref setupPoolInitializationTicks)),
        WeakLinkDiscoveryInitialPropagationTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoveryInitialPropagationTicks)),
        WeakLinkDiscoveryScratchCloneTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoveryScratchCloneTicks)),
        WeakLinkDiscoveryCopyTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoveryCopyTicks)),
        WeakLinkDiscoverySetValueTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoverySetValueTicks)),
        WeakLinkDiscoveryProbePropagationTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoveryProbePropagationTicks)),
        WeakLinkDiscoveryLinkScanTime = TimestampTicksToTimeSpan(Interlocked.Read(ref weakLinkDiscoveryLinkScanTicks)),
        WeakLinkDiscoveryPasses = Interlocked.Read(ref weakLinkDiscoveryPasses),
        WeakLinkDiscoveryProbes = Interlocked.Read(ref weakLinkDiscoveryProbes),
        WeakLinkDiscoveryInvalidProbes = Interlocked.Read(ref weakLinkDiscoveryInvalidProbes),
        WeakLinkDiscoveryLinksAdded = Interlocked.Read(ref weakLinkDiscoveryLinksAdded),
    };

    private static void AddElapsedTicks(ref long ticks, long startTimestamp)
    {
        Interlocked.Add(ref ticks, Stopwatch.GetTimestamp() - startTimestamp);
    }

    private static TimeSpan TimestampTicksToTimeSpan(long ticks) => TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
}