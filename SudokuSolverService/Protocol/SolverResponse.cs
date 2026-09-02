using System.Text.Json;

namespace SudokuSolverService.Protocol;

/// <summary>Contains one typed native solver progress or terminal response.</summary>
public sealed class SolverResponse
{
    /// <summary>Gets the response kind: progress, result, error, or canceled.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the native solver protocol version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>Gets the request correlation identifier.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the originating document revision.</summary>
    public required long DocumentRevision { get; init; }

    /// <summary>Gets the originating semantic revision.</summary>
    public required long SemanticRevision { get; init; }

    /// <summary>Gets the server-verified semantic hash, or an empty value if verification was impossible.</summary>
    public required string SemanticHash { get; init; }

    /// <summary>Gets the owning candidate or playtest context identifier.</summary>
    public required string ContextId { get; init; }

    /// <summary>Gets the operation that produced this response.</summary>
    public required string Operation { get; init; }

    /// <summary>Gets an optional native capability result.</summary>
    public CapabilityResultDto? Capability { get; init; }

    /// <summary>Gets an optional native solve result.</summary>
    public SolveResultDto? Solve { get; init; }

    /// <summary>Gets an optional bounded-count result or progress value.</summary>
    public CountResultDto? Count { get; init; }

    /// <summary>Gets an optional true-candidates result.</summary>
    public TrueCandidatesResultDto? TrueCandidates { get; init; }

    /// <summary>Gets an optional structured logical result.</summary>
    public LogicalResultDto? Logical { get; init; }

    /// <summary>Gets an optional typed error.</summary>
    public SolverErrorDto? Error { get; init; }

    /// <summary>Parses one native response using the source-generated protocol contract.</summary>
    /// <param name="json">The response JSON.</param>
    /// <returns>The parsed response.</returns>
    /// <exception cref="JsonException">The JSON is malformed or does not contain a response.</exception>
    public static SolverResponse Parse(string json)
        => JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.SolverResponse)
            ?? throw new JsonException("Native solver response was empty.");
}

/// <summary>Describes projection capabilities and contradiction state.</summary>
public sealed class CapabilityResultDto
{
    /// <summary>Gets the selected stable projection identifier.</summary>
    public required string ProjectionId { get; init; }

    /// <summary>Gets whether the projected position is contradictory.</summary>
    public required bool Contradiction { get; init; }

    /// <summary>Gets capability outcomes keyed by kind-qualified entity identifier.</summary>
    public required Dictionary<string, CapabilityEntityDto> Entities { get; init; }
}

/// <summary>Describes the solver capability of one native semantic entity.</summary>
public sealed class CapabilityEntityDto
{
    /// <summary>Gets the native entity kind.</summary>
    public required string EntityKind { get; init; }

    /// <summary>Gets the stable native entity identifier.</summary>
    public required string EntityId { get; init; }

    /// <summary>Gets the lower-camel capability status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets an optional reason when support is incomplete.</summary>
    public string? Reason { get; init; }
}

/// <summary>Contains solved value identifiers keyed by stable cell identifier.</summary>
public sealed class SolveResultDto
{
    /// <summary>Gets solved stable value identifiers keyed by stable cell identifier.</summary>
    public required Dictionary<string, string> ValuesByCellId { get; init; }
}

/// <summary>Contains a bounded solution-count progress or result value.</summary>
public sealed class CountResultDto
{
    /// <summary>Gets the current solution count, clamped to the request maximum.</summary>
    public required long SolutionCount { get; init; }

    /// <summary>Gets the request maximum used to bound the search.</summary>
    public required long MaxSolutions { get; init; }

    /// <summary>Gets whether the result reached the request maximum.</summary>
    public required bool IsClamped { get; init; }
}

/// <summary>Contains typed true-candidate counts in stable projected order.</summary>
public sealed class TrueCandidatesResultDto
{
    /// <summary>Gets stable cell identifiers in projected order.</summary>
    public required string[] CellIds { get; init; }

    /// <summary>Gets stable value identifiers in solver-value order.</summary>
    public required string[] ValueIdsBySolverValue { get; init; }

    /// <summary>Gets flattened nonnegative per-candidate solution counts.</summary>
    public required long[] SolutionCounts { get; init; }

    /// <summary>Gets optional flattened logical candidate masks.</summary>
    public int[]? LogicalCandidateMasks { get; init; }

    /// <summary>Gets the solution-count cap used by the request.</summary>
    public required long SolutionCountCap { get; init; }
}

/// <summary>Contains a typed logical-session result without open-ended payload values.</summary>
public sealed class LogicalResultDto
{
    /// <summary>Gets the stable logical session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the available stable deduction identifiers.</summary>
    public required string[] DeductionIds { get; init; }
}

/// <summary>Contains a machine-readable native solver error.</summary>
public sealed class SolverErrorDto
{
    /// <summary>Gets the stable error code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets a concise diagnostic message.</summary>
    public required string Message { get; init; }
}
