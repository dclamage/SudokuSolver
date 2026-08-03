#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SudokuSolver;

namespace SudokuSolverBenchmark;

/// <summary>
/// Bulk-imports puzzles from sigh's ISS index (https://sigh.github.io/iss-sudoku-index/) into a
/// benchmark corpus, validating every import against the solution count ISS recorded for it.
/// </summary>
/// <remarks>
/// The point of this is corpus size. The hand-maintained corpus is 28 cases, which is far too few
/// to tune a heuristic against without over-fitting; the index is 1,671 puzzles that each ship a
/// known answer, so an import can check itself. Coverage is partial by design — ISS models much of
/// its library with a general constraint DSL that has no counterpart here, and
/// <see cref="IssParser"/> raises <see cref="IssUnsupportedConstraintException"/> rather than
/// mistranslating, so those simply do not import.
///
/// A puzzle only enters the corpus if our own count *equals* ISS's. A disagreement is never
/// written out; it is reported, because it means either the parser translated something wrongly or
/// the solver is wrong, and both are bugs worth a person's attention.
/// </remarks>
internal static class IssImport
{
    /// <summary>Fraction of imported puzzles reserved as a never-tuned-against holdout.</summary>
    private const double DefaultHoldoutFraction = 0.30;

    /// <summary>
    /// Wall-clock ceiling per puzzle during import. Puzzles slower than this are reported as
    /// timeouts and left out: an unfinished count cannot be validated.
    /// </summary>
    private const int DefaultBudgetMs = 10_000;

    /// <summary>
    /// Only puzzles that counted in under this go into the corpus, so a full corpus run stays
    /// usable. Slower agreeing puzzles are listed in the report for deliberate promotion later.
    /// </summary>
    private const int DefaultCorpusMaxMs = 2_000;

    internal sealed class Options
    {
        public string IndexPath = "";
        public string PuzzleDir = "";
        public string? OutPath;
        public string? ReportPath;
        public int BudgetMs = DefaultBudgetMs;
        public int CorpusMaxMs = DefaultCorpusMaxMs;
        public double HoldoutFraction = DefaultHoldoutFraction;
        public int Limit;
    }

    private sealed class IssIndex
    {
        [JsonPropertyName("rows")] public List<IssRow> Rows { get; set; } = [];
    }

    private sealed class IssRow
    {
        [JsonPropertyName("puzzle_id")] public string PuzzleId { get; set; } = "";
        [JsonPropertyName("puzzle_title")] public string? PuzzleTitle { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("constraint_types")] public string[] ConstraintTypes { get; set; } = [];
        [JsonPropertyName("search_completed")] public bool? SearchCompleted { get; set; }
        [JsonPropertyName("hit_cap")] public bool? HitCap { get; set; }
        [JsonPropertyName("unique_solution")] public bool? UniqueSolution { get; set; }
        // Null on rows ISS could not run at all.
        [JsonPropertyName("solutions_found")] public long? SolutionsFound { get; set; }
        [JsonPropertyName("guesses")] public long? Guesses { get; set; }
        [JsonPropertyName("solve_ms")] public double? SolveMs { get; set; }
    }

    /// <summary>What happened to one index row.</summary>
    private enum Outcome
    {
        /// <summary>ISS itself has no exact count for this puzzle, so there is nothing to check against.</summary>
        NoExactCount,
        /// <summary>The .iss file was not in the puzzle directory.</summary>
        Missing,
        /// <summary>A constraint this solver has no equivalent for.</summary>
        Unsupported,
        /// <summary>The parser rejected the file for some other reason — worth looking at.</summary>
        ParseError,
        /// <summary>The count did not finish inside the budget.</summary>
        Timeout,
        /// <summary>The solve threw.</summary>
        SolveError,
        /// <summary>Our count disagrees with ISS's. A bug somewhere; never imported.</summary>
        Disagree,
        /// <summary>Our count matches ISS's.</summary>
        Agree,
    }

    private sealed class ImportRecord
    {
        public string PuzzleId { get; set; } = "";
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string Outcome { get; set; } = "";
        /// <summary>The blocking constraint name, or the exception message.</summary>
        public string? Reason { get; set; }
        public long IssSolutions { get; set; }
        public long OurSolutions { get; set; }
        public double IssMs { get; set; }
        public double OurMs { get; set; }
        public long IssGuesses { get; set; }
        public string[]? ConstraintTypes { get; set; }
        /// <summary>"tune", "holdout", or null when the puzzle did not make it into the corpus.</summary>
        public string? Split { get; set; }
        public bool InCorpus { get; set; }
    }

    public static int Run(Options options)
    {
        if (!File.Exists(options.IndexPath))
        {
            Console.Error.WriteLine($"ISS index not found: {options.IndexPath}");
            return 2;
        }
        if (!Directory.Exists(options.PuzzleDir))
        {
            Console.Error.WriteLine($"ISS puzzle directory not found: {options.PuzzleDir}");
            return 2;
        }

        var index = JsonSerializer.Deserialize<IssIndex>(File.ReadAllText(options.IndexPath))!;
        List<IssRow> rows = index.Rows;
        if (options.Limit > 0)
        {
            rows = rows.Take(options.Limit).ToList();
        }

        Console.WriteLine($"rows={rows.Count}  budget={options.BudgetMs}ms  corpusMax={options.CorpusMaxMs}ms  holdout={options.HoldoutFraction:0.00}");
        Console.WriteLine();

        var records = new List<ImportRecord>();
        var cases = new List<BenchCase>();
        var unsupportedTally = new Dictionary<string, int>();
        var outcomeTally = new Dictionary<Outcome, int>();
        int processed = 0;

        foreach (IssRow row in rows)
        {
            ImportRecord record = Import(row, options, cases);
            records.Add(record);

            var outcome = Enum.Parse<Outcome>(record.Outcome);
            outcomeTally[outcome] = outcomeTally.GetValueOrDefault(outcome) + 1;
            if (outcome == Outcome.Unsupported && record.Reason is not null)
            {
                unsupportedTally[record.Reason] = unsupportedTally.GetValueOrDefault(record.Reason) + 1;
            }

            // Disagreements are the whole reason this validates itself; surface them immediately
            // rather than only in the report, since each one needs explaining.
            if (outcome == Outcome.Disagree)
            {
                Console.WriteLine($"DISAGREE {row.PuzzleId,-14} iss={record.IssSolutions} ours={record.OurSolutions}  \"{row.PuzzleTitle}\"");
            }
            else if (outcome == Outcome.ParseError)
            {
                Console.WriteLine($"PARSE-ERR {row.PuzzleId,-13} {record.Reason}");
            }

            if (++processed % 100 == 0)
            {
                Console.WriteLine($"  ... {processed}/{rows.Count}  agree={outcomeTally.GetValueOrDefault(Outcome.Agree)}  corpus={cases.Count}");
            }
        }

        PrintSummary(records, cases, outcomeTally, unsupportedTally, options);

        // camelCase to match the hand-maintained corpus.json, which is read with the same
        // case-insensitive options either way.
        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        if (options.OutPath is not null)
        {
            File.WriteAllText(options.OutPath, JsonSerializer.Serialize(cases, writeOptions));
            Console.WriteLine($"wrote corpus: {options.OutPath} ({cases.Count} cases)");
        }
        if (options.ReportPath is not null)
        {
            File.WriteAllText(options.ReportPath, JsonSerializer.Serialize(records, writeOptions));
            Console.WriteLine($"wrote report: {options.ReportPath} ({records.Count} rows)");
        }

        // A disagreement means the parser or the solver is wrong, so fail the run.
        return outcomeTally.GetValueOrDefault(Outcome.Disagree) > 0 ? 1 : 0;
    }

    private static ImportRecord Import(IssRow row, Options options, List<BenchCase> cases)
    {
        var record = new ImportRecord
        {
            PuzzleId = row.PuzzleId,
            Title = row.PuzzleTitle,
            Author = row.Author,
            IssSolutions = row.SolutionsFound ?? -1,
            IssMs = row.SolveMs ?? 0,
            IssGuesses = row.Guesses ?? -1,
            ConstraintTypes = row.ConstraintTypes,
        };

        // ISS only knows the exact count when its own search ran to completion without hitting its
        // solution cap. Everything else ("too-slow", "partial") gives a lower bound, which cannot
        // validate an import.
        if (row.SearchCompleted != true || row.HitCap == true || row.SolutionsFound is null)
        {
            record.Outcome = nameof(Outcome.NoExactCount);
            record.Reason = row.Status;
            return record;
        }

        string path = Path.Combine(options.PuzzleDir, row.PuzzleId + ".iss");
        if (!File.Exists(path))
        {
            record.Outcome = nameof(Outcome.Missing);
            return record;
        }
        string issText = File.ReadAllText(path);

        Solver solver;
        try
        {
            solver = SolverFactory.CreateFromIss(issText);
        }
        catch (IssUnsupportedConstraintException ex)
        {
            record.Outcome = nameof(Outcome.Unsupported);
            record.Reason = ex.ConstraintName;
            return record;
        }
        catch (Exception ex)
        {
            record.Outcome = nameof(Outcome.ParseError);
            record.Reason = $"{ex.GetType().Name}: {ex.Message}";
            return record;
        }

        using var cancellation = new CancellationTokenSource(options.BudgetMs);
        var stopwatch = Stopwatch.StartNew();
        long count;
        try
        {
            count = solver.CountSolutions(cancellationToken: cancellation.Token);
        }
        catch (Exception ex)
        {
            record.Outcome = nameof(Outcome.SolveError);
            record.Reason = $"{ex.GetType().Name}: {ex.Message}";
            record.OurMs = stopwatch.Elapsed.TotalMilliseconds;
            return record;
        }
        stopwatch.Stop();

        // CountSolutions swallows OperationCanceledException and returns however many solutions it
        // had found so far, which is indistinguishable from a completed count. Checking the token is
        // the only way to tell, and it is essential here: a cancelled count that happened to have
        // already found `solutions_found` solutions would otherwise be recorded as an *agreement*,
        // silently putting an unverified puzzle into the corpus.
        if (cancellation.IsCancellationRequested)
        {
            record.Outcome = nameof(Outcome.Timeout);
            record.OurMs = stopwatch.Elapsed.TotalMilliseconds;
            record.OurSolutions = count;
            return record;
        }

        record.OurMs = stopwatch.Elapsed.TotalMilliseconds;
        record.OurSolutions = count;

        if (count != row.SolutionsFound)
        {
            record.Outcome = nameof(Outcome.Disagree);
            return record;
        }

        record.Outcome = nameof(Outcome.Agree);
        record.Split = IsHoldout(row.PuzzleId, options.HoldoutFraction) ? "holdout" : "tune";

        if (record.OurMs <= options.CorpusMaxMs)
        {
            record.InCorpus = true;
            cases.Add(new BenchCase
            {
                Name = "iss-" + row.PuzzleId,
                Category = "iss-" + record.Split,
                Iss = issText,
                Op = "count",
                Expected = count,
            });
        }

        return record;
    }

    /// <summary>
    /// Assigns a puzzle to the holdout split from a hash of its id alone.
    /// </summary>
    /// <remarks>
    /// Deliberately not random and deliberately not <see cref="string.GetHashCode()"/>, which is
    /// randomised per process. A puzzle's split has to be a pure function of its id so that
    /// re-importing — with more puzzles, or after extending the parser — never moves a puzzle from
    /// holdout to tune. If it could, a heuristic tuned on an earlier import would silently
    /// contaminate the holdout of a later one.
    /// </remarks>
    private static bool IsHoldout(string puzzleId, double holdoutFraction)
    {
        // FNV-1a, 32-bit.
        uint hash = 2166136261;
        foreach (char c in puzzleId)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash % 1000 < holdoutFraction * 1000;
    }

    private static void PrintSummary(
        List<ImportRecord> records,
        List<BenchCase> cases,
        Dictionary<Outcome, int> outcomeTally,
        Dictionary<string, int> unsupportedTally,
        Options options)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 72));
        Console.WriteLine("outcomes");
        foreach ((Outcome outcome, int count) in outcomeTally.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {outcome,-14} {count,5}");
        }

        Console.WriteLine();
        Console.WriteLine("unsupported constraints, most-blocking first");
        foreach ((string name, int count) in unsupportedTally.OrderByDescending(kv => kv.Value).Take(30))
        {
            Console.WriteLine($"  {count,5}  {name}");
        }

        var agreed = records.Where(r => r.Outcome == nameof(Outcome.Agree)).ToList();
        if (agreed.Count > 0)
        {
            List<double> ours = agreed.Select(r => r.OurMs).OrderBy(ms => ms).ToList();
            Console.WriteLine();
            Console.WriteLine($"agreed: {agreed.Count}   in corpus: {cases.Count}   too slow for corpus (>{options.CorpusMaxMs}ms): {agreed.Count - cases.Count}");
            Console.WriteLine($"  our ms: median {ours[ours.Count / 2]:0.0}  p90 {ours[(int)(0.9 * ours.Count)]:0.0}  max {ours[^1]:0.0}");
            Console.WriteLine($"  corpus total: {cases.Count} cases, {agreed.Where(r => r.InCorpus).Sum(r => r.OurMs) / 1000.0:0.0}s of counting");
            Console.WriteLine($"  split: tune {agreed.Count(r => r.InCorpus && r.Split == "tune")}  holdout {agreed.Count(r => r.InCorpus && r.Split == "holdout")}");

            // ISS's own timings are in the index, so the import doubles as a like-for-like speed
            // comparison on a far larger sample than the 2 puzzles measured by hand so far.
            var comparable = agreed.Where(r => r.IssMs > 1.0).ToList();
            if (comparable.Count > 0)
            {
                List<double> ratios = comparable.Select(r => r.OurMs / r.IssMs).OrderBy(x => x).ToList();
                Console.WriteLine($"  ours/ISS on {comparable.Count} puzzles with ISS >1ms: median {ratios[ratios.Count / 2]:0.00}x  p10 {ratios[(int)(0.1 * ratios.Count)]:0.00}x  p90 {ratios[(int)(0.9 * ratios.Count)]:0.00}x");
            }
        }
        Console.WriteLine(new string('-', 72));
    }
}
