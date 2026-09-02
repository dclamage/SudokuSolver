using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace SudokuSolver.PuzzleFormats.Native;

/// <summary>
/// Represents a version-one native puzzle package shared with the web frontend.
/// </summary>
public sealed class NativePuzzlePackage
{
    private List<JsonElement>? _assets;

    /// <summary>Gets the native package schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the stable document identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the document revision.</summary>
    [JsonRequired]
    public long Revision { get; init; }

    /// <summary>Gets the revision of portable puzzle semantics.</summary>
    [JsonRequired]
    public long SemanticRevision { get; init; }

    /// <summary>Gets display metadata that does not affect solver semantics.</summary>
    public required NativeMetadata Metadata { get; init; }

    /// <summary>Gets value domains keyed by stable domain identifier.</summary>
    public required Dictionary<string, NativeDomain> Domains { get; init; }

    /// <summary>Gets cells keyed by stable cell identifier.</summary>
    public required Dictionary<string, NativeCell> Cells { get; init; }

    /// <summary>Gets boards keyed by stable board identifier.</summary>
    public required Dictionary<string, NativeBoard> Boards { get; init; }

    /// <summary>Gets semantic groups keyed by stable group identifier.</summary>
    public required Dictionary<string, NativeGroup> Groups { get; init; }

    /// <summary>Gets adjacency records keyed by stable identifier.</summary>
    public required Dictionary<string, NativeAdjacency> Adjacency { get; init; }

    /// <summary>Gets presentation points keyed by stable identifier.</summary>
    public required Dictionary<string, NativePoint> Points { get; init; }

    /// <summary>Gets edges keyed by stable identifier.</summary>
    public required Dictionary<string, NativeEdge> Edges { get; init; }

    /// <summary>Gets paths keyed by stable identifier.</summary>
    public required Dictionary<string, NativePath> Paths { get; init; }

    /// <summary>Gets native givens keyed by stable cell identifier.</summary>
    public required Dictionary<string, string> Givens { get; init; }

    /// <summary>Gets ordered constraint instances.</summary>
    public required List<NativeConstraintInstance> Constraints { get; init; }

    /// <summary>Gets explicit solver projections.</summary>
    public required List<NativeSolverProjection> SolverProjections { get; init; }

    /// <summary>Gets presentation state.</summary>
    public required NativePresentation Presentation { get; init; }

    /// <summary>Gets optional source-format information.</summary>
    public JsonElement? Source { get; set; }

    /// <summary>Gets optional compiled release information.</summary>
    public JsonElement? Release { get; set; }

    /// <summary>Gets optional embedded asset descriptors.</summary>
    public List<JsonElement>? Assets
    {
        get => _assets;
        init
        {
            _assets = value;
            AssetsWasSpecified = true;
        }
    }

    /// <summary>Gets whether the optional assets property was present in the input.</summary>
    [JsonIgnore]
    internal bool AssetsWasSpecified { get; set; }

    /// <summary>Gets optional provenance information.</summary>
    public JsonElement? Provenance { get; set; }

    /// <summary>Gets portable authoring workspace state.</summary>
    public required NativeAuthoringState Authoring { get; init; }

    /// <summary>Gets namespaced extension sections.</summary>
    public required Dictionary<string, NativeExtension> Extensions { get; init; }

    /// <summary>Gets unknown root members retained for forward compatibility.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];

