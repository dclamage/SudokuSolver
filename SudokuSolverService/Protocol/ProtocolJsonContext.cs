using System.Text.Json.Serialization;

namespace SudokuSolverService.Protocol;

/// <summary>Provides AOT-safe source-generated metadata for native and legacy protocol payloads.</summary>
[JsonSerializable(typeof(SolverRequest))]
[JsonSerializable(typeof(SolverRequestHeader))]
[JsonSerializable(typeof(SolverResponse))]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(CanceledResponse))]
[JsonSerializable(typeof(InvalidResponse))]
[JsonSerializable(typeof(TrueCandidatesResponse))]
[JsonSerializable(typeof(SolvedResponse))]
[JsonSerializable(typeof(CountResponse))]
[JsonSerializable(typeof(LogicalResponse))]
[JsonSerializable(typeof(EstimateResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
public partial class ProtocolJsonContext : JsonSerializerContext;