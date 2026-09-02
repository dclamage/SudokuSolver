using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

#nullable enable

namespace SudokuSolver.PuzzleFormats.Native;

/// <summary>Computes the cross-runtime semantic identity of a native puzzle package.</summary>
public static class NativeSemanticHasher
{
    private const long MaxSafeInteger = 9_007_199_254_740_991;

    /// <summary>Computes a lowercase SHA-256 hash of the canonical portable semantic view.</summary>
    /// <param name="package">The native puzzle package.</param>
    /// <returns>A hash in <c>sha256:&lt;lowercase-hex&gt;</c> form.</returns>
    /// <exception cref="InvalidOperationException">A semantic number is not a finite safe integer.</exception>
    public static string Compute(NativePuzzlePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        JsonObject semanticView = CreateSemanticView(package);
        using JsonDocument document = JsonDocument.Parse(semanticView.ToJsonString());
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        }))
        {
            WriteCanonical(writer, document.RootElement);
        }
        byte[] digest = SHA256.HashData(stream.ToArray());
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static JsonObject CreateSemanticView(NativePuzzlePackage package)
    {
        HashSet<string> referencedPathIds = [];
        HashSet<string> referencedEdgeIds = [];
        bool hasReferencedRelease = false;
        foreach (NativeConstraintInstance constraint in package.Constraints)
        {
            hasReferencedRelease |= constraint.DefinitionReleaseId is not null;
            foreach (List<NativeEntityReference> references in constraint.Bindings.Values)
            {
                foreach (NativeEntityReference reference in references)
                {
                    if (string.Equals(reference.Kind, "path", StringComparison.Ordinal))
                    {
                        referencedPathIds.Add(reference.Id);
                    }
                    else if (string.Equals(reference.Kind, "edge", StringComparison.Ordinal))
                    {
                        referencedEdgeIds.Add(reference.Id);
                    }
                }
            }
        }

        JsonObject view = new()
        {
            ["schemaVersion"] = package.SchemaVersion,
            ["domains"] = MapRecord(package.Domains, static domain => new JsonObject
            {
                ["id"] = domain.Id,
                ["values"] = new JsonArray(domain.Values.Select(static value =>
                {
                    JsonObject node = new() { ["id"] = value.Id };
                    if (value.NumericValue.HasValue)
                    {
                        node["numericValue"] = value.NumericValue.Value;
                    }
                    return (JsonNode)node;
                }).ToArray()),
            }),
            ["cells"] = MapRecord(package.Cells, static cell => new JsonObject
            {
                ["id"] = cell.Id,
                ["domainId"] = cell.DomainId,
                ["input"] = new JsonObject
                {
                    ["acceptsValue"] = cell.Input.AcceptsValue,
                    ["acceptsCandidates"] = cell.Input.AcceptsCandidates,
                },
            }),
            ["boards"] = MapRecord(package.Boards, static board => new JsonObject
            {
                ["id"] = board.Id,
                ["cellIds"] = StringArray(board.CellIds),
                ["groupIds"] = StringArray(board.GroupIds),
            }),
            ["groups"] = MapRecord(package.Groups, static group => new JsonObject
            {
                ["id"] = group.Id,
                ["roles"] = StringArray(group.Roles),
                ["cellIds"] = StringArray(group.CellIds),
            }),
            ["adjacency"] = MapRecord(package.Adjacency, static adjacency => new JsonObject
            {
                ["id"] = adjacency.Id,
                ["kind"] = adjacency.Kind,
                ["fromCellId"] = adjacency.FromCellId,
                ["toCellId"] = adjacency.ToCellId,
            }),
            ["edges"] = MapFilteredRecord(package.Edges, referencedEdgeIds, static edge => new JsonObject
            {
                ["id"] = edge.Id,
                ["fromPointId"] = edge.FromPointId,
                ["toPointId"] = edge.ToPointId,
            }),
            ["paths"] = MapFilteredRecord(package.Paths, referencedPathIds, static path => new JsonObject
            {
                ["id"] = path.Id,
                ["pointIds"] = StringArray(path.PointIds),
                ["closed"] = path.Closed,
            }),
            ["givens"] = StringRecord(package.Givens),
            ["constraints"] = ConstraintArray(package.Constraints),
            ["solverProjections"] = ProjectionArray(package.SolverProjections),
            ["extensions"] = SemanticExtensions(package.Extensions),
        };
        if (hasReferencedRelease && package.Release.HasValue)
        {
            view["release"] = ParseElement(package.Release.Value);
        }
        return view;
    }

    private static JsonObject MapRecord<T>(
        IReadOnlyDictionary<string, T> record,
        Func<T, JsonNode> mapValue)
    {
        JsonObject result = [];
        foreach ((string key, T value) in record)
        {
            result[key] = mapValue(value);
        }
        return result;
    }

    private static JsonObject MapFilteredRecord<T>(
        IReadOnlyDictionary<string, T> record,
        IReadOnlySet<string> includedKeys,
        Func<T, JsonNode> mapValue)
    {
        JsonObject result = [];
        foreach ((string key, T value) in record)
        {
            if (includedKeys.Contains(key))
            {
                result[key] = mapValue(value);
            }
        }
        return result;
    }

    private static JsonObject StringRecord(IReadOnlyDictionary<string, string> record)
    {
        JsonObject result = [];
        foreach ((string key, string value) in record)
        {
            result[key] = value;
        }
        return result;
    }

    private static JsonArray ConstraintArray(IEnumerable<NativeConstraintInstance> constraints)
    {
        JsonArray result = [];
        foreach (NativeConstraintInstance constraint in constraints)
        {
            JsonObject node = new()
            {
                ["id"] = constraint.Id,
                ["typeId"] = constraint.TypeId,
                ["bindings"] = BindingRecord(constraint.Bindings),
                ["parameters"] = ElementRecord(constraint.Parameters),
            };
            if (constraint.DefinitionReleaseId is not null)
            {
                node["definitionReleaseId"] = constraint.DefinitionReleaseId;
            }
            result.Add(node);
        }
        return result;
    }

    private static JsonObject BindingRecord(
        IReadOnlyDictionary<string, List<NativeEntityReference>> bindings)
    {
        JsonObject result = [];
        foreach ((string role, List<NativeEntityReference> references) in bindings)
        {
            result[role] = new JsonArray(references.Select(static reference => (JsonNode)new JsonObject
            {
                ["kind"] = reference.Kind,
                ["id"] = reference.Id,
            }).ToArray());
        }
        return result;
    }

    private static JsonObject ElementRecord(IReadOnlyDictionary<string, JsonElement> elements)
    {
        JsonObject result = [];
        foreach ((string key, JsonElement value) in elements)
        {
            result[key] = ParseElement(value);
        }
        return result;
    }

    private static JsonArray ProjectionArray(IEnumerable<NativeSolverProjection> projections)
    {
        JsonArray result = [];
        foreach (NativeSolverProjection projection in projections)
        {
            result.Add(new JsonObject
            {
                ["id"] = projection.Id,
                ["kind"] = projection.Kind,
                ["boardId"] = projection.BoardId,
                ["domainId"] = projection.DomainId,
                ["valueIdsBySolverValue"] = StringArray(projection.ValueIdsBySolverValue),
                ["cellIdsByRow"] = new JsonArray(projection.CellIdsByRow
                    .Select(static row => (JsonNode)StringArray(row))
                    .ToArray()),
            });
        }
        return result;
    }

    private static JsonObject SemanticExtensions(IReadOnlyDictionary<string, NativeExtension> extensions)
    {
        JsonObject result = [];
        foreach ((string key, NativeExtension extension) in extensions)
        {
            if (string.Equals(extension.Impact, "semantic", StringComparison.Ordinal))
            {
                result[key] = new JsonObject
                {
                    ["impact"] = extension.Impact,
                    ["data"] = ParseElement(extension.Data),
                };
            }
        }
        return result;
    }

    private static JsonArray StringArray(IEnumerable<string> values)
        => new(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonNode? ParseElement(JsonElement element)
        => JsonNode.Parse(element.GetRawText());

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                    static property => property.Name,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out long number)
                    || number < -MaxSafeInteger
                    || number > MaxSafeInteger)
                {
                    throw new InvalidOperationException(
                        "Canonical semantic JSON numbers must be finite safe integers.");
                }
                writer.WriteNumberValue(number);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported semantic JSON kind {element.ValueKind}.");
        }
    }
}