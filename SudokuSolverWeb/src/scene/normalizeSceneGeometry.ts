import type { CellShape, PuzzlePackageV1 } from "../domain/puzzle/types";
import type { SceneGeometryIssue } from "./types";

/** Smallest feature whose topology this SVG renderer promises to preserve. */
export const MIN_NORMALIZED_FEATURE_SIZE = 1e-9;
const NORMALIZED_COORDINATE_QUANTUM =
  MIN_NORMALIZED_FEATURE_SIZE / 1_000;

export interface NormalizedPoint {
  x: number;
  y: number;
}

export interface NormalizedBounds {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface NormalizedCellGeometry {
  kind: "rect" | "polygon" | "path";
  vertices: readonly NormalizedPoint[];
  contentVertices: readonly NormalizedPoint[];
  bounds?: NormalizedBounds;
  geometryIssue?: SceneGeometryIssue;
}

export interface NormalizedPuzzleGeometry {
  cells: ReadonlyMap<string, NormalizedCellGeometry>;
  width: number;
  height: number;
  displayScale: number;
}

interface RawCellGeometry {
  kind: NormalizedCellGeometry["kind"];
  vertices: readonly NormalizedPoint[];
  geometryIssue?: SceneGeometryIssue;
}

function issue(
  code: SceneGeometryIssue["code"],
  affects: SceneGeometryIssue["affects"],
  message: string,
): SceneGeometryIssue {
  return { code, affects, message };
}

function rawVertices(shape: CellShape): RawCellGeometry {
  if (shape.kind === "path") {
    return {
      kind: "path",
      vertices: [],
      geometryIssue: issue(
        "unsupported-path-geometry",
        "topology-and-content",
        "path geometry cannot be normalized without explicit vertices.",
      ),
    };
  }

  const vertices =
    shape.kind === "rect"
      ? [
          { x: shape.x, y: shape.y },
          { x: shape.x + shape.width, y: shape.y },
          { x: shape.x + shape.width, y: shape.y + shape.height },
          { x: shape.x, y: shape.y + shape.height },
        ]
      : shape.points;
  if (
    vertices.some(
      (point) => !Number.isFinite(point.x) || !Number.isFinite(point.y),
    )
  ) {
    return {
      kind: shape.kind,
      vertices: [],
      geometryIssue: issue(
        "non-finite-geometry",
        "topology-and-content",
        `${shape.kind} contains a non-finite coordinate.`,
      ),
    };
  }
  return { kind: shape.kind, vertices };
}

function boundsOf(vertices: readonly NormalizedPoint[]) {
  const first = vertices[0];
  if (first === undefined) {
    return undefined;
  }
  return vertices.slice(1).reduce<NormalizedBounds>(
    (bounds, point) => ({
      minX: Math.min(bounds.minX, point.x),
      minY: Math.min(bounds.minY, point.y),
      maxX: Math.max(bounds.maxX, point.x),
      maxY: Math.max(bounds.maxY, point.y),
    }),
    { minX: first.x, minY: first.y, maxX: first.x, maxY: first.y },
  );
}

function pointsEqual(first: NormalizedPoint, second: NormalizedPoint) {
  return first.x === second.x && first.y === second.y;
}

function topologyVertices(vertices: readonly NormalizedPoint[]) {
  const distinct: NormalizedPoint[] = [];
  for (const point of vertices) {
    if (distinct.length === 0 || !pointsEqual(distinct.at(-1)!, point)) {
      distinct.push(point);
    }
  }
  if (
    distinct.length > 1 &&
    pointsEqual(distinct[0], distinct[distinct.length - 1])
  ) {
    distinct.pop();
  }
  return distinct;
}

function signedArea(vertices: readonly NormalizedPoint[]) {
  const origin = vertices[0];
  if (origin === undefined) {
    return 0;
  }
  return (
    vertices.reduce((area, point, index) => {
      const next = vertices[(index + 1) % vertices.length];
      return (
        area +
        (point.x - origin.x) * (next.y - origin.y) -
        (next.x - origin.x) * (point.y - origin.y)
      );
    }, 0) / 2
  );
}

function hasBelowThresholdFeature(
  vertices: readonly NormalizedPoint[],
  bounds: NormalizedBounds,
) {
  const width = bounds.maxX - bounds.minX;
  const height = bounds.maxY - bounds.minY;
  if (
    width < MIN_NORMALIZED_FEATURE_SIZE ||
    height < MIN_NORMALIZED_FEATURE_SIZE
  ) {
    return true;
  }
  return vertices.some((point, index) => {
    const next = vertices[(index + 1) % vertices.length];
    const length = Math.hypot(next.x - point.x, next.y - point.y);
    return length > 0 && length < MIN_NORMALIZED_FEATURE_SIZE;
  });
}

function median(values: number[]) {
  values.sort((first, second) => first - second);
  const middle = Math.floor(values.length / 2);
  return values.length % 2 === 0
    ? (values[middle - 1] + values[middle]) / 2
    : values[middle];
}

function normalizedCoordinate(value: number) {
  return (
    Math.round(value / NORMALIZED_COORDINATE_QUANTUM) *
    NORMALIZED_COORDINATE_QUANTUM
  );
}

/** Projects immutable package geometry into one translation/scale-invariant space. */
export function normalizePuzzleGeometry(
  puzzle: PuzzlePackageV1,
): NormalizedPuzzleGeometry {
  const rawByCellId = new Map(
    Object.values(puzzle.cells).map((cell) => [
      cell.id,
      rawVertices(cell.shape),
    ]),
  );
  const finitePoints = [...rawByCellId.values()].flatMap((geometry) =>
    geometry.geometryIssue === undefined ? geometry.vertices : [],
  );
  const globalBounds = boundsOf(finitePoints);
  const spanX =
    globalBounds === undefined ? 0 : globalBounds.maxX - globalBounds.minX;
  const spanY =
    globalBounds === undefined ? 0 : globalBounds.maxY - globalBounds.minY;
  const span = Math.max(spanX, spanY);
  const hasUsableTransform = Number.isFinite(span) && span > 0;
  const originX = globalBounds?.minX ?? 0;
  const originY = globalBounds?.minY ?? 0;

  const cells = new Map<string, NormalizedCellGeometry>();
  const displayUnitCandidates: number[] = [];
  for (const [cellId, raw] of rawByCellId) {
    if (raw.geometryIssue !== undefined) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: raw.geometryIssue,
      });
      continue;
    }
    if (!hasUsableTransform) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: issue(
          "invalid-topology",
          "topology-and-content",
          `${raw.kind} cannot be normalized from collapsed global bounds.`,
        ),
      });
      continue;
    }

    const contentVertices = raw.vertices.map((point) => ({
      x: normalizedCoordinate((point.x - originX) / span),
      y: normalizedCoordinate((point.y - originY) / span),
    }));
    const rawBounds = boundsOf(raw.vertices);
    const normalizedContentBounds = boundsOf(contentVertices);
    if (
      rawBounds !== undefined &&
      rawBounds.maxX > rawBounds.minX &&
      rawBounds.maxY > rawBounds.minY &&
      normalizedContentBounds !== undefined &&
      hasBelowThresholdFeature(contentVertices, normalizedContentBounds)
    ) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: issue(
          "below-minimum-feature",
          "topology-and-content",
          `${raw.kind} has a feature below the minimum normalized renderer scale (${MIN_NORMALIZED_FEATURE_SIZE}).`,
        ),
      });
      continue;
    }
    const vertices = topologyVertices(contentVertices);
    const bounds = boundsOf(vertices);
    if (
      bounds === undefined ||
      vertices.length < 3 ||
      Math.abs(signedArea(vertices)) <
        MIN_NORMALIZED_FEATURE_SIZE * MIN_NORMALIZED_FEATURE_SIZE
    ) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: issue(
          "invalid-topology",
          "topology-and-content",
          `${raw.kind} has no usable normalized topology.`,
        ),
      });
      continue;
    }
    cells.set(cellId, {
      kind: raw.kind,
      vertices,
      contentVertices,
      bounds,
    });
    displayUnitCandidates.push(
      Math.max(bounds.maxX - bounds.minX, bounds.maxY - bounds.minY),
    );
  }

  const displayUnit =
    displayUnitCandidates.length === 0 ? 1 : median(displayUnitCandidates);
  const displayScale = 1 / displayUnit;
  return {
    cells,
    width: hasUsableTransform ? (spanX / span) * displayScale : 1,
    height: hasUsableTransform ? (spanY / span) * displayScale : 1,
    displayScale,
  };
}
