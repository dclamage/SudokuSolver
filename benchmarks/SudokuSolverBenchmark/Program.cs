using System.Diagnostics;
using System.Text.Json;
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
    /// <summary>"count" (CountSolutions) or "solve" (FindSolution -> 1/0).</summary>
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
/// A lean, repeatable performance harness for the solver. Loads a curated corpus, times each
/// puzzle's solve/count (min + median over N iterations, plus allocation), validates results
/// against expected counts, and optionally diffs against a saved baseline to flag regressions.
///
/// Usage (from repo root):
///   dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- [options]
///     [corpus.json]         positional path to the corpus (default: benchmarks/corpus.json)
///     --iterations N        timed iterations per case (default 3)
///     --filter TEXT         only cases whose name or category contains TEXT
///     --multithread         force multi-threaded solving for every case
///     --save FILE           write results as JSON (use as a future baseline)
///     --baseline FILE       diff min-time against a saved baseline; flags >5% regressions
///
/// Exit codes: 0 ok, 1 validation failure, 3 perf regression vs baseline.
/// </summary>
internal static class Program
{
    private const double RegressionThresholdPercent = 5.0;

    private static int Main(string[] args)
    {
        string corpusPath = "benchmarks/corpus.json";
        string? baselinePath = null;
        string? savePath = null;
        string? filter = null;
        int iterations = 3;
        bool forceMultiThread = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline": baselinePath = args[++i]; break;
                case "--save": savePath = args[++i]; break;
                case "--filter": filter = args[++i]; break;
                case "--iterations": iterations = int.Parse(args[++i]); break;
                case "--multithread": forceMultiThread = true; break;
                default:
                    if (!args[i].StartsWith("--")) corpusPath = args[i];
                    break;
            }
        }

        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"Corpus not found: {corpusPath} (run from the repo root, or pass a path).");
            return 2;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var cases = JsonSerializer.Deserialize<List<BenchCase>>(File.ReadAllText(corpusPath), jsonOptions)!;
        if (filter is not null)
        {
            cases = cases.Where(c =>
                c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (c.Category?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        var baseline = new Dictionary<string, BenchResult>();
        if (baselinePath is not null && File.Exists(baselinePath))
        {
            foreach (var r in JsonSerializer.Deserialize<List<BenchResult>>(File.ReadAllText(baselinePath), jsonOptions)!)
            {
                baseline[r.Name] = r;
            }
        }

        Console.WriteLine($"iterations={iterations}  multithread={forceMultiThread}  cases={cases.Count}");
        Console.WriteLine($"{"name",-18}{"op",-7}{"result",13}  {"ok",-4}{"min ms",10}{"med ms",10}{"alloc MB",10}   {(baseline.Count > 0 ? "vs base" : "")}");
        Console.WriteLine(new string('-', 96));

        var results = new List<BenchResult>();
        bool anyFail = false;
        bool anyRegress = false;

        foreach (var c in cases)
        {
            BenchResult r;
            try
            {
                r = Run(c, iterations, forceMultiThread);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{c.Name,-18}{c.Op,-7}{"ERROR",13}  {"ERR",-4}{ex.Message}");
                anyFail = true;
                continue;
            }

            results.Add(r);
            if (!r.Ok) anyFail = true;

            string cmp = "";
            if (baseline.TryGetValue(c.Name, out var b) && b.MinMs > 0)
            {
                double deltaPercent = (r.MinMs - b.MinMs) / b.MinMs * 100.0;
                cmp = $"{deltaPercent,+7:0.0}%";
                if (deltaPercent > RegressionThresholdPercent)
                {
                    cmp += "  REGRESSION";
                    anyRegress = true;
                }
            }

            Console.WriteLine($"{c.Name,-18}{c.Op,-7}{r.Result,13}  {(r.Ok ? "ok" : "FAIL"),-4}{r.MinMs,10:0.00}{r.MedianMs,10:0.00}{r.AllocMB,10:0.00}   {cmp}");
        }

        Console.WriteLine(new string('-', 96));
        Console.WriteLine($"total min ms: {results.Sum(r => r.MinMs):0.0}");

        if (savePath is not null)
        {
            File.WriteAllText(savePath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"saved baseline: {savePath}");
        }

        if (anyFail)
        {
            Console.Error.WriteLine("VALIDATION FAILURES present (a result did not match its expected value).");
            return 1;
        }
        if (anyRegress)
        {
            Console.Error.WriteLine($"PERFORMANCE REGRESSIONS (> {RegressionThresholdPercent}% slower than baseline).");
            return 3;
        }
        return 0;
    }

    private static BenchResult Run(BenchCase c, int iterations, bool forceMultiThread)
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

    private static Solver Build(BenchCase c)
    {
        IEnumerable<string>? constraints = c.Constraints;
        if (c.Fpuzzles is not null) return SolverFactory.CreateFromFPuzzles(c.Fpuzzles, constraints);
        if (c.Givens is not null) return SolverFactory.CreateFromGivens(c.Givens, constraints);
        if (c.Blank is int size) return SolverFactory.CreateBlank(size, constraints);
        throw new InvalidOperationException($"Case '{c.Name}' has no input (set fpuzzles, givens, or blank).");
    }

    private static long RunOp(Solver solver, BenchCase c, bool forceMultiThread)
    {
        bool multiThread = forceMultiThread || c.MultiThread;
        return c.Op switch
        {
            "solve" => solver.FindSolution(multiThread: multiThread) ? 1 : 0,
            "count" => solver.CountSolutions(maxSolutions: c.MaxCount ?? 0, multiThread: multiThread),
            _ => throw new InvalidOperationException($"Unknown op '{c.Op}' for case '{c.Name}' (use \"count\" or \"solve\")."),
        };
    }
}