    /// <summary>Parses a native JSON package and takes ownership of all open JSON payloads.</summary>
    /// <param name="json">The native package JSON.</param>
    /// <returns>The parsed native package.</returns>
    /// <exception cref="ArgumentException">The JSON is empty or does not describe schema version one.</exception>
    /// <exception cref="JsonException">The JSON is malformed or omits a required member.</exception>
    public static NativePuzzlePackage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Native puzzle JSON cannot be empty.", nameof(json));
        }

        using JsonDocument document = JsonDocument.Parse(json);
        NativePuzzlePackage package = JsonSerializer.Deserialize(
            document.RootElement,
            NativePuzzleJsonContext.Default.NativePuzzlePackage)
            ?? throw new JsonException("Native puzzle JSON did not contain a package.");
        package.ApplyOptionalPropertyPresence(document.RootElement);
        if (package.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported native puzzle schema version {package.SchemaVersion}.", nameof(json));
        }

        NativePuzzleValidator.Validate(package);
        package.CloneOpenPayloads();
        return package;
    }

    /// <summary>Serializes the native package with the shared camel-case JSON contract.</summary>
    /// <returns>The serialized native package JSON.</returns>
    public string ToJson()
    {
        NativePuzzleValidator.Validate(this);
        return JsonSerializer.Serialize(this, NativePuzzleJsonContext.Default.NativePuzzlePackage);
    }

    private void CloneOpenPayloads()
    {
        Source = Source?.Clone();
        Release = Release?.Clone();
        Provenance = Provenance?.Clone();
        CloneElements(Assets);
        CloneExtensionData(ExtensionData);
        CloneExtensionData(Metadata.ExtensionData);

        foreach (NativeDomain domain in Domains.Values)
        {
            CloneExtensionData(domain.ExtensionData);
            foreach (NativeDomainValue value in domain.Values)
            {
                CloneExtensionData(value.ExtensionData);
            }
        }

        foreach (NativeCell cell in Cells.Values)
        {
            CloneExtensionData(cell.ExtensionData);
            CloneExtensionData(cell.Shape.ExtensionData);
            foreach (NativeShapePoint point in cell.Shape.Points ?? [])
            {
                CloneExtensionData(point.ExtensionData);
            }
            CloneExtensionData(cell.Input.ExtensionData);
        }

        foreach (NativeBoard board in Boards.Values)
        {
            CloneExtensionData(board.ExtensionData);
        }
        foreach (NativeGroup group in Groups.Values)
        {
            CloneExtensionData(group.ExtensionData);
        }
        foreach (NativeAdjacency adjacency in Adjacency.Values)
        {
            CloneExtensionData(adjacency.ExtensionData);
        }
        foreach (NativePoint point in Points.Values)
        {
            CloneExtensionData(point.ExtensionData);
        }
        foreach (NativeEdge edge in Edges.Values)
        {
            CloneExtensionData(edge.ExtensionData);
        }
        foreach (NativePath path in Paths.Values)
        {
            CloneExtensionData(path.ExtensionData);
        }

        foreach (NativeConstraintInstance constraint in Constraints)
        {
            CloneElementDictionary(constraint.Parameters);
            CloneElementDictionary(constraint.StyleOverrides);
            CloneExtensionData(constraint.ExtensionData);
            foreach (List<NativeEntityReference> references in constraint.Bindings.Values)
            {
                foreach (NativeEntityReference reference in references)
                {
                    CloneExtensionData(reference.ExtensionData);
                }
            }
        }

        foreach (NativeSolverProjection projection in SolverProjections)
        {
            CloneExtensionData(projection.ExtensionData);
        }

        CloneElementDictionary(Presentation.Styles);
        CloneElements(Presentation.SceneElements);
        CloneExtensionData(Presentation.ExtensionData);
        foreach (NativeCandidateContext context in Authoring.CandidateContexts)
        {
            CloneExtensionData(context.ExtensionData);
        }
        CloneExtensionData(Authoring.ExtensionData);
        foreach (NativeExtension extension in Extensions.Values)
        {
            extension.Data = extension.Data.Clone();
            CloneExtensionData(extension.ExtensionData);
        }
    }

    private void ApplyOptionalPropertyPresence(JsonElement root)
    {
        AssetsWasSpecified = root.TryGetProperty("assets", out _);

        if (root.TryGetProperty("domains", out JsonElement domains)
            && domains.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty domainProperty in domains.EnumerateObject())
            {
                if (!Domains.TryGetValue(domainProperty.Name, out NativeDomain? domain)
                    || domainProperty.Value.ValueKind != JsonValueKind.Object
                    || !domainProperty.Value.TryGetProperty("values", out JsonElement values)
                    || values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                int valueIndex = 0;
                foreach (JsonElement valueElement in values.EnumerateArray())
                {
                    if (valueIndex < domain.Values.Count && valueElement.ValueKind == JsonValueKind.Object)
                    {
                        domain.Values[valueIndex].NumericValueWasSpecified = valueElement.TryGetProperty(
                            "numericValue",
                            out _);
                    }
                    valueIndex++;
                }
            }
        }

        if (root.TryGetProperty("cells", out JsonElement cells)
            && cells.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty cellProperty in cells.EnumerateObject())
            {
                if (Cells.TryGetValue(cellProperty.Name, out NativeCell? cell)
                    && cellProperty.Value.ValueKind == JsonValueKind.Object)
                {
                    cell.LabelWasSpecified = cellProperty.Value.TryGetProperty("label", out _);
                }
            }
        }

        if (root.TryGetProperty("constraints", out JsonElement constraints)
            && constraints.ValueKind == JsonValueKind.Array)
        {
            int constraintIndex = 0;
            foreach (JsonElement constraintElement in constraints.EnumerateArray())
            {
                if (constraintIndex < Constraints.Count && constraintElement.ValueKind == JsonValueKind.Object)
                {
                    NativeConstraintInstance constraint = Constraints[constraintIndex];
                    constraint.DefinitionReleaseIdWasSpecified = constraintElement.TryGetProperty(
                        "definitionReleaseId",
                        out _);
                    constraint.StyleOverridesWasSpecified = constraintElement.TryGetProperty("styleOverrides", out _);
                }
                constraintIndex++;
            }
        }
    }

    private static void CloneElementDictionary(Dictionary<string, JsonElement>? elements)
    {
        if (elements is null)
        {
            return;
        }
        foreach (string key in elements.Keys.ToArray())
        {
            elements[key] = elements[key].Clone();
        }
    }

    private static void CloneElements(List<JsonElement>? elements)
    {
        if (elements is null)
        {
            return;
        }
        for (int index = 0; index < elements.Count; index++)
        {
            elements[index] = elements[index].Clone();
        }
    }

    private static void CloneExtensionData(Dictionary<string, JsonElement> extensionData)
        => CloneElementDictionary(extensionData);
}

