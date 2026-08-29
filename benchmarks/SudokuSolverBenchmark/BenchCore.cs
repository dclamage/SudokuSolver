#nullable enable
using System.Diagnostics;
using SudokuSolver;

namespace SudokuSolverBenchmark;

/// <summary>One puzzle to benchmark. Exactly one of Fpuzzles/Givens/Iss/Blank must be set.</summary>
internal sealed class BenchCase
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Fpuzzles { get; set; }
    public string? Givens { get; set; }
    /// <summary>Interactive Sudoku Solver puzzle text; see <see cref="IssImport"/>.</summary>
    public string? Iss { get; set; }
    public int? Blank { get; set; }
    public string[]? Constraints { get; set; }
    /// <summary>
    /// "count" (CountSolutions), "solve" (FindSolution -> 1/0), "logical" (ConsolidateBoard ->
    /// remaining candidates), "truecandidates" (TrueCandidates -> summed capped counts), or
    /// "estimate" (EstimateSolutions -> samples completed).
    /// </summary>
    public string Op { get; set; } = "count";
    /// <summary>Expected result; when set, a mismatch is a validation failure.</summary>
    public long? Expected { get; set; }
    /// <summary>Solution cap for "count" (0 = uncapped).</summary>
    public long? MaxCount { get; set; }
    /// <summary>Per-candidate solution cap for "truecandidates" (default 8, as the UI uses).</summary>
    public long? NumSolutionsCap { get; set; }
    /// <summary>Sample count for "estimate" (default 200).</summary>
    public long? EstimateIterations { get; set; }
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
    /// <summary>
    /// Search nodes expanded, median over the timed iterations. Exact and machine-independent, so
    /// it is the metric for a pruning change: see <see cref="Solver.NodesVisited"/>. Deterministic
    /// ops give the same figure every iteration; the "estimate" op samples randomly and does not.
    /// </summary>
    public long Nodes { get; set; }
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
        var nodes = new long[iterations];
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
            // Read off the same solver the op ran on: the counter is shared with every clone in
            // the search tree, so nested searches are already included.
            nodes[i] = solver.NodesVisited;
        }

        Array.Sort(times);
        Array.Sort(nodes);
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
            Nodes = nodes[iterations / 2],
        };
    }

    /// <summary>
    /// Runs the true-candidates scan and scores it as the sum of per-candidate solution counts,
    /// each clamped to the cap. Invalid boards score -1.
    /// </summary>
    /// <remarks>
    /// This is the operation a setting UI actually leans on: it runs on every grid edit, usually
    /// against a board that is still under-constrained and has many solutions — which is a very
    /// different workload from proving a finished puzzle unique. The summed count is a strict
    /// validation signal: any change in candidate counts anywhere moves it.
    /// </remarks>
    private static long RunTrueCandidates(Solver solver, BenchCase c, bool multiThread)
    {
        long[] perCandidate = solver.TrueCandidates(
            multiThread: multiThread,
            numSolutionsCap: c.NumSolutionsCap ?? 8);

        if (perCandidate is null)
        {
            return -1;
        }

        // The solver returns raw counts; callers clamp (see WebsocketListener.SendTrueCandidates).
        // Clamping here is also what makes the score deterministic — the raw totals depend on how
        // many solutions the search happened to enumerate before every candidate was covered.
        long cap = c.NumSolutionsCap ?? 8;
        long total = 0;
        foreach (long n in perCandidate)
        {
            total += Math.Min(n, cap);
        }
        return total;
    }

    /// <summary>
    /// Runs the Monte-Carlo solution-count estimator and scores it as the number of samples
    /// completed.
    /// </summary>
    /// <remarks>
    /// The estimate itself is stochastic, so it cannot be an expected value; the sample count can,
    /// and it still catches a path that short-circuits or throws. What these cases are really for
    /// is time and allocation: each sample clones a child per open candidate and keeps only one,
    /// so allocation scales with sample count — which matters because the browser exposes
    /// estimation as a long-running operation.
    /// </remarks>
    private static long RunEstimate(Solver solver, BenchCase c, bool multiThread)
    {
        long iterations = c.EstimateIterations ?? 200;
        long completed = 0;
        solver.EstimateSolutions(
            iterations,
            progressData => completed = progressData.iterations,
            multiThread: multiThread);
        return completed;
    }

    public static Solver Build(BenchCase c)
    {
        IEnumerable<string>? constraints = c.Constraints;
        if (c.Fpuzzles is not null) return SolverFactory.CreateFromFPuzzles(c.Fpuzzles, constraints);
        if (c.Givens is not null) return SolverFactory.CreateFromGivens(c.Givens, constraints);
        if (c.Iss is not null) return SolverFactory.CreateFromIss(c.Iss, constraints);
        if (c.Blank is int size) return SolverFactory.CreateBlank(size, constraints);
        throw new InvalidOperationException($"Case '{c.Name}' has no input (set fpuzzles, givens, iss, or blank).");
    }

    public static long RunOp(Solver solver, BenchCase c, bool forceMultiThread)
    {
        bool multiThread = forceMultiThread || c.MultiThread;
        return c.Op switch
        {
            "solve" => solver.FindSolution(multiThread: multiThread) ? 1 : 0,
            "count" => solver.CountSolutions(maxSolutions: c.MaxCount ?? 0, multiThread: multiThread),
            "logical" => RunLogical(solver),
            "truecandidates" => RunTrueCandidates(solver, c, multiThread),
            "estimate" => RunEstimate(solver, c, multiThread),
            _ => throw new InvalidOperationException($"Unknown op '{c.Op}' for case '{c.Name}' (use \"count\", \"solve\", \"logical\", \"truecandidates\", or \"estimate\")."),
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
