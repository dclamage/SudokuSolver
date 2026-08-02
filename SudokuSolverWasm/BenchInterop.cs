using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using SudokuSolverBenchmark;

namespace SudokuSolverWasm;

[JsonSerializable(typeof(BenchCase))]
[JsonSerializable(typeof(BenchResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class BenchJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Runs the native benchmark harness's cases inside the browser. It compiles the very same
/// <see cref="BenchCore"/> file the native harness uses, so timings differ only by host.
///
/// Cases are run one at a time from JS rather than as one batch: the runtime is blocked for the
/// duration of each call, and per-case returns let the page report progress.
/// </summary>
public static partial class BenchInterop
{
    [JSExport]
    public static string RunCase(string caseJson, int iterations, bool multiThread)
    {
        BenchCase benchCase = JsonSerializer.Deserialize(caseJson, BenchJsonContext.Default.BenchCase);
        BenchResult result = BenchCore.Run(benchCase, iterations, multiThread);
        return JsonSerializer.Serialize(result, BenchJsonContext.Default.BenchResult);
    }

    /// <summary>
    /// Task-returning entry point. Threads-enabled builds reject synchronous exports called from
    /// the main JS thread, and running on a pool thread also keeps the deputy thread free.
    /// </summary>
    [JSExport]
    public static Task<string> RunCaseAsync(string caseJson, int iterations, bool multiThread)
        => Task.Run(() => RunCase(caseJson, iterations, multiThread));

    /// <summary>
    /// Times BitOperations against software equivalents, to establish whether Mono's WASM AOT
    /// lowers them to i32.popcnt / i32.clz / i32.ctz. Returns the formatted table.
    /// </summary>
    [JSExport]
    public static Task<string> RunBitOpsAsync()
        => Task.Run(() => BitOpsBench.Format(BitOpsBench.Run()));
}
