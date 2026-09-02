using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        NativePuzzleValidator.Validate(package);
        JsonObject semanticView = CreateSemanticView(package);
        using JsonDocument document = JsonDocument.Parse(semanticView.ToJsonString());
        StringBuilder canonical = new();
        AppendCanonical(canonical, document.RootElement);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
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
        if (hasReferencedRelease && package.Release is not null)
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

    private static void AppendCanonical(StringBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                bool firstProperty = true;
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                    static property => property.Name,
                    StringComparer.Ordinal))
                {
                    if (!firstProperty)
                    {
                        builder.Append(',');
                    }
                    firstProperty = false;
                    AppendJsonString(builder, property.Name);
                    builder.Append(':');
                    AppendCanonical(builder, property.Value);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                bool firstItem = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }
                    firstItem = false;
                    AppendCanonical(builder, item);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                AppendJsonString(builder, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out long number)
                    || number < -MaxSafeInteger
                    || number > MaxSafeInteger)
                {
                    throw new InvalidOperationException(
                        "Canonical semantic JSON numbers must be finite safe integers.");
                }
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new InvalidOperationException($"Unsupported semantic JSON kind {element.ValueKind}.");
        }
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ' || IsUnpairedSurrogate(value, index))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

    private static bool IsUnpairedSurrogate(string value, int index)
    {
        char character = value[index];
        if (char.IsHighSurrogate(character))
        {
            return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        }
        return char.IsLowSurrogate(character)
            && (index == 0 || !char.IsHighSurrogate(value[index - 1]));
    }
}