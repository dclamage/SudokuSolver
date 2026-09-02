using SudokuSolver.PuzzleFormats.Native;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SudokuSolverService.Protocol;

/// <summary>Contains version and correlation fields common to native request envelopes.</summary>
public sealed class SolverRequestHeader
{
    /// <summary>Gets the native solver protocol version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>Gets the request correlation identifier.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the originating document revision.</summary>
    public required long DocumentRevision { get; init; }

    /// <summary>Gets the originating semantic revision.</summary>
    public required long SemanticRevision { get; init; }

    /// <summary>Gets the client-computed semantic hash, which remains unverified at this boundary.</summary>
    public required string SemanticHash { get; init; }

    /// <summary>Gets the owning candidate or playtest context identifier.</summary>
    public required string ContextId { get; init; }

    /// <summary>Gets the requested operation name.</summary>
    public required string Operation { get; init; }
}

/// <summary>Contains one revision-correlated native solver operation request.</summary>
public sealed class SolverRequest
{
    /// <summary>Gets the native solver protocol version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>Gets the request correlation identifier.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the originating document revision.</summary>
    public required long DocumentRevision { get; init; }

    /// <summary>Gets the originating semantic revision.</summary>
    public required long SemanticRevision { get; init; }

    /// <summary>Gets the client-computed semantic hash to verify.</summary>
    public required string SemanticHash { get; init; }

    /// <summary>Gets the owning candidate or playtest context identifier.</summary>
    public required string ContextId { get; init; }

    /// <summary>Gets the requested operation name.</summary>
    public required string Operation { get; init; }

    /// <summary>Gets the native puzzle package to project.</summary>
    [JsonConverter(typeof(NativePuzzlePackageProtocolConverter))]
    public required NativePuzzlePackage Puzzle { get; init; }

    /// <summary>Gets validation options when <see cref="Operation"/> is <c>validate</c>.</summary>
    public ValidateOptionsDto? ValidateOptions { get; init; }

    /// <summary>Gets solve options when <see cref="Operation"/> is <c>solve</c>.</summary>
    public SolveOptionsDto? SolveOptions { get; init; }

    /// <summary>Gets bounded-count options when <see cref="Operation"/> is <c>count</c>.</summary>
    public CountOptionsDto? CountOptions { get; init; }
}

/// <summary>Uses the native package's authoritative parser and serializer at the protocol boundary.</summary>
public sealed class NativePuzzlePackageProtocolConverter : JsonConverter<NativePuzzlePackage>
{
    /// <inheritdoc/>
    public override NativePuzzlePackage Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return NativePuzzlePackage.Parse(document.RootElement.GetRawText());
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        NativePuzzlePackage value,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.Parse(value.ToJson());
        document.RootElement.WriteTo(writer);
    }
}

/// <summary>Identifies the projection used by native validation.</summary>
public sealed class ValidateOptionsDto
{
    /// <summary>Gets the stable native projection identifier.</summary>
    public required string ProjectionId { get; init; }
}

/// <summary>Identifies the projection used by native solving.</summary>
public sealed class SolveOptionsDto
{
    /// <summary>Gets the stable native projection identifier.</summary>
    public required string ProjectionId { get; init; }
}

/// <summary>Contains options for a bounded native solution count.</summary>
public sealed class CountOptionsDto
{
    /// <summary>Gets the stable native projection identifier.</summary>
    public required string ProjectionId { get; init; }

    /// <summary>Gets the maximum solution count to return.</summary>
    public required long MaxSolutions { get; init; }
}