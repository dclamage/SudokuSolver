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

interface InteriorSearchCell extends Point {
  halfSize: number;
  distance: number;
  maximumDistance: number;
}

function pointDistanceSquared(first: Point, second: Point) {
  const deltaX = first.x - second.x;
  const deltaY = first.y - second.y;
  return deltaX * deltaX + deltaY * deltaY;
}

function pointToSegmentDistance(point: Point, segment: Segment) {
  const segmentX = segment.to.x - segment.from.x;
  const segmentY = segment.to.y - segment.from.y;
  const lengthSquared = segmentX * segmentX + segmentY * segmentY;
  if (lengthSquared === 0) {
    return Math.sqrt(pointDistanceSquared(point, segment.from));
  }

  const projection = Math.max(
    0,
    Math.min(
      1,
      ((point.x - segment.from.x) * segmentX +
        (point.y - segment.from.y) * segmentY) /
        lengthSquared,
    ),
  );
  return Math.sqrt(
    pointDistanceSquared(point, {
      x: segment.from.x + projection * segmentX,
      y: segment.from.y + projection * segmentY,
    }),
  );
}

function polygonSegments(vertices: readonly Point[]): Segment[] {
  return vertices.map((from, index) => ({
    from,
    to: vertices[(index + 1) % vertices.length],
  }));
}

