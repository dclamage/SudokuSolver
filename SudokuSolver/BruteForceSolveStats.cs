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
}

internal sealed class BruteForceSolveStatsTracker
{
    private long guesses;
    private long valuesTried;

    public void IncrementGuesses() => Interlocked.Increment(ref guesses);

    public void IncrementValuesTried() => Interlocked.Increment(ref valuesTried);

    public BruteForceSolveStats Snapshot(TimeSpan puzzleSetupTime, TimeSpan runtime) => new()
    {
        Guesses = Interlocked.Read(ref guesses),
        ValuesTried = Interlocked.Read(ref valuesTried),
        PuzzleSetupTime = puzzleSetupTime,
        Runtime = runtime,
    };
}