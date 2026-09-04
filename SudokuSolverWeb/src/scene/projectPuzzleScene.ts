import type { PuzzlePackageV1 } from "../domain/puzzle/types";
import type { CapabilityEntityResult } from "../solver/protocol";
import {
  MIN_NORMALIZED_FEATURE_SIZE,
  normalizeNativePoint,
  normalizePuzzleGeometry,
  normalizedCoordinate,
} from "./normalizeSceneGeometry";
import type { NormalizedCellGeometry } from "./normalizeSceneGeometry";
import { normalizeSvgPathData } from "./normalizeSvgPath";
import type {
  PuzzleScene,
  PuzzleSceneView,
  SceneCellNode,
  SceneClipRect,
  SceneGeometryIssue,
  ScenePathNode,
  SceneTextNode,
} from "./types";
import type { TrueCandidatePresentation } from "../domain/candidates/types";

export { MIN_NORMALIZED_FEATURE_SIZE } from "./normalizeSceneGeometry";

/** Comparison slack is 1/65536 of the smallest publicly preserved feature. */
const UNION_COMPARISON_EPSILON = MIN_NORMALIZED_FEATURE_SIZE / 65_536;
const COORDINATE_ULP_FACTOR = 16;

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

interface ResolvedAnnotationPath {
  key: string;
  d: string;
  annotationShape?: "point";
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
  geometryIssue?: SceneGeometryIssue;
}

function sceneCoordinate(value: number) {
  const integer = Math.round(value);
  const snapped = Math.abs(value - integer) <=
    Number.EPSILON * 32 * Math.max(1, Math.abs(value))
    ? integer
    : value;
  return Number(snapped.toPrecision(15));
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
  geometry: NormalizedCellGeometry,
  displayScale: number,
  metrics?: PuzzleSceneProjectionMetrics,
): ProjectedShape {
  if (geometry.geometryIssue !== undefined) {
    return {
      path: "",
      vertices: [],
      geometryIssue: geometry.geometryIssue,
    };
  }

  const vertices = geometry.vertices;
  const bounds = geometry.bounds!;
  const scalePoint = (point: Point) => ({
    x: sceneCoordinate(point.x * displayScale),
    y: sceneCoordinate(point.y * displayScale),
  });
  const sceneVertices = vertices.map(scalePoint);
  const path =
    geometry.kind === "rect"
      ? `M ${sceneVertices[0].x} ${sceneVertices[0].y} H ${sceneVertices[1].x} V ${sceneVertices[2].y} H ${sceneVertices[3].x} Z`
      : `M ${sceneVertices.map((point) => `${point.x} ${point.y}`).join(" L ")} Z`;
  const normalizedContentRegion =
    geometry.kind === "rect"
      ? {
          ...bounds,
          center: {
            x: (bounds.minX + bounds.maxX) / 2,
            y: (bounds.minY + bounds.maxY) / 2,
          },
        }
      : geometry.kind === "polygon"
        ? findPolygonContentRegion(
            geometry.contentVertices,
            bounds,
            metrics,
          )
        : undefined;
  if (normalizedContentRegion === undefined) {
    return {
      path,
      bounds: {
        minX: sceneCoordinate(bounds.minX * displayScale),
        minY: sceneCoordinate(bounds.minY * displayScale),
        maxX: sceneCoordinate(bounds.maxX * displayScale),
        maxY: sceneCoordinate(bounds.maxY * displayScale),
      },
      vertices: geometry.topologyVertices,
      geometryIssue: {
        code: "unusable-content-anchor",
        affects: "content",
        message: `${geometry.kind} has no usable content anchor.`,
      },
    };
  }

  return {
    path,
    bounds: {
      minX: sceneCoordinate(bounds.minX * displayScale),
      minY: sceneCoordinate(bounds.minY * displayScale),
      maxX: sceneCoordinate(bounds.maxX * displayScale),
      maxY: sceneCoordinate(bounds.maxY * displayScale),
    },
    contentRegion: {
      center: scalePoint(normalizedContentRegion.center),
      minX: sceneCoordinate(normalizedContentRegion.minX * displayScale),
      minY: sceneCoordinate(normalizedContentRegion.minY * displayScale),
      maxX: sceneCoordinate(normalizedContentRegion.maxX * displayScale),
      maxY: sceneCoordinate(normalizedContentRegion.maxY * displayScale),
    },
    vertices: geometry.topologyVertices,
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
  if (view.candidateMarks === undefined) {
    return projectCornerCandidateNodes(
      puzzle,
      cellId,
      cellIndex,
      region,
      view.candidates[cellId],
      undefined,
      view.candidatePresentation?.[cellId],
    );
  }

  const corner = projectCornerCandidateNodes(
    puzzle,
    cellId,
    cellIndex,
    region,
    view.candidateMarks.corner[cellId],
    "corner",
  );
  const centreIds = view.candidateMarks.centre[cellId];
  if (centreIds === undefined || centreIds.length === 0) {
    return corner;
  }
  const cell = puzzle.cells[cellId];
  const centreSet = new Set(centreIds);
  const labels = puzzle.domains[cell.domainId].values
    .filter((value) => centreSet.has(value.id))
    .map((value) => value.label);
  if (labels.length === 0) {
    return corner;
  }
  return [
    ...corner,
    {
      kind: "text",
      id: `candidate-centre-${cellId}`,
      x: region.center.x,
      y: region.center.y,
      text: labels.join(" "),
      role: "candidate",
      candidateKind: "centre",
    },
  ];
}

function projectCornerCandidateNodes(
  puzzle: PuzzlePackageV1,
  cellId: string,
  cellIndex: number,
  region: ContentRegion,
  candidateIds: readonly string[] | undefined,
  candidateKind?: "corner",
  candidatePresentation?: Readonly<Record<string, TrueCandidatePresentation>>,
): SceneTextNode[] {
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
        id:
          candidateKind === undefined
            ? `candidate-${cellId}-${value.id}`
            : `candidate-${candidateKind}-${cellId}-${value.id}`,
        x: region.minX + ((column + 0.5) * regionWidth) / columns,
        y: region.minY + ((row + 0.5) * regionHeight) / columns,
        text: value.label,
        valueId: value.id,
        role: "candidate" as const,
        candidateKind,
        candidateTone: candidatePresentation?.[value.id]?.tone,
        candidateLabel: candidatePresentation?.[value.id]?.label,
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
      valueId: value.id,
      role,
    },
  ];
}