function isPointInPolygon(point: Point, vertices: readonly Point[]) {
  let inside = false;
  for (
    let index = 0, previousIndex = vertices.length - 1;
    index < vertices.length;
    previousIndex = index, index += 1
  ) {
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

function clearanceAt(point: Point, segments: readonly Segment[]) {
  return Math.min(
    ...segments.map((segment) => pointToSegmentDistance(point, segment)),
  );
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
) {
  const distance = clearanceAt(point, segments);
  return isPointInPolygon(point, vertices) ? distance : -distance;
}

function createInteriorSearchCell(
  x: number,
  y: number,
  halfSize: number,
  vertices: readonly Point[],
  segments: readonly Segment[],
): InteriorSearchCell {
  const distance = signedDistanceToPolygon({ x, y }, vertices, segments);
  return {
    x,
    y,
    halfSize,
    distance,
    maximumDistance: distance + halfSize * Math.SQRT2,
  };
}

function pushSearchCell(
  heap: InteriorSearchCell[],
  cell: InteriorSearchCell,
) {
  heap.push(cell);
  let index = heap.length - 1;
  while (index > 0) {
    const parentIndex = Math.floor((index - 1) / 2);
    if (heap[parentIndex].maximumDistance >= cell.maximumDistance) {
      break;
    }
    heap[index] = heap[parentIndex];
    index = parentIndex;
  }
  heap[index] = cell;
}

function popSearchCell(heap: InteriorSearchCell[]) {
  const first = heap[0];
  const last = heap.pop();
  if (last === undefined || heap.length === 0) {
    return first;
  }

  let index = 0;
  while (true) {
    const leftIndex = index * 2 + 1;
    const rightIndex = leftIndex + 1;
    if (leftIndex >= heap.length) {
      break;
    }
    const childIndex =
      rightIndex < heap.length &&
      heap[rightIndex].maximumDistance > heap[leftIndex].maximumDistance
        ? rightIndex
        : leftIndex;
    if (heap[childIndex].maximumDistance <= last.maximumDistance) {
      break;
    }
    heap[index] = heap[childIndex];
    index = childIndex;
  }
  heap[index] = last;
  return first;
}

function polygonCentroid(vertices: readonly Point[], area: number): Point {
  const origin = vertices[0];
  let x = 0;
  let y = 0;
  for (let index = 0; index < vertices.length; index += 1) {
    const point = vertices[index];
    const next = vertices[(index + 1) % vertices.length];
    const pointX = point.x - origin.x;
    const pointY = point.y - origin.y;
    const nextX = next.x - origin.x;
    const nextY = next.y - origin.y;
    const relativeCross = pointX * nextY - nextX * pointY;
    x += (pointX + nextX) * relativeCross;
    y += (pointY + nextY) * relativeCross;
  }
  const scale = 1 / (6 * area);
  return { x: origin.x + x * scale, y: origin.y + y * scale };
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
  bounds: Bounds,
) {
  const orientation = area > 0 ? 1 : -1;
  const areaTolerance = Math.max(
    coordinateResolution(bounds) ** 2,
    Math.abs(area) * Number.EPSILON * COORDINATE_ULP_FACTOR,
  );
  const remaining = vertices.map((_, index) => index);
  const triangles: [Point, Point, Point][] = [];
  while (remaining.length > 3) {
    let earIndex = -1;
    for (let index = 0; index < remaining.length; index += 1) {
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
  const firstWeight = Math.sqrt(pointDistanceSquared(second, third));
  const secondWeight = Math.sqrt(pointDistanceSquared(first, third));
  const thirdWeight = Math.sqrt(pointDistanceSquared(first, second));
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

function coordinateResolution(bounds: Bounds) {
  const magnitude = Math.max(
    1,
    Math.abs(bounds.minX),
    Math.abs(bounds.minY),
    Math.abs(bounds.maxX),
    Math.abs(bounds.maxY),
  );
  return magnitude * Number.EPSILON * COORDINATE_ULP_FACTOR;
}

function findPolygonContentRegion(
  vertices: readonly Point[],
  bounds: Bounds,
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

  const initialSize = Math.min(width, height);
  const heap: InteriorSearchCell[] = [];
  for (let x = bounds.minX; x < bounds.maxX; x += initialSize) {
    for (let y = bounds.minY; y < bounds.maxY; y += initialSize) {
      pushSearchCell(
        heap,
        createInteriorSearchCell(
          x + initialSize / 2,
          y + initialSize / 2,
          initialSize / 2,
          vertices,
          segments,
        ),
      );
    }
  }

  const centroid = polygonCentroid(vertices, area);
  let bestCell = createInteriorSearchCell(
    centroid.x,
    centroid.y,
    0,
    vertices,
    segments,
  );
  const boundsCenter = createInteriorSearchCell(
    (bounds.minX + bounds.maxX) / 2,
    (bounds.minY + bounds.maxY) / 2,
    0,
    vertices,
    segments,
  );
  if (boundsCenter.distance > bestCell.distance) {
    bestCell = boundsCenter;
  }
  const triangles = triangulatePolygon(vertices, area, bounds);
  if (triangles.length === 0) {
    return undefined;
  }
  for (const triangle of triangles) {
    const incenter = triangleIncenter(triangle);
    const incenterCell = createInteriorSearchCell(
      incenter.x,
      incenter.y,
      0,
      vertices,
      segments,
    );
    if (incenterCell.distance > bestCell.distance) {
      bestCell = incenterCell;
    }
  }

  const precision = Math.max(
    initialSize * 1e-6,
    coordinateResolution(bounds),
  );
  if (bestCell.distance > 0 && bestCell.distance <= precision) {
    const halfExtent = (bestCell.distance * 0.9) / Math.sqrt(2);
    return {
      center: { x: bestCell.x, y: bestCell.y },
      minX: bestCell.x - halfExtent,
      minY: bestCell.y - halfExtent,
      maxX: bestCell.x + halfExtent,
      maxY: bestCell.y + halfExtent,
    };
  }
  while (heap.length > 0) {
    const cell = popSearchCell(heap);
    if (cell.distance > bestCell.distance) {
      bestCell = cell;
    }
    if (cell.maximumDistance - bestCell.distance <= precision) {
      continue;
    }

    const halfSize = cell.halfSize / 2;
    for (const offsetX of [-halfSize, halfSize]) {
      for (const offsetY of [-halfSize, halfSize]) {
        pushSearchCell(
          heap,
          createInteriorSearchCell(
            cell.x + offsetX,
            cell.y + offsetY,
            halfSize,
            vertices,
            segments,
          ),
        );
      }
    }
  }

  if (bestCell.distance <= coordinateResolution(bounds)) {
    return undefined;
  }

  const halfExtent = (bestCell.distance * 0.9) / Math.sqrt(2);
  return {
    center: { x: bestCell.x, y: bestCell.y },
    minX: bestCell.x - halfExtent,
    minY: bestCell.y - halfExtent,
    maxX: bestCell.x + halfExtent,
    maxY: bestCell.y + halfExtent,
  };
}

function projectShape(shape: CellShape): ProjectedShape {
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
        ? findPolygonContentRegion(vertices, bounds)
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
  return Math.sqrt(pointDistanceSquared(segment.from, segment.to));
}

function segmentTolerance(segment: Segment) {
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

function pointAt(segment: Segment, ratio: number): Point {
  return {
    x: segment.from.x + (segment.to.x - segment.from.x) * ratio,
    y: segment.from.y + (segment.to.y - segment.from.y) * ratio,
  };
}

function lineDistance(point: Point, segment: Segment) {
  const length = segmentLength(segment);
  if (length === 0) {
    return Math.sqrt(pointDistanceSquared(point, segment.from));
  }
  return (
    Math.abs(
      (segment.to.x - segment.from.x) * (segment.from.y - point.y) -
        (segment.from.x - point.x) * (segment.to.y - segment.from.y),
    ) / length
  );
}

function cross(deltaAX: number, deltaAY: number, deltaBX: number, deltaBY: number) {
  return deltaAX * deltaBY - deltaAY * deltaBX;
}

function projectRatio(point: Point, segment: Segment) {
  const deltaX = segment.to.x - segment.from.x;
  const deltaY = segment.to.y - segment.from.y;
  return (
    ((point.x - segment.from.x) * deltaX +
      (point.y - segment.from.y) * deltaY) /
    (deltaX * deltaX + deltaY * deltaY)
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
  const tolerance = segmentTolerance(segment);
  const ratioTolerance = tolerance / length;
  for (const other of allSegments) {
    if (other === segment || segmentLength(other) === 0) {
      continue;
    }

    const otherDeltaX = other.to.x - other.from.x;
    const otherDeltaY = other.to.y - other.from.y;
    const otherLength = segmentLength(other);
    const pairTolerance = Math.max(tolerance, segmentTolerance(other));
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
    return end - start <= ratioTolerance
      ? []
      : [{ from: pointAt(segment, start), to: pointAt(segment, end) }];
  });
}

function pointInUnion(point: Point, polygons: readonly (readonly Point[])[]) {
  return polygons.some((vertices) => isPointInPolygon(point, vertices));
}

function isUnionBoundary(
  segment: Segment,
  polygons: readonly (readonly Point[])[],
) {
  const length = segmentLength(segment);
  const midpoint = pointAt(segment, 0.5);
  const probeDistance = segmentTolerance(segment) * 4;
  const normalX = -(segment.to.y - segment.from.y) / length;
  const normalY = (segment.to.x - segment.from.x) / length;
  const firstSide = pointInUnion(
    {
      x: midpoint.x + normalX * probeDistance,
      y: midpoint.y + normalY * probeDistance,
    },
    polygons,
  );
  const secondSide = pointInUnion(
    {
      x: midpoint.x - normalX * probeDistance,
      y: midpoint.y - normalY * probeDistance,
    },
    polygons,
  );
  return firstSide !== secondSide;
}

function segmentsAreEquivalent(first: Segment, second: Segment) {
  const tolerance = Math.max(
    segmentTolerance(first),
    segmentTolerance(second),
  );
  const toleranceSquared = tolerance * tolerance;
  return (
    (pointDistanceSquared(first.from, second.from) <= toleranceSquared &&
      pointDistanceSquared(first.to, second.to) <= toleranceSquared) ||
    (pointDistanceSquared(first.from, second.to) <= toleranceSquared &&
      pointDistanceSquared(first.to, second.from) <= toleranceSquared)
  );
}

function deduplicateSegments(segments: readonly Segment[]) {
  const uniqueSegments: Segment[] = [];
  for (const segment of segments) {
    if (
      !uniqueSegments.some((existing) =>
        segmentsAreEquivalent(existing, segment),
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
    .flatMap((vertices) => polygonSegments(vertices))
    .filter((segment) => segmentLength(segment) > segmentTolerance(segment));
  if (segments.length === 0) {
    return "";
  }

  return deduplicateSegments(
    segments
      .flatMap((segment) => splitSegment(segment, segments))
      .filter((piece) => isUnionBoundary(piece, polygons)),
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
): PuzzleScene {
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  const geometryByCellId = new Map<string, ProjectedShape>();
  const selectedCellIds = new Set(view.selectedCellIds);

  const nodes: SceneCellNode[] = Object.values(puzzle.cells).map(
    (cell, cellIndex) => {
      const geometry = projectShape(cell.shape);
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
