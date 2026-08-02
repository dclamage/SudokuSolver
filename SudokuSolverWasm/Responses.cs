using System.Text.Json.Serialization;

namespace SudokuSolverWasm;

// These types mirror the websocket protocol in SudokuSolverConsole/WebsocketListener.cs exactly,
// so the browser front-end (and, later, the f-puzzles userscript) can speak the same wire format
// whether it is talking to the native websocket server or to the WASM module.
//
// If adding a new BaseResponse, be sure to also add it to:
// - WasmJsonContext
// - SolverCommandProcessor.Serialize

#pragma warning disable IDE1006 // Naming Styles
internal class Message
{
    public int nonce { get; set; }
    public string command { get; set; }
    public string dataType { get; set; }
    public string data { get; set; }
}

internal class BaseResponse(int nonce, string type)
{
    public int nonce { get; set; } = nonce;
    public string type { get; set; } = type;
}

internal class CanceledResponse(int nonce) : BaseResponse(nonce, "canceled")
{
}

internal class InvalidResponse(int nonce) : BaseResponse(nonce, "invalid")
{
    public string message { get; set; }
}

internal class TrueCandidatesResponse(int nonce) : BaseResponse(nonce, "truecandidates")
{
    public long[] solutionsPerCandidate { get; set; }
}

internal class SolvedResponse(int nonce) : BaseResponse(nonce, "solved")
{
    public int[] solution { get; set; }
}

internal class CountResponse(int nonce) : BaseResponse(nonce, "count")
{
    public long count { get; set; }
    public bool inProgress { get; set; }
}

internal class EstimateResponse(int nonce) : BaseResponse(nonce, "estimate")
{
    public double estimate { get; set; }
    public double stderr { get; set; }
    public long iterations { get; set; }
    public double ci95_lower { get; set; }
    public double ci95_upper { get; set; }
    public double relErrPercent { get; set; }
}

internal class LogicalCell
{
    public int value { get; set; }
    public int[] candidates { get; set; }
}

internal class LogicalResponse(int nonce) : BaseResponse(nonce, "logical")
{
    public LogicalCell[] cells { get; set; }
    public string message { get; set; }
    public bool isValid { get; set; }
}

/// <summary>Runtime facts reported to the page so a benchmark run is self-describing.</summary>
internal class RuntimeInfo
{
    public bool threadsEnabled { get; set; }
    public int processorCount { get; set; }
    public string runtimeVersion { get; set; }
    public string osDescription { get; set; }
}
#pragma warning restore IDE1006 // Naming Styles

[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(CanceledResponse))]
[JsonSerializable(typeof(InvalidResponse))]
[JsonSerializable(typeof(TrueCandidatesResponse))]
[JsonSerializable(typeof(SolvedResponse))]
[JsonSerializable(typeof(CountResponse))]
[JsonSerializable(typeof(LogicalResponse))]
[JsonSerializable(typeof(EstimateResponse))]
[JsonSerializable(typeof(RuntimeInfo))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
)]
internal partial class WasmJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
