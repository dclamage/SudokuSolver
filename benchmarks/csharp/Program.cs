using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

ArrowTables.Touch();

BenchmarkOptions benchmarkOptions;
try
{
    benchmarkOptions = BenchmarkOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    BenchmarkOptions.PrintUsage();
    return 2;
}

if (benchmarkOptions.ShowHelp)
{
    BenchmarkOptions.PrintUsage();
    return 0;
}

string issText = File.ReadAllText(benchmarkOptions.PuzzlePath);
BenchmarkOutput output = ArrowBenchmark.RunSamples(issText, benchmarkOptions);

JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
return 0;

file sealed class BenchmarkOptions
{
    public string PuzzlePath { get; private init; } = string.Empty;
    public int Warmup { get; private init; } = 5;
    public int Samples { get; private init; } = 20;
    public int MaxSolutions { get; private init; }
    public int TraceLimit { get; private init; }
    public bool ShowHelp { get; private init; }

    public static BenchmarkOptions Parse(string[] args)
    {
        string puzzlePath = FindDefaultPuzzlePath();
        int warmup = 5;
        int samples = 20;
        int maxSolutions = 0;
        int traceLimit = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h")
            {
                return new BenchmarkOptions { ShowHelp = true };
            }

            string name;
            string value;
            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                name = arg[..equals];
                value = arg[(equals + 1)..];
            }
            else
            {
                name = arg;
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {name}.");
                }
                value = args[++i];
            }

            switch (name)
            {
                case "--puzzle":
                    puzzlePath = value;
                    break;
                case "--warmup":
                    warmup = ParseNonNegativeInt(name, value);
                    break;
                case "--samples":
                    samples = Math.Max(1, ParseNonNegativeInt(name, value));
                    break;
                case "--max-solutions":
                    maxSolutions = ParseNonNegativeInt(name, value);
                    break;
                case "--trace-limit":
                    traceLimit = ParseNonNegativeInt(name, value);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {name}");
            }
        }

        string fullPuzzlePath = Path.GetFullPath(puzzlePath);
        if (!File.Exists(fullPuzzlePath))
        {
            throw new InvalidOperationException($"Puzzle file not found: {fullPuzzlePath}");
        }

        return new BenchmarkOptions
        {
            PuzzlePath = fullPuzzlePath,
            Warmup = warmup,
            Samples = samples,
            MaxSolutions = maxSolutions,
            TraceLimit = traceLimit,
        };
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: dotnet run -c Release --project benchmarks/csharp/SudokuArrowCSharpBench.csproj -- [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --puzzle <path>          ISS .Arrow text puzzle. Defaults to iss-arrow-data.txt found above the cwd/project.");
        Console.Error.WriteLine("  --warmup <count>         Warmup solves, default 5.");
        Console.Error.WriteLine("  --samples <count>        Measured solves, default 20.");
        Console.Error.WriteLine("  --max-solutions <count>  Stop after count solutions, 0 = unlimited.");
        Console.Error.WriteLine("  --trace-limit <count>    Capture first count branch trace entries, default 0.");
    }

    private static int ParseNonNegativeInt(string name, string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
        {
            throw new ArgumentException($"{name} must be a non-negative integer: {value}");
        }
        return parsed;
    }

    private static string FindDefaultPuzzlePath()
    {
        string? directory = Environment.CurrentDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "iss-arrow-data.txt");
            if (File.Exists(candidate)) return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "iss-arrow-data.txt");
            if (File.Exists(candidate)) return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        return Path.Combine(Environment.CurrentDirectory, "iss-arrow-data.txt");
    }
}