/// <summary>Contains nonsemantic puzzle metadata.</summary>
public sealed class NativeMetadata
{
    /// <summary>Gets the puzzle title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets the puzzle author text.</summary>
    public required string Author { get; init; }
    /// <summary>Gets the puzzle rules text.</summary>
    public required string Rules { get; init; }
    /// <summary>Gets unknown metadata members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines an ordered native value domain.</summary>
public sealed class NativeDomain
{
    /// <summary>Gets the stable domain identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the ordered domain values.</summary>
    public required List<NativeDomainValue> Values { get; init; }
    /// <summary>Gets unknown domain members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines one stable value in a native domain.</summary>
public sealed class NativeDomainValue
{
    private long? _numericValue;

    /// <summary>Gets the stable value identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the display label.</summary>
    public required string Label { get; init; }
    /// <summary>Gets the optional arithmetic interpretation.</summary>
    public long? NumericValue
    {
        get => _numericValue;
        init
        {
            _numericValue = value;
            NumericValueWasSpecified = true;
        }
    }

    /// <summary>Gets whether the optional numeric interpretation was present in the input.</summary>
    [JsonIgnore]
    internal bool NumericValueWasSpecified { get; set; }
    /// <summary>Gets unknown domain-value members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a native puzzle cell.</summary>
public sealed class NativeCell
{
    private string? _label;

    /// <summary>Gets the stable cell identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable domain identifier.</summary>
    public required string DomainId { get; init; }
    /// <summary>Gets presentation geometry.</summary>
    public required NativeCellShape Shape { get; init; }
    /// <summary>Gets input capabilities.</summary>
    public required NativeCellInput Input { get; init; }
    /// <summary>Gets an optional visual label.</summary>
    public string? Label
    {
        get => _label;
        init
        {
            _label = value;
            LabelWasSpecified = true;
        }
    }

