#nullable enable
using System.Diagnostics;
using SudokuSolver;

namespace SudokuSolverBenchmark;

/// <summary>One puzzle to benchmark. Exactly one of Fpuzzles/Givens/Blank must be set.</summary>
internal sealed class BenchCase
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Fpuzzles { get; set; }
    public string? Givens { get; set; }
    public int? Blank { get; set; }
    public string[]? Constraints { get; set; }
    /// <summary>
    /// "count" (CountSolutions), "solve" (FindSolution -> 1/0), or "logical" (ConsolidateBoard ->
    /// remaining candidates).
    /// </summary>
    public string Op { get; set; } = "count";
    /// <summary>Expected result; when set, a mismatch is a validation failure.</summary>
    public long? Expected { get; set; }
    /// <summary>Solution cap for "count" (0 = uncapped).</summary>
    public long? MaxCount { get; set; }
    public bool MultiThread { get; set; }
}

internal sealed class BenchResult
{
    public string Name { get; set; } = "";
    public string Op { get; set; } = "";
    public long Result { get; set; }
    public bool Ok { get; set; }
    public double MinMs { get; set; }
    public double MedianMs { get; set; }
    public double AllocMB { get; set; }
}

/// <summary>
/// The measurement core, kept free of console/CLI concerns so the identical timing procedure can
/// be run from the native harness and from the browser WASM build. Any divergence here would make
/// a native-vs-WASM comparison meaningless, so both hosts compile this same file.
/// </summary>
internal static class BenchCore
{
    public static BenchResult Run(BenchCase c, int iterations, bool forceMultiThread)
    {
        // Warm up JIT and caches.
        RunOp(Build(c), c, forceMultiThread);

        var times = new double[iterations];
        long result = 0;
        long allocatedBytes = 0;
        var stopwatch = new Stopwatch();

        for (int i = 0; i < iterations; i++)
        {
            // A fresh solver each iteration: FindSolution mutates the board, and a clean start
            // keeps allocation measurement honest.
            Solver solver = Build(c);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            long before = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Restart();
            result = RunOp(solver, c, forceMultiThread);
            stopwatch.Stop();
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - before;

            times[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        bool ok = c.Expected is null || result == c.Expected;
        return new BenchResult
        {
            Name = c.Name,
            Op = c.Op,
            Result = result,
            Ok = ok,
            MinMs = times[0],
            MedianMs = times[iterations / 2],
            AllocMB = allocatedBytes / 1_000_000.0,
        };
    }

    public static Solver Build(BenchCase c)
    {
        IEnumerable<string>? constraints = c.Constraints;
        if (c.Fpuzzles is not null) return SolverFactory.CreateFromFPuzzles(c.Fpuzzles, constraints);
        if (c.Givens is not null) return SolverFactory.CreateFromGivens(c.Givens, constraints);
        if (c.Blank is int size) return SolverFactory.CreateBlank(size, constraints);
        throw new InvalidOperationException($"Case '{c.Name}' has no input (set fpuzzles, givens, or blank).");
    }

    public static long RunOp(Solver solver, BenchCase c, bool forceMultiThread)
    {
        bool multiThread = forceMultiThread || c.MultiThread;
        return c.Op switch
        {
            "solve" => solver.FindSolution(multiThread: multiThread) ? 1 : 0,
            "count" => solver.CountSolutions(maxSolutions: c.MaxCount ?? 0, multiThread: multiThread),
            "logical" => RunLogical(solver),
            _ => throw new InvalidOperationException($"Unknown op '{c.Op}' for case '{c.Name}' (use \"count\", \"solve\", or \"logical\")."),
        };
    }

    /// <summary>
    /// Runs human-style logic to exhaustion, the way the "solvepath" command does, and scores how
    /// far it got as the number of candidates still standing across the grid. A fully solved grid
    /// therefore scores exactly one per cell; -1 means the logic proved the board invalid.
    /// </summary>
    /// <remarks>
    /// This is deliberately a different axis from "solve"/"count", which only exercise brute
    /// force. The logical solver — <c>AICSolver</c>, <c>SolverLogic</c>, and the constraints'
    /// non-brute-force <c>StepLogic</c> — is what a setting UI leans on, and nothing else in the
    /// corpus touches it. The score is sensitive to logic changes by design: adding a technique
    /// should move it, which forces a deliberate re-baseline rather than a silent drift.
    /// </remarks>
    private static long RunLogical(Solver solver)
    {
        if (solver.ConsolidateBoard() == LogicResult.Invalid)
        {
            return -1;
        }

        long remainingCandidates = 0;
        IReadOnlyList<uint> flatBoard = solver.FlatBoard;
        for (int i = 0; i < flatBoard.Count; i++)
        {
            remainingCandidates += SolverUtility.ValueCount(flatBoard[i]);
        }
        return remainingCandidates;
    }
}
