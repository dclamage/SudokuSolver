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

export interface SceneAffineTransform {
  scale: number;
  translateX: number;
  translateY: number;
}

export interface NormalizedPuzzleGeometry {
  cells: ReadonlyMap<string, NormalizedCellGeometry>;
  width: number;
  height: number;
  displayScale: number;
  nativeToSceneTransform?: SceneAffineTransform;
}

interface RawCellGeometry {
  kind: NormalizedCellGeometry["kind"];
  vertices: readonly NormalizedPoint[];
  geometryIssue?: SceneGeometryIssue;
}

interface NormalizationFrame {
  coordinateScale: number;
  minX: number;
  minY: number;
  spanX: number;
  spanY: number;
  span: number;
}

function issue(
  code: SceneGeometryIssue["code"],
  affects: SceneGeometryIssue["affects"],
  message: string,
): SceneGeometryIssue {
  return { code, affects, message };
}

function invalidTopology(kind: RawCellGeometry["kind"], detail?: string) {
  return issue(
    "invalid-topology",
    "topology-and-content",
    detail ?? `${kind} has no usable normalized topology.`,
  );
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

function coordinateScale(vertices: readonly NormalizedPoint[]) {
  let scale = 0;
  for (const point of vertices) {
    scale = Math.max(scale, Math.abs(point.x), Math.abs(point.y));
  }
  return scale === 0 ? 1 : scale;
}

function createNormalizationFrame(
  vertices: readonly NormalizedPoint[],
): NormalizationFrame | undefined {
  if (vertices.length === 0) {
    return undefined;
  }
  const scale = coordinateScale(vertices);
  const scaledBounds = boundsOf(
    vertices.map((point) => ({ x: point.x / scale, y: point.y / scale })),
  );
  if (scaledBounds === undefined) {
    return undefined;
  }
  const spanX = scaledBounds.maxX - scaledBounds.minX;
  const spanY = scaledBounds.maxY - scaledBounds.minY;
  const span = Math.max(spanX, spanY);
  if (!Number.isFinite(span) || !(span > 0)) {
    return undefined;
  }
  return {
    coordinateScale: scale,
    minX: scaledBounds.minX,
    minY: scaledBounds.minY,
    spanX,
    spanY,
    span,
  };
}

function normalizePoint(point: NormalizedPoint, frame: NormalizationFrame) {
  return {
    x: (point.x / frame.coordinateScale - frame.minX) / frame.span,
    y: (point.y / frame.coordinateScale - frame.minY) / frame.span,
  };
}

function cross(
  first: NormalizedPoint,
  second: NormalizedPoint,
  third: NormalizedPoint,
) {
  return (
    (second.x - first.x) * (third.y - first.y) -
    (second.y - first.y) * (third.x - first.x)
  );
}

function pointOnSegment(
  point: NormalizedPoint,
  from: NormalizedPoint,
  to: NormalizedPoint,
) {
  return (
    cross(from, to, point) === 0 &&
    point.x >= Math.min(from.x, to.x) &&
    point.x <= Math.max(from.x, to.x) &&
    point.y >= Math.min(from.y, to.y) &&
    point.y <= Math.max(from.y, to.y)
  );
}

function segmentsIntersect(
  firstFrom: NormalizedPoint,
  firstTo: NormalizedPoint,
  secondFrom: NormalizedPoint,
  secondTo: NormalizedPoint,
) {
  const firstTurn = cross(firstFrom, firstTo, secondFrom);
  const secondTurn = cross(firstFrom, firstTo, secondTo);
  const thirdTurn = cross(secondFrom, secondTo, firstFrom);
  const fourthTurn = cross(secondFrom, secondTo, firstTo);
  if (
    Math.sign(firstTurn) !== Math.sign(secondTurn) &&
    Math.sign(thirdTurn) !== Math.sign(fourthTurn) &&
    firstTurn !== 0 &&
    secondTurn !== 0 &&
    thirdTurn !== 0 &&
    fourthTurn !== 0
  ) {
    return true;
  }
  return (
    (firstTurn === 0 && pointOnSegment(secondFrom, firstFrom, firstTo)) ||
    (secondTurn === 0 && pointOnSegment(secondTo, firstFrom, firstTo)) ||
    (thirdTurn === 0 && pointOnSegment(firstFrom, secondFrom, secondTo)) ||
    (fourthTurn === 0 && pointOnSegment(firstTo, secondFrom, secondTo))
  );
}

function validateSimpleTopology(raw: RawCellGeometry) {
  if (raw.geometryIssue !== undefined) {
    return raw;
  }
  const hasExplicitClosure =
    raw.vertices.length > 1 &&
    pointsEqual(raw.vertices[0], raw.vertices[raw.vertices.length - 1]);
  const vertices = hasExplicitClosure
    ? raw.vertices.slice(0, -1)
    : [...raw.vertices];
  if (vertices.length < 3) {
    return { ...raw, geometryIssue: invalidTopology(raw.kind) };
  }
  for (let index = 0; index < vertices.length; index += 1) {
    if (pointsEqual(vertices[index], vertices[(index + 1) % vertices.length])) {
      return {
        ...raw,
        geometryIssue: invalidTopology(
          raw.kind,
          `${raw.kind} contains a repeated vertex or zero-length edge.`,
        ),
      };
    }
    for (
      let otherIndex = index + 1;
      otherIndex < vertices.length;
      otherIndex += 1
    ) {
      if (pointsEqual(vertices[index], vertices[otherIndex])) {
        return {
          ...raw,
          geometryIssue: invalidTopology(
            raw.kind,
            `${raw.kind} contains a repeated vertex or zero-length edge.`,
          ),
        };
      }
    }
  }

  const localFrame = createNormalizationFrame(vertices);
  if (localFrame === undefined) {
    return { ...raw, geometryIssue: invalidTopology(raw.kind) };
  }
  const local = vertices.map((point) => normalizePoint(point, localFrame));
  const area = signedArea(local);
  if (!Number.isFinite(area) || area === 0) {
    return { ...raw, geometryIssue: invalidTopology(raw.kind) };
  }
  for (let firstIndex = 0; firstIndex < local.length; firstIndex += 1) {
    const firstNext = (firstIndex + 1) % local.length;
    for (
      let secondIndex = firstIndex + 1;
      secondIndex < local.length;
      secondIndex += 1
    ) {
      const secondNext = (secondIndex + 1) % local.length;
      const adjacent = firstIndex === secondNext || firstNext === secondIndex;
      if (adjacent) {
        const firstVector = {
          x: local[firstNext].x - local[firstIndex].x,
          y: local[firstNext].y - local[firstIndex].y,
        };
        const secondVector = {
          x: local[secondNext].x - local[secondIndex].x,
          y: local[secondNext].y - local[secondIndex].y,
        };
        if (
          firstVector.x * secondVector.y -
              firstVector.y * secondVector.x ===
            0 &&
          firstVector.x * secondVector.x +
              firstVector.y * secondVector.y <
            0
        ) {
          return {
            ...raw,
            geometryIssue: invalidTopology(
              raw.kind,
              `${raw.kind} contains overlapping adjacent edges.`,
            ),
          };
        }
        continue;
      }
      if (
        segmentsIntersect(
          local[firstIndex],
          local[firstNext],
          local[secondIndex],
          local[secondNext],
        )
      ) {
        return {
          ...raw,
          geometryIssue: invalidTopology(
            raw.kind,
            `${raw.kind} contains intersecting or overlapping edges.`,
          ),
        };
      }
    }
  }
  return raw;
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
      validateSimpleTopology(rawVertices(cell.shape)),
    ]),
  );
  const validPoints = [...rawByCellId.values()].flatMap((geometry) =>
    geometry.geometryIssue === undefined ? geometry.vertices : [],
  );
  const globalFrame = createNormalizationFrame(validPoints);

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
    if (globalFrame === undefined) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: invalidTopology(
          raw.kind,
          `${raw.kind} cannot be normalized from collapsed global bounds.`,
        ),
      });
      continue;
    }

    const unquantizedVertices = raw.vertices.map((point) =>
      normalizePoint(point, globalFrame),
    );
    const unquantizedBounds = boundsOf(unquantizedVertices);
    if (
      unquantizedBounds === undefined ||
      hasBelowThresholdFeature(unquantizedVertices, unquantizedBounds)
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

    const contentVertices = unquantizedVertices.map((point) => ({
      x: normalizedCoordinate(point.x),
      y: normalizedCoordinate(point.y),
    }));
    const vertices = topologyVertices(contentVertices);
    const bounds = boundsOf(vertices);
    if (
      bounds === undefined ||
      vertices.length < 3 ||
      signedArea(vertices) === 0
    ) {
      cells.set(cellId, {
        kind: raw.kind,
        vertices: [],
        contentVertices: [],
        geometryIssue: issue(
          "below-minimum-feature",
          "topology-and-content",
          `${raw.kind} collapsed while preparing normalized renderer coordinates.`,
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
  const nativeToSceneTransform =
    globalFrame === undefined
      ? undefined
      : {
          scale:
            (displayScale / globalFrame.coordinateScale) / globalFrame.span,
          translateX:
            (-globalFrame.minX / globalFrame.span) * displayScale,
          translateY:
            (-globalFrame.minY / globalFrame.span) * displayScale,
        };
  return {
    cells,
    width:
      globalFrame === undefined
        ? 1
        : (globalFrame.spanX / globalFrame.span) * displayScale,
    height:
      globalFrame === undefined
        ? 1
        : (globalFrame.spanY / globalFrame.span) * displayScale,
    displayScale,
    nativeToSceneTransform:
      nativeToSceneTransform !== undefined &&
      Object.values(nativeToSceneTransform).every(Number.isFinite)
        ? nativeToSceneTransform
        : undefined,
  };
}
