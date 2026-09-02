import { canonicalJson } from "./canonicalJson";
import type { JsonValue, PuzzlePackageV1 } from "./types";

function mapRecord<T, U>(
  record: Readonly<Record<string, T>>,
  mapValue: (value: T) => U,
): Record<string, U> {
  return Object.fromEntries(
    Object.entries(record).map(([key, value]) => [key, mapValue(value)]),
  );
}

function referencedReleaseSemantics(
  release: JsonValue | undefined,
  releaseIds: ReadonlySet<string>,
): JsonValue | undefined {
  if (release === undefined || releaseIds.size === 0) {
    return undefined;
  }
  if (
    typeof release === "object" &&
    release !== null &&
    !Array.isArray(release)
  ) {
    const releaseRecord = release as Readonly<Record<string, JsonValue>>;
    const releases = releaseRecord.releases;
    if (
      typeof releases === "object" &&
      releases !== null &&
      !Array.isArray(releases)
    ) {
      const releasesRecord = releases as Readonly<Record<string, JsonValue>>;
      return {
        releases: Object.fromEntries(
          [...releaseIds]
            .filter((releaseId) => releaseId in releasesRecord)
            .map((releaseId) => [releaseId, releasesRecord[releaseId]]),
        ),
      };
    }
  }
  return release;
}

function semanticView(puzzle: PuzzlePackageV1): JsonValue {
  const referencedPathIds = new Set<string>();
  const referencedEdgeIds = new Set<string>();
  const releaseIds = new Set<string>();
  for (const constraint of puzzle.constraints) {
    if (constraint.definitionReleaseId !== undefined) {
      releaseIds.add(constraint.definitionReleaseId);
    }
    for (const references of Object.values(constraint.bindings)) {
      for (const reference of references) {
        if (reference.kind === "path") {
          referencedPathIds.add(reference.id);
        } else if (reference.kind === "edge") {
          referencedEdgeIds.add(reference.id);
        }
      }
    }
  }

  const release = referencedReleaseSemantics(puzzle.release, releaseIds);
  return {
    schemaVersion: puzzle.schemaVersion,
    domains: mapRecord(puzzle.domains, (domain) => ({
      id: domain.id,
      values: domain.values.map((value) => ({
        id: value.id,
        ...(value.numericValue === undefined
          ? {}
          : { numericValue: value.numericValue }),
      })),
    })),
    cells: mapRecord(puzzle.cells, (cell) => ({
      id: cell.id,
      domainId: cell.domainId,
      input: {
        acceptsValue: cell.input.acceptsValue,
        acceptsCandidates: cell.input.acceptsCandidates,
      },
    })),
    boards: mapRecord(puzzle.boards, (board) => ({
      id: board.id,
      cellIds: board.cellIds,
      groupIds: board.groupIds,
    })),
    groups: mapRecord(puzzle.groups, (group) => ({
      id: group.id,
      roles: group.roles,
      cellIds: group.cellIds,
    })),
    adjacency: mapRecord(puzzle.adjacency, (adjacency) => ({
      id: adjacency.id,
      kind: adjacency.kind,
      fromCellId: adjacency.fromCellId,
      toCellId: adjacency.toCellId,
    })),
    edges: Object.fromEntries(
      Object.entries(puzzle.edges)
        .filter(([edgeId]) => referencedEdgeIds.has(edgeId))
        .map(([edgeId, edge]) => [
          edgeId,
          {
            id: edge.id,
            fromPointId: edge.fromPointId,
            toPointId: edge.toPointId,
          },
        ]),
    ),
    paths: Object.fromEntries(
      Object.entries(puzzle.paths)
        .filter(([pathId]) => referencedPathIds.has(pathId))
        .map(([pathId, path]) => [
          pathId,
          { id: path.id, pointIds: path.pointIds, closed: path.closed },
        ]),
    ),
    givens: puzzle.givens,
    constraints: puzzle.constraints.map((constraint) => ({
      id: constraint.id,
      typeId: constraint.typeId,
      ...(constraint.definitionReleaseId === undefined
        ? {}
        : { definitionReleaseId: constraint.definitionReleaseId }),
      bindings: constraint.bindings,
      parameters: constraint.parameters,
    })),
    ...(release === undefined ? {} : { release }),
    solverProjections: puzzle.solverProjections.map((projection) => ({
      id: projection.id,
      kind: projection.kind,
      boardId: projection.boardId,
      domainId: projection.domainId,
      valueIdsBySolverValue: projection.valueIdsBySolverValue,
      cellIdsByRow: projection.cellIdsByRow,
    })),
    extensions: Object.fromEntries(
      Object.entries(puzzle.extensions).filter(
        ([, extension]) => extension.impact === "semantic",
      ),
    ),
  };
}

export async function computeSemanticHash(
  puzzle: PuzzlePackageV1,
): Promise<string> {
  const bytes = new TextEncoder().encode(canonicalJson(semanticView(puzzle)));
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  const hex = [...new Uint8Array(digest)]
    .map((byte) => byte.toString(16).padStart(2, "0"))
    .join("");
  return `sha256:${hex}`;
}