file static class ArrowBenchmark
{
    public static BenchmarkOutput RunSamples(string issText, BenchmarkOptions options)
    {
        SolveOptions solveOptions = new(options.MaxSolutions, options.TraceLimit);

        for (int i = 0; i < options.Warmup; i++)
        {
            RunSolve(issText, solveOptions);
        }

        SampleResult[] samples = new SampleResult[options.Samples];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = RunSolve(issText, solveOptions);
        }

        return new BenchmarkOutput(
            "arrow-csharp-samples",
            options.Warmup,
            samples,
            SummarizeSamples(samples),
            new EnvironmentInfo(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount));
    }

    private static SampleResult RunSolve(string issText, SolveOptions options)
    {
        long setupStart = Stopwatch.GetTimestamp();
        ParsedPuzzle puzzle = IssArrowParser.Parse(issText);
        ArrowSudokuSolver solver = new(puzzle, options);
        double setupMs = ElapsedMs(setupStart);

        long runtimeStart = Stopwatch.GetTimestamp();
        int solutions = solver.CountSolutions(options.MaxSolutions);
        double runtimeMs = ElapsedMs(runtimeStart);

        SolverStats stats = solver.ResultStats();
        return new SampleResult(
            "csharp-arrow-reference",
            "csharp",
            ".net",
            new PuzzleInfo(ArrowTables.Size, puzzle.Arrows.Length, 0),
            setupMs,
            runtimeMs,
            setupMs + runtimeMs,
            solutions,
            stats.Guesses,
            stats.ValuesTried,
            stats.NodesSearched,
            stats.Backtracks,
            stats.DomainEliminations,
            stats.PropagationSteps,
            stats.TraceHash,
            stats.Trace);
    }

    private static SummaryResult SummarizeSamples(SampleResult[] samples)
    {
        double[] setup = new double[samples.Length];
        double[] runtime = new double[samples.Length];
        double[] total = new double[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            setup[i] = samples[i].SetupMs;
            runtime[i] = samples[i].RuntimeMs;
            total[i] = samples[i].TotalMs;
        }

        SampleResult first = samples[0];
        bool consistent = true;
        for (int i = 0; i < samples.Length; i++)
        {
            SampleResult sample = samples[i];
            consistent &= sample.Solutions == first.Solutions
                && sample.Guesses == first.Guesses
                && sample.ValuesTried == first.ValuesTried
                && sample.NodesSearched == first.NodesSearched
                && sample.Backtracks == first.Backtracks
                && sample.TraceHash == first.TraceHash;
        }

        return new SummaryResult(
            SummarizeNumbers(setup),
            SummarizeNumbers(runtime),
            SummarizeNumbers(total),
            first.Solutions,
            first.Guesses,
            first.ValuesTried,
            first.NodesSearched,
            first.Backtracks,
            first.DomainEliminations,
            first.PropagationSteps,
            first.TraceHash,
            consistent);
    }

    private static NumberSummary SummarizeNumbers(double[] values)
    {
        Array.Sort(values);
        return new NumberSummary(
            values[0],
            Percentile(values, 0.10),
            Percentile(values, 0.50),
            Percentile(values, 0.90),
            values[^1]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];

        double index = (sorted.Length - 1) * p;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];

        double weight = index - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}

