import type { CellShape, PuzzlePackageV1 } from "../domain/puzzle/types";
import type { CapabilityEntityResult } from "../solver/protocol";
import type {
  PuzzleScene,
  PuzzleSceneView,
  SceneCellNode,
  SceneClipRect,
  SceneTextNode,
} from "./types";

const GEOMETRY_TOLERANCE_FACTOR = 1e-7;

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
  bounds: Bounds;
  contentRegion: ContentRegion;
  vertices: readonly Point[];
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

function findPolygonContentRegion(
  vertices: readonly Point[],
  bounds: Bounds,
): ContentRegion {
  const segments = polygonSegments(vertices);
  const width = bounds.maxX - bounds.minX;
  const height = bounds.maxY - bounds.minY;
  const samplesPerAxis = 24;
  let bestPoint: Point | undefined;
  let bestClearance = -1;

  for (let row = 0; row < samplesPerAxis; row += 1) {
    for (let column = 0; column < samplesPerAxis; column += 1) {
      const point = {
        x: bounds.minX + ((column + 0.5) * width) / samplesPerAxis,
        y: bounds.minY + ((row + 0.5) * height) / samplesPerAxis,
      };
      if (!isPointInPolygon(point, vertices)) {
        continue;
      }
      const clearance = clearanceAt(point, segments);
      if (clearance > bestClearance) {
        bestPoint = point;
        bestClearance = clearance;
      }
    }
  }

  if (bestPoint === undefined) {
    throw new Error("Polygon cells require a non-empty interior.");
  }

  let refinedPoint: Point = bestPoint;
  let stepX = width / samplesPerAxis;
  let stepY = height / samplesPerAxis;
  for (let refinement = 0; refinement < 7; refinement += 1) {
    const origin: Point = refinedPoint;
    stepX /= 2;
    stepY /= 2;
    for (let row = -2; row <= 2; row += 1) {
      for (let column = -2; column <= 2; column += 1) {
        const point: Point = {
          x: origin.x + column * stepX,
          y: origin.y + row * stepY,
        };
        if (!isPointInPolygon(point, vertices)) {
          continue;
        }
        const clearance = clearanceAt(point, segments);
        if (clearance > bestClearance) {
          refinedPoint = point;
          bestClearance = clearance;
        }
      }
    }
  }

  const halfExtent = (bestClearance * 0.9) / Math.sqrt(2);
  return {
    center: refinedPoint,
    minX: refinedPoint.x - halfExtent,
    minY: refinedPoint.y - halfExtent,
    maxX: refinedPoint.x + halfExtent,
    maxY: refinedPoint.y + halfExtent,
  };
}

function projectShape(shape: CellShape): ProjectedShape {
  if (shape.kind === "path") {
    throw new Error("Path cells require explicit bounds before scene projection.");
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
    throw new Error("Polygon cells require at least one point.");
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

  return {
    path:
      shape.kind === "rect"
        ? `M ${vertices[0].x} ${vertices[0].y} H ${vertices[1].x} V ${vertices[2].y} H ${vertices[3].x} Z`
        : `M ${vertices.map((point) => `${point.x} ${point.y}`).join(" L ")} Z`,
    bounds,
    contentRegion:
      shape.kind === "rect"
        ? { ...bounds, center }
        : findPolygonContentRegion(vertices, bounds),
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

function geometryTolerance(segments: readonly Segment[]) {
  const points = segments.flatMap((segment) => [segment.from, segment.to]);
  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  const geometrySpan = Math.max(
    1,
    Math.max(...xs) - Math.min(...xs),
    Math.max(...ys) - Math.min(...ys),
  );
  return geometrySpan * GEOMETRY_TOLERANCE_FACTOR;
}

function segmentLength(segment: Segment) {
  return Math.sqrt(pointDistanceSquared(segment.from, segment.to));
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

function segmentsAreCollinear(
  first: Segment,
  second: Segment,
  tolerance: number,
) {
  const firstX = first.to.x - first.from.x;
  const firstY = first.to.y - first.from.y;
  const secondX = second.to.x - second.from.x;
  const secondY = second.to.y - second.from.y;
  const cross = Math.abs(firstX * secondY - firstY * secondX);
  const scaledTolerance =
    tolerance * Math.max(segmentLength(first), segmentLength(second), 1);
  return (
    cross <= scaledTolerance &&
    lineDistance(second.from, first) <= tolerance &&
    lineDistance(second.to, first) <= tolerance
  );
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

function segmentCoversPoint(
  segment: Segment,
  point: Point,
  tolerance: number,
) {
  if (lineDistance(point, segment) > tolerance) {
    return false;
  }
  const ratio = projectRatio(point, segment);
  const ratioTolerance = tolerance / Math.max(segmentLength(segment), 1);
  return ratio >= -ratioTolerance && ratio <= 1 + ratioTolerance;
}

function splitSegment(
  segment: Segment,
  allSegments: readonly Segment[],
  tolerance: number,
): Segment[] {
  const ratios = [0, 1];
  for (const other of allSegments) {
    if (!segmentsAreCollinear(segment, other, tolerance)) {
      continue;
    }
    for (const point of [other.from, other.to]) {
      const ratio = projectRatio(point, segment);
      if (ratio > 0 && ratio < 1) {
        ratios.push(ratio);
      }
    }
  }

  ratios.sort((first, second) => first - second);
  const ratioTolerance = tolerance / Math.max(segmentLength(segment), 1);
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

function projectGroupBorder(
  cellIds: readonly string[],
  geometryByCellId: ReadonlyMap<string, ProjectedShape>,
) {
  const uniqueCellIds = [...new Set(cellIds)];
  const segments = uniqueCellIds.flatMap((cellId) => {
    const vertices = geometryByCellId.get(cellId)?.vertices;
    return vertices === undefined ? [] : polygonSegments(vertices);
  });
  if (segments.length === 0) {
    return "";
  }

  const tolerance = geometryTolerance(segments);
  return segments
    .flatMap((segment) => splitSegment(segment, segments, tolerance))
    .filter((piece) => {
      const midpoint = pointAt(piece, 0.5);
      const coverage = segments.filter((segment) =>
        segmentCoversPoint(segment, midpoint, tolerance),
      ).length;
      return coverage % 2 === 1;
    })
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
      minX = Math.min(minX, geometry.bounds.minX);
      minY = Math.min(minY, geometry.bounds.minY);
      maxX = Math.max(maxX, geometry.bounds.maxX);
      maxY = Math.max(maxY, geometry.bounds.maxY);

      const capability = getCellCapability(cell.id, view);
      const solverParticipation = capability?.status ?? "unknown";
      const content = projectCellContent(
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
        description: `Solver participation: ${humanizeParticipation(solverParticipation)}.${reason === undefined || reason === "" ? "" : ` ${reason}`}`,
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

  const width = maxX - minX;
  const height = maxY - minY;
  return {
    label: puzzle.metadata.title || "Sudoku puzzle",
    viewBox: `${minX} ${minY} ${width} ${height}`,
    width,
    height,
    nodes: [...nodes, ...groupBorders, ...selections, ...annotations],
    clips,
  };
}
