using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

#nullable enable

namespace SudokuSolver.PuzzleFormats.Native;

/// <summary>Validates native packages against the shared version-one structural and reference contract.</summary>
internal static class NativePuzzleValidator
{
    private const long MaxSafeInteger = 9_007_199_254_740_991;
    private static readonly HashSet<string> EntityKinds = ["cell", "group", "edge", "point", "path"];
    private static readonly string[] RequiredContextIds = ["setter-notes", "true-candidates", "logical-solver"];

    /// <summary>Validates a parsed or programmatically modified native package.</summary>
    /// <param name="package">The package to validate.</param>
    /// <exception cref="ArgumentException">The package violates the shared version-one contract.</exception>
    internal static void Validate(NativePuzzlePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.SchemaVersion != 1)
        {
            Invalid("unsupported puzzle schema version");
        }
        RequireNonEmpty(package.Id, "puzzle id");
        RequireNonNegativeSafeInteger(package.Revision, "revision");
        RequireNonNegativeSafeInteger(package.SemanticRevision, "semanticRevision");
        if (package.SemanticRevision > package.Revision)
        {
            Invalid("semanticRevision cannot exceed revision");
        }

        ValidateMetadata(package.Metadata);
        ValidateDomains(package.Domains);
        ValidateCells(package.Cells, package.Domains);
        ValidateGroups(package.Groups, package.Cells);
        ValidateBoards(package.Boards, package.Cells, package.Groups);
        ValidateAdjacency(package.Adjacency, package.Cells);
        ValidatePoints(package.Points);
        ValidateEdges(package.Edges, package.Points);
        ValidatePaths(package.Paths, package.Points);
        ValidateConstraints(package);
        ValidateGivens(package);
        ValidateProjections(package);
        ValidatePresentation(package.Presentation);
        ValidateAuthoring(package);
        ValidateExtensions(package.Extensions);
        ValidateOptionalPayloads(package);
    }

    private static void ValidateMetadata(NativeMetadata metadata)
    {
        if (metadata is null)
        {
            Invalid("metadata must be an object");
        }
        RequireNonEmpty(metadata.Title, "metadata title");
        if (metadata.Author is null)
        {
            Invalid("metadata author must be a string");
        }
        if (metadata.Rules is null)
        {
            Invalid("metadata rules must be a string");
        }
    }

    private static void ValidateDomains(IReadOnlyDictionary<string, NativeDomain> domains)
    {
        RequireRecord(domains, "domains");
        foreach ((string domainKey, NativeDomain domain) in domains)
        {
            RequireNonEmpty(domainKey, "domain key");
            RequireObject(domain, $"domain {domainKey}");
            RequireNonEmpty(domain.Id, $"domain {domainKey} id");
            if (!string.Equals(domainKey, domain.Id, StringComparison.Ordinal))
            {
                Invalid($"domain key {domainKey} does not match id {domain.Id}");
            }
            RequireList(domain.Values, $"domain {domain.Id} values");
            HashSet<string> valueIds = new(StringComparer.Ordinal);
            foreach (NativeDomainValue value in domain.Values)
            {
                RequireObject(value, $"domain {domain.Id} value");
                RequireNonEmpty(value.Id, $"domain {domain.Id} value id");
                if (!valueIds.Add(value.Id))
                {
                    Invalid($"domain {domain.Id} contains duplicate value {value.Id}");
                }
                RequireNonEmpty(value.Label, $"domain {domain.Id} value {value.Id} label");
                if (value.NumericValueWasSpecified && !value.NumericValue.HasValue)
                {
                    Invalid("numericValue must be omitted rather than null");
                }
                if (value.NumericValue.HasValue)
                {
                    RequireSafeInteger(value.NumericValue.Value, $"domain {domain.Id} value {value.Id} numericValue");
                }
            }
        }
    }

    private static void ValidateCells(
        IReadOnlyDictionary<string, NativeCell> cells,
        IReadOnlyDictionary<string, NativeDomain> domains)
    {
        RequireRecord(cells, "cells");
        foreach ((string cellKey, NativeCell cell) in cells)
        {
            RequireNonEmpty(cellKey, "cell key");
            RequireObject(cell, $"cell {cellKey}");
            RequireNonEmpty(cell.Id, $"cell {cellKey} id");
            if (!string.Equals(cellKey, cell.Id, StringComparison.Ordinal))
            {
                Invalid($"cell key {cellKey} does not match id {cell.Id}");
            }
            RequireNonEmpty(cell.DomainId, $"cell {cell.Id} domainId");
            if (!domains.ContainsKey(cell.DomainId))
            {
                Invalid($"cell {cell.Id} references missing domain {cell.DomainId}");
            }
            ValidateShape(cell.Shape, cell.Id);
            if (cell.Input is null)
            {
                Invalid($"cell {cell.Id} input must be an object");
            }
            if (cell.LabelWasSpecified && cell.Label is null)
            {
                Invalid("label must be omitted rather than null");
            }
            if (cell.Label is not null)
            {
                RequireNonEmpty(cell.Label, $"cell {cell.Id} label");
            }
        }
    }

    private static void ValidateShape(NativeCellShape shape, string cellId)
    {
        if (shape is null)
        {
            Invalid($"cell {cellId} shape must be an object");
        }
        switch (shape.Kind)
        {
            case "rect":
                RequireFinite(shape.X, $"cell {cellId} shape x");
                RequireFinite(shape.Y, $"cell {cellId} shape y");
                RequireFinite(shape.Width, $"cell {cellId} shape width");
                RequireFinite(shape.Height, $"cell {cellId} shape height");
                break;
            case "polygon":
                RequireList(shape.Points, $"cell {cellId} polygon points");
                foreach (NativeShapePoint point in shape.Points)
                {
                    if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    {
                        Invalid($"cell {cellId} polygon points must use finite coordinates");
                    }
                }
                break;
            case "path":
                RequireNonEmpty(shape.D, $"cell {cellId} shape path");
                break;
            default:
                Invalid($"cell {cellId} has invalid shape");
                break;
        }
    }

    private static void ValidateGroups(
        IReadOnlyDictionary<string, NativeGroup> groups,
        IReadOnlyDictionary<string, NativeCell> cells)
    {
        RequireRecord(groups, "groups");
        foreach ((string groupKey, NativeGroup group) in groups)
        {
            RequireObject(group, $"group {groupKey}");
            ValidateKeyAndId("group", groupKey, group.Id);
            ValidateStringList(group.Roles, $"group {group.Id} roles");
            ValidateStringList(group.CellIds, $"group {group.Id} cellIds");
            foreach (string cellId in group.CellIds)
            {
                if (!cells.ContainsKey(cellId))
                {
                    Invalid($"group {group.Id} references missing cell {cellId}");
                }
            }
        }
    }

    private static void ValidateBoards(
        IReadOnlyDictionary<string, NativeBoard> boards,
        IReadOnlyDictionary<string, NativeCell> cells,
        IReadOnlyDictionary<string, NativeGroup> groups)
    {
        RequireRecord(boards, "boards");
        foreach ((string boardKey, NativeBoard board) in boards)
        {
            RequireObject(board, $"board {boardKey}");
            ValidateKeyAndId("board", boardKey, board.Id);
            RequireNonEmpty(board.Name, $"board {board.Id} name");
            ValidateStringList(board.CellIds, $"board {board.Id} cellIds");
            ValidateStringList(board.GroupIds, $"board {board.Id} groupIds");
            foreach (string cellId in board.CellIds)
            {
                if (!cells.ContainsKey(cellId))
                {
                    Invalid($"board {board.Id} references missing cell {cellId}");
                }
            }
            foreach (string groupId in board.GroupIds)
            {
                if (!groups.ContainsKey(groupId))
                {
                    Invalid($"board {board.Id} references missing group {groupId}");
                }
            }
        }
    }

    private static void ValidateAdjacency(
        IReadOnlyDictionary<string, NativeAdjacency> adjacency,
        IReadOnlyDictionary<string, NativeCell> cells)
    {
        RequireRecord(adjacency, "adjacency");
        foreach ((string adjacencyKey, NativeAdjacency item) in adjacency)
        {
            RequireObject(item, $"adjacency {adjacencyKey}");
            ValidateKeyAndId("adjacency", adjacencyKey, item.Id);
            RequireNonEmpty(item.Kind, $"adjacency {item.Id} kind");
            ValidateCellReference(cells, item.FromCellId, $"adjacency {item.Id}");
            ValidateCellReference(cells, item.ToCellId, $"adjacency {item.Id}");
        }
    }

    private static void ValidatePoints(IReadOnlyDictionary<string, NativePoint> points)
    {
        RequireRecord(points, "points");
        foreach ((string pointKey, NativePoint point) in points)
        {
            RequireObject(point, $"point {pointKey}");
            ValidateKeyAndId("point", pointKey, point.Id);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                Invalid($"point {point.Id} coordinates must be finite numbers");
            }
        }
    }

    private static void ValidateEdges(
        IReadOnlyDictionary<string, NativeEdge> edges,
        IReadOnlyDictionary<string, NativePoint> points)
    {
        RequireRecord(edges, "edges");
        foreach ((string edgeKey, NativeEdge edge) in edges)
        {
            RequireObject(edge, $"edge {edgeKey}");
            ValidateKeyAndId("edge", edgeKey, edge.Id);
            ValidatePointReference(points, edge.FromPointId, $"edge {edge.Id}");
            ValidatePointReference(points, edge.ToPointId, $"edge {edge.Id}");
        }
    }

    private static void ValidatePaths(
        IReadOnlyDictionary<string, NativePath> paths,
        IReadOnlyDictionary<string, NativePoint> points)
    {
        RequireRecord(paths, "paths");
        foreach ((string pathKey, NativePath path) in paths)
        {
            RequireObject(path, $"path {pathKey}");
            ValidateKeyAndId("path", pathKey, path.Id);
            ValidateStringList(path.PointIds, $"path {path.Id} pointIds");
            foreach (string pointId in path.PointIds)
            {
                ValidatePointReference(points, pointId, $"path {path.Id}");
            }
        }
    }

    private static void ValidateConstraints(NativePuzzlePackage package)
    {
        RequireList(package.Constraints, "constraints");
        foreach (NativeConstraintInstance constraint in package.Constraints)
        {
            RequireObject(constraint, "constraint");
            RequireNonEmpty(constraint.Id, "constraint id");
            RequireNonEmpty(constraint.TypeId, $"constraint {constraint.Id} typeId");
            if (constraint.DefinitionReleaseIdWasSpecified && constraint.DefinitionReleaseId is null)
            {
                Invalid("definitionReleaseId must be omitted rather than null");
            }
            if (constraint.DefinitionReleaseId is not null)
            {
                RequireNonEmpty(constraint.DefinitionReleaseId, $"constraint {constraint.Id} definitionReleaseId");
            }
            RequireRecord(constraint.Bindings, $"constraint {constraint.Id} bindings");
            foreach ((string role, List<NativeEntityReference> references) in constraint.Bindings)
            {
                RequireNonEmpty(role, $"constraint {constraint.Id} binding role");
                RequireList(references, $"constraint {constraint.Id} binding {role}");
                foreach (NativeEntityReference reference in references)
                {
                    if (reference is null || !EntityKinds.Contains(reference.Kind))
                    {
                        Invalid($"constraint {constraint.Id} binding {role} has invalid entity kind");
                    }
                    RequireNonEmpty(reference.Id, $"constraint {constraint.Id} binding {role} reference id");
                    if (!ReferenceExists(package, reference.Kind, reference.Id))
                    {
                        Invalid($"constraint {constraint.Id} references missing {reference.Kind} {reference.Id}");
                    }
                }
            }
            ValidateJsonRecord(constraint.Parameters, $"constraint {constraint.Id} parameters", requireSafeIntegers: true);
            if (constraint.StyleOverridesWasSpecified && constraint.StyleOverrides is null)
            {
                Invalid("styleOverrides must be omitted rather than null");
            }
            if (constraint.StyleOverrides is not null)
            {
                ValidateJsonRecord(
                    constraint.StyleOverrides,
                    $"constraint {constraint.Id} styleOverrides",
                    requireSafeIntegers: false);
            }
        }
    }

    private static void ValidateGivens(NativePuzzlePackage package)
    {
        RequireRecord(package.Givens, "givens");
        foreach ((string cellId, string valueId) in package.Givens)
        {
            if (!package.Cells.TryGetValue(cellId, out NativeCell? cell))
            {
                Invalid($"given references missing cell {cellId}");
            }
            RequireNonEmpty(valueId, $"given {cellId} value");
            NativeDomain domain = package.Domains[cell.DomainId];
            if (!domain.Values.Any(value => string.Equals(value.Id, valueId, StringComparison.Ordinal)))
            {
                Invalid($"given {cellId} references missing value {valueId}");
            }
        }
    }

    private static void ValidateProjections(NativePuzzlePackage package)
    {
        RequireList(package.SolverProjections, "solverProjections");
        foreach (NativeSolverProjection projection in package.SolverProjections)
        {
            RequireObject(projection, "projection");
            RequireNonEmpty(projection.Id, "projection id");
            if (!string.Equals(projection.Kind, "latin-square", StringComparison.Ordinal))
            {
                Invalid($"projection {projection.Id} has unsupported kind");
            }
            if (!package.Boards.TryGetValue(projection.BoardId, out NativeBoard? board))
            {
                Invalid($"projection {projection.Id} references missing board {projection.BoardId}");
            }
            if (!package.Domains.TryGetValue(projection.DomainId, out NativeDomain? domain))
            {
                Invalid($"projection {projection.Id} references missing domain {projection.DomainId}");
            }
            RequireList(projection.CellIdsByRow, $"projection {projection.Id} cellIdsByRow");
            int firstRowLength = projection.CellIdsByRow.Count == 0 ? 0 : projection.CellIdsByRow[0]?.Count ?? -1;
            if (projection.CellIdsByRow.Any(row => row is null || row.Count != firstRowLength))
            {
                Invalid($"projection {projection.Id} rows must have equal lengths");
            }
            if (projection.CellIdsByRow.Count == 0 || firstRowLength != projection.CellIdsByRow.Count)
            {
                Invalid($"projection {projection.Id} must be square");
            }
            HashSet<string> boardCellIds = new(board.CellIds, StringComparer.Ordinal);
            HashSet<string> projectedCellIds = new(StringComparer.Ordinal);
            foreach (List<string> row in projection.CellIdsByRow)
            {
                ValidateStringList(row, $"projection {projection.Id} row");
                foreach (string cellId in row)
                {
                    if (!projectedCellIds.Add(cellId))
                    {
                        Invalid($"projection contains duplicate cell {cellId}");
                    }
                    if (!package.Cells.TryGetValue(cellId, out NativeCell? cell))
                    {
                        Invalid($"projection {projection.Id} references missing cell {cellId}");
                    }
                    if (!boardCellIds.Contains(cellId))
                    {
                        Invalid($"projection {projection.Id} cell {cellId} is not on its board");
                    }
                    if (!string.Equals(cell.DomainId, projection.DomainId, StringComparison.Ordinal))
                    {
                        Invalid($"projection {projection.Id} cell {cellId} uses a different domain");
                    }
                }
            }
            if (domain.Values.Count != projection.CellIdsByRow.Count)
            {
                Invalid($"projection {projection.Id} domain size must match grid size");
            }
            ValidateStringList(
                projection.ValueIdsBySolverValue,
                $"projection {projection.Id} valueIdsBySolverValue");
            HashSet<string> domainValueIds = new(domain.Values.Select(value => value.Id), StringComparer.Ordinal);
            if (projection.ValueIdsBySolverValue.Count != domain.Values.Count
                || projection.ValueIdsBySolverValue.Distinct(StringComparer.Ordinal).Count() != domain.Values.Count
                || projection.ValueIdsBySolverValue.Any(valueId => !domainValueIds.Contains(valueId)))
            {
                Invalid($"projection {projection.Id} value order must cover its domain exactly");
            }
            for (int index = 0; index < projection.ValueIdsBySolverValue.Count; index++)
            {
                string valueId = projection.ValueIdsBySolverValue[index];
                NativeDomainValue value = domain.Values.Single(item => string.Equals(item.Id, valueId, StringComparison.Ordinal));
                if (value.NumericValue != index + 1)
                {
                    Invalid($"projection {projection.Id} value {valueId} must have numeric interpretation {index + 1}");
                }
            }
        }
    }

    private static void ValidatePresentation(NativePresentation presentation)
    {
        if (presentation is null)
        {
            Invalid("presentation must be an object");
        }
        ValidateJsonRecord(presentation.Styles, "presentation styles", requireSafeIntegers: false);
        RequireList(presentation.SceneElements, "presentation sceneElements");
        foreach (JsonElement sceneElement in presentation.SceneElements)
        {
            ValidateJson(sceneElement, "presentation sceneElement", requireSafeIntegers: false);
        }
    }

    private static void ValidateAuthoring(NativePuzzlePackage package)
    {
        NativeAuthoringState authoring = package.Authoring;
        if (authoring is null)
        {
            Invalid("authoring must be an object");
        }
        RequireList(authoring.CandidateContexts, "authoring candidateContexts");
        HashSet<string> contextIds = new(StringComparer.Ordinal);
        foreach (NativeCandidateContext context in authoring.CandidateContexts)
        {
            RequireObject(context, "candidate context");
            RequireNonEmpty(context.Id, "candidate context id");
            if (!contextIds.Add(context.Id))
            {
                Invalid($"duplicate candidate context {context.Id}");
            }
            RequireNonEmpty(context.Name, $"candidate context {context.Id} name");
            switch (context.Kind)
            {
                case "manual":
                    break;
                case "trueCandidates":
                    if (context.Refresh is not ("automatic" or "onRequest"))
                    {
                        Invalid($"candidate context {context.Id} has invalid refresh mode");
                    }
                    if (context.Display is not ("possibility" or "solutionFrequency" or "logicComparison"))
                    {
                        Invalid($"candidate context {context.Id} has invalid display mode");
                    }
                    if (!context.SolutionCountCap.HasValue)
                    {
                        Invalid($"candidate context {context.Id} solutionCountCap must be a non-negative safe integer");
                    }
                    RequireNonNegativeSafeInteger(
                        context.SolutionCountCap.Value,
                        $"candidate context {context.Id} solutionCountCap");
                    break;
                case "logicalSolver":
                    if (context.FollowPuzzleRevision is not true)
                    {
                        Invalid($"candidate context {context.Id} must follow the puzzle revision");
                    }
                    ValidateStringList(
                        context.EnabledTechniqueIds,
                        $"candidate context {context.Id} enabledTechniqueIds");
                    break;
                default:
                    Invalid($"candidate context {context.Id} has invalid kind");
                    break;
            }
        }
        foreach (string requiredContextId in RequiredContextIds)
        {
            if (!contextIds.Contains(requiredContextId))
            {
                Invalid($"missing candidate context {requiredContextId}");
            }
        }

        RequireRecord(authoring.ManualMarks, "authoring manualMarks");
        foreach ((string contextId, Dictionary<string, List<string>> marks) in authoring.ManualMarks)
        {
            if (!contextIds.Contains(contextId))
            {
                Invalid($"manual marks reference missing context {contextId}");
            }
            RequireRecord(marks, $"manual marks {contextId}");
            foreach ((string cellId, List<string> valueIds) in marks)
            {
                if (!package.Cells.TryGetValue(cellId, out NativeCell? cell))
                {
                    Invalid($"manual marks reference missing cell {cellId}");
                }
                ValidateStringList(valueIds, $"manual marks {contextId} cell {cellId}");
                HashSet<string> domainValueIds = new(
                    package.Domains[cell.DomainId].Values.Select(value => value.Id),
                    StringComparer.Ordinal);
                foreach (string valueId in valueIds)
                {
                    if (!domainValueIds.Contains(valueId))
                    {
                        Invalid($"manual marks {contextId} cell {cellId} references missing value {valueId}");
                    }
                }
            }
        }
    }

    private static void ValidateExtensions(IReadOnlyDictionary<string, NativeExtension> extensions)
    {
        RequireRecord(extensions, "extensions");
        foreach ((string extensionId, NativeExtension extension) in extensions)
        {
            int separatorIndex = extensionId.IndexOf(':');
            if (separatorIndex < 0
                || string.IsNullOrWhiteSpace(extensionId[..separatorIndex])
                || string.IsNullOrWhiteSpace(extensionId[(separatorIndex + 1)..]))
            {
                Invalid($"extension {extensionId} must use <namespace>:<name>");
            }
            RequireObject(extension, $"extension {extensionId}");
            if (extension.Impact is not ("semantic" or "cosmetic"))
            {
                Invalid($"extension {extensionId} has invalid impact");
            }
            ValidateJson(
                extension.Data,
                $"extension {extensionId} data",
                requireSafeIntegers: string.Equals(extension.Impact, "semantic", StringComparison.Ordinal));
        }
    }

    private static void ValidateOptionalPayloads(NativePuzzlePackage package)
    {
        if (package.Source.HasValue)
        {
            ValidateJson(package.Source.Value, "source", requireSafeIntegers: false);
        }
        if (package.Release.HasValue)
        {
            ValidateJson(package.Release.Value, "release", requireSafeIntegers: true);
        }
        if (package.AssetsWasSpecified && package.Assets is null)
        {
            Invalid("assets must be omitted rather than null");
        }
        if (package.Assets is not null)
        {
            foreach (JsonElement asset in package.Assets)
            {
                ValidateJson(asset, "asset", requireSafeIntegers: false);
            }
        }
        if (package.Provenance.HasValue)
        {
            ValidateJson(package.Provenance.Value, "provenance", requireSafeIntegers: false);
        }
    }

    private static void ValidateJsonRecord(
        IReadOnlyDictionary<string, JsonElement> record,
        string description,
        bool requireSafeIntegers)
    {
        RequireRecord(record, description);
        foreach ((string key, JsonElement value) in record)
        {
            ValidateJson(value, $"{description} {key}", requireSafeIntegers);
        }
    }

    private static void ValidateJson(JsonElement element, string description, bool requireSafeIntegers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ValidateJson(property.Value, $"{description}.{property.Name}", requireSafeIntegers);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ValidateJson(item, description, requireSafeIntegers);
                }
                break;
            case JsonValueKind.Number:
                if (requireSafeIntegers
                    && (!element.TryGetInt64(out long number)
                        || number < -MaxSafeInteger
                        || number > MaxSafeInteger))
                {
                    Invalid($"{description} must use finite safe integers");
                }
                break;
            case JsonValueKind.String:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                Invalid($"{description} must be valid JSON");
                break;
        }
    }

    private static bool ReferenceExists(NativePuzzlePackage package, string kind, string id)
        => kind switch
        {
            "cell" => package.Cells.ContainsKey(id),
            "group" => package.Groups.ContainsKey(id),
            "edge" => package.Edges.ContainsKey(id),
            "point" => package.Points.ContainsKey(id),
            "path" => package.Paths.ContainsKey(id),
            _ => false,
        };

    private static void ValidateCellReference(
        IReadOnlyDictionary<string, NativeCell> cells,
        string cellId,
        string description)
    {
        RequireNonEmpty(cellId, $"{description} cell reference");
        if (!cells.ContainsKey(cellId))
        {
            Invalid($"{description} references missing cell {cellId}");
        }
    }

    private static void ValidatePointReference(
        IReadOnlyDictionary<string, NativePoint> points,
        string pointId,
        string description)
    {
        RequireNonEmpty(pointId, $"{description} point reference");
        if (!points.ContainsKey(pointId))
        {
            Invalid($"{description} references missing point {pointId}");
        }
    }

    private static void ValidateKeyAndId(string entityType, string key, string? id)
    {
        RequireNonEmpty(key, $"{entityType} key");
        RequireNonEmpty(id, $"{entityType} {key} id");
        if (!string.Equals(key, id, StringComparison.Ordinal))
        {
            Invalid($"{entityType} key {key} does not match id {id}");
        }
    }

    private static void ValidateStringList(IEnumerable<string>? values, string description)
    {
        RequireList(values, description);
        foreach (string value in values)
        {
            RequireNonEmpty(value, description);
        }
    }

    private static void RequireFinite(double? value, string description)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            Invalid($"{description} must be a finite number");
        }
    }

    private static void RequireNonNegativeSafeInteger(long value, string description)
    {
        RequireSafeInteger(value, description);
        if (value < 0)
        {
            Invalid($"{description} must be a non-negative safe integer");
        }
    }

    private static void RequireSafeInteger(long value, string description)
    {
        if (value < -MaxSafeInteger || value > MaxSafeInteger)
        {
            Invalid($"{description} must be a safe integer");
        }
    }

    private static void RequireObject<TValue>([NotNull] TValue? value, string description)
        where TValue : class
    {
        if (value is null)
        {
            Invalid($"{description} must be an object");
        }
    }

    private static void RequireNonEmpty([NotNull] string? value, string description)
    {
        if (string.IsNullOrEmpty(value))
        {
            Invalid($"{description} must be a non-empty string");
        }
    }

    private static void RequireRecord<TValue>(
        [NotNull] IReadOnlyDictionary<string, TValue>? record,
        string description)
    {
        if (record is null)
        {
            Invalid($"{description} must be an object");
        }
    }

    private static void RequireList<TValue>([NotNull] IEnumerable<TValue>? values, string description)
    {
        if (values is null)
        {
            Invalid($"{description} must be an array");
        }
    }

    [DoesNotReturn]
    private static void Invalid(string message)
        => throw new ArgumentException(message, "package");
}