file sealed class ArrowSudokuSolver
{
    private const int MaxFrames = ArrowTables.NumCells * 2 + 4;
    private const int QueueCapacity = 32768;

    private readonly SolveOptions _options;
    private readonly ArrowConstraint[] _arrows;
    private readonly byte[][] _constraintsByCell;
    private readonly ushort[] _initialGrid = new ushort[ArrowTables.NumCells];
    private readonly int[] _conflictScores = new int[ArrowTables.NumCells];
    private readonly int[] _valueConflictScores = new int[ArrowTables.Size];
    private readonly ushort[] _supports = new ushort[ArrowTables.Size];
    private readonly byte[] _constraintQueued;
    private readonly int[] _constraintQueue = new int[QueueCapacity];
    private readonly ushort[][] _gridPool = new ushort[MaxFrames][];
    private readonly int[] _freeStack = new int[MaxFrames];
    private readonly int[] _frameGridIndex = new int[MaxFrames];
    private readonly int[] _frameDepth = new int[MaxFrames];
    private readonly int[] _framePendingCell = new int[MaxFrames];
    private readonly int[] _framePendingForced = new int[MaxFrames];
    private readonly ushort[] _frameConflictValue = new ushort[MaxFrames];
    private readonly List<TraceEntry> _trace;

    private int _freeTop;
    private int _frameTop;
    private int _queueHead;
    private int _queueTail;
    private int _propagationNewSingletons;
    private int _branchCell;
    private ushort _branchValueMask;
    private int _branchCount;
    private int _valueScoreMask;
    private int _valueScore;
    private uint _traceHash = 0x811c9dc5u;

    private int _solutions;
    private int _guesses;
    private int _valuesTried;
    private int _nodesSearched;
    private int _backtracks;
    private int _domainEliminations;
    private int _propagationSteps;

    public ArrowSudokuSolver(ParsedPuzzle puzzle, SolveOptions options)
    {
        _options = options;
        _arrows = new ArrowConstraint[puzzle.Arrows.Length];
        for (int i = 0; i < _arrows.Length; i++)
        {
            _arrows[i] = BuildArrow(puzzle.Arrows[i]);
        }

        _constraintsByCell = BuildConstraintsByCell(_arrows);
        Array.Fill(_initialGrid, (ushort)ArrowTables.AllValues);
        _constraintQueued = new byte[ArrowTables.Units.Length + _arrows.Length];
        _trace = new List<TraceEntry>(Math.Max(0, options.TraceLimit));

        for (int i = 0; i < _gridPool.Length; i++)
        {
            _gridPool[i] = new ushort[ArrowTables.NumCells];
        }

        SeedConflictScores();
    }

    public int CountSolutions(int maxSolutions = 0)
    {
        ResetSearchState();

        int rootIndex = AllocGrid();
        ushort[] rootGrid = _gridPool[rootIndex];
        Array.Copy(_initialGrid, rootGrid, ArrowTables.NumCells);
        _propagationNewSingletons = 0;
        if (!PropagateAll(rootGrid))
        {
            _backtracks++;
            FreeGrid(rootIndex);
            return 0;
        }

        _valuesTried += _propagationNewSingletons;
        PushFrame(rootIndex, 0, -1, 0, 0);
        Search(maxSolutions);
        return maxSolutions > 0 && _solutions > maxSolutions ? maxSolutions : _solutions;
    }

    public SolverStats ResultStats()
    {
        return new SolverStats(
            _guesses,
            _valuesTried,
            _nodesSearched,
            _backtracks,
            _domainEliminations,
            _propagationSteps,
            _traceHash.ToString("x8", CultureInfo.InvariantCulture),
            _trace);
    }

    private void SeedConflictScores()
    {
        Array.Fill(_conflictScores, ArrowTables.Size * 3);
        for (int arrowIndex = 0; arrowIndex < _arrows.Length; arrowIndex++)
        {
            byte[] cells = _arrows[arrowIndex].Cells;
            int priority = Math.Max(ArrowTables.Size * 2 - cells.Length, ArrowTables.Size);
            for (int i = 0; i < cells.Length; i++)
            {
                _conflictScores[cells[i]] += priority;
            }
        }
    }

    private void Search(int maxSolutions)
    {
        while (_frameTop > 0)
        {
            int frameIndex = --_frameTop;
            int gridIndex = _frameGridIndex[frameIndex];
            ushort[] grid = _gridPool[gridIndex];
            int depth = _frameDepth[frameIndex];
            int pendingCell = _framePendingCell[frameIndex];
            ushort conflictValue = _frameConflictValue[frameIndex];

            if (pendingCell >= 0)
            {
                _propagationNewSingletons = _framePendingForced[frameIndex];
                if (!PropagateFromCell(grid, pendingCell))
                {
                    _backtracks++;
                    IncrementConflict(pendingCell, conflictValue);
                    FreeGrid(gridIndex);
                    continue;
                }
                _valuesTried += _propagationNewSingletons;
            }

            _nodesSearched++;

            SelectBestBranch(grid);
            if (_branchCell < 0)
            {
                _solutions++;
                MixTrace(255, depth, _solutions & 255);
                FreeGrid(gridIndex);
                if (maxSolutions > 0 && _solutions >= maxSolutions)
                {
                    break;
                }
                continue;
            }

            int cell = _branchCell;
            ushort mask = grid[cell];
            ushort valueMask = _branchValueMask;
            _guesses++;
            _valuesTried++;
            MixTrace(cell, depth, ArrowTables.LowValue[valueMask]);

            if (_trace.Count < _options.TraceLimit)
            {
                _trace.Add(new TraceEntry(depth, CellToId(cell), ArrowTables.LowValue[valueMask], MaskToDigits(mask)));
            }

            int clearIndex = AllocGrid();
            ushort[] clearGrid = _gridPool[clearIndex];
            Array.Copy(grid, clearGrid, ArrowTables.NumCells);
            ushort clearMask = (ushort)(mask & ~valueMask);
            clearGrid[cell] = clearMask;
            PushFrame(
                clearIndex,
                depth + 1,
                cell,
                (clearMask & (clearMask - 1)) == 0 ? 1 : 0,
                valueMask);

            grid[cell] = valueMask;
            PushFrame(gridIndex, depth + 1, cell, 0, valueMask);
        }

        while (_frameTop > 0)
        {
            FreeGrid(_frameGridIndex[--_frameTop]);
        }
    }

    private void ResetSearchState()
    {
        _freeTop = MaxFrames;
        for (int i = 0; i < MaxFrames; i++)
        {
            _freeStack[i] = i;
        }
        _frameTop = 0;
    }

    private int AllocGrid()
    {
        if (_freeTop == 0)
        {
            throw new InvalidOperationException("Search grid pool exhausted.");
        }
        return _freeStack[--_freeTop];
    }

    private void FreeGrid(int gridIndex)
    {
        _freeStack[_freeTop++] = gridIndex;
    }

    private void PushFrame(int gridIndex, int depth, int pendingCell, int pendingForced, ushort conflictValue)
    {
        if (_frameTop == MaxFrames)
        {
            throw new InvalidOperationException("Search frame stack exhausted.");
        }

        int frameIndex = _frameTop++;
        _frameGridIndex[frameIndex] = gridIndex;
        _frameDepth[frameIndex] = depth;
        _framePendingCell[frameIndex] = pendingCell;
        _framePendingForced[frameIndex] = pendingForced;
        _frameConflictValue[frameIndex] = conflictValue;
    }

    private bool PropagateAll(ushort[] grid)
    {
        ResetQueue();
        for (int constraintIndex = 0; constraintIndex < _constraintQueued.Length; constraintIndex++)
        {
            EnqueueConstraint(constraintIndex);
        }
        return DrainQueue(grid);
    }

    private bool PropagateFromCell(ushort[] grid, int cell)
    {
        ResetQueue();
        EnqueueCell(cell);
        return DrainQueue(grid);
    }

    private void ResetQueue()
    {
        Array.Clear(_constraintQueued);
        _queueHead = 0;
        _queueTail = 0;
    }

    private void EnqueueCell(int cell)
    {
        byte[] indexes = _constraintsByCell[cell];
        for (int i = 0; i < indexes.Length; i++)
        {
            EnqueueConstraint(indexes[i]);
        }
    }

    private void EnqueueConstraint(int constraintIndex)
    {
        if (_constraintQueued[constraintIndex] != 0) return;
        _constraintQueued[constraintIndex] = 1;
        if (_queueTail == _constraintQueue.Length)
        {
            throw new InvalidOperationException("Constraint queue exhausted.");
        }
        _constraintQueue[_queueTail++] = constraintIndex;
    }

    private bool DrainQueue(ushort[] grid)
    {
        while (_queueHead < _queueTail)
        {
            int constraintIndex = _constraintQueue[_queueHead++];
            _constraintQueued[constraintIndex] = 0;
            _propagationSteps++;

            bool valid = constraintIndex < ArrowTables.Units.Length
                ? EnforceHouse(grid, constraintIndex)
                : EnforceArrow(grid, constraintIndex - ArrowTables.Units.Length);

            if (!valid) return false;
        }

        return true;
    }

    private bool EnforceHouse(ushort[] grid, int unitIndex)
    {
        byte[] unit = ArrowTables.Units[unitIndex];
        int fixedMask = 0;

        for (int i = 0; i < ArrowTables.Size; i++)
        {
            int mask = grid[unit[i]];
            if (mask == 0) return false;
            if ((mask & (mask - 1)) == 0)
            {
                if ((fixedMask & mask) != 0) return false;
                fixedMask |= mask;
            }
        }

        int once = 0;
        int twice = 0;
        for (int i = 0; i < ArrowTables.Size; i++)
        {
            int cell = unit[i];
            int oldMask = grid[cell];
            int nextMask = oldMask;

            if ((oldMask & (oldMask - 1)) != 0)
            {
                nextMask = oldMask & ~fixedMask;
                if (nextMask == 0) return false;
                if (nextMask != oldMask)
                {
                    NarrowCell(grid, cell, (ushort)nextMask);
                    EnqueueCell(cell);
                }
            }

            twice |= once & nextMask;
            once |= nextMask;
        }

        if ((once & ArrowTables.AllValues) != ArrowTables.AllValues) return false;

        int hiddenSingles = once & ~twice;
        while (hiddenSingles != 0)
        {
            int bit = hiddenSingles & -hiddenSingles;
            hiddenSingles ^= bit;
            int lastCell = -1;

            for (int i = 0; i < ArrowTables.Size; i++)
            {
                int cell = unit[i];
                if ((grid[cell] & bit) != 0)
                {
                    lastCell = cell;
                    break;
                }
            }

            if (lastCell < 0) return false;
            if (grid[lastCell] != bit)
            {
                NarrowCell(grid, lastCell, (ushort)bit);
                EnqueueCell(lastCell);
            }
        }

        return true;
    }

    private bool EnforceArrow(ushort[] grid, int arrowIndex)
    {
        ArrowConstraint arrow = _arrows[arrowIndex];
        byte[] cells = arrow.Cells;
        byte[] tupleValues = arrow.TupleValues;
        int width = cells.Length;

        Array.Clear(_supports, 0, width);

        for (int offset = 0; offset < tupleValues.Length; offset += width)
        {
            bool valid = true;
            for (int i = 0; i < width; i++)
            {
                if ((grid[cells[i]] & ArrowTables.ValueMasks[tupleValues[offset + i]]) == 0)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid) continue;

            for (int i = 0; i < width; i++)
            {
                _supports[i] |= ArrowTables.ValueMasks[tupleValues[offset + i]];
            }
        }

        for (int i = 0; i < width; i++)
        {
            int cell = cells[i];
            ushort oldMask = grid[cell];
            ushort nextMask = (ushort)(oldMask & _supports[i]);
            if (nextMask == 0) return false;
            if (nextMask != oldMask)
            {
                NarrowCell(grid, cell, nextMask);
                EnqueueCell(cell);
            }
        }

        return true;
    }

    private void NarrowCell(ushort[] grid, int cell, ushort nextMask)
    {
        ushort oldMask = grid[cell];
        _domainEliminations += ArrowTables.PopCount[oldMask] - ArrowTables.PopCount[nextMask];
        if ((oldMask & (oldMask - 1)) != 0 && (nextMask & (nextMask - 1)) == 0)
        {
            _propagationNewSingletons++;
        }
        grid[cell] = nextMask;
    }

    private void SelectBestBranch(ushort[] grid)
    {
        SetMaxValueScore();
        int bestCell = -1;
        double bestScore = -1.0;
        int bestCount = 1;
        ushort bestMask = 0;

        for (int cell = 0; cell < ArrowTables.NumCells; cell++)
        {
            ushort mask = grid[cell];
            int count = ArrowTables.PopCount[mask];
            if (count <= 1) continue;

            double scoreUnnormalized = _conflictScores[cell];
            if ((mask & _valueScoreMask) != 0)
            {
                scoreUnnormalized += _valueScore * 0.2;
            }

            double score = scoreUnnormalized / count;
            if (bestCell < 0 || score > bestScore || (score == bestScore && count < bestCount))
            {
                bestCell = cell;
                bestScore = score;
                bestCount = count;
                bestMask = mask;
            }
        }

        if (bestCell < 0)
        {
            _branchCell = -1;
            _branchValueMask = 0;
            _branchCount = 0;
            return;
        }

        if (_options.UseHouseBilocals && bestCount > 2 && bestScore > 0)
        {
            FindBestHouseBilocal(grid, bestScore);
            if (_branchCell >= 0) return;
        }

        _branchCell = bestCell;
        _branchValueMask = (ushort)(bestMask & -bestMask);
        _branchCount = bestCount;
    }

    private void FindBestHouseBilocal(ushort[] grid, double currentScore)
    {
        int bestCell = -1;
        ushort bestValueMask = 0;
        double bestScore = currentScore;

        for (int unitIndex = 0; unitIndex < ArrowTables.Units.Length; unitIndex++)
        {
            byte[] unit = ArrowTables.Units[unitIndex];
            int once = 0;
            int twice = 0;
            int more = 0;

            for (int i = 0; i < ArrowTables.Size; i++)
            {
                int mask = grid[unit[i]];
                more |= twice & mask;
                twice |= once & mask;
                once |= mask;
            }

            int exactlyTwice = twice & ~more;
            while (exactlyTwice != 0)
            {
                int valueMask = exactlyTwice & -exactlyTwice;
                exactlyTwice ^= valueMask;

                int cell0 = -1;
                int cell1 = -1;
                int maxScore = 0;
                for (int i = 0; i < ArrowTables.Size; i++)
                {
                    int cell = unit[i];
                    if ((grid[cell] & valueMask) == 0) continue;
                    if ((grid[cell] & (grid[cell] - 1)) == 0)
                    {
                        cell0 = -1;
                        cell1 = -1;
                        break;
                    }
                    if (cell0 < 0) cell0 = cell;
                    else cell1 = cell;
                    if (_conflictScores[cell] > maxScore)
                    {
                        maxScore = _conflictScores[cell];
                    }
                }

                if (cell0 < 0 || cell1 < 0) continue;

                double score = maxScore * 0.5;
                if (score <= bestScore) continue;

                bestScore = score;
                bestValueMask = (ushort)valueMask;
                bestCell = _conflictScores[cell1] >= _conflictScores[cell0] ? cell1 : cell0;
            }
        }

        _branchCell = bestCell;
        _branchValueMask = bestValueMask;
        _branchCount = bestCell >= 0 ? 2 : 0;
    }

    private void SetMaxValueScore()
    {
        if (!_options.UseValueConflictScores)
        {
            _valueScoreMask = 0;
            _valueScore = 0;
            return;
        }

        int maxScore = 0;
        int minScore = int.MaxValue;
        int valueMask = 0;

        for (int i = 0; i < ArrowTables.Size; i++)
        {
            int score = _valueConflictScores[i];
            if (score > maxScore)
            {
                maxScore = score;
                valueMask = 1 << i;
            }
            if (score != 0 && score < minScore)
            {
                minScore = score;
            }
        }

        if (maxScore < ArrowTables.Size || (maxScore << 1) <= minScore * 3)
        {
            _valueScoreMask = 0;
            _valueScore = 0;
            return;
        }

        _valueScoreMask = valueMask;
        _valueScore = maxScore;
    }

    private void IncrementConflict(int cell, ushort valueMask)
    {
        _conflictScores[cell]++;
        if (valueMask != 0)
        {
            _valueConflictScores[ArrowTables.LowValue[valueMask] - 1]++;
        }
    }

    private void MixTrace(int cell, int depth, int value)
    {
        unchecked
        {
            uint hash = _traceHash;
            hash = (hash ^ (uint)(cell + 1)) * 16777619u;
            hash = (hash ^ (uint)(depth + 1)) * 16777619u;
            hash = (hash ^ (uint)value) * 16777619u;
            _traceHash = hash;
        }
    }

    private static ArrowConstraint BuildArrow(byte[] cells)
    {
        List<byte> tuples = new();
        byte[] values = new byte[cells.Length];

        for (byte circleValue = 1; circleValue <= ArrowTables.Size; circleValue++)
        {
            values[0] = circleValue;
            BuildArrowTuples(cells, values, tuples, 1, circleValue);
        }

        return new ArrowConstraint(cells, tuples.ToArray());
    }

    private static byte[][] BuildConstraintsByCell(ArrowConstraint[] arrows)
    {
        List<byte>[] lists = new List<byte>[ArrowTables.NumCells];
        for (int i = 0; i < lists.Length; i++)
        {
            lists[i] = new List<byte>();
        }

        for (byte unitIndex = 0; unitIndex < ArrowTables.Units.Length; unitIndex++)
        {
            byte[] unit = ArrowTables.Units[unitIndex];
            for (int i = 0; i < ArrowTables.Size; i++)
            {
                lists[unit[i]].Add(unitIndex);
            }
        }

        for (int arrowIndex = 0; arrowIndex < arrows.Length; arrowIndex++)
        {
            byte constraintIndex = (byte)(ArrowTables.Units.Length + arrowIndex);
            byte[] cells = arrows[arrowIndex].Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                lists[cells[i]].Add(constraintIndex);
            }
        }

        byte[][] result = new byte[ArrowTables.NumCells][];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = lists[i].ToArray();
        }
        return result;
    }

    private static void BuildArrowTuples(byte[] cells, byte[] values, List<byte> tuples, int index, int remainingSum)
    {
        if (index == cells.Length)
        {
            if (remainingSum == 0 && TupleRespectsPeers(cells, values))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    tuples.Add(values[i]);
                }
            }
            return;
        }

        int remainingCells = cells.Length - index - 1;
        int minRemaining = remainingCells;
        int maxRemaining = remainingCells * ArrowTables.Size;
        int maxValue = Math.Min(ArrowTables.Size, remainingSum - minRemaining);

        for (byte value = 1; value <= maxValue; value++)
        {
            int nextRemaining = remainingSum - value;
            if (nextRemaining < minRemaining || nextRemaining > maxRemaining) continue;
            values[index] = value;
            BuildArrowTuples(cells, values, tuples, index + 1, nextRemaining);
        }
    }

    private static bool TupleRespectsPeers(byte[] cells, byte[] values)
    {
        for (int i = 0; i < cells.Length - 1; i++)
        {
            for (int j = i + 1; j < cells.Length; j++)
            {
                if (values[i] == values[j] && ArrowTables.PeerMatrix[cells[i] * ArrowTables.NumCells + cells[j]] != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static string CellToId(int cell)
    {
        return string.Create(CultureInfo.InvariantCulture, $"R{cell / ArrowTables.Size + 1}C{cell % ArrowTables.Size + 1}");
    }

    private static string MaskToDigits(int mask)
    {
        Span<char> chars = stackalloc char[ArrowTables.Size];
        int length = 0;
        for (int value = 1; value <= ArrowTables.Size; value++)
        {
            if ((mask & ArrowTables.ValueMasks[value]) != 0)
            {
                chars[length++] = (char)('0' + value);
            }
        }
        return new string(chars[..length]);
    }
}

file static class IssArrowParser
{
    public static ParsedPuzzle Parse(string text)
    {
        List<byte[]> arrows = new();
        string source = (text ?? string.Empty).Trim();
        int searchIndex = 0;

        while (searchIndex < source.Length)
        {
            int arrowStart = source.IndexOf(".Arrow~", searchIndex, StringComparison.Ordinal);
            if (arrowStart < 0) break;

            int payloadStart = arrowStart + ".Arrow~".Length;
            int payloadEnd = source.IndexOf('.', payloadStart);
            if (payloadEnd < 0) payloadEnd = source.Length;

            string payload = source[payloadStart..payloadEnd];
            string[] ids = payload.Split('~', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length < 2)
            {
                throw new InvalidOperationException($"Arrow requires a circle and at least one line cell: {payload}");
            }

            byte[] cells = new byte[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                cells[i] = ParseCellId(ids[i]);
            }
            arrows.Add(cells);
            searchIndex = payloadEnd;
        }

        if (arrows.Count == 0)
        {
            throw new InvalidOperationException("No .Arrow constraints found in ISS text.");
        }

        return new ParsedPuzzle(arrows.ToArray());
    }

    private static byte ParseCellId(string id)
    {
        ReadOnlySpan<char> span = id.AsSpan().Trim();
        if (span.Length != 4 || (span[0] != 'R' && span[0] != 'r') || (span[2] != 'C' && span[2] != 'c'))
        {
            throw new InvalidOperationException($"Unsupported ISS cell id: {id}");
        }

        int row = span[1] - '1';
        int col = span[3] - '1';
        if ((uint)row >= ArrowTables.Size || (uint)col >= ArrowTables.Size)
        {
            throw new InvalidOperationException($"Unsupported ISS cell id: {id}");
        }

        return (byte)(row * ArrowTables.Size + col);
    }
}

file static class ArrowTables
{
    public const int Size = 9;
    public const int BoxSize = 3;
    public const int NumCells = Size * Size;
    public const int AllValues = (1 << Size) - 1;

    public static readonly ushort[] ValueMasks = new ushort[Size + 1];
    public static readonly byte[] PopCount = new byte[1 << Size];
    public static readonly byte[] LowValue = new byte[1 << Size];
    public static readonly byte[] SumByMask = new byte[1 << Size];
    public static readonly byte[][] Units;
    public static readonly byte[] PeerMatrix;

    static ArrowTables()
    {
        for (int value = 1; value <= Size; value++)
        {
            ValueMasks[value] = (ushort)(1 << (value - 1));
        }

        for (int mask = 1; mask <= AllValues; mask++)
        {
            int lowBit = mask & -mask;
            PopCount[mask] = (byte)(PopCount[mask & (mask - 1)] + 1);
            LowValue[mask] = (byte)(32 - BitOperationsLeadingZeroCount((uint)lowBit));
            SumByMask[mask] = (byte)(SumByMask[mask & (mask - 1)] + LowValue[lowBit]);
        }

        Units = BuildUnits();
        PeerMatrix = BuildPeerMatrix();
    }

    public static void Touch()
    {
    }

    private static byte[][] BuildUnits()
    {
        byte[][] units = new byte[Size * 3][];
        int unitIndex = 0;

        for (int row = 0; row < Size; row++)
        {
            byte[] unit = new byte[Size];
            for (int col = 0; col < Size; col++) unit[col] = (byte)(row * Size + col);
            units[unitIndex++] = unit;
        }

        for (int col = 0; col < Size; col++)
        {
            byte[] unit = new byte[Size];
            for (int row = 0; row < Size; row++) unit[row] = (byte)(row * Size + col);
            units[unitIndex++] = unit;
        }

        for (int boxRow = 0; boxRow < BoxSize; boxRow++)
        {
            for (int boxCol = 0; boxCol < BoxSize; boxCol++)
            {
                byte[] unit = new byte[Size];
                int index = 0;
                for (int dr = 0; dr < BoxSize; dr++)
                {
                    for (int dc = 0; dc < BoxSize; dc++)
                    {
                        unit[index++] = (byte)((boxRow * BoxSize + dr) * Size + boxCol * BoxSize + dc);
                    }
                }
                units[unitIndex++] = unit;
            }
        }

        return units;
    }

    private static byte[] BuildPeerMatrix()
    {
        byte[] matrix = new byte[NumCells * NumCells];

        for (int cell = 0; cell < NumCells; cell++)
        {
            int row = cell / Size;
            int col = cell % Size;
            int boxRow = row / BoxSize;
            int boxCol = col / BoxSize;

            for (int other = 0; other < NumCells; other++)
            {
                if (cell == other) continue;

                int otherRow = other / Size;
                int otherCol = other % Size;
                if (row == otherRow || col == otherCol || (boxRow == otherRow / BoxSize && boxCol == otherCol / BoxSize))
                {
                    matrix[cell * NumCells + other] = 1;
                }
            }
        }

        return matrix;
    }

    private static int BitOperationsLeadingZeroCount(uint value)
    {
        return System.Numerics.BitOperations.LeadingZeroCount(value);
    }
}

file sealed record SolveOptions(int MaxSolutions, int TraceLimit)
{
    public bool UseHouseBilocals => false;
    public bool UseValueConflictScores => false;
}

file sealed record ParsedPuzzle(byte[][] Arrows);

file sealed record ArrowConstraint(byte[] Cells, byte[] TupleValues);

file sealed record TraceEntry(int Depth, string Cell, int Value, string Candidates);

file sealed record SolverStats(
    int Guesses,
    int ValuesTried,
    int NodesSearched,
    int Backtracks,
    int DomainEliminations,
    int PropagationSteps,
    string TraceHash,
    IReadOnlyList<TraceEntry> Trace);

file sealed record PuzzleInfo(int Size, int Arrows, int Givens);

file sealed record SampleResult(
    string Engine,
    string Language,
    string Runtime,
    PuzzleInfo Puzzle,
    double SetupMs,
    double RuntimeMs,
    double TotalMs,
    int Solutions,
    int Guesses,
    int ValuesTried,
    int NodesSearched,
    int Backtracks,
    int DomainEliminations,
    int PropagationSteps,
    string TraceHash,
    IReadOnlyList<TraceEntry> Trace);

file sealed record NumberSummary(double Min, double P10, double Median, double P90, double Max);

file sealed record SummaryResult(
    NumberSummary SetupMs,
    NumberSummary RuntimeMs,
    NumberSummary TotalMs,
    int Solutions,
    int Guesses,
    int ValuesTried,
    int NodesSearched,
    int Backtracks,
    int DomainEliminations,
    int PropagationSteps,
    string TraceHash,
    bool Consistent);

file sealed record EnvironmentInfo(string FrameworkDescription, string OsDescription, string ProcessArchitecture, int ProcessorCount);

file sealed record BenchmarkOutput(
    string Kind,
    int Warmup,
    IReadOnlyList<SampleResult> Samples,
    SummaryResult Summary,
    EnvironmentInfo Environment);