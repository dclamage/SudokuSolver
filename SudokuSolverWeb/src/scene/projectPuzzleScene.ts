import type {
  CellShape,
  PuzzlePackageV1,
} from "../domain/puzzle/types";
import type {
  PuzzleScene,
  PuzzleSceneView,
  SceneCellNode,
  SceneTextNode,
  SolverParticipation,
} from "./types";

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

interface ProjectedShape {
  path: string;
  bounds: Bounds;
  vertices?: readonly Point[];
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

  return {
    path:
      shape.kind === "rect"
        ? `M ${vertices[0].x} ${vertices[0].y} H ${vertices[1].x} V ${vertices[2].y} H ${vertices[3].x} Z`
        : `M ${vertices.map((point) => `${point.x} ${point.y}`).join(" L ")} Z`,
    bounds,
    vertices,
  };
}

function getCellParticipation(
  cellId: string,
  view: PuzzleSceneView,
): SolverParticipation {
  const capability = view.entityCapabilities[`cell:${cellId}`];
  return capability?.entityKind === "cell" && capability.entityId === cellId
    ? capability.status
    : "unknown";
}

function projectCandidateNodes(
  puzzle: PuzzlePackageV1,
  cellId: string,
  bounds: Bounds,
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
  const cellWidth = bounds.maxX - bounds.minX;
  const cellHeight = bounds.maxY - bounds.minY;

  return domain.values.flatMap((value, index) => {
    if (!candidateSet.has(value.id)) {
      return [];
    }

    const column = index % columns;
    const row = Math.floor(index / columns);
    return [
      {
        kind: "text" as const,
        id: `candidate-${cellId}-${value.id}`,
        x: bounds.minX + ((column + 0.5) * cellWidth) / columns,
        y: bounds.minY + ((row + 0.5) * cellHeight) / columns,
        text: value.label,
        role: "candidate" as const,
      },
    ];
  });
}

function projectCellContent(
  puzzle: PuzzlePackageV1,
  cellId: string,
  bounds: Bounds,
  view: PuzzleSceneView,
): SceneTextNode[] {
  const givenValueId = puzzle.givens[cellId];
  const projectedValueId = view.values[cellId];
  const valueId = givenValueId ?? projectedValueId;
  if (valueId === undefined) {
    return projectCandidateNodes(puzzle, cellId, bounds, view);
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
      x: (bounds.minX + bounds.maxX) / 2,
      y: (bounds.minY + bounds.maxY) / 2,
      text: value.label,
      role,
    },
  ];
}

function edgeKey(from: Point, to: Point) {
  const fromKey = `${from.x},${from.y}`;
  const toKey = `${to.x},${to.y}`;
  return fromKey < toKey ? `${fromKey}|${toKey}` : `${toKey}|${fromKey}`;
}

function projectGroupBorder(
  cellIds: readonly string[],
  geometryByCellId: ReadonlyMap<string, ProjectedShape>,
) {
  const edges = new Map<
    string,
    { from: Point; to: Point; occurrences: number }
  >();

  for (const cellId of cellIds) {
    const vertices = geometryByCellId.get(cellId)?.vertices;
    if (vertices === undefined) {
      continue;
    }

    for (let index = 0; index < vertices.length; index += 1) {
      const from = vertices[index];
      const to = vertices[(index + 1) % vertices.length];
      const key = edgeKey(from, to);
      const existing = edges.get(key);
      if (existing === undefined) {
        edges.set(key, { from, to, occurrences: 1 });
      } else {
        existing.occurrences += 1;
      }
    }
  }

  return [...edges.values()]
    .filter((edge) => edge.occurrences === 1)
    .map((edge) => `M ${edge.from.x} ${edge.from.y} L ${edge.to.x} ${edge.to.y}`)
    .join(" ");
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

  const nodes: SceneCellNode[] = Object.values(puzzle.cells).map((cell) => {
    const geometry = projectShape(cell.shape);
    geometryByCellId.set(cell.id, geometry);
    minX = Math.min(minX, geometry.bounds.minX);
    minY = Math.min(minY, geometry.bounds.minY);
    maxX = Math.max(maxX, geometry.bounds.maxX);
    maxY = Math.max(maxY, geometry.bounds.maxY);

    return {
      kind: "cell",
      id: `cell-${cell.id}`,
      cellId: cell.id,
      path: geometry.path,
      label: cell.label ?? `Cell ${cell.id}`,
      solverParticipation: getCellParticipation(cell.id, view),
      content: projectCellContent(puzzle, cell.id, geometry.bounds, view),
    };
  });

  const groupBorders = Object.values(puzzle.groups).flatMap((group) => {
    if (!group.roles.includes("region")) {
      return [];
    }

    return [
      {
        kind: "path" as const,
        id: `group-border-${group.id}`,
        d: projectGroupBorder(group.cellIds, geometryByCellId),
        role: "grid" as const,
      },
    ];
  });
  const selections = view.selectedCellIds.flatMap((cellId) => {
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
  const annotations = view.annotations.map((annotation) => ({
    kind: "path" as const,
    id: `annotation-${annotation.id}`,
    d: annotation.d,
    role: "annotation" as const,
    label: annotation.label,
  }));

  const width = maxX - minX;
  const height = maxY - minY;
  return {
    label: puzzle.metadata.title || "Sudoku puzzle",
    viewBox: `${minX} ${minY} ${width} ${height}`,
    width,
    height,
    nodes: [...nodes, ...groupBorders, ...selections, ...annotations],
  };
}