function describeCellContent(
  content: readonly SceneTextNode[],
  fill: { label: string } | undefined,
) {
  const displayedValue = content.find((node) => node.role !== "candidate");
  if (displayedValue !== undefined) {
    const valueDescription = `${displayedValue.role} ${displayedValue.text}`;
    return fill === undefined
      ? valueDescription
      : `${valueDescription}; color ${fill.label}`;
  }
  const describeCandidates = (kind: SceneTextNode["candidateKind"]) => {
    const values = content
      .filter(
        (node) => node.role === "candidate" && node.candidateKind === kind,
      )
      .map((node) =>
        node.candidateLabel === undefined
          ? node.text
          : `${node.text} (${node.candidateLabel})`,
      );
    return values.length === 0 ? undefined : values.join(", ");
  };
  const legacy = describeCandidates(undefined);
  const corner = describeCandidates("corner");
  const centre = describeCandidates("centre");
  const descriptions = [
    legacy === undefined ? undefined : `candidates ${legacy}`,
    corner === undefined ? undefined : `corner candidates ${corner}`,
    centre === undefined ? undefined : `centre candidates ${centre}`,
    fill === undefined ? undefined : `color ${fill.label}`,
  ].filter((description): description is string => description !== undefined);
  return descriptions.length === 0 ? undefined : descriptions.join("; ");
}

function segmentLength(segment: Segment) {
  return Math.hypot(
    segment.to.x - segment.from.x,
    segment.to.y - segment.from.y,
  );
}

function baseSegmentTolerance() {
  return Math.max(
    UNION_COMPARISON_EPSILON,
    Number.EPSILON * COORDINATE_ULP_FACTOR,
  );
}

function isBelowMinimumBoundaryLength(length: number) {
  return length < MIN_NORMALIZED_FEATURE_SIZE;
}

function containsPointOnSnappedLine(segment: Segment, point: Point) {
  if (lineDistance(point, segment) > UNION_COMPARISON_EPSILON) {
    return false;
  }
  const ratio = projectRatio(point, segment);
  const ratioTolerance = UNION_COMPARISON_EPSILON / segmentLength(segment);
  return ratio >= -ratioTolerance && ratio <= 1 + ratioTolerance;
}

