using System.Text.Json;

namespace SudokuSolverBenchmark;

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
///                           (the logical-solve cases share the category "logical")
///     --multithread         force multi-threaded solving for every case
///     --save FILE           write results as JSON (use as a future baseline)
///     --baseline FILE       diff min-time against a saved baseline; flags >5% regressions
///     --bitops              run the BitOperations vs software micro-benchmark and exit
///     --import-iss          bulk-import sigh's ISS index into a corpus and exit; see IssImport
///       --iss-index FILE      the index JSON (rows[] with puzzle_id and solutions_found)
///       --iss-dir DIR         directory of <puzzle_id>.iss files
///       --out FILE            corpus to write
///       --report FILE         per-puzzle import report to write
///       --budget-ms N         wall-clock ceiling per puzzle while validating
///       --corpus-max-ms N     only puzzles faster than this enter the corpus
///       --holdout F           fraction reserved as a never-tuned-against holdout
///       --limit N             only the first N index rows (for a smoke test)
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
        bool runBitOps = false;
        bool importIss = false;
        var issOptions = new IssImport.Options();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline": baselinePath = args[++i]; break;
                case "--save": savePath = args[++i]; break;
                case "--filter": filter = args[++i]; break;
                case "--iterations": iterations = int.Parse(args[++i]); break;
                case "--multithread": forceMultiThread = true; break;
                case "--bitops": runBitOps = true; break;
                case "--import-iss": importIss = true; break;
                case "--iss-index": issOptions.IndexPath = args[++i]; break;
                case "--iss-dir": issOptions.PuzzleDir = args[++i]; break;
                case "--out": issOptions.OutPath = args[++i]; break;
                case "--report": issOptions.ReportPath = args[++i]; break;
                case "--budget-ms": issOptions.BudgetMs = int.Parse(args[++i]); break;
                case "--corpus-max-ms": issOptions.CorpusMaxMs = int.Parse(args[++i]); break;
                case "--holdout": issOptions.HoldoutFraction = double.Parse(args[++i]); break;
                case "--limit": issOptions.Limit = int.Parse(args[++i]); break;
                default:
                    if (!args[i].StartsWith("--")) corpusPath = args[i];
                    break;
            }
        }

        if (runBitOps)
        {
            Console.WriteLine(BitOpsBench.Format(BitOpsBench.Run()));
            return 0;
        }

        if (importIss)
        {
            return IssImport.Run(issOptions);
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
        Console.WriteLine($"{"name",-24}{"op",-7}{"result",13}  {"ok",-4}{"min ms",10}{"med ms",10}{"alloc MB",10}   {(baseline.Count > 0 ? "vs base" : "")}");
        Console.WriteLine(new string('-', 96));

        var results = new List<BenchResult>();
        var ratios = new List<(string Name, string Category, double Ratio, double BaseMs)>();
        bool anyFail = false;
        bool anyRegress = false;

        foreach (var c in cases)
        {
            BenchResult r;
            try
            {
                r = BenchCore.Run(c, iterations, forceMultiThread);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{c.Name,-24}{c.Op,-7}{"ERROR",13}  {"ERR",-4}{ex.Message}");
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
                ratios.Add((c.Name, string.IsNullOrEmpty(c.Category) ? "(none)" : c.Category, r.MinMs / b.MinMs, b.MinMs));
                if (deltaPercent > RegressionThresholdPercent)
                {
                    cmp += "  REGRESSION";
                    anyRegress = true;
                }
            }

            Console.WriteLine($"{c.Name,-24}{c.Op,-7}{r.Result,13}  {(r.Ok ? "ok" : "FAIL"),-4}{r.MinMs,10:0.00}{r.MedianMs,10:0.00}{r.AllocMB,10:0.00}   {cmp}");
        }

        Console.WriteLine(new string('-', 96));
        double totalMs = results.Sum(r => r.MinMs);
        Console.WriteLine($"total min ms: {totalMs:0.0}");

        // Case times span five orders of magnitude here, so the grand total is a sum dominated by
        // whichever few cases are slowest — a change that helps only those looks like a
        // corpus-wide win. Print what is actually driving it so that cannot pass unnoticed.
        if (totalMs > 0 && results.Count > 1)
        {
            var byCategory = cases
                .Where(c => results.Any(r => r.Name == c.Name))
                .GroupBy(c => string.IsNullOrEmpty(c.Category) ? "(none)" : c.Category)
                .Select(g => (Category: g.Key,
                              Ms: g.Sum(c => results.First(r => r.Name == c.Name).MinMs)))
                .OrderByDescending(g => g.Ms)
                .ToList();

            Console.WriteLine("share of total:");
            foreach (var (category, ms) in byCategory)
            {
                Console.WriteLine($"  {category,-22}{ms,10:0.0} ms{ms / totalMs * 100,8:0.0}%");
            }

            var top = results.OrderByDescending(r => r.MinMs).First();
            double topShare = top.MinMs / totalMs * 100;
            Console.WriteLine($"  slowest single case: {top.Name} at {topShare:0.0}% of total");
            if (topShare > 25)
            {
                Console.WriteLine("  NOTE: one case exceeds a quarter of the total — read per-case deltas, not the total.");
            }
        }

        ReportRatioSummary(ratios);

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

    /// <summary>
    /// Minimum baseline time for a case's ratio to be trusted. Below this, timer granularity and
    /// scheduling jitter dominate, and a ratio built from two sub-millisecond numbers is noise
    /// given equal weight.
    /// </summary>
    private const double RatioFloorMs = 1.0;

    /// <summary>
    /// Summarises the per-case new/baseline ratios.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the number to read, not <c>total min ms</c>. The total is a sum over case times
    /// spanning five orders of magnitude, so it weights each case by its duration: a change that
    /// helps only the two slowest cases reads as a corpus-wide win, and adding slow cases of one
    /// constraint family silently reweights every later comparison toward that family.
    /// </para>
    /// <para>
    /// The aggregate is a <b>geometric</b> mean because these are ratios. It is the only mean that
    /// is symmetric under swapping the arms — rerun with the arms exchanged and every statistic
    /// here inverts exactly — and it cannot be dragged around by one case that happened to be slow.
    /// An arithmetic mean of ratios is biased upward and would call a wash an improvement.
    /// </para>
    /// <para>
    /// The distribution matters as much as the centre. A change that is 0.95x across the board is a
    /// different animal from one that is 0.5x on three cases and 1.1x on the rest, and only the
    /// spread and the better/worse counts tell them apart.
    /// </para>
    /// </remarks>
    private static void ReportRatioSummary(List<(string Name, string Category, double Ratio, double BaseMs)> ratios)
    {
        if (ratios.Count == 0)
        {
            return;
        }

        var usable = ratios.Where(x => x.BaseMs >= RatioFloorMs && x.Ratio > 0).ToList();
        int excluded = ratios.Count - usable.Count;
        if (usable.Count == 0)
        {
            Console.WriteLine($"vs baseline: all {excluded} cases below the {RatioFloorMs:0.#} ms floor; no reliable ratios.");
            return;
        }

        double logSum = 0;
        foreach (var u in usable)
        {
            logSum += Math.Log(u.Ratio);
        }
        double geoMean = Math.Exp(logSum / usable.Count);

        var sorted = usable.OrderBy(u => u.Ratio).ToList();
        double Quantile(double q)
        {
            double pos = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(pos);
            int hi = (int)Math.Ceiling(pos);
            return sorted[lo].Ratio + (sorted[hi].Ratio - sorted[lo].Ratio) * (pos - lo);
        }

        int better = usable.Count(u => u.Ratio < 0.99);
        int worse = usable.Count(u => u.Ratio > 1.01);

        Console.WriteLine();
        Console.WriteLine($"vs baseline over {usable.Count} cases (per-case ratios, unweighted by duration):");
        Console.WriteLine($"  geomean {geoMean,6:0.000}x ({(geoMean - 1) * 100,+5:0.0}%)   median {Quantile(0.5),6:0.000}x");
        Console.WriteLine($"  p10 {Quantile(0.1),6:0.000}x   p90 {Quantile(0.9),6:0.000}x");
        Console.WriteLine($"  best {sorted[0].Ratio,6:0.000}x {sorted[0].Name}   worst {sorted[^1].Ratio,6:0.000}x {sorted[^1].Name}");
        Console.WriteLine($"  {better} better, {worse} worse, {usable.Count - better - worse} within 1%");
        if (excluded > 0)
        {
            Console.WriteLine($"  ({excluded} case(s) excluded: baseline under {RatioFloorMs:0.#} ms, too short to time reliably)");
        }

        // Per-case ratios remove *duration* weighting but not *composition* weighting: N similar
        // cases still cast N votes. Grouping by category and giving each group one vote caps that,
        // and printing the per-category figures shows where a headline number came from.
        var groups = usable
            .GroupBy(u => u.Category)
            .Select(g => (Category: g.Key, Count: g.Count(), GeoMean: GeoMean(g.Select(u => u.Ratio))))
            .OrderBy(g => g.GeoMean)
            .ToList();

        if (groups.Count > 1)
        {
            Console.WriteLine($"  by category (each category weighted equally: {GeoMean(groups.Select(g => g.GeoMean)),6:0.000}x):");
            foreach (var (category, count, mean) in groups)
            {
                Console.WriteLine($"    {category,-22}{mean,7:0.000}x  n={count}");
            }

            int biggest = groups.Max(g => g.Count);
            if (biggest * 2 > usable.Count)
            {
                Console.WriteLine($"    NOTE: one category is {biggest} of {usable.Count} cases — the unweighted geomean is mostly measuring it.");
            }
        }
    }

    private static double GeoMean(IEnumerable<double> values)
    {
        double logSum = 0;
        int n = 0;
        foreach (double v in values)
        {
            if (v <= 0)
            {
                continue;
            }
            logSum += Math.Log(v);
            n++;
        }
        return n == 0 ? double.NaN : Math.Exp(logSum / n);
    }
}
