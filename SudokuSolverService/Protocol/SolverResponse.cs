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
public sealed record LogicalResultDto
{
    /// <summary>Gets the stable logical session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the deterministic hash of the session's current position.</summary>
    public required string PositionHash { get; init; }

    /// <summary>Gets the current projected board in stable cell order.</summary>
    public required LogicalCellStateDto[] Cells { get; init; }

    /// <summary>Gets the complete currently available structured deductions.</summary>
    public required LogicalDeductionDto[] AvailableDeductions { get; init; }

    /// <summary>Gets ordered deduction identifiers already applied to the session.</summary>
    public required string[] HistoryDeductionIds { get; init; }

    /// <summary>Gets available identifiers for compatibility with the initial protocol scaffold.</summary>
    public string[] DeductionIds => AvailableDeductions.Select(deduction => deduction.Id).ToArray();
}

/// <summary>Contains one stable cell's current logical value or candidates.</summary>
public sealed class LogicalCellStateDto
{
    /// <summary>Gets the stable cell identifier.</summary>
    public required string CellId { get; init; }

    /// <summary>Gets the stable placed value identifier, or <see langword="null"/> when unset.</summary>
    public string? ValueId { get; init; }

    /// <summary>Gets stable candidate value identifiers when unset.</summary>
    public required string[] CandidateValueIds { get; init; }
}

/// <summary>Contains one fully typed logical deduction for native transport.</summary>
public sealed class LogicalDeductionDto
{
    /// <summary>Gets the deterministic deduction identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the stable technique identifier.</summary>
    public required string TechniqueId { get; init; }

    /// <summary>Gets the stable owning constraint identifier.</summary>
    public required string OwningConstraintId { get; init; }

    /// <summary>Gets the exact position hash required by this deduction.</summary>
    public required string PreconditionHash { get; init; }

    /// <summary>Gets typed premises used by the deduction.</summary>
    public required LogicalPremiseDto[] Premises { get; init; }

    /// <summary>Gets the stable-ID semantic delta.</summary>
    public required LogicalDeltaDto Delta { get; init; }

    /// <summary>Gets ordered semantic walkthrough frames.</summary>
    public required LogicalWalkthroughFrameDto[] Frames { get; init; }
}

/// <summary>Contains one typed logical premise.</summary>
public sealed class LogicalPremiseDto
{
    /// <summary>Gets the stable premise kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets an optional stable cell identifier.</summary>
    public string? CellId { get; init; }

    /// <summary>Gets an optional stable value identifier.</summary>
    public string? ValueId { get; init; }
}

/// <summary>Contains one typed logical board delta.</summary>
public sealed class LogicalDeltaDto
{
    /// <summary>Gets stable placements.</summary>
    public required LogicalPlacementDto[] Placements { get; init; }

    /// <summary>Gets stable eliminations.</summary>
    public required LogicalEliminationDto[] Eliminations { get; init; }
}

/// <summary>Contains one stable placement.</summary>
public sealed class LogicalPlacementDto
{
    /// <summary>Gets the stable cell identifier.</summary>
    public required string CellId { get; init; }

    /// <summary>Gets the stable value identifier.</summary>
    public required string ValueId { get; init; }
}

/// <summary>Contains one stable candidate elimination.</summary>
public sealed class LogicalEliminationDto
{
    /// <summary>Gets the stable cell identifier.</summary>
    public required string CellId { get; init; }

    /// <summary>Gets the stable value identifier.</summary>
    public required string ValueId { get; init; }
}

/// <summary>Contains one semantic walkthrough frame.</summary>
public sealed class LogicalWalkthroughFrameDto
{
    /// <summary>Gets normally focused semantic entities.</summary>
    public required LogicalEntityReferenceDto[] Focus { get; init; }

    /// <summary>Gets semantically de-emphasized entities.</summary>
    public required LogicalEntityReferenceDto[] Dim { get; init; }

    /// <summary>Gets emphasized semantic entities.</summary>
    public required LogicalEntityReferenceDto[] Highlight { get; init; }

    /// <summary>Gets typed explanation content.</summary>
    public required LogicalExplanationDto Explanation { get; init; }
}

/// <summary>References one stable semantic entity.</summary>
public sealed class LogicalEntityReferenceDto
{
    /// <summary>Gets the stable entity kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the stable entity identifier.</summary>
    public required string Id { get; init; }
}

/// <summary>Contains one localization key and ordered typed arguments.</summary>
public sealed class LogicalExplanationDto
{
    /// <summary>Gets the stable localization key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets ordered typed arguments.</summary>
    public required LogicalExplanationArgumentDto[] Arguments { get; init; }
}

/// <summary>Contains one typed explanation argument.</summary>
public sealed class LogicalExplanationArgumentDto
{
    /// <summary>Gets the semantic argument kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the stable identifier or literal argument value.</summary>
    public required string Value { get; init; }
}

/// <summary>Contains a machine-readable native solver error.</summary>
public sealed class SolverErrorDto
{
    /// <summary>Gets the stable error code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets a concise diagnostic message.</summary>
    public required string Message { get; init; }
}