function segmentTolerance(
  segment: Segment,
  arrangement?: readonly Segment[],
) {
  let tolerance = baseSegmentTolerance();
  if (arrangement === undefined || segment.polygonIndex === undefined) {
    return tolerance;
  }

  const midpoint = pointAt(segment, 0.5);
  for (const candidate of arrangement) {
    if (
      candidate.polygonIndex !== segment.polygonIndex ||
      containsPointOnSnappedLine(candidate, midpoint)
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
  const tangentX = deltaX / length;
  const tangentY = deltaY / length;
  const fromDeltaX = point.x - segment.from.x;
  const fromDeltaY = point.y - segment.from.y;
  const toDeltaX = point.x - segment.to.x;
  const toDeltaY = point.y - segment.to.y;
  if (
    Math.hypot(fromDeltaX, fromDeltaY) <= Math.hypot(toDeltaX, toDeltaY)
  ) {
    return (fromDeltaX * tangentX + fromDeltaY * tangentY) / length;
  }
  return 1 + (toDeltaX * tangentX + toDeltaY * tangentY) / length;
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
) {
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
  const pieces = uniqueRatios.slice(0, -1).flatMap((start, index) => {
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
  return pieces;
}

function pointInUnionInSegmentFrame(
  segment: Segment,
  along: number,
  normal: number,
  polygons: readonly (readonly Point[])[],
) {
  const length = segmentLength(segment);
  const tangentX = (segment.to.x - segment.from.x) / length;
  const tangentY = (segment.to.y - segment.from.y) / length;
  const localPoint = { x: along, y: normal };
  return polygons.some((vertices) =>
    isPointInPolygon(
      localPoint,
      vertices.map((point) => {
        const deltaX = point.x - segment.from.x;
        const deltaY = point.y - segment.from.y;
        return {
          x: deltaX * tangentX + deltaY * tangentY,
          y: -deltaX * tangentY + deltaY * tangentX,
        };
      }),
    ),
  );
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


function isUnionBoundary(
  segment: Segment,
  polygons: readonly (readonly Point[])[],
  arrangement: readonly Segment[],
) {
  const length = segmentLength(segment);
  const probeDistance = localProbeDistance(segment, arrangement);
  const firstSide = pointInUnionInSegmentFrame(
    segment,
    length / 2,
    probeDistance,
    polygons,
  );
  const secondSide = pointInUnionInSegmentFrame(
    segment,
    length / 2,
    -probeDistance,
    polygons,
  );
  return firstSide !== secondSide;
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
  displayScale: number,
) {
  const uniqueCellIds = [...new Set(cellIds)];
  const omittedCellIds = uniqueCellIds.filter((cellId) => {
    const geometryIssue = geometryByCellId.get(cellId)?.geometryIssue;
    return (
      geometryIssue?.affects === "topology" ||
      geometryIssue?.affects === "topology-and-content"
    );
  });
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
  const geometryIssues: SceneGeometryIssue[] = [];
  if (omittedCellIds.length > 0) {
    geometryIssues.push({
      code: "member-geometry-omitted",
      affects: "topology",
      message: `Group border omitted renderer-invalid member geometry: ${omittedCellIds.join(", ")}.`,
    });
  }
  if (segments.length === 0) {
    return {
      d: "",
      geometryIssue: geometryIssues[0],
      geometryIssues,
    };
  }

  const atomicPieces = segments.flatMap((segment) =>
    splitSegment(segment, segments),
  );
  const boundaryPieces = atomicPieces
    .filter((piece) => {
      const length = segmentLength(piece);
      return Number.isFinite(length) && length > 0;
    })
    .filter((piece) => isUnionBoundary(piece, polygons, segments));
  const emittedBoundaryPieces = deduplicateSegments(
    boundaryPieces.filter(
      (piece) => !isBelowMinimumBoundaryLength(segmentLength(piece)),
    ),
    segments,
  );
  const d = emittedBoundaryPieces
    .map(
      (segment) =>
        `M ${sceneCoordinate(normalizedCoordinate(segment.from.x) * displayScale)} ${sceneCoordinate(normalizedCoordinate(segment.from.y) * displayScale)} L ${sceneCoordinate(normalizedCoordinate(segment.to.x) * displayScale)} ${sceneCoordinate(normalizedCoordinate(segment.to.y) * displayScale)}`,
    )
    .join(" ");
  return {
    d,
    geometryIssue: geometryIssues[0],
    geometryIssues,
  };
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
  const normalizedGeometry = normalizePuzzleGeometry(puzzle);
  const geometryByCellId = new Map<string, ProjectedShape>();
  const selectedCellIds = new Set(view.selectedCellIds);

  let nodes: SceneCellNode[] = Object.values(puzzle.cells).map(
    (cell, cellIndex) => {
      const normalizedCell = normalizedGeometry.cells.get(cell.id);
      const geometry =
        normalizedCell === undefined
          ? {
              path: "",
              vertices: [],
              geometryIssue: {
                code: "invalid-topology" as const,
                affects: "topology-and-content" as const,
                message: "cell geometry was not available for normalization.",
              },
            }
          : projectShape(
              normalizedCell,
              normalizedGeometry.displayScale,
              metrics,
            );
      geometryByCellId.set(cell.id, geometry);

      const capability = getCellCapability(cell.id, view);
      const solverParticipation = capability?.status ?? "unknown";
      const fill = view.cellFills?.[cell.id];
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
      const contentDescription = describeCellContent(content, fill);
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
        description: `Solver participation: ${humanizeParticipation(solverParticipation)}.${reason === undefined || reason === "" ? "" : ` ${reason}`}${geometry.geometryIssue === undefined ? "" : ` Presentation geometry: ${geometry.geometryIssue.message}`}`,
        selected: selectedCellIds.has(cell.id),
        solverParticipation,
        fill,
        geometryIssue: geometry.geometryIssue,
        content,
      };
    },
  );

  const groupPaths = new Map(Object.values(puzzle.groups).map((group) => [
    group.id,
    projectGroupBorder(
      group.cellIds,
      geometryByCellId,
      normalizedGeometry.displayScale,
    ),
  ]));
  const groupBorders = Object.values(puzzle.groups).flatMap((group) => {
    if (!group.roles.includes("region")) {
      return [];
    }
    const border = groupPaths.get(group.id)!;
    return [
      {
        kind: "path" as const,
        id: `group-border-${group.id}`,
        d: border.d,
        role: "grid" as const,
        geometryIssue: border.geometryIssue,
        geometryIssues: border.geometryIssues,
      },
    ];
  });
  const entityAnnotations = deduplicateAnnotations(view).filter(
    (annotation) => "entity" in annotation,
  );
  const emphasisPriority = { dim: 0, focus: 1, highlight: 2 } as const;
  const valueEmphasis = new Map<string, "focus" | "dim" | "highlight">();
  for (const annotation of entityAnnotations) {
    if (annotation.entity.kind !== "value") {
      continue;
    }
    const current = valueEmphasis.get(annotation.entity.id);
    if (
      current === undefined ||
      emphasisPriority[annotation.emphasis] > emphasisPriority[current]
    ) {
      valueEmphasis.set(annotation.entity.id, annotation.emphasis);
    }
  }
  if (valueEmphasis.size > 0) {
    const logicalCellIds = new Set([
      ...Object.keys(view.values),
      ...Object.keys(view.candidates),
    ]);
    nodes = nodes.map((node) =>
      logicalCellIds.has(node.cellId)
        ? {
            ...node,
            content: node.content.map((content) => ({
              ...content,
              annotationEmphasis:
                content.valueId === undefined
                  ? undefined
                  : valueEmphasis.get(content.valueId),
            })),
          }
        : node,
    );
  }
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
  const annotations: ScenePathNode[] = deduplicateAnnotations(view).flatMap(
    (annotation): ScenePathNode[] => {
      if ("entity" in annotation) {
        const bindingPaths = (
          kind: string,
          id: string,
        ): ResolvedAnnotationPath[] => {
          if (kind === "cell") {
            const d = geometryByCellId.get(id)?.path;
            return d === undefined || d === "" ? [] : [{ key: `cell-${id}`, d }];
          }
          if (kind === "group") {
            const d = groupPaths.get(id)?.d;
            return d === undefined || d === "" ? [] : [{ key: `group-${id}`, d }];
          }
          const frame = normalizedGeometry.nativeToSceneFrame;
          if (frame === undefined) {
            return [];
          }
          if (kind === "edge") {
            const edge = puzzle.edges[id];
            const from = edge === undefined ? undefined : puzzle.points[edge.fromPointId];
            const to = edge === undefined ? undefined : puzzle.points[edge.toPointId];
            if (from === undefined || to === undefined) {
              return [];
            }
            const sceneFrom = normalizeNativePoint(from, frame);
            const sceneTo = normalizeNativePoint(to, frame);
            return [{
              key: `edge-${id}`,
              d: `M ${sceneCoordinate(sceneFrom.x)} ${sceneCoordinate(sceneFrom.y)} L ${sceneCoordinate(sceneTo.x)} ${sceneCoordinate(sceneTo.y)}`,
            }];
          }
          if (kind === "path") {
            const path = puzzle.paths[id];
            const points = path?.pointIds.flatMap((pointId) => {
              const point = puzzle.points[pointId];
              return point === undefined ? [] : [normalizeNativePoint(point, frame)];
            }) ?? [];
            if (path === undefined || points.length < 2) {
              return [];
            }
            return [{
              key: `path-${id}`,
              d: `${points.map((point, index) => `${index === 0 ? "M" : "L"} ${sceneCoordinate(point.x)} ${sceneCoordinate(point.y)}`).join(" ")}${path.closed ? " Z" : ""}`,
            }];
          }
          if (kind === "point") {
            const point = puzzle.points[id];
            if (point === undefined) {
              return [];
            }
            const scenePoint = normalizeNativePoint(point, frame);
            const x = sceneCoordinate(scenePoint.x);
            const y = sceneCoordinate(scenePoint.y);
            return [{
              key: `point-${id}`,
              d: `M ${x} ${y} L ${x} ${y}`,
              annotationShape: "point" as const,
            }];
          }
          return [];
        };
        const paths =
          annotation.entity.kind === "constraint"
            ? (() => {
                const constraint = puzzle.constraints.find(
                  (candidate) => candidate.id === annotation.entity.id,
                );
                if (constraint === undefined) {
                  return [];
                }
                const seen = new Set<string>();
                return Object.values(constraint.bindings)
                  .flat()
                  .flatMap((binding) => bindingPaths(binding.kind, binding.id))
                  .filter((path) => {
                    if (seen.has(path.key)) {
                      return false;
                    }
                    seen.add(path.key);
                    return true;
                  });
              })()
            : bindingPaths(annotation.entity.kind, annotation.entity.id);
        return paths.map((path, index) => ({
          kind: "path" as const,
          id:
            annotation.entity.kind === "constraint"
              ? `annotation-${annotation.id}-binding-${path.key}`
              : index === 0
                ? `annotation-${annotation.id}`
                : `annotation-${annotation.id}-${path.key}`,
          d: path.d,
          role: "annotation" as const,
          label: annotation.label,
          annotationShape: path.annotationShape,
          annotationEmphasis: annotation.emphasis,
          geometryIssues: [],
        }));
      }
      const normalizedPath =
        normalizedGeometry.nativeToSceneFrame === undefined
          ? {
              error: "the scene normalization frame is unavailable.",
              code: "annotation-frame-unavailable" as const,
            }
          : {
              ...normalizeSvgPathData(
                annotation.d,
                normalizedGeometry.nativeToSceneFrame,
              ),
              code: "malformed-annotation-path" as const,
            };
      const geometryIssue =
        normalizedPath.error === undefined
          ? undefined
          : {
              code: normalizedPath.code,
              affects: "topology-and-content" as const,
              message: normalizedPath.error,
            };
      return [{
        kind: "path" as const,
        id: `annotation-${annotation.id}`,
        d: "d" in normalizedPath ? (normalizedPath.d ?? "") : "",
        role: "annotation" as const,
        label: annotation.label,
        geometryIssue,
        geometryIssues: geometryIssue === undefined ? [] : [geometryIssue],
      }];
    },
  );
  const clips = nodes.flatMap((node) =>
    node.content.flatMap((content) =>
      content.clip === undefined ? [] : [content.clip],
    ),
  );

  const width = sceneCoordinate(Math.max(normalizedGeometry.width, 1));
  const height = sceneCoordinate(Math.max(normalizedGeometry.height, 1));
  const groupGeometryDescriptions = groupBorders.flatMap((border) =>
    (border.geometryIssues ?? []).map(
      (geometryIssue) =>
        `Group ${border.id.slice("group-border-".length)}: ${geometryIssue.message}`,
    ),
  );
  const annotationGeometryDescriptions = annotations.flatMap((annotation) =>
    (annotation.geometryIssues ?? []).map(
      (geometryIssue) =>
        `Annotation ${annotation.id.slice("annotation-".length)}: ${geometryIssue.message}`,
    ),
  );
  const presentationDescriptions = [
    ...groupGeometryDescriptions,
    ...annotationGeometryDescriptions,
  ];
  return {
    label: puzzle.metadata.title || "Sudoku puzzle",
    description:
      presentationDescriptions.length === 0
        ? undefined
        : `Presentation geometry: ${presentationDescriptions.join(" ")}`,
    viewBox: `0 0 ${width} ${height}`,
    width,
    height,
    nodes: [...nodes, ...groupBorders, ...selections, ...annotations],
    clips,
  };
}