    /// <summary>Gets whether the optional label property was present in the input.</summary>
    [JsonIgnore]
    internal bool LabelWasSpecified { get; set; }
    /// <summary>Gets unknown cell members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines the presentation geometry for a cell.</summary>
public sealed class NativeCellShape
{
    /// <summary>Gets the shape discriminator.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the rectangle X coordinate.</summary>
    public double? X { get; init; }
    /// <summary>Gets the rectangle Y coordinate.</summary>
    public double? Y { get; init; }
    /// <summary>Gets the rectangle width.</summary>
    public double? Width { get; init; }
    /// <summary>Gets the rectangle height.</summary>
    public double? Height { get; init; }
    /// <summary>Gets polygon vertices.</summary>
    public List<NativeShapePoint>? Points { get; init; }
    /// <summary>Gets path geometry.</summary>
    public string? D { get; init; }
    /// <summary>Gets unknown shape members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines one presentation point in a polygon cell shape.</summary>
public sealed class NativeShapePoint
{
    /// <summary>Gets the X coordinate.</summary>
    [JsonRequired]
    public double X { get; init; }
    /// <summary>Gets the Y coordinate.</summary>
    [JsonRequired]
    public double Y { get; init; }
    /// <summary>Gets unknown shape-point members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines the interactions accepted by a cell.</summary>
public sealed class NativeCellInput
{
    /// <summary>Gets whether the cell accepts a value.</summary>
    [JsonRequired]
    public bool AcceptsValue { get; init; }
    /// <summary>Gets whether the cell accepts candidate marks.</summary>
    [JsonRequired]
    public bool AcceptsCandidates { get; init; }
    /// <summary>Gets unknown input members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a named collection of native cells and groups.</summary>
public sealed class NativeBoard
{
    /// <summary>Gets the stable board identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the board display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets ordered stable cell identifiers.</summary>
    public required List<string> CellIds { get; init; }
    /// <summary>Gets ordered stable group identifiers.</summary>
    public required List<string> GroupIds { get; init; }
    /// <summary>Gets unknown board members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines an explicit semantic group.</summary>
public sealed class NativeGroup
{
    /// <summary>Gets the stable group identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets semantic roles such as row, column, or region.</summary>
    public required List<string> Roles { get; init; }
    /// <summary>Gets ordered stable cell identifiers.</summary>
    public required List<string> CellIds { get; init; }
    /// <summary>Gets unknown group members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines an explicit adjacency relationship.</summary>
public sealed class NativeAdjacency
{
    /// <summary>Gets the stable adjacency identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the adjacency kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the stable source cell identifier.</summary>
    public required string FromCellId { get; init; }
    /// <summary>Gets the stable target cell identifier.</summary>
    public required string ToCellId { get; init; }
    /// <summary>Gets unknown adjacency members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a named presentation point.</summary>
public sealed class NativePoint
{
    /// <summary>Gets the stable point identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the X coordinate.</summary>
    [JsonRequired]
    public double X { get; init; }
    /// <summary>Gets the Y coordinate.</summary>
    [JsonRequired]
    public double Y { get; init; }
    /// <summary>Gets unknown point members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a stable edge between two points.</summary>
public sealed class NativeEdge
{
    /// <summary>Gets the stable edge identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable source point identifier.</summary>
    public required string FromPointId { get; init; }
    /// <summary>Gets the stable target point identifier.</summary>
    public required string ToPointId { get; init; }
    /// <summary>Gets unknown edge members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a stable path through named points.</summary>
public sealed class NativePath
{
    /// <summary>Gets the stable path identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets ordered stable point identifiers.</summary>
    public required List<string> PointIds { get; init; }
    /// <summary>Gets whether the path is closed.</summary>
    [JsonRequired]
    public bool Closed { get; init; }
    /// <summary>Gets unknown path members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a native constraint instance with typed entity bindings.</summary>
public sealed class NativeConstraintInstance
{
    private string? _definitionReleaseId;
    private Dictionary<string, JsonElement>? _styleOverrides;

    /// <summary>Gets the stable constraint identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable constraint type identifier.</summary>
    public required string TypeId { get; init; }
    /// <summary>Gets the optional immutable custom-definition release identifier.</summary>
    public string? DefinitionReleaseId
    {
        get => _definitionReleaseId;
        init
        {
            _definitionReleaseId = value;
            DefinitionReleaseIdWasSpecified = true;
        }
    }

    /// <summary>Gets whether the optional definition-release property was present in the input.</summary>
    [JsonIgnore]
    internal bool DefinitionReleaseIdWasSpecified { get; set; }
    /// <summary>Gets role-named ordered entity bindings.</summary>
    public required Dictionary<string, List<NativeEntityReference>> Bindings { get; init; }
    /// <summary>Gets typed semantic parameters.</summary>
    public required Dictionary<string, JsonElement> Parameters { get; init; }
    /// <summary>Gets optional nonsemantic style overrides.</summary>
    public Dictionary<string, JsonElement>? StyleOverrides
    {
        get => _styleOverrides;
        init
        {
            _styleOverrides = value;
            StyleOverridesWasSpecified = true;
        }
    }

    /// <summary>Gets whether the optional style-overrides property was present in the input.</summary>
    [JsonIgnore]
    internal bool StyleOverridesWasSpecified { get; set; }
    /// <summary>Gets unknown constraint members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>References a stable entity from a constraint binding.</summary>
public sealed class NativeEntityReference
{
    /// <summary>Gets the entity kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the stable entity identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets unknown reference members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines an explicit projection into the current square Latin solver.</summary>
public sealed class NativeSolverProjection
{
    /// <summary>Gets the stable projection identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the projection kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the projected board identifier.</summary>
    public required string BoardId { get; init; }
    /// <summary>Gets the projected domain identifier.</summary>
    public required string DomainId { get; init; }
    /// <summary>Gets stable value identifiers in current solver-value order.</summary>
    public required List<string> ValueIdsBySolverValue { get; init; }
    /// <summary>Gets stable cell identifiers in explicit row-major order.</summary>
    public required List<List<string>> CellIdsByRow { get; init; }
    /// <summary>Gets unknown projection members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Contains nonsemantic native presentation state.</summary>
public sealed class NativePresentation
{
    /// <summary>Gets named style payloads.</summary>
    public required Dictionary<string, JsonElement> Styles { get; init; }
    /// <summary>Gets ordered scene elements.</summary>
    public required List<JsonElement> SceneElements { get; init; }
    /// <summary>Gets unknown presentation members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Contains portable setter workspace state.</summary>
public sealed class NativeAuthoringState
{
    /// <summary>Gets ordered candidate-context payloads.</summary>
    public required List<NativeCandidateContext> CandidateContexts { get; init; }
    /// <summary>Gets manual marks by context, cell, and stable value identifier.</summary>
    public required Dictionary<string, Dictionary<string, List<string>>> ManualMarks { get; init; }
    /// <summary>Gets unknown authoring members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a portable setter candidate context.</summary>
public sealed class NativeCandidateContext
{
    /// <summary>Gets the stable candidate-context identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the context display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the context kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the optional True Candidates refresh policy.</summary>
    public string? Refresh { get; init; }
    /// <summary>Gets the optional True Candidates display mode.</summary>
    public string? Display { get; init; }
    /// <summary>Gets the optional True Candidates solution-count cap.</summary>
    public long? SolutionCountCap { get; init; }
    /// <summary>Gets whether a logical context follows the current semantic revision.</summary>
    public bool? FollowPuzzleRevision { get; init; }
    /// <summary>Gets enabled logical technique identifiers.</summary>
    public List<string>? EnabledTechniqueIds { get; init; }
    /// <summary>Gets unknown context members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Defines a namespaced extension and its semantic impact.</summary>
public sealed class NativeExtension
{
    /// <summary>Gets whether the extension is semantic or cosmetic.</summary>
    public required string Impact { get; init; }
    /// <summary>Gets the extension-owned JSON payload.</summary>
    [JsonRequired]
    public JsonElement Data { get; set; }
    /// <summary>Gets unknown extension members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

/// <summary>Provides source-generated JSON metadata for native puzzle packages.</summary>
[JsonSerializable(typeof(NativePuzzlePackage))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class NativePuzzleJsonContext : JsonSerializerContext
{
}