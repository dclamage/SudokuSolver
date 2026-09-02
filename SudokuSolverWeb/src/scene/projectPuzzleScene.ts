import type { CellShape, PuzzlePackageV1 } from "../domain/puzzle/types";
import type { CapabilityEntityResult } from "../solver/protocol";
import type {
  PuzzleScene,
  PuzzleSceneView,
  SceneCellNode,
  SceneClipRect,
  SceneTextNode,
} from "./types";

const RELATIVE_GEOMETRY_TOLERANCE = 1e-7;
const MAX_LENGTH_TOLERANCE = 1e-6;
const COORDINATE_ULP_FACTOR = 16;
const floatingPointBuffer = new ArrayBuffer(8);
const floatingPointView = new DataView(floatingPointBuffer);

interface Bounds {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

interface Point {
  x: number;
  y: number;
}

interface Segment {
  from: Point;
  to: Point;
  polygonIndex?: number;
}

interface ContentRegion extends Bounds {
  center: Point;
}

interface ProjectedShape {
  path: string;
  bounds?: Bounds;
  contentRegion?: ContentRegion;
  vertices: readonly Point[];
  geometryIssue?: string;
}

export interface PuzzleSceneProjectionMetrics {
  interiorWorkUnits: number;
  earCandidateScans: number;
  pointInTriangleTests: number;
  triangleEvaluations: number;
  nearestEdgeScans: number;
  pointInPolygonEdgeScans: number;
}

type InteriorWorkCategory = Exclude<
  keyof PuzzleSceneProjectionMetrics,
  "interiorWorkUnits"
>;

function recordInteriorWork(
  metrics: PuzzleSceneProjectionMetrics | undefined,
  category: InteriorWorkCategory,
) {
  if (metrics === undefined) {
    return;
  }
  metrics[category] += 1;
  metrics.interiorWorkUnits += 1;
}

function pointToSegmentDistance(point: Point, segment: Segment) {
  const segmentX = segment.to.x - segment.from.x;
  const segmentY = segment.to.y - segment.from.y;
  const length = Math.hypot(segmentX, segmentY);
  if (length === 0) {
    return Math.hypot(
      point.x - segment.from.x,
      point.y - segment.from.y,
    );
  }

  const tangentX = segmentX / length;
  const tangentY = segmentY / length;
  const projection = Math.max(
    0,
    Math.min(
      length,
      (point.x - segment.from.x) * tangentX +
        (point.y - segment.from.y) * tangentY,
    ),
  );
  return Math.hypot(
    point.x - (segment.from.x + projection * tangentX),
    point.y - (segment.from.y + projection * tangentY),
  );
}

function polygonSegments(vertices: readonly Point[]): Segment[] {
  return vertices.map((from, index) => ({
    from,
    to: vertices[(index + 1) % vertices.length],
  }));
}

function isPointInPolygon(
  point: Point,
  vertices: readonly Point[],
  metrics?: PuzzleSceneProjectionMetrics,
) {
  let inside = false;
  for (
    let index = 0, previousIndex = vertices.length - 1;
    index < vertices.length;
    previousIndex = index, index += 1
  ) {
    recordInteriorWork(metrics, "pointInPolygonEdgeScans");
    const current = vertices[index];
    const previous = vertices[previousIndex];
    if (
      current.y > point.y !== previous.y > point.y &&
      point.x <
        ((previous.x - current.x) * (point.y - current.y)) /
          (previous.y - current.y) +
          current.x
    ) {
      inside = !inside;
    }
  }
  return inside;
}

function clearanceAt(
  point: Point,
  segments: readonly Segment[],
  metrics?: PuzzleSceneProjectionMetrics,
) {
  let clearance = Number.POSITIVE_INFINITY;
  for (const segment of segments) {
    recordInteriorWork(metrics, "nearestEdgeScans");
    clearance = Math.min(clearance, pointToSegmentDistance(point, segment));
  }
  return clearance;
}

function polygonArea(vertices: readonly Point[]) {
  const origin = vertices[0];
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

function signedDistanceToPolygon(
  point: Point,
  vertices: readonly Point[],
  segments: readonly Segment[],
  metrics?: PuzzleSceneProjectionMetrics,
) {
  const distance = clearanceAt(point, segments, metrics);
  return isPointInPolygon(point, vertices, metrics) ? distance : -distance;
}

function triangleTurn(first: Point, second: Point, third: Point) {
  return cross(
    second.x - first.x,
    second.y - first.y,
    third.x - second.x,
    third.y - second.y,
  );
}

function pointInTriangle(
  point: Point,
  first: Point,
  second: Point,
  third: Point,
  orientation: number,
  tolerance: number,
) {
  const firstSide =
    cross(
      second.x - first.x,
      second.y - first.y,
      point.x - first.x,
      point.y - first.y,
    ) * orientation;
  const secondSide =
    cross(
      third.x - second.x,
      third.y - second.y,
      point.x - second.x,
      point.y - second.y,
    ) * orientation;
  const thirdSide =
    cross(
      first.x - third.x,
      first.y - third.y,
      point.x - third.x,
      point.y - third.y,
    ) * orientation;
  return (
    firstSide >= -tolerance &&
    secondSide >= -tolerance &&
    thirdSide >= -tolerance
  );
}

function triangulatePolygon(
  vertices: readonly Point[],
  area: number,
  metrics?: PuzzleSceneProjectionMetrics,
) {
  const orientation = area > 0 ? 1 : -1;
  const areaTolerance =
    Math.abs(area) * Number.EPSILON * COORDINATE_ULP_FACTOR;
  const remaining = vertices.map((_, index) => index);
  const triangles: [Point, Point, Point][] = [];
  while (remaining.length > 3) {
    let earIndex = -1;
    for (let index = 0; index < remaining.length; index += 1) {
      recordInteriorWork(metrics, "earCandidateScans");
      const previousIndex = remaining[(index + remaining.length - 1) % remaining.length];
      const currentIndex = remaining[index];
      const nextIndex = remaining[(index + 1) % remaining.length];
      const previous = vertices[previousIndex];
      const current = vertices[currentIndex];
      const next = vertices[nextIndex];
      if (triangleTurn(previous, current, next) * orientation <= areaTolerance) {
        continue;
      }
      const containsVertex = remaining.some((candidateIndex) => {
        if (
          candidateIndex === previousIndex ||
          candidateIndex === currentIndex ||
          candidateIndex === nextIndex
        ) {
          return false;
        }
        recordInteriorWork(metrics, "pointInTriangleTests");
        return pointInTriangle(
          vertices[candidateIndex],
          previous,
          current,
          next,
          orientation,
          areaTolerance,
        );
      });
      if (!containsVertex) {
        earIndex = index;
        triangles.push([previous, current, next]);
        break;
      }
    }
    if (earIndex < 0) {
      return [];
    }
    remaining.splice(earIndex, 1);
  }
  if (remaining.length === 3) {
    triangles.push([
      vertices[remaining[0]],
      vertices[remaining[1]],
      vertices[remaining[2]],
    ]);
  }
  return triangles;
}

function triangleIncenter([first, second, third]: [Point, Point, Point]) {
  const firstWeight = Math.hypot(second.x - third.x, second.y - third.y);
  const secondWeight = Math.hypot(first.x - third.x, first.y - third.y);
  const thirdWeight = Math.hypot(first.x - second.x, first.y - second.y);
  const totalWeight = firstWeight + secondWeight + thirdWeight;
  return {
    x:
      first.x +
      (secondWeight * (second.x - first.x) +
        thirdWeight * (third.x - first.x)) /
        totalWeight,
    y:
      first.y +
      (secondWeight * (second.y - first.y) +
        thirdWeight * (third.y - first.y)) /
        totalWeight,
  };
}

function findPolygonContentRegion(
  vertices: readonly Point[],
  bounds: Bounds,
  metrics?: PuzzleSceneProjectionMetrics,
): ContentRegion | undefined {
  const segments = polygonSegments(vertices);
  const width = bounds.maxX - bounds.minX;
  const height = bounds.maxY - bounds.minY;
  const area = polygonArea(vertices);
  if (
    vertices.length < 3 ||
    !Number.isFinite(area) ||
    area === 0 ||
    width <= 0 ||
    height <= 0
  ) {
    return undefined;
  }

  const triangles = triangulatePolygon(vertices, area, metrics);
  if (triangles.length === 0) {
    return undefined;
  }
  let bestPoint: Point | undefined;
  let bestDistance = Number.NEGATIVE_INFINITY;
  for (const triangle of triangles) {
    recordInteriorWork(metrics, "triangleEvaluations");
    const incenter = triangleIncenter(triangle);
    const distance = signedDistanceToPolygon(
      incenter,
      vertices,
      segments,
      metrics,
    );
    if (distance > bestDistance) {
      bestPoint = incenter;
      bestDistance = distance;
    }
  }

  if (
    bestPoint === undefined ||
    !(bestDistance > 0) ||
    !isPointInPolygon(bestPoint, vertices, metrics)
  ) {
    return undefined;
  }

  const halfExtent = (bestDistance * 0.9) / Math.sqrt(2);
  const region = {
    center: bestPoint,
    minX: bestPoint.x - halfExtent,
    minY: bestPoint.y - halfExtent,
    maxX: bestPoint.x + halfExtent,
    maxY: bestPoint.y + halfExtent,
  };
  return region.minX < region.maxX && region.minY < region.maxY
    ? region
    : undefined;
}

function projectShape(
  shape: CellShape,
  metrics?: PuzzleSceneProjectionMetrics,
): ProjectedShape {
  if (shape.kind === "path") {
    return {
      path: shape.d,
      vertices: [],
      geometryIssue: "Visual geometry unavailable: path cells require explicit bounds.",
    };
  }

  const vertices: readonly Point[] =
    shape.kind === "rect"
      ? [
          { x: shape.x, y: shape.y },
          { x: shape.x + shape.width, y: shape.y },
          { x: shape.x + shape.width, y: shape.y + shape.height },
          { x: shape.x, y: shape.y + shape.height },
        ]
      : shape.points;
  const [firstPoint, ...remainingPoints] = vertices;
  if (firstPoint === undefined) {
    return {
      path: "",
      vertices: [],
      geometryIssue: "Visual geometry unavailable: polygon has no usable interior.",
    };
  }

  const bounds = remainingPoints.reduce<Bounds>(
    (current, point) => ({
      minX: Math.min(current.minX, point.x),
      minY: Math.min(current.minY, point.y),
      maxX: Math.max(current.maxX, point.x),
      maxY: Math.max(current.maxY, point.y),
    }),
    {
      minX: firstPoint.x,
      minY: firstPoint.y,
      maxX: firstPoint.x,
      maxY: firstPoint.y,
    },
  );
  const center = {
    x: (bounds.minX + bounds.maxX) / 2,
    y: (bounds.minY + bounds.maxY) / 2,
  };
  const path =
    shape.kind === "rect"
      ? `M ${vertices[0].x} ${vertices[0].y} H ${vertices[1].x} V ${vertices[2].y} H ${vertices[3].x} Z`
      : `M ${vertices.map((point) => `${point.x} ${point.y}`).join(" L ")} Z`;
  const contentRegion =
    shape.kind === "rect" && bounds.maxX > bounds.minX && bounds.maxY > bounds.minY
      ? { ...bounds, center }
      : shape.kind === "polygon"
        ? findPolygonContentRegion(vertices, bounds, metrics)
        : undefined;
  if (contentRegion === undefined) {
    return {
      path,
      bounds,
      vertices: [],
      geometryIssue: `Visual geometry unavailable: ${shape.kind} has no usable interior.`,
    };
  }

  return {
    path,
    bounds,
    contentRegion,
    vertices,
  };
}

function getCellCapability(
  cellId: string,
  view: PuzzleSceneView,
): CapabilityEntityResult | undefined {
  const capability = view.entityCapabilities[`cell:${cellId}`];
  return capability?.entityKind === "cell" && capability.entityId === cellId
    ? capability
    : undefined;
}

function humanizeParticipation(
  status: CapabilityEntityResult["status"] | "unknown",
) {
  switch (status) {
    case "fullyVerified":
      return "fully verified";
    case "partiallyVerified":
      return "partially verified";
    case "visualOnly":
      return "visual only";
    case "invalidDefinition":
      return "invalid definition";
    default:
      return "unknown";
  }
}

function createCandidateClip(
  id: string,
  region: ContentRegion,
  columns: number,
  column: number,
  row: number,
): SceneClipRect {
  const slotWidth = (region.maxX - region.minX) / columns;
  const slotHeight = (region.maxY - region.minY) / columns;
  const insetX = slotWidth * 0.04;
  const insetY = slotHeight * 0.04;
  return {
    id,
    x: region.minX + column * slotWidth + insetX,
    y: region.minY + row * slotHeight + insetY,
    width: slotWidth - 2 * insetX,
    height: slotHeight - 2 * insetY,
  };
}

function projectCandidateNodes(
  puzzle: PuzzlePackageV1,
  cellId: string,
  cellIndex: number,
  region: ContentRegion,
  view: PuzzleSceneView,
): SceneTextNode[] {
  const candidateIds = view.candidates[cellId];
  if (candidateIds === undefined || candidateIds.length === 0) {
    return [];
  }

  const cell = puzzle.cells[cellId];
  const domain = puzzle.domains[cell.domainId];
  const columns = Math.ceil(Math.sqrt(domain.values.length));
  const candidateSet = new Set(candidateIds);
  const regionWidth = region.maxX - region.minX;
  const regionHeight = region.maxY - region.minY;

  return domain.values.flatMap((value, domainIndex) => {
    if (!candidateSet.has(value.id)) {
      return [];
    }

    const column = domainIndex % columns;
    const row = Math.floor(domainIndex / columns);
    return [
      {
        kind: "text" as const,
        id: `candidate-${cellId}-${value.id}`,
        x: region.minX + ((column + 0.5) * regionWidth) / columns,
        y: region.minY + ((row + 0.5) * regionHeight) / columns,
        text: value.label,
        role: "candidate" as const,
        clip: createCandidateClip(
          `candidate-clip-${cellIndex}-${domainIndex}`,
          region,
          columns,
          column,
          row,
        ),
      },
    ];
  });
}

function projectCellContent(
  puzzle: PuzzlePackageV1,
  cellId: string,
  cellIndex: number,
  region: ContentRegion,
  view: PuzzleSceneView,
): SceneTextNode[] {
  const givenValueId = puzzle.givens[cellId];
  const projectedValueId = view.values[cellId];
  const valueId = givenValueId ?? projectedValueId;
  if (valueId === undefined) {
    return projectCandidateNodes(puzzle, cellId, cellIndex, region, view);
  }

  const cell = puzzle.cells[cellId];
  const value = puzzle.domains[cell.domainId].values.find(
    (domainValue) => domainValue.id === valueId,
  );
  if (value === undefined) {
    return [];
  }

  const role = givenValueId === undefined ? "value" : "given";
  return [
    {
      kind: "text",
      id: `${role}-${cellId}`,
      x: region.center.x,
      y: region.center.y,
      text: value.label,
      role,
    },
  ];
}

function describeCellContent(content: readonly SceneTextNode[]) {
  const displayedValue = content.find((node) => node.role !== "candidate");
  if (displayedValue !== undefined) {
    return `${displayedValue.role} ${displayedValue.text}`;
  }
  const candidates = content
    .filter((node) => node.role === "candidate")
    .map((node) => node.text);
  return candidates.length === 0 ? undefined : `candidates ${candidates.join(", ")}`;
}

function segmentLength(segment: Segment) {
  return Math.hypot(
    segment.to.x - segment.from.x,
    segment.to.y - segment.from.y,
  );
}

function baseSegmentTolerance(segment: Segment) {
  const lengthTolerance = Math.min(
    segmentLength(segment) * RELATIVE_GEOMETRY_TOLERANCE,
    MAX_LENGTH_TOLERANCE,
  );
  const coordinateMagnitude = Math.max(
    1,
    Math.abs(segment.from.x),
    Math.abs(segment.from.y),
    Math.abs(segment.to.x),
    Math.abs(segment.to.y),
  );
  return Math.max(
    lengthTolerance,
    coordinateMagnitude * Number.EPSILON * COORDINATE_ULP_FACTOR,
  );
}

function exactlyContainsPoint(segment: Segment, point: Point) {
  if (lineDistance(point, segment) !== 0) {
    return false;
  }
  const ratio = projectRatio(point, segment);
  return ratio >= 0 && ratio <= 1;
}

function segmentTolerance(
  segment: Segment,
  arrangement?: readonly Segment[],
) {
  const length = segmentLength(segment);
  let tolerance = Math.min(baseSegmentTolerance(segment), length / 4);
  if (arrangement === undefined || segment.polygonIndex === undefined) {
    return tolerance;
  }

  const midpoint = pointAt(segment, 0.5);
  for (const candidate of arrangement) {
    if (
      candidate.polygonIndex !== segment.polygonIndex ||
      exactlyContainsPoint(candidate, midpoint)
    ) {
      continue;
    }
    const separation = pointToSegmentDistance(midpoint, candidate);
    if (separation > 0) {
      tolerance = Math.min(tolerance, separation / 4);
    }
  }
  return tolerance;
}

function pointAt(segment: Segment, ratio: number): Point {
  return {
    x: segment.from.x + (segment.to.x - segment.from.x) * ratio,
    y: segment.from.y + (segment.to.y - segment.from.y) * ratio,
  };
}

function signedLineDistance(point: Point, segment: Segment) {
  const length = segmentLength(segment);
  if (length === 0) {
    return 0;
  }
  const tangentX = (segment.to.x - segment.from.x) / length;
  const tangentY = (segment.to.y - segment.from.y) / length;
  return (
    -(point.x - segment.from.x) * tangentY +
    (point.y - segment.from.y) * tangentX
  );
}

function lineDistance(point: Point, segment: Segment) {
  return Math.abs(signedLineDistance(point, segment));
}

function cross(deltaAX: number, deltaAY: number, deltaBX: number, deltaBY: number) {
  return deltaAX * deltaBY - deltaAY * deltaBX;
}

function projectRatio(point: Point, segment: Segment) {
  const deltaX = segment.to.x - segment.from.x;
  const deltaY = segment.to.y - segment.from.y;
  const length = Math.hypot(deltaX, deltaY);
  if (length === 0) {
    return 0;
  }
  return (
    ((point.x - segment.from.x) * (deltaX / length) +
      (point.y - segment.from.y) * (deltaY / length)) /
    length
  );
}

function addSplitRatio(
  ratios: number[],
  ratio: number,
  ratioTolerance: number,
) {
  if (ratio >= -ratioTolerance && ratio <= 1 + ratioTolerance) {
    ratios.push(Math.max(0, Math.min(1, ratio)));
  }
}

function splitSegment(
  segment: Segment,
  allSegments: readonly Segment[],
): Segment[] {
  const ratios = [0, 1];
  const segmentDeltaX = segment.to.x - segment.from.x;
  const segmentDeltaY = segment.to.y - segment.from.y;
  const length = segmentLength(segment);
  const tolerance = segmentTolerance(segment, allSegments);
  const ratioTolerance = tolerance / length;
  for (const other of allSegments) {
    if (other === segment || segmentLength(other) === 0) {
      continue;
    }

    const otherDeltaX = other.to.x - other.from.x;
    const otherDeltaY = other.to.y - other.from.y;
    const otherLength = segmentLength(other);
    const pairTolerance = Math.min(
      tolerance,
      segmentTolerance(other, allSegments),
    );
    const parallelThreshold =
      pairTolerance * Math.max(length, otherLength);
    const denominator = cross(
      segmentDeltaX,
      segmentDeltaY,
      otherDeltaX,
      otherDeltaY,
    );
    if (Math.abs(denominator) <= parallelThreshold) {
      if (
        lineDistance(other.from, segment) <= pairTolerance &&
        lineDistance(other.to, segment) <= pairTolerance
      ) {
        addSplitRatio(
          ratios,
          projectRatio(other.from, segment),
          ratioTolerance,
        );
        addSplitRatio(
          ratios,
          projectRatio(other.to, segment),
          ratioTolerance,
        );
      }
      continue;
    }

    const offsetX = other.from.x - segment.from.x;
    const offsetY = other.from.y - segment.from.y;
    const segmentRatio =
      cross(offsetX, offsetY, otherDeltaX, otherDeltaY) / denominator;
    const otherRatio =
      cross(offsetX, offsetY, segmentDeltaX, segmentDeltaY) / denominator;
    const otherRatioTolerance = pairTolerance / otherLength;
    if (
      otherRatio >= -otherRatioTolerance &&
      otherRatio <= 1 + otherRatioTolerance
    ) {
      addSplitRatio(ratios, segmentRatio, ratioTolerance);
    }
  }

  ratios.sort((first, second) => first - second);
  const uniqueRatios = ratios.filter(
    (ratio, index) =>
      index === 0 || Math.abs(ratio - ratios[index - 1]) > ratioTolerance,
  );
  return uniqueRatios.slice(0, -1).flatMap((start, index) => {
    const end = uniqueRatios[index + 1];
    return end <= start
      ? []
      : [
          {
            from: pointAt(segment, start),
            to: pointAt(segment, end),
            polygonIndex: segment.polygonIndex,
          },
        ];
  });
}

function pointInUnion(point: Point, polygons: readonly (readonly Point[])[]) {
  return polygons.some((vertices) => isPointInPolygon(point, vertices));
}

function nextUp(value: number) {
  if (Number.isNaN(value) || value === Number.POSITIVE_INFINITY) {
    return value;
  }
  if (value === 0) {
    return Number.MIN_VALUE;
  }
  floatingPointView.setFloat64(0, value);
  const bits = floatingPointView.getBigUint64(0);
  floatingPointView.setBigUint64(0, value > 0 ? bits + 1n : bits - 1n);
  return floatingPointView.getFloat64(0);
}

function nextDown(value: number) {
  return -nextUp(-value);
}

function stepRepresentably(value: number, delta: number) {
  if (delta === 0) {
    return value;
  }
  const offset = value + delta;
  if (Number.isFinite(offset) && offset !== value) {
    return offset;
  }
  return delta > 0 ? nextUp(value) : nextDown(value);
}

function segmentContainsPointWithinTolerance(
  segment: Segment,
  point: Point,
  tolerance: number,
) {
  if (lineDistance(point, segment) > tolerance) {
    return false;
  }
  const ratio = projectRatio(point, segment);
  const ratioTolerance = tolerance / segmentLength(segment);
  return ratio >= -ratioTolerance && ratio <= 1 + ratioTolerance;
}

function localProbeDistance(
  segment: Segment,
  arrangement: readonly Segment[],
) {
  const midpoint = pointAt(segment, 0.5);
  const tolerance = segmentTolerance(segment, arrangement);
  let nearestDistinctEdge = Number.POSITIVE_INFINITY;
  for (const candidate of arrangement) {
    const pairTolerance = Math.min(
      tolerance,
      segmentTolerance(candidate, arrangement),
    );
    if (
      segmentContainsPointWithinTolerance(candidate, midpoint, pairTolerance)
    ) {
      continue;
    }
    nearestDistinctEdge = Math.min(
      nearestDistinctEdge,
      pointToSegmentDistance(midpoint, candidate),
    );
  }
  return Math.min(
    tolerance,
    nearestDistinctEdge / 4,
  );
}

function representableSideProbe(
  segment: Segment,
  side: 1 | -1,
  arrangement: readonly Segment[],
) {
  const length = segmentLength(segment);
  const midpoint = pointAt(segment, 0.5);
  const midpointRatio = projectRatio(midpoint, segment);
  if (!(midpointRatio > 0 && midpointRatio < 1)) {
    return undefined;
  }

  const probeDistance = localProbeDistance(segment, arrangement);
  const normalX = -(segment.to.y - segment.from.y) / length;
  const normalY = (segment.to.x - segment.from.x) / length;
  const point = {
    x: stepRepresentably(midpoint.x, normalX * probeDistance * side),
    y: stepRepresentably(midpoint.y, normalY * probeDistance * side),
  };
  if (
    (point.x === midpoint.x && point.y === midpoint.y) ||
    signedLineDistance(point, segment) * side <= 0
  ) {
    return undefined;
  }
  for (const candidate of arrangement) {
    if (
      segmentContainsPointWithinTolerance(
        candidate,
        point,
        segmentTolerance(candidate, arrangement),
      )
    ) {
      return undefined;
    }
  }
  return point;
}

function polygonBounds(vertices: readonly Point[]): Bounds | undefined {
  const first = vertices[0];
  if (first === undefined) {
    return undefined;
  }
  return vertices.slice(1).reduce<Bounds>(
    (bounds, point) => ({
      minX: Math.min(bounds.minX, point.x),
      minY: Math.min(bounds.minY, point.y),
      maxX: Math.max(bounds.maxX, point.x),
      maxY: Math.max(bounds.maxY, point.y),
    }),
    { minX: first.x, minY: first.y, maxX: first.x, maxY: first.y },
  );
}

function isUnionBoundaryInLocalCoordinates(
  segment: Segment,
  polygons: readonly (readonly Point[])[],
) {
  const sourcePolygon =
    segment.polygonIndex === undefined
      ? undefined
      : polygons[segment.polygonIndex];
  const bounds =
    sourcePolygon === undefined ? undefined : polygonBounds(sourcePolygon);
  if (bounds === undefined) {
    return false;
  }
  const scaleX = bounds.maxX - bounds.minX;
  const scaleY = bounds.maxY - bounds.minY;
  if (!(scaleX > 0) || !(scaleY > 0)) {
    return false;
  }

  const normalizePoint = (point: Point): Point => ({
    x: (point.x - bounds.minX) / scaleX,
    y: (point.y - bounds.minY) / scaleY,
  });
  const normalizedPolygons = polygons.map((vertices) =>
    vertices.map(normalizePoint),
  );
  if (
    normalizedPolygons.some((vertices) =>
      vertices.some(
        (point) => !Number.isFinite(point.x) || !Number.isFinite(point.y),
      ),
    )
  ) {
    return false;
  }
  const normalizedArrangement = normalizedPolygons.flatMap(
    (vertices, polygonIndex) =>
      polygonSegments(vertices).map((candidate) => ({
        ...candidate,
        polygonIndex,
      })),
  );
  const normalizedSegment: Segment = {
    from: normalizePoint(segment.from),
    to: normalizePoint(segment.to),
    polygonIndex: segment.polygonIndex,
  };
  const length = segmentLength(normalizedSegment);
  if (!(length > 0)) {
    return false;
  }
  const midpoint = pointAt(normalizedSegment, 0.5);
  const probeDistance = localProbeDistance(
    normalizedSegment,
    normalizedArrangement,
  );
  const normalX =
    -(normalizedSegment.to.y - normalizedSegment.from.y) / length;
  const normalY =
    (normalizedSegment.to.x - normalizedSegment.from.x) / length;
  const firstSide = pointInUnion(
    {
      x: midpoint.x + normalX * probeDistance,
      y: midpoint.y + normalY * probeDistance,
    },
    normalizedPolygons,
  );
  const secondSide = pointInUnion(
    {
      x: midpoint.x - normalX * probeDistance,
      y: midpoint.y - normalY * probeDistance,
    },
    normalizedPolygons,
  );
  return firstSide !== secondSide;
}

function isUnionBoundary(
  segment: Segment,
  polygons: readonly (readonly Point[])[],
  arrangement: readonly Segment[],
) {
  const firstProbe = representableSideProbe(segment, 1, arrangement);
  const secondProbe = representableSideProbe(segment, -1, arrangement);
  if (firstProbe === undefined || secondProbe === undefined) {
    return isUnionBoundaryInLocalCoordinates(segment, polygons);
  }
  return (
    pointInUnion(firstProbe, polygons) !== pointInUnion(secondProbe, polygons)
  );
}

function segmentsAreEquivalent(
  first: Segment,
  second: Segment,
  arrangement: readonly Segment[],
) {
  const tolerance = Math.min(
    segmentTolerance(first, arrangement),
    segmentTolerance(second, arrangement),
  );
  return (
    (Math.hypot(
      first.from.x - second.from.x,
      first.from.y - second.from.y,
    ) <= tolerance &&
      Math.hypot(first.to.x - second.to.x, first.to.y - second.to.y) <=
        tolerance) ||
    (Math.hypot(
      first.from.x - second.to.x,
      first.from.y - second.to.y,
    ) <= tolerance &&
      Math.hypot(first.to.x - second.from.x, first.to.y - second.from.y) <=
        tolerance)
  );
}

function deduplicateSegments(
  segments: readonly Segment[],
  arrangement: readonly Segment[],
) {
  const uniqueSegments: Segment[] = [];
  for (const segment of segments) {
    if (
      !uniqueSegments.some((existing) =>
        segmentsAreEquivalent(existing, segment, arrangement),
      )
    ) {
      uniqueSegments.push(segment);
    }
  }
  return uniqueSegments;
}

function projectGroupBorder(
  cellIds: readonly string[],
  geometryByCellId: ReadonlyMap<string, ProjectedShape>,
) {
  const uniqueCellIds = [...new Set(cellIds)];
  const polygons = uniqueCellIds.flatMap((cellId) => {
    const vertices = geometryByCellId.get(cellId)?.vertices;
    return vertices === undefined || vertices.length < 3 ? [] : [vertices];
  });
  const segments = polygons
    .flatMap((vertices, polygonIndex) =>
      polygonSegments(vertices).map((segment) => ({
        ...segment,
        polygonIndex,
      })),
    )
    .filter((segment) => {
      const length = segmentLength(segment);
      return Number.isFinite(length) && length > 0;
    });
  if (segments.length === 0) {
    return "";
  }

  return deduplicateSegments(
    segments
      .flatMap((segment) => splitSegment(segment, segments))
      .filter((piece) => isUnionBoundary(piece, polygons, segments)),
    segments,
  )
    .map(
      (segment) =>
        `M ${segment.from.x} ${segment.from.y} L ${segment.to.x} ${segment.to.y}`,
    )
    .join(" ");
}

function deduplicateAnnotations(view: PuzzleSceneView) {
  const annotations = new Map<string, (typeof view.annotations)[number]>();
  for (const annotation of view.annotations) {
    if (!annotations.has(annotation.id)) {
      annotations.set(annotation.id, annotation);
    }
  }
  return [...annotations.values()];
}

export function projectPuzzleScene(
  puzzle: PuzzlePackageV1,
  view: PuzzleSceneView,
  metrics?: PuzzleSceneProjectionMetrics,
): PuzzleScene {
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  const geometryByCellId = new Map<string, ProjectedShape>();
  const selectedCellIds = new Set(view.selectedCellIds);

  const nodes: SceneCellNode[] = Object.values(puzzle.cells).map(
    (cell, cellIndex) => {
      const geometry = projectShape(cell.shape, metrics);
      geometryByCellId.set(cell.id, geometry);
      if (geometry.bounds !== undefined) {
        minX = Math.min(minX, geometry.bounds.minX);
        minY = Math.min(minY, geometry.bounds.minY);
        maxX = Math.max(maxX, geometry.bounds.maxX);
        maxY = Math.max(maxY, geometry.bounds.maxY);
      }

      const capability = getCellCapability(cell.id, view);
      const solverParticipation = capability?.status ?? "unknown";
      const content =
        geometry.contentRegion === undefined
          ? []
          : projectCellContent(
              puzzle,
              cell.id,
              cellIndex,
              geometry.contentRegion,
              view,
            );
      const contentDescription = describeCellContent(content);
      const baseLabel = cell.label ?? `Cell ${cell.id}`;
      const reason = capability?.reason?.trim();
      return {
        kind: "cell",
        id: `cell-${cell.id}`,
        cellId: cell.id,
        path: geometry.path,
        label:
          contentDescription === undefined
            ? baseLabel
            : `${baseLabel}, ${contentDescription}`,
        description: `Solver participation: ${humanizeParticipation(solverParticipation)}.${reason === undefined || reason === "" ? "" : ` ${reason}`}${geometry.geometryIssue === undefined ? "" : ` ${geometry.geometryIssue}`}`,
        selected: selectedCellIds.has(cell.id),
        solverParticipation,
        content,
      };
    },
  );

  const groupBorders = Object.values(puzzle.groups).flatMap((group) =>
    group.roles.includes("region")
      ? [
          {
            kind: "path" as const,
            id: `group-border-${group.id}`,
            d: projectGroupBorder(group.cellIds, geometryByCellId),
            role: "grid" as const,
          },
        ]
      : [],
  );
  const selections = [...selectedCellIds].flatMap((cellId) => {
    const geometry = geometryByCellId.get(cellId);
    return geometry === undefined
      ? []
      : [
          {
            kind: "path" as const,
            id: `selection-${cellId}`,
            d: geometry.path,
            role: "selection" as const,
          },
        ];
  });
  const annotations = deduplicateAnnotations(view).map((annotation) => ({
    kind: "path" as const,
    id: `annotation-${annotation.id}`,
    d: annotation.d,
    role: "annotation" as const,
    label: annotation.label,
  }));
  const clips = nodes.flatMap((node) =>
    node.content.flatMap((content) =>
      content.clip === undefined ? [] : [content.clip],
    ),
  );

  if (![minX, minY, maxX, maxY].every(Number.isFinite)) {
    minX = 0;
    minY = 0;
    maxX = 1;
    maxY = 1;
  }
  const width = Math.max(maxX - minX, 1);
  const height = Math.max(maxY - minY, 1);
  return {
    label: puzzle.metadata.title || "Sudoku puzzle",
    viewBox: `${minX} ${minY} ${width} ${height}`,
    width,
    height,
    nodes: [...nodes, ...groupBorders, ...selections, ...annotations],
    clips,
  };
}
