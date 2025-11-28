using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SudokuSolver.Wasm;

#nullable enable

public class Message
{
    public int nonce { get; set; }
    public string? command { get; set; }
    public string? dataType { get; set; }
    public string? data { get; set; }
    public bool multithread { get; set; } = false;
}

public class BaseResponse
{
    public int nonce { get; set; }
    public string type { get; set; }

    public BaseResponse(int nonce, string type)
    {
        this.nonce = nonce;
        this.type = type;
    }
}

public class CanceledResponse : BaseResponse
{
    public CanceledResponse(int nonce) : base(nonce, "canceled") { }
}

public class InvalidResponse : BaseResponse
{
    public string? message { get; set; }
    public InvalidResponse(int nonce) : base(nonce, "invalid") { }
}

public class TrueCandidatesResponse : BaseResponse
{
    public long[]? solutionsPerCandidate { get; set; }
    public TrueCandidatesResponse(int nonce) : base(nonce, "truecandidates") { }
}

public class SolvedResponse : BaseResponse
{
    public int[]? solution { get; set; }
    public SolvedResponse(int nonce) : base(nonce, "solved") { }
}

public class CountResponse : BaseResponse
{
    public long count { get; set; }
    public bool inProgress { get; set; }
    public CountResponse(int nonce) : base(nonce, "count") { }
}

public class EstimateResponse : BaseResponse
{
    public double estimate { get; set; }
    public double stderr { get; set; }
    public long iterations { get; set; }
    public double ci95_lower { get; set; }
    public double ci95_upper { get; set; }
    public double relErrPercent { get; set; }
    public EstimateResponse(int nonce) : base(nonce, "estimate") { }
}

public class LogicalCell
{
    public int value { get; set; }
    public int[]? candidates { get; set; }
}

public class LogicalResponse : BaseResponse
{
    public LogicalCell[]? cells { get; set; }
    public string? message { get; set; }
    public bool isValid { get; set; }
    public LogicalResponse(int nonce) : base(nonce, "logical") { }
}

[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(CanceledResponse))]
[JsonSerializable(typeof(InvalidResponse))]
[JsonSerializable(typeof(TrueCandidatesResponse))]
[JsonSerializable(typeof(SolvedResponse))]
[JsonSerializable(typeof(CountResponse))]
[JsonSerializable(typeof(LogicalResponse))]
[JsonSerializable(typeof(EstimateResponse))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
internal partial class WasmJsonContext : JsonSerializerContext
{
}
