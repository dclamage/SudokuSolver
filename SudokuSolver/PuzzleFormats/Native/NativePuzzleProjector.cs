using System.Text.Json;

namespace SudokuSolver.PuzzleFormats.Native;

#nullable enable

/// <summary>Projects native packages into the current square Latin solver.</summary>
public static class NativePuzzleProjector
{
    /// <summary>Projects one explicitly named native solver projection.</summary>
    /// <param name="package">The native package to project.</param>
    /// <param name="projectionId">The stable projection identifier.</param>
    /// <returns>The initialized solver, stable mappings, and entity capabilities.</returns>
    /// <exception cref="ArgumentException">The package cannot be represented by the selected projection.</exception>
    /// <exception cref="InvalidOperationException">The projected constraints or givens are contradictory.</exception>
    public static NativeProjectionResult Project(NativePuzzlePackage package, string projectionId)
    {
        ArgumentNullException.ThrowIfNull(package);
        NativePuzzleValidator.Validate(package);
        if (string.IsNullOrWhiteSpace(projectionId))
        {
            throw new ArgumentException("Projection ID cannot be empty.", nameof(projectionId));
        }

        NativeSolverProjection projection = package.SolverProjections.SingleOrDefault(
            candidate => string.Equals(candidate.Id, projectionId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Native puzzle does not contain projection {projectionId}.", nameof(projectionId));
        if (!string.Equals(projection.Kind, "latin-square", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Projection {projectionId} has unsupported kind {projection.Kind}.", nameof(projectionId));
        }

        int size = ValidateSquareGrid(projection);
        NativeDomain domain = ValidateDomain(package, projection, size);
        NativeBoard board = ValidateBoard(package, projection);
        (Dictionary<string, int> cellIndexById, List<string> cellIdByIndex) =
            ValidateAndMapCells(package, projection, board, size);
        Dictionary<string, int> solverValueById = ValidateAndMapValues(projection, domain, size);
        int[] regions = MapRegions(package, board, cellIndexById, size);

        Solver solver = new(size, size, size);
        solver.SetRegions(regions);
        if (!solver.FinalizeConstraints())
        {
            throw new InvalidOperationException($"Projection {projectionId} has contradictory base constraints.");
        }

        foreach ((string cellId, string valueId) in package.Givens)
        {
            if (!cellIndexById.TryGetValue(cellId, out int cellIndex))
            {
                continue;
            }
            if (!solverValueById.TryGetValue(valueId, out int solverValue))
            {
                throw new ArgumentException(
                    $"Projected given {cellId} references value {valueId} outside projection {projectionId}.",
                    nameof(package));
            }
            if (!solver.SetValue(cellIndex, solverValue))
            {
                throw new InvalidOperationException($"Projected given {cellId}={valueId} is contradictory.");
            }
        }

        CapabilityReport capabilities = BuildCapabilities(package, projection, cellIndexById);
        return new NativeProjectionResult(solver, cellIndexById, cellIdByIndex, capabilities);
    }

    private static int ValidateSquareGrid(NativeSolverProjection projection)
    {
        if (projection.CellIdsByRow.Count == 0)
        {
            throw new ArgumentException($"Projection {projection.Id} must contain at least one row.", nameof(projection));
        }

        int width = projection.CellIdsByRow[0].Count;
        if (projection.CellIdsByRow.Any(row => row.Count != width))
        {
            throw new ArgumentException($"Projection {projection.Id} rows must have equal lengths.", nameof(projection));
        }
        if (projection.CellIdsByRow.Count != width)
        {
            throw new ArgumentException($"Projection {projection.Id} must be square.", nameof(projection));
        }
        return width;
    }

    private static NativeDomain ValidateDomain(
        NativePuzzlePackage package,
        NativeSolverProjection projection,
        int size)
    {
        if (!package.Domains.TryGetValue(projection.DomainId, out NativeDomain? domain))
        {
            throw new ArgumentException(
                $"Projection {projection.Id} references missing domain {projection.DomainId}.",
                nameof(package));
        }
        if (domain.Values.Count != size)
        {
            throw new ArgumentException($"Projection {projection.Id} domain size must match grid size.", nameof(package));
        }
        return domain;
    }

    private static NativeBoard ValidateBoard(NativePuzzlePackage package, NativeSolverProjection projection)
    {
        if (!package.Boards.TryGetValue(projection.BoardId, out NativeBoard? board))
        {
            throw new ArgumentException(
                $"Projection {projection.Id} references missing board {projection.BoardId}.",
                nameof(package));
        }
        return board;
    }

    private static (Dictionary<string, int> CellIndexById, List<string> CellIdByIndex) ValidateAndMapCells(
        NativePuzzlePackage package,
        NativeSolverProjection projection,
        NativeBoard board,
        int size)
    {
        HashSet<string> boardCellIds = new(board.CellIds, StringComparer.Ordinal);
        Dictionary<string, int> cellIndexById = new(StringComparer.Ordinal);
        List<string> cellIdByIndex = new(size * size);
        foreach (List<string> row in projection.CellIdsByRow)
        {
            foreach (string cellId in row)
            {
                if (!package.Cells.TryGetValue(cellId, out NativeCell? cell))
                {
                    throw new ArgumentException(
                        $"Projection {projection.Id} references missing cell {cellId}.",
                        nameof(package));
                }
                if (!boardCellIds.Contains(cellId))
                {
                    throw new ArgumentException(
                        $"Projection {projection.Id} cell {cellId} is not a member of board {board.Id}.",
                        nameof(package));
                }
                if (!string.Equals(cell.DomainId, projection.DomainId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Projection {projection.Id} cell {cellId} does not use domain {projection.DomainId}.",
                        nameof(package));
                }
                if (!cellIndexById.TryAdd(cellId, cellIdByIndex.Count))
                {
                    throw new ArgumentException(
                        $"Projection {projection.Id} contains duplicate cell {cellId}.",
                        nameof(package));
                }
                cellIdByIndex.Add(cellId);
            }
        }
        return (cellIndexById, cellIdByIndex);
    }

    private static Dictionary<string, int> ValidateAndMapValues(
        NativeSolverProjection projection,
        NativeDomain domain,
        int size)
    {
        if (projection.ValueIdsBySolverValue.Count != size)
        {
            throw new ArgumentException(
                $"Projection {projection.Id} value order must cover its domain exactly.",
                nameof(projection));
        }

        Dictionary<string, NativeDomainValue> valuesById = new(StringComparer.Ordinal);
        foreach (NativeDomainValue value in domain.Values)
        {
            if (!valuesById.TryAdd(value.Id, value))
            {
                throw new ArgumentException($"Domain {domain.Id} contains duplicate value {value.Id}.", nameof(domain));
            }
        }

        Dictionary<string, int> solverValueById = new(StringComparer.Ordinal);
        for (int index = 0; index < projection.ValueIdsBySolverValue.Count; index++)
        {
            string valueId = projection.ValueIdsBySolverValue[index];
            int solverValue = index + 1;
            if (!valuesById.TryGetValue(valueId, out NativeDomainValue? value)
                || !solverValueById.TryAdd(valueId, solverValue))
            {
                throw new ArgumentException(
                    $"Projection {projection.Id} value order must cover its domain exactly.",
                    nameof(projection));
            }
            if (value.NumericValue != solverValue)
            {
                throw new ArgumentException(
                    $"Projection {projection.Id} value {valueId} must have numeric interpretation {solverValue}.",
                    nameof(projection));
            }
        }
        if (solverValueById.Count != valuesById.Count)
        {
            throw new ArgumentException(
                $"Projection {projection.Id} value order must cover its domain exactly.",
                nameof(projection));
        }
        return solverValueById;
    }

    private static int[] MapRegions(
        NativePuzzlePackage package,
        NativeBoard board,
        IReadOnlyDictionary<string, int> cellIndexById,
        int size)
    {
        int[] regions = Enumerable.Repeat(-1, size * size).ToArray();
        int regionIndex = 0;
        foreach (string groupId in board.GroupIds)
        {
            if (!package.Groups.TryGetValue(groupId, out NativeGroup? group))
            {
                throw new ArgumentException($"Board {board.Id} references missing group {groupId}.", nameof(package));
            }
            if (!group.Roles.Contains("region", StringComparer.Ordinal))
            {
                continue;
            }
            if (group.CellIds.Count != size)
            {
                throw new ArgumentException($"Projected region {groupId} must contain {size} cells.", nameof(package));
            }
            foreach (string cellId in group.CellIds)
            {
                if (!cellIndexById.TryGetValue(cellId, out int cellIndex))
                {
                    throw new ArgumentException(
                        $"Projected region {groupId} contains unprojected cell {cellId}.",
                        nameof(package));
                }
                if (regions[cellIndex] != -1)
                {
                    throw new ArgumentException(
                        $"Projected cell {cellId} belongs to more than one region.",
                        nameof(package));
                }
                regions[cellIndex] = regionIndex;
            }
            regionIndex++;
        }

        if (regionIndex != size || regions.Any(region => region < 0))
        {
            throw new ArgumentException(
                $"Board {board.Id} must define exactly {size} regions covering every projected cell once.",
                nameof(package));
        }
        return regions;
    }

    private static CapabilityReport BuildCapabilities(
        NativePuzzlePackage package,
        NativeSolverProjection selectedProjection,
        IReadOnlyDictionary<string, int> projectedCells)
    {
        Dictionary<string, EntityCapabilityResult> entities = new(StringComparer.Ordinal);
        NativeBoard selectedBoard = package.Boards[selectedProjection.BoardId];
        HashSet<string> selectedGroupIds = new(selectedBoard.GroupIds, StringComparer.Ordinal);
        List<HashSet<string>> projectedRows = selectedProjection.CellIdsByRow
            .Select(row => new HashSet<string>(row, StringComparer.Ordinal))
            .ToList();
        List<HashSet<string>> projectedColumns = Enumerable.Range(0, selectedProjection.CellIdsByRow.Count)
            .Select(column => new HashSet<string>(
                selectedProjection.CellIdsByRow.Select(row => row[column]),
                StringComparer.Ordinal))
            .ToList();
        HashSet<string> referencedPointIds = [];
        HashSet<string> referencedEdgeIds = [];
        HashSet<string> referencedPathIds = [];
        HashSet<string> referencedReleaseIds = [];
        foreach (NativeConstraintInstance constraint in package.Constraints)
        {
            if (constraint.DefinitionReleaseId is not null)
            {
                referencedReleaseIds.Add(constraint.DefinitionReleaseId);
            }
            foreach (List<NativeEntityReference> references in constraint.Bindings.Values)
            {
                foreach (NativeEntityReference reference in references)
                {
                    if (string.Equals(reference.Kind, "point", StringComparison.Ordinal))
                    {
                        referencedPointIds.Add(reference.Id);
                    }
                    else if (string.Equals(reference.Kind, "edge", StringComparison.Ordinal))
                    {
                        referencedEdgeIds.Add(reference.Id);
                    }
                    else if (string.Equals(reference.Kind, "path", StringComparison.Ordinal))
                    {
                        referencedPathIds.Add(reference.Id);
                    }
                }
            }
        }

        foreach (string domainId in package.Domains.Keys)
        {
            if (string.Equals(domainId, selectedProjection.DomainId, StringComparison.Ordinal))
            {
                SetCapability(entities, "domain", domainId, EntityCapability.FullyVerified);
            }
            else
            {
                SetCapability(
                    entities,
                    "domain",
                    domainId,
                    EntityCapability.VisualOnly,
                    $"Domain {domainId} is not used by projection {selectedProjection.Id}.");
            }
        }
        foreach (string cellId in package.Cells.Keys)
        {
            if (projectedCells.ContainsKey(cellId))
            {
                SetCapability(entities, "cell", cellId, EntityCapability.FullyVerified);
            }
            else
            {
                SetCapability(
                    entities,
                    "cell",
                    cellId,
                    EntityCapability.VisualOnly,
                    $"Not included in projection {selectedProjection.Id}.");
            }
        }
        foreach (string boardId in package.Boards.Keys)
        {
            if (string.Equals(boardId, selectedProjection.BoardId, StringComparison.Ordinal))
            {
                SetCapability(entities, "board", boardId, EntityCapability.FullyVerified);
            }
            else
            {
                SetCapability(
                    entities,
                    "board",
                    boardId,
                    EntityCapability.VisualOnly,
                    $"Board {boardId} is not represented by projection {selectedProjection.Id}.");
            }
        }
        foreach ((string groupId, NativeGroup group) in package.Groups)
        {
            if (!selectedGroupIds.Contains(groupId))
            {
                SetCapability(
                    entities,
                    "group",
                    groupId,
                    EntityCapability.VisualOnly,
                    $"Group {groupId} is not part of projected board {selectedProjection.BoardId}.");
            }
            else
            {
                EntityCapabilityResult groupCapability = EvaluateGroupCapability(
                    group,
                    selectedProjection,
                    projectedRows,
                    projectedColumns);
                entities[CapabilityReport.GetEntityKey("group", groupId)] = groupCapability;
            }
        }
        foreach (NativeSolverProjection projection in package.SolverProjections)
        {
            if (string.Equals(projection.Id, selectedProjection.Id, StringComparison.Ordinal))
            {
                SetCapability(entities, "solverProjection", projection.Id, EntityCapability.FullyVerified);
            }
            else
            {
                SetCapability(
                    entities,
                    "solverProjection",
                    projection.Id,
                    EntityCapability.PartiallyVerified,
                    $"Projection {projection.Id} was not selected; projection {selectedProjection.Id} was represented.");
            }
        }
        foreach (NativeAdjacency adjacency in package.Adjacency.Values)
        {
            SetCapability(
                entities,
                "adjacency",
                adjacency.Id,
                EntityCapability.PartiallyVerified,
                $"Adjacency kind {adjacency.Kind} is preserved but not enforced by projection {selectedProjection.Id}.");
        }
        foreach (NativePoint point in package.Points.Values)
        {
            bool isReferenced = referencedPointIds.Contains(point.Id);
            SetCapability(
                entities,
                "point",
                point.Id,
                isReferenced ? EntityCapability.PartiallyVerified : EntityCapability.VisualOnly,
                isReferenced
                    ? $"Point {point.Id} is preserved for an unsupported constraint binding."
                    : $"Point {point.Id} is presentation geometry and is not bound to a semantic constraint.");
        }
        foreach (NativeEdge edge in package.Edges.Values)
        {
            bool isReferenced = referencedEdgeIds.Contains(edge.Id);
            SetCapability(
                entities,
                "edge",
                edge.Id,
                isReferenced ? EntityCapability.PartiallyVerified : EntityCapability.VisualOnly,
                isReferenced
                    ? $"Edge {edge.Id} is preserved for an unsupported constraint binding."
                    : $"Edge {edge.Id} is presentation geometry and is not bound to a semantic constraint.");
        }
        foreach (NativePath path in package.Paths.Values)
        {
            bool isReferenced = referencedPathIds.Contains(path.Id);
            SetCapability(
                entities,
                "path",
                path.Id,
                isReferenced ? EntityCapability.PartiallyVerified : EntityCapability.VisualOnly,
                isReferenced
                    ? $"Path {path.Id} is preserved for an unsupported constraint binding."
                    : $"Path {path.Id} is presentation geometry and is not bound to a semantic constraint.");
        }

        HashSet<string> availableReleaseIds = GetDefinitionReleaseIds(package.Release);
        foreach (NativeConstraintInstance constraint in package.Constraints)
        {
            if (constraint.DefinitionReleaseId is not null
                && !availableReleaseIds.Contains(constraint.DefinitionReleaseId))
            {
                string reason = package.Release is not null
                    ? $"Constraint {constraint.Id} references missing definition release {constraint.DefinitionReleaseId}."
                    : $"Constraint {constraint.Id} references definition release {constraint.DefinitionReleaseId}, but no release payload is present.";
                SetCapability(
                    entities,
                    "constraint",
                    constraint.Id,
                    EntityCapability.InvalidDefinition,
                    reason);
                SetCapability(
                    entities,
                    "definitionRelease",
                    constraint.DefinitionReleaseId,
                    EntityCapability.InvalidDefinition,
                    reason);
            }
            else
            {
                SetCapability(
                    entities,
                    "constraint",
                    constraint.Id,
                    EntityCapability.PartiallyVerified,
                    $"Constraint type {constraint.TypeId} is preserved but not enforced by projection {selectedProjection.Id}.");
                if (constraint.DefinitionReleaseId is not null)
                {
                    SetCapability(
                        entities,
                        "definitionRelease",
                        constraint.DefinitionReleaseId,
                        EntityCapability.PartiallyVerified,
                        $"Definition release {constraint.DefinitionReleaseId} is preserved but not executable by projection {selectedProjection.Id}.");
                }
            }
        }
        foreach (string releaseId in availableReleaseIds.Except(referencedReleaseIds, StringComparer.Ordinal))
        {
            SetCapability(
                entities,
                "definitionRelease",
                releaseId,
                EntityCapability.VisualOnly,
                $"Definition release {releaseId} is present but not referenced by a constraint.");
        }
        foreach ((string extensionId, NativeExtension extension) in package.Extensions)
        {
            if (string.Equals(extension.Impact, "semantic", StringComparison.Ordinal))
            {
                SetCapability(
                    entities,
                    "extension",
                    extensionId,
                    EntityCapability.PartiallyVerified,
                    $"Semantic extension {extensionId} is preserved but not enforced by projection {selectedProjection.Id}.");
            }
        }
        return new CapabilityReport(entities);
    }

    private static EntityCapabilityResult EvaluateGroupCapability(
        NativeGroup group,
        NativeSolverProjection selectedProjection,
        IReadOnlyList<HashSet<string>> projectedRows,
        IReadOnlyList<HashSet<string>> projectedColumns)
    {
        if (group.Roles.Count == 0)
        {
            return new(
                "group",
                group.Id,
                EntityCapability.PartiallyVerified,
                $"Group {group.Id} declares no solver-enforced role.");
        }

        foreach (string role in group.Roles)
        {
            bool isEnforced = role switch
            {
                "row" => MatchesAnyProjectedGroup(group.CellIds, projectedRows),
                "column" => MatchesAnyProjectedGroup(group.CellIds, projectedColumns),
                "region" => true,
                _ => false,
            };
            if (!isEnforced)
            {
                string reason = role is "row" or "column"
                    ? $"Group {group.Id} role {role} does not match any projected {role}."
                    : $"Group {group.Id} role {role} is not enforced by projection {selectedProjection.Id}.";
                return new("group", group.Id, EntityCapability.PartiallyVerified, reason);
            }
        }

        return new("group", group.Id, EntityCapability.FullyVerified);
    }

    private static bool MatchesAnyProjectedGroup(
        IReadOnlyList<string> groupCellIds,
        IReadOnlyList<HashSet<string>> projectedGroups)
    {
        HashSet<string> groupCells = new(groupCellIds, StringComparer.Ordinal);
        return groupCells.Count == groupCellIds.Count
            && projectedGroups.Any(projectedGroup => projectedGroup.SetEquals(groupCells));
    }

    private static HashSet<string> GetDefinitionReleaseIds(NativeOptionalJsonValue? release)
    {
        if (release is null
            || release.Value.ValueKind != JsonValueKind.Object
            || !release.Value.TryGetProperty("releases", out JsonElement releases)
            || releases.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        return new HashSet<string>(
            releases.EnumerateObject().Select(property => property.Name),
            StringComparer.Ordinal);
    }

    private static void SetCapability(
        Dictionary<string, EntityCapabilityResult> entities,
        string entityKind,
        string entityId,
        EntityCapability status,
        string? reason = null)
    {
        entities[CapabilityReport.GetEntityKey(entityKind, entityId)] = new(
            entityKind,
            entityId,
            status,
            reason);
    }
}

/// <summary>Contains the solver and stable mappings produced by a native projection.</summary>
/// <param name="Solver">The initialized current solver.</param>
/// <param name="CellIndexById">Solver cell indices keyed by stable native cell identifier.</param>
/// <param name="CellIdByIndex">Stable native cell identifiers in solver row-major order.</param>
/// <param name="Capabilities">Entity-level support results.</param>
public sealed record NativeProjectionResult(
    Solver Solver,
    IReadOnlyDictionary<string, int> CellIndexById,
    IReadOnlyList<string> CellIdByIndex,
    CapabilityReport Capabilities);