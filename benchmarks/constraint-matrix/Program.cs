using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SudokuSolver;

BenchmarkOptions options;
try
{
    options = BenchmarkOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    BenchmarkOptions.PrintUsage();
    return 2;
}

if (options.ShowHelp)
{
    BenchmarkOptions.PrintUsage();
    return 0;
}

List<BenchmarkCase> corpus = TestCorpusLoader.Load(options.PuzzlesPath, options.SumTestsPath);
List<BenchmarkCase> selectedCases = CaseSelector.Select(corpus, options);

if (options.ListCases)
{
    BenchmarkReport.WriteCaseList(Console.Out, selectedCases);
    return 0;
}

List<BenchmarkSample> samples = MatrixBenchmark.Run(selectedCases, options);
if (options.OutputPath.Length > 0)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? ".");
    using StreamWriter writer = new(options.OutputPath, false, Encoding.UTF8);
    BenchmarkReport.WriteSamples(writer, samples, options);
}
else
{
    BenchmarkReport.WriteSamples(Console.Out, samples, options);
}

return samples.Any(sample => !sample.Passed) ? 1 : 0;

file sealed class BenchmarkOptions
{
    public string PuzzlesPath { get; private init; } = string.Empty;
    public string SumTestsPath { get; private init; } = string.Empty;
    public string OutputPath { get; private init; } = string.Empty;
    public string[] CaseFilters { get; private init; } = [];
    public int Warmup { get; private init; }
    public int Samples { get; private init; } = 1;
    public long MaxSolutions { get; private init; }
    public bool AllCases { get; private init; }
    public bool IncludeLarge { get; private init; }
    public bool ListCases { get; private init; }
    public bool ShowHelp { get; private init; }
    public ThreadMode ThreadMode { get; private init; } = ThreadMode.Single;

    public static BenchmarkOptions Parse(string[] args)
    {
        string puzzlesPath = FindRepoFile("SudokuTests", "Puzzles.cs");
        string sumTestsPath = FindRepoFile("SudokuTests", "SumConstraintTests.cs");
        string outputPath = string.Empty;
        List<string> caseFilters = [];
        int warmup = 0;
        int samples = 1;
        long maxSolutions = 0;
        bool allCases = false;
        bool includeLarge = false;
        bool listCases = false;
        ThreadMode threadMode = ThreadMode.Single;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h")
            {
                return new BenchmarkOptions { ShowHelp = true };
            }

            switch (arg)
            {
                case "--all":
                    allCases = true;
                    continue;
                case "--include-large":
                    includeLarge = true;
                    continue;
                case "--list":
                    listCases = true;
                    continue;
                case "--multithread":
                    threadMode = ThreadMode.Multi;
                    continue;
            }

            (string name, string value) = SplitArg(args, ref i);
            switch (name)
            {
                case "--puzzles":
                    puzzlesPath = Path.GetFullPath(value);
                    break;
                case "--sum-tests":
                    sumTestsPath = Path.GetFullPath(value);
                    break;
                case "--out":
                    outputPath = value;
                    break;
                case "--case":
                    caseFilters.AddRange(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--warmup":
                    warmup = ParseNonNegativeInt(name, value);
                    break;
                case "--samples":
                    samples = Math.Max(1, ParseNonNegativeInt(name, value));
                    break;
                case "--max-solutions":
                    maxSolutions = ParseNonNegativeLong(name, value);
                    break;
                case "--threads":
                    threadMode = ParseThreadMode(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {name}");
            }
        }

        if (!File.Exists(puzzlesPath))
        {
            throw new InvalidOperationException($"Puzzles source not found: {puzzlesPath}");
        }

        if (!File.Exists(sumTestsPath))
        {
            throw new InvalidOperationException($"Sum tests source not found: {sumTestsPath}");
        }

        return new BenchmarkOptions
        {
            PuzzlesPath = puzzlesPath,
            SumTestsPath = sumTestsPath,
            OutputPath = outputPath,
            CaseFilters = [.. caseFilters],
            Warmup = warmup,
            Samples = samples,
            MaxSolutions = maxSolutions,
            AllCases = allCases,
            IncludeLarge = includeLarge,
            ListCases = listCases,
            ThreadMode = threadMode,
        };
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: dotnet run -c Release --project benchmarks/constraint-matrix/SudokuConstraintMatrixBench.csproj -- [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --list                    List selected harvested cases without solving.");
        Console.Error.WriteLine("  --all                     Run every harvested test-corpus case instead of the curated matrix.");
        Console.Error.WriteLine("  --include-large           Include non-9x9 cases in selection.");
        Console.Error.WriteLine("  --case <id-or-family>     Filter cases by id, title, or family. Comma-separated values are allowed.");
        Console.Error.WriteLine("  --threads <mode>          single, multi, or both. Default: single.");
        Console.Error.WriteLine("  --multithread             Shorthand for --threads multi.");
        Console.Error.WriteLine("  --warmup <count>          Warmup solves per case/thread before measuring. Default: 0.");
        Console.Error.WriteLine("  --samples <count>         Measured solves per case/thread. Default: 1.");
        Console.Error.WriteLine("  --max-solutions <count>   Stop after count solutions, 0 = unlimited.");
        Console.Error.WriteLine("  --out <path>              Write TSV output to a file.");
        Console.Error.WriteLine("  --puzzles <path>          Source Puzzles.cs path. Defaults to SudokuTests/Puzzles.cs.");
        Console.Error.WriteLine("  --sum-tests <path>        Source SumConstraintTests.cs path. Defaults to SudokuTests/SumConstraintTests.cs.");
    }

    private static (string Name, string Value) SplitArg(string[] args, ref int index)
    {
        string arg = args[index];
        int equals = arg.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            return (arg[..equals], arg[(equals + 1)..]);
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {arg}.");
        }

        return (arg, args[++index]);
    }

    private static int ParseNonNegativeInt(string name, string value)
    {
        long parsed = ParseNonNegativeLong(name, value);
        if (parsed > int.MaxValue)
        {
            throw new ArgumentException($"{name} is too large: {value}");
        }

        return (int)parsed;
    }

    private static long ParseNonNegativeLong(string name, string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) || parsed < 0)
        {
            throw new ArgumentException($"{name} must be a non-negative integer: {value}");
        }

        return parsed;
    }

    private static ThreadMode ParseThreadMode(string value) => value.ToLowerInvariant() switch
    {
        "single" => ThreadMode.Single,
        "multi" => ThreadMode.Multi,
        "both" => ThreadMode.Both,
        _ => throw new ArgumentException($"--threads must be single, multi, or both: {value}"),
    };

    private static string FindRepoFile(params string[] pathSegments)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null)
            {
                string candidate = Path.Combine([directory.FullName, .. pathSegments]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine(pathSegments);
    }
}

file static class TestCorpusLoader
{
    private static readonly Regex StringTupleRegex = new(
        @"\(\s*(?:@""(?<leftVerbatim>[^""]*)""|""(?<left>[^""]*)"")\s*,\s*(?:@""(?<rightVerbatim>[^""]*)""|""(?<right>[^""]*)"")\s*\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CommentedTupleRegex = new(
        @"(?m)^\s*//\s*(?<comment>[^\r\n]+)\r?\n\s*\(\s*(?:@""(?<payloadVerbatim>[^""]*)""|""(?<payload>[^""]*)"")\s*,\s*(?:@""(?<solutionVerbatim>[^""]*)""|""(?<solution>[^""]*)"")\s*\),",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SumPuzzleUrlRegex = new(
        @"private\s+const\s+string\s+(?<name>InniePuzzleUrl|KillerCagePuzzleUrl)\s*=\s*\r?\n\s*""(?<url>[^""]+)"";",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static List<BenchmarkCase> Load(string puzzlesPath, string sumTestsPath)
    {
        string puzzlesSource = File.ReadAllText(puzzlesPath);
        List<BenchmarkCase> cases = [];

        cases.AddRange(LoadClassicGivens(puzzlesSource));
        cases.AddRange(LoadClassicFpuzzles(puzzlesSource));
        cases.AddRange(LoadVariantFpuzzles(puzzlesSource));
        cases.AddRange(LoadSumConstraintPuzzles(File.ReadAllText(sumTestsPath)));

        return cases;
    }

    private static IEnumerable<BenchmarkCase> LoadClassicGivens(string source)
    {
        string section = ExtractArrayInitializer(source, "uniqueClassics");
        int index = 0;
        foreach (Match match in StringTupleRegex.Matches(section))
        {
            index++;
            string givens = Capture(match, "leftVerbatim", "left");
            string solution = Capture(match, "rightVerbatim", "right");
            yield return new BenchmarkCase(
                $"classic-givens-{index:000}",
                "SudokuTests/Puzzles.cs:uniqueClassics",
                "standard",
                $"Classic givens #{index}",
                "test corpus",
                PuzzleInputKind.Givens,
                givens,
                solution);
        }
    }

    private static IEnumerable<BenchmarkCase> LoadClassicFpuzzles(string source)
    {
        string section = ExtractArrayInitializer(source, "uniqueClassicFPuzzles");
        int index = 0;
        foreach (Match match in StringTupleRegex.Matches(section))
        {
            index++;
            string payload = Capture(match, "leftVerbatim", "left");
            string solution = Capture(match, "rightVerbatim", "right");
            yield return new BenchmarkCase(
                $"classic-fpuzzles-{index:000}",
                "SudokuTests/Puzzles.cs:uniqueClassicFPuzzles",
                "standard fpuzzles",
                $"Classic f-puzzles #{index}",
                "test corpus",
                PuzzleInputKind.Fpuzzles,
                payload,
                solution);
        }
    }

    private static IEnumerable<BenchmarkCase> LoadVariantFpuzzles(string source)
    {
        string section = ExtractArrayInitializer(source, "uniqueVariantFPuzzles");
        int index = 0;
        foreach (Match match in CommentedTupleRegex.Matches(section))
        {
            index++;
            string comment = match.Groups["comment"].Value.Trim();
            string payload = Capture(match, "payloadVerbatim", "payload");
            string solution = Capture(match, "solutionVerbatim", "solution");
            ParsedComment parsedComment = ParseComment(comment);
            yield return new BenchmarkCase(
                $"variant-{index:000}-{Slug(parsedComment.Title)}",
                "SudokuTests/Puzzles.cs:uniqueVariantFPuzzles",
                parsedComment.Families,
                parsedComment.Title,
                parsedComment.Author,
                PuzzleInputKind.Fpuzzles,
                payload,
                solution);
        }
    }

    private static IEnumerable<BenchmarkCase> LoadSumConstraintPuzzles(string source)
    {
        foreach (Match match in SumPuzzleUrlRegex.Matches(source))
        {
            string name = match.Groups["name"].Value;
            string url = match.Groups["url"].Value;
            if (name == "KillerCagePuzzleUrl")
            {
                yield return new BenchmarkCase(
                    "sum-killer-cage-only",
                    "SudokuTests/SumConstraintTests.cs:KillerCagePuzzleUrl",
                    "killer cage sum",
                    "Killer cage only true-candidates puzzle",
                    "test corpus",
                    PuzzleInputKind.Fpuzzles,
                    url,
                    string.Empty);
            }
            else if (name == "InniePuzzleUrl")
            {
                yield return new BenchmarkCase(
                    "sum-innie-cage",
                    "SudokuTests/SumConstraintTests.cs:InniePuzzleUrl",
                    "killer cage innie sum",
                    "Innie cage optimizer puzzle",
                    "test corpus",
                    PuzzleInputKind.Fpuzzles,
                    url,
                    string.Empty,
                    ExpectedSolutionCount: null);
            }
        }
    }

    private static string ExtractArrayInitializer(string source, string arrayName)
    {
        int nameIndex = source.IndexOf(arrayName, StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            throw new InvalidOperationException($"Could not find {arrayName} in Puzzles.cs.");
        }

        int start = source.IndexOf('{', nameIndex);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find initializer for {arrayName}.");
        }

        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not parse initializer for {arrayName}.");
    }

    private static ParsedComment ParseComment(string comment)
    {
        string title = comment;
        string author = string.Empty;
        string families = "variant";

        int openParen = comment.LastIndexOf('(');
        int closeParen = comment.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
        {
            families = comment[(openParen + 1)..closeParen].Trim();
            title = comment[..openParen].Trim();
        }
        else
        {
            int familyColon = comment.LastIndexOf(':');
            if (familyColon > 0)
            {
                families = comment[(familyColon + 1)..].Trim();
                title = comment[..familyColon].Trim();
            }
        }

        if (title.StartsWith('"'))
        {
            int endQuote = title.IndexOf('"', 1);
            if (endQuote > 1)
            {
                string quotedTitle = title[1..endQuote];
                string suffix = title[(endQuote + 1)..].Trim();
                title = quotedTitle;
                if (suffix.StartsWith("by ", StringComparison.OrdinalIgnoreCase))
                {
                    author = suffix[3..].Trim().TrimEnd(':');
                }
            }
        }
        else
        {
            int byIndex = title.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
            if (byIndex > 0)
            {
                author = title[(byIndex + 4)..].Trim().TrimEnd(':');
                title = title[..byIndex].Trim().TrimEnd(':');
            }
            else
            {
                title = title.Trim().TrimEnd(':');
            }
        }

        return new ParsedComment(title, author, families);
    }

    private static string Capture(Match match, string firstGroup, string secondGroup)
    {
        Group first = match.Groups[firstGroup];
        return first.Success ? first.Value : match.Groups[secondGroup].Value;
    }

    private static string Slug(string text)
    {
        StringBuilder builder = new();
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}

file static class CaseSelector
{
    private static readonly (string Label, string[] Needles)[] CuratedTargets =
    [
        ("arrow", ["arrow"]),
        ("killer", ["killer"]),
        ("little-killer", ["little killer"]),
        ("sandwich", ["sandwich"]),
        ("x-sums", ["x-sums", "xsum"]),
        ("skyscraper", ["skyscraper"]),
        ("thermo", ["thermo"]),
        ("renban", ["renban"]),
        ("whispers", ["whispers"]),
        ("local-binary", ["king", "knight", "nonconsecutive", "orthogonal", "kropki", "xv"]),
        ("nfa-custom", ["nfa", "custom"]),
    ];

    public static List<BenchmarkCase> Select(IReadOnlyList<BenchmarkCase> corpus, BenchmarkOptions options)
    {
        IEnumerable<BenchmarkCase> candidates = corpus;
        if (!options.IncludeLarge)
        {
            candidates = candidates.Where(c => c.IsStandardNineByNine);
        }

        List<BenchmarkCase> candidateList = [.. candidates];
        List<BenchmarkCase> selected = options.AllCases || options.CaseFilters.Length > 0
            ? candidateList
            : SelectCurated(candidateList);

        if (options.CaseFilters.Length > 0)
        {
            selected = [.. selected.Where(c => MatchesAnyFilter(c, options.CaseFilters))];
        }

        return selected;
    }

    private static List<BenchmarkCase> SelectCurated(IReadOnlyList<BenchmarkCase> corpus)
    {
        List<BenchmarkCase> selected = [];
        HashSet<string> usedPayloads = [];

        AddFirst(corpus, selected, usedPayloads, c => c.Id.StartsWith("classic-givens-", StringComparison.Ordinal));
        AddFirst(corpus, selected, usedPayloads, c => c.Id.StartsWith("classic-fpuzzles-", StringComparison.Ordinal));
        AddFirst(corpus, selected, usedPayloads, c => c.Id == "sum-killer-cage-only");
        AddFirst(corpus, selected, usedPayloads, c => c.Id == "sum-innie-cage");

        foreach ((_, string[] needles) in CuratedTargets)
        {
            AddFirst(corpus, selected, usedPayloads, c => ContainsAny(c.Family, needles));
        }

        AddFirst(corpus, selected, usedPayloads, c => ConstraintFamilyCount(c.Family) >= 4);
        return selected;
    }

    private static void AddFirst(
        IReadOnlyList<BenchmarkCase> corpus,
        List<BenchmarkCase> selected,
        HashSet<string> usedPayloads,
        Func<BenchmarkCase, bool> predicate)
    {
        foreach (BenchmarkCase benchmarkCase in corpus)
        {
            if (usedPayloads.Contains(benchmarkCase.Payload))
            {
                continue;
            }

            if (!predicate(benchmarkCase))
            {
                continue;
            }

            selected.Add(benchmarkCase);
            usedPayloads.Add(benchmarkCase.Payload);
            return;
        }
    }

    private static bool MatchesAnyFilter(BenchmarkCase benchmarkCase, string[] filters) => filters.Any(filter =>
        benchmarkCase.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || benchmarkCase.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || benchmarkCase.Family.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, string[] needles) => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static int ConstraintFamilyCount(string family) => family.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length;
}

file static class MatrixBenchmark
{
    public static List<BenchmarkSample> Run(IReadOnlyList<BenchmarkCase> cases, BenchmarkOptions options)
    {
        List<BenchmarkSample> samples = [];
        foreach (BenchmarkCase benchmarkCase in cases)
        {
            foreach (bool multiThread in ThreadModes(options.ThreadMode))
            {
                for (int i = 0; i < options.Warmup; i++)
                {
                    RunOne(benchmarkCase, multiThread, options.MaxSolutions, -1);
                }

                for (int sampleIndex = 0; sampleIndex < options.Samples; sampleIndex++)
                {
                    samples.Add(RunOne(benchmarkCase, multiThread, options.MaxSolutions, sampleIndex));
                }
            }
        }

        return samples;
    }

    private static BenchmarkSample RunOne(BenchmarkCase benchmarkCase, bool multiThread, long maxSolutions, int sampleIndex)
    {
        ForceFullCollection();
        long factoryAllocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch factoryWatch = Stopwatch.StartNew();
        Solver solver = benchmarkCase.CreateSolver();
        factoryWatch.Stop();
        long factoryAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - factoryAllocatedStart;

        long solutionCount = solver.CountSolutions(maxSolutions: maxSolutions, multiThread: multiThread);
        BruteForceSolveStats? stats = solver.LastBruteForceSolveStats;
        bool exactSolutionCount = maxSolutions <= 0 || solutionCount < maxSolutions;
        bool passed = !exactSolutionCount || benchmarkCase.ExpectedSolutionCount is null || solutionCount == benchmarkCase.ExpectedSolutionCount.Value;

        return new BenchmarkSample(
            benchmarkCase.Id,
            benchmarkCase.Source,
            benchmarkCase.Family,
            solver.Title ?? benchmarkCase.Title,
            solver.Author ?? benchmarkCase.Author,
            benchmarkCase.SizeLabel,
            benchmarkCase.InputKind.ToString(),
            multiThread,
            sampleIndex,
            exactSolutionCount,
            passed,
            solutionCount,
            stats?.Guesses ?? 0,
            stats?.ValuesTried ?? 0,
            factoryWatch.Elapsed.TotalMilliseconds,
            stats?.PuzzleSetupTime.TotalMilliseconds ?? 0,
            stats?.Runtime.TotalMilliseconds ?? 0,
            stats?.SetupStateInitializationTime.TotalMilliseconds ?? 0,
            stats?.SetupCloneTime.TotalMilliseconds ?? 0,
            stats?.SetupWeakLinkDiscoveryTime.TotalMilliseconds ?? 0,
            stats?.SetupFinalPropagationTime.TotalMilliseconds ?? 0,
            stats?.SetupPoolInitializationTime.TotalMilliseconds ?? 0,
            stats?.SetupOtherTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryInitialPropagationTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryScratchCloneTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryCopyTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoverySetValueTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryProbePropagationTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryLinkScanTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryOtherTime.TotalMilliseconds ?? 0,
            stats?.WeakLinkDiscoveryPasses ?? 0,
            stats?.WeakLinkDiscoveryProbes ?? 0,
            stats?.WeakLinkDiscoveryInvalidProbes ?? 0,
            stats?.WeakLinkDiscoveryLinksAdded ?? 0,
            factoryAllocatedBytes,
            stats?.PuzzleSetupAllocatedBytes ?? 0,
            stats?.RuntimeAllocatedBytes ?? 0,
            benchmarkCase.Payload.Length);
    }

    private static IEnumerable<bool> ThreadModes(ThreadMode threadMode) => threadMode switch
    {
        ThreadMode.Single => [false],
        ThreadMode.Multi => [true],
        ThreadMode.Both => [false, true],
        _ => throw new ArgumentOutOfRangeException(nameof(threadMode), threadMode, null),
    };

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

file static class BenchmarkReport
{
    public static void WriteCaseList(TextWriter writer, IReadOnlyList<BenchmarkCase> cases)
    {
        writer.WriteLine("id\tsource\tfamily\ttitle\tauthor\tsize\tinputKind\tpayloadBytes");
        foreach (BenchmarkCase benchmarkCase in cases)
        {
            writer.WriteLine(string.Join('\t',
                Tsv(benchmarkCase.Id),
                Tsv(benchmarkCase.Source),
                Tsv(benchmarkCase.Family),
                Tsv(benchmarkCase.Title),
                Tsv(benchmarkCase.Author),
                Tsv(benchmarkCase.SizeLabel),
                Tsv(benchmarkCase.InputKind.ToString()),
                benchmarkCase.Payload.Length.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static void WriteSamples(TextWriter writer, IReadOnlyList<BenchmarkSample> samples, BenchmarkOptions options)
    {
        writer.WriteLine($"# runtime={Tsv(RuntimeInformation.FrameworkDescription)} os={Tsv(RuntimeInformation.OSDescription)} arch={RuntimeInformation.ProcessArchitecture} processors={Environment.ProcessorCount} warmup={options.Warmup} samples={options.Samples} maxSolutions={options.MaxSolutions}");
        writer.WriteLine("id\tsource\tfamily\ttitle\tauthor\tsize\tinputKind\tmultiThread\tsample\texactCount\tpassed\tsolutions\tguesses\tvaluesTried\tfactoryMs\tbruteSetupMs\truntimeMs\tsetupStateInitMs\tsetupCloneMs\tsetupDiscoverWeakLinksMs\tsetupFinalPropagateMs\tsetupPoolInitMs\tsetupOtherMs\tdiscoverInitialPropagateMs\tdiscoverScratchCloneMs\tdiscoverCopyMs\tdiscoverSetValueMs\tdiscoverProbePropagateMs\tdiscoverLinkScanMs\tdiscoverOtherMs\tdiscoverPasses\tdiscoverProbes\tdiscoverInvalidProbes\tdiscoverDirectionalLinksAdded\tfactoryAllocatedBytes\tbruteSetupAllocatedBytes\truntimeAllocatedBytes\tpayloadBytes");
        foreach (BenchmarkSample sample in samples)
        {
            writer.WriteLine(string.Join('\t',
                Tsv(sample.Id),
                Tsv(sample.Source),
                Tsv(sample.Family),
                Tsv(sample.Title),
                Tsv(sample.Author),
                Tsv(sample.Size),
                Tsv(sample.InputKind),
                sample.MultiThread.ToString(CultureInfo.InvariantCulture),
                sample.SampleIndex.ToString(CultureInfo.InvariantCulture),
                sample.ExactSolutionCount.ToString(CultureInfo.InvariantCulture),
                sample.Passed.ToString(CultureInfo.InvariantCulture),
                sample.Solutions.ToString(CultureInfo.InvariantCulture),
                sample.Guesses.ToString(CultureInfo.InvariantCulture),
                sample.ValuesTried.ToString(CultureInfo.InvariantCulture),
                sample.FactoryMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.BruteSetupMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.RuntimeMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupStateInitMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupCloneMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupDiscoverWeakLinksMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupFinalPropagateMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupPoolInitMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.SetupOtherMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverInitialPropagateMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverScratchCloneMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverCopyMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverSetValueMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverProbePropagateMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverLinkScanMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverOtherMs.ToString("F3", CultureInfo.InvariantCulture),
                sample.DiscoverPasses.ToString(CultureInfo.InvariantCulture),
                sample.DiscoverProbes.ToString(CultureInfo.InvariantCulture),
                sample.DiscoverInvalidProbes.ToString(CultureInfo.InvariantCulture),
                sample.DiscoverDirectionalLinksAdded.ToString(CultureInfo.InvariantCulture),
                sample.FactoryAllocatedBytes.ToString(CultureInfo.InvariantCulture),
                sample.BruteSetupAllocatedBytes.ToString(CultureInfo.InvariantCulture),
                sample.RuntimeAllocatedBytes.ToString(CultureInfo.InvariantCulture),
                sample.PayloadBytes.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static string Tsv(string value) => value
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
}

file sealed record BenchmarkCase(
    string Id,
    string Source,
    string Family,
    string Title,
    string Author,
    PuzzleInputKind InputKind,
    string Payload,
    string ExpectedSolution,
    long? ExpectedSolutionCount = 1)
{
    public bool IsStandardNineByNine => ExpectedSolution.Length is 0 or 81;

    public string SizeLabel => ExpectedSolution.Length switch
    {
        0 => "unknown",
        16 => "4x4",
        36 => "6x6",
        49 => "7x7",
        81 => "9x9",
        _ when ExpectedSolution.Length % 2 == 0 && IsPerfectSquare(ExpectedSolution.Length / 2, out int size) => $"{size}x{size}",
        _ => $"solution-chars:{ExpectedSolution.Length}",
    };

    public Solver CreateSolver() => InputKind switch
    {
        PuzzleInputKind.Givens => SolverFactory.CreateFromGivens(Payload),
        PuzzleInputKind.Fpuzzles => SolverFactory.CreateFromFPuzzles(Payload),
        _ => throw new ArgumentOutOfRangeException(nameof(InputKind), InputKind, null),
    };

    private static bool IsPerfectSquare(int value, out int size)
    {
        size = (int)Math.Sqrt(value);
        return size * size == value;
    }
}

file sealed record BenchmarkSample(
    string Id,
    string Source,
    string Family,
    string Title,
    string Author,
    string Size,
    string InputKind,
    bool MultiThread,
    int SampleIndex,
    bool ExactSolutionCount,
    bool Passed,
    long Solutions,
    long Guesses,
    long ValuesTried,
    double FactoryMs,
    double BruteSetupMs,
    double RuntimeMs,
    double SetupStateInitMs,
    double SetupCloneMs,
    double SetupDiscoverWeakLinksMs,
    double SetupFinalPropagateMs,
    double SetupPoolInitMs,
    double SetupOtherMs,
    double DiscoverInitialPropagateMs,
    double DiscoverScratchCloneMs,
    double DiscoverCopyMs,
    double DiscoverSetValueMs,
    double DiscoverProbePropagateMs,
    double DiscoverLinkScanMs,
    double DiscoverOtherMs,
    long DiscoverPasses,
    long DiscoverProbes,
    long DiscoverInvalidProbes,
    long DiscoverDirectionalLinksAdded,
    long FactoryAllocatedBytes,
    long BruteSetupAllocatedBytes,
    long RuntimeAllocatedBytes,
    int PayloadBytes);

file sealed record ParsedComment(string Title, string Author, string Families);

file enum PuzzleInputKind
{
    Givens,
    Fpuzzles,
}

file enum ThreadMode
{
    Single,
    Multi,
    Both,
}