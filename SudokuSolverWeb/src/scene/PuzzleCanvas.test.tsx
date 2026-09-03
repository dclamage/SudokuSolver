import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { renderToString } from "react-dom/server";
import { afterEach, describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../domain/puzzle/createStarterPuzzle";
import { PuzzleCanvas } from "./PuzzleCanvas";
import { projectPuzzleScene } from "./projectPuzzleScene";
import type { PuzzleSceneView } from "./types";

interface TestPoint {
  x: number;
  y: number;
}

function readTextPoint(element: HTMLElement | SVGElement): TestPoint {
  return {
    x: Number(element.getAttribute("x")),
    y: Number(element.getAttribute("y")),
  };
}

function isPointInPolygon(point: TestPoint, polygon: readonly TestPoint[]) {
  let inside = false;
  for (
    let index = 0, previousIndex = polygon.length - 1;
    index < polygon.length;
    previousIndex = index, index += 1
  ) {
    const current = polygon[index];
    const previous = polygon[previousIndex];
    const crossesRay =
      current.y > point.y !== previous.y > point.y &&
      point.x <
        ((previous.x - current.x) * (point.y - current.y)) /
          (previous.y - current.y) +
          current.x;
    if (crossesRay) {
      inside = !inside;
    }
  }
  return inside;
}

function readLineSegments(path: Element) {
  const matches = [
    ...(path.getAttribute("d") ?? "").matchAll(
      /M ([\d.eE+-]+) ([\d.eE+-]+) L ([\d.eE+-]+) ([\d.eE+-]+)/g,
    ),
  ];
  return matches.map((match) => ({
    from: { x: Number(match[1]), y: Number(match[2]) },
    to: { x: Number(match[3]), y: Number(match[4]) },
  }));
}

function segmentLength(segment: { from: TestPoint; to: TestPoint }) {
  return Math.hypot(
    segment.to.x - segment.from.x,
    segment.to.y - segment.from.y,
  );
}

function totalSegmentLength(
  segments: readonly { from: TestPoint; to: TestPoint }[],
) {
  return segments.reduce((total, segment) => total + segmentLength(segment), 0);
}

function applySvgMatrix(transform: string, point: TestPoint): TestPoint {
  const match = transform.match(
    /^matrix\(([-\d.eE+]+) ([-\d.eE+]+) ([-\d.eE+]+) ([-\d.eE+]+) ([-\d.eE+]+) ([-\d.eE+]+)\)$/,
  );
  expect(match).not.toBeNull();
  const [, a, b, c, d, e, f] = match!.map(Number);
  return {
    x: a * point.x + c * point.y + e,
    y: b * point.x + d * point.y + f,
  };
}

const puzzle = createStarterPuzzle(() => "scene");
const emptySceneView: PuzzleSceneView = {
  values: {},
  candidates: {},
  selectedCellIds: [],
  annotations: [],
  entityCapabilities: {
    "cell:aux-1": {
      entityKind: "cell",
      entityId: "aux-1",
      status: "visualOnly",
      reason: "Not included in projection main-latin-square.",
    },
  },
};

afterEach(cleanup);

describe("PuzzleCanvas", () => {
  it("normalizes scene geometry invariantly across translation and uniform scale", () => {
    const sourcePuzzle = structuredClone(puzzle);
    sourcePuzzle.cells.r1c1.shape = {
      kind: "polygon",
      points: [
        { x: 0, y: 0 },
        { x: 1, y: 0 },
        { x: 1, y: 0.25 },
        { x: 0.25, y: 0.25 },
        { x: 0.25, y: 1 },
        { x: 0, y: 1 },
      ],
    };
    sourcePuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 1,
      y: 0,
      width: 2 ** -16,
      height: 1,
    };
    sourcePuzzle.cells.r1c3.shape = {
      kind: "rect",
      x: 0.5,
      y: 0.5,
      width: 1,
      height: 1,
    };
    sourcePuzzle.groups["normalized-overlap"] = {
      id: "normalized-overlap",
      roles: ["region"],
      cellIds: ["r1c1", "r1c3"],
    };

    const transformedPuzzle = structuredClone(sourcePuzzle);
    for (const cell of Object.values(transformedPuzzle.cells)) {
      if (cell.shape.kind === "rect") {
        cell.shape.x = cell.shape.x * 8 + 1_048_576;
        cell.shape.y = cell.shape.y * 8 - 2_097_152;
        cell.shape.width *= 8;
        cell.shape.height *= 8;
      } else if (cell.shape.kind === "polygon") {
        cell.shape.points = cell.shape.points.map((point) => ({
          x: point.x * 8 + 1_048_576,
          y: point.y * 8 - 2_097_152,
        }));
      }
    }

    const view = {
      ...emptySceneView,
      values: { r1c1: "5" },
      candidates: { r1c2: ["1", "4", "9"] },
    };
    const snapshot = (scene: ReturnType<typeof projectPuzzleScene>) => ({
      viewBox: scene.viewBox,
      width: scene.width,
      height: scene.height,
      nodes: scene.nodes.map((node) =>
        node.kind === "cell"
          ? {
              id: node.id,
              path: node.path,
              content: node.content.map(({ x, y }) => ({ x, y })),
            }
          : { id: node.id, path: node.d },
      ),
    });

    expect(snapshot(projectPuzzleScene(transformedPuzzle, view))).toEqual(
      snapshot(projectPuzzleScene(sourcePuzzle, view)),
    );
  });

  it("exports the practical minimum normalized feature threshold", async () => {
    const projectionModule = (await import("./projectPuzzleScene")) as Record<
      string,
      unknown
    >;
    expect(projectionModule.MIN_NORMALIZED_FEATURE_SIZE).toBe(1e-9);
  });

  it("keeps large-coordinate thin geometry above the normalized threshold", () => {
    const thinPuzzle = structuredClone(puzzle);
    for (const cell of Object.values(thinPuzzle.cells)) {
      if (cell.shape.kind === "rect") {
        cell.shape.x += 1_000_000_000;
      } else if (cell.shape.kind === "polygon") {
        cell.shape.points = cell.shape.points.map((point) => ({
          x: point.x + 1_000_000_000,
          y: point.y,
        }));
      }
    }
    thinPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 1_000_000_000,
      y: 0,
      width: 0.000001,
      height: 1,
    };
    thinPuzzle.groups["normalized-thin"] = {
      id: "normalized-thin",
      roles: ["region"],
      cellIds: ["r1c1"],
    };

    const scene = projectPuzzleScene(thinPuzzle, emptySceneView);
    expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: expect.stringMatching(/^M .* Z$/),
      geometryIssue: undefined,
    });
    expect(
      scene.nodes.find((node) => node.id === "group-border-normalized-thin"),
    ).toMatchObject({ d: expect.stringMatching(/^M /) });
    expect(JSON.stringify(scene)).not.toMatch(/NaN|Infinity/);
  });

  it("isolates below-threshold geometry without corrupting an ordinary group member", () => {
    const mixedPuzzle = structuredClone(puzzle);
    mixedPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 0,
      y: 0,
      width: Number.MIN_VALUE,
      height: 1,
    };
    mixedPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 100,
      y: 0,
      width: 1,
      height: 1,
    };
    mixedPuzzle.groups["mixed-scale"] = {
      id: "mixed-scale",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    const scene = projectPuzzleScene(mixedPuzzle, emptySceneView);
    expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: "",
      geometryIssue: {
        code: "below-minimum-feature",
        affects: "topology-and-content",
      },
    });
    expect(scene.nodes.find((node) => node.id === "cell-r1c2")).toMatchObject({
      geometryIssue: undefined,
    });
    expect(
      scene.nodes.find((node) => node.id === "group-border-mixed-scale"),
    ).toMatchObject({
      d: expect.stringMatching(/^M /),
      geometryIssue: {
        code: "member-geometry-omitted",
        affects: "topology",
      },
    });
    expect(JSON.stringify(scene)).not.toMatch(/NaN|Infinity/);
  });

  it("keeps topology and solver capability when only the content anchor is unusable", () => {
    const duplicateClosurePuzzle = structuredClone(puzzle);
    duplicateClosurePuzzle.cells.r1c1.shape = {
      kind: "polygon",
      points: [
        { x: 0, y: 0 },
        { x: 1, y: 0 },
        { x: 1, y: 1 },
        { x: 0, y: 1 },
        { x: 0, y: 0 },
      ],
    };
    duplicateClosurePuzzle.groups["anchor-independent"] = {
      id: "anchor-independent",
      roles: ["region"],
      cellIds: ["r1c1"],
    };
    const view: PuzzleSceneView = {
      ...emptySceneView,
      values: { r1c1: "5" },
      entityCapabilities: {
        ...emptySceneView.entityCapabilities,
        "cell:r1c1": {
          entityKind: "cell",
          entityId: "r1c1",
          status: "fullyVerified",
          reason: "Solver geometry remains authoritative.",
        },
      },
    };

    const scene = projectPuzzleScene(duplicateClosurePuzzle, view);
    expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      solverParticipation: "fullyVerified",
      content: [],
      geometryIssue: {
        code: "unusable-content-anchor",
        affects: "content",
      },
      description: expect.stringContaining(
        "Presentation geometry: polygon has no usable content anchor.",
      ),
    });
    expect(
      scene.nodes.find(
        (node) => node.id === "group-border-anchor-independent",
      ),
    ).toMatchObject({ d: expect.stringMatching(/^M /) });
  });

  it("excludes invalid far geometry from global bounds and normalizes finite extremes safely", () => {
    const projectWithInvalidAuxiliary = (points: TestPoint[]) => {
      const invalidPuzzle = structuredClone(puzzle);
      invalidPuzzle.cells["aux-1"].shape = { kind: "polygon", points };
      invalidPuzzle.groups["invalid-far-member"] = {
        id: "invalid-far-member",
        roles: ["region"],
        cellIds: ["r1c1", "aux-1"],
      };
      return projectPuzzleScene(invalidPuzzle, emptySceneView);
    };
    const near = projectWithInvalidAuxiliary([
      { x: 20, y: 0 },
      { x: 20, y: 1 },
      { x: 20, y: 2 },
    ]);
    const far = projectWithInvalidAuxiliary([
      { x: 1e200, y: 0 },
      { x: 1e200, y: 1e192 },
      { x: 1e200, y: 2e192 },
    ]);
    const ordinarySnapshot = (scene: ReturnType<typeof projectPuzzleScene>) => ({
      viewBox: scene.viewBox,
      cell: scene.nodes.find((node) => node.id === "cell-r1c1"),
      border: scene.nodes.find(
        (node) => node.id === "group-border-invalid-far-member",
      ),
    });

    expect(ordinarySnapshot(far)).toEqual(ordinarySnapshot(near));
    expect(far.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: expect.stringMatching(/^M /),
      geometryIssue: undefined,
    });
    expect(far.nodes.find((node) => node.id === "cell-aux-1")).toMatchObject({
      path: "",
      geometryIssue: { code: "invalid-topology" },
    });

    const extremePuzzle = structuredClone(puzzle);
    extremePuzzle.cells = {
      r1c1: {
        ...extremePuzzle.cells.r1c1,
        shape: {
          kind: "polygon",
          points: [
            { x: -1e308, y: -1e308 },
            { x: 1e308, y: -1e308 },
            { x: 1e308, y: 1e308 },
            { x: -1e308, y: 1e308 },
          ],
        },
      },
    };
    extremePuzzle.groups = {
      extreme: { id: "extreme", roles: ["region"], cellIds: ["r1c1"] },
    };
    const extremeScene = projectPuzzleScene(extremePuzzle, emptySceneView);
    expect(extremeScene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: expect.stringMatching(/^M /),
      geometryIssue: undefined,
    });
    expect(JSON.stringify(extremeScene)).not.toMatch(/NaN|Infinity/);
  });

  it("applies the scene affine to arbitrary native annotation paths", () => {
    const sourcePuzzle = structuredClone(puzzle);
    const transformedPuzzle = structuredClone(puzzle);
    for (const cell of Object.values(transformedPuzzle.cells)) {
      if (cell.shape.kind === "rect") {
        cell.shape.x = cell.shape.x * 8 + 1_048_576;
        cell.shape.y = cell.shape.y * 8 - 2_097_152;
        cell.shape.width *= 8;
        cell.shape.height *= 8;
      } else if (cell.shape.kind === "polygon") {
        cell.shape.points = cell.shape.points.map((point) => ({
          x: point.x * 8 + 1_048_576,
          y: point.y * 8 - 2_097_152,
        }));
      }
    }
    const sourceView = {
      ...emptySceneView,
      annotations: [
        { id: "native", d: "M 0.5 0.5 C 2 3 8 1 10.5 4.5", label: "Native" },
      ],
    };
    const transformedView = {
      ...emptySceneView,
      annotations: [
        {
          id: "native",
          d: "M 1048580 -2097148 C 1048592 -2097128 1048640 -2097144 1048660 -2097116",
          label: "Native",
        },
      ],
    };
    const sourceAnnotation = projectPuzzleScene(
      sourcePuzzle,
      sourceView,
    ).nodes.find((node) => node.id === "annotation-native");
    const transformedAnnotation = projectPuzzleScene(
      transformedPuzzle,
      transformedView,
    ).nodes.find((node) => node.id === "annotation-native");

    expect(sourceAnnotation).toMatchObject({
      kind: "path",
      d: sourceView.annotations[0].d,
      transform: expect.stringMatching(/^matrix\(/),
    });
    expect(transformedAnnotation).toMatchObject({
      kind: "path",
      d: transformedView.annotations[0].d,
      transform: expect.stringMatching(/^matrix\(/),
    });
    if (
      sourceAnnotation?.kind !== "path" ||
      transformedAnnotation?.kind !== "path"
    ) {
      throw new Error("expected projected annotation paths");
    }
    const sourceStart = applySvgMatrix(sourceAnnotation.transform ?? "", {
      x: 0.5,
      y: 0.5,
    });
    const transformedStart = applySvgMatrix(
      transformedAnnotation.transform ?? "",
      { x: 1_048_580, y: -2_097_148 },
    );
    expect(transformedStart.x).toBeCloseTo(sourceStart.x, 10);
    expect(transformedStart.y).toBeCloseTo(sourceStart.y, 10);
    expect(`${sourceAnnotation.transform} ${transformedAnnotation.transform}`).not.toMatch(
      /NaN|Infinity/,
    );

    const { getByRole } = render(
      <PuzzleCanvas
        puzzle={transformedPuzzle}
        view={transformedView}
        onSelectCell={() => undefined}
      />,
    );
    expect(getByRole("img", { name: "Native" })).toHaveAttribute(
      "transform",
      transformedAnnotation.transform,
    );
  });

  it("isolates self-intersecting polygons in either winding at large coordinates", () => {
    const invalidPuzzle = structuredClone(puzzle);
    const offset = 1_000_000_000;
    const translate = (points: TestPoint[]) =>
      points.map(({ x, y }) => ({ x: offset + x * 100, y: offset + y * 100 }));
    const bowTie = translate([
      { x: 0, y: 0 },
      { x: 4, y: 4 },
      { x: 0, y: 4 },
      { x: 4, y: 0 },
    ]);
    const nonzeroAreaCrossing = translate([
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 0, y: 4 },
      { x: 4, y: 4 },
      { x: 0, y: 1 },
    ]);
    const cases = [
      ["r1c1", bowTie],
      ["r1c2", [...bowTie].reverse()],
      ["r1c3", nonzeroAreaCrossing],
      ["r1c4", [...nonzeroAreaCrossing].reverse()],
    ] as const;
    for (const [cellId, points] of cases) {
      invalidPuzzle.cells[cellId].shape = { kind: "polygon", points };
    }
    invalidPuzzle.groups["crossing-members"] = {
      id: "crossing-members",
      roles: ["region"],
      cellIds: [...cases.map(([cellId]) => cellId), "r2c2"],
    };

    const scene = projectPuzzleScene(invalidPuzzle, emptySceneView);
    for (const [cellId] of cases) {
      expect(scene.nodes.find((node) => node.id === `cell-${cellId}`)).toMatchObject({
        path: "",
        geometryIssue: {
          code: "invalid-topology",
          affects: "topology-and-content",
        },
      });
    }
    expect(scene.nodes.find((node) => node.id === "cell-r2c2")).toMatchObject({
      path: expect.stringMatching(/^M /),
      geometryIssue: undefined,
    });
    expect(
      scene.nodes.find((node) => node.id === "group-border-crossing-members"),
    ).toMatchObject({ d: expect.stringMatching(/^M /) });
    expect(JSON.stringify(scene)).not.toMatch(/NaN|Infinity/);
  });

  it("reports omitted members and partial below-threshold group borders accessibly", () => {
    const lossyPuzzle = structuredClone(puzzle);
    lossyPuzzle.cells["aux-1"].shape = { kind: "polygon", points: [] };
    lossyPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 0,
      y: 0,
      width: 1,
      height: 1,
    };
    lossyPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 5e-10,
      y: -1,
      width: 1,
      height: 1.5,
    };
    lossyPuzzle.groups["omitted-review"] = {
      id: "omitted-review",
      roles: ["region"],
      cellIds: ["aux-1", "r3c3"],
    };
    lossyPuzzle.groups["partial-review"] = {
      id: "partial-review",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    const scene = projectPuzzleScene(lossyPuzzle, emptySceneView);
    expect(
      scene.nodes.find((node) => node.id === "group-border-omitted-review"),
    ).toMatchObject({
      geometryIssues: [
        {
          code: "member-geometry-omitted",
          affects: "topology",
          message: "Group border omitted renderer-invalid member geometry: aux-1.",
        },
      ],
    });
    expect(
      scene.nodes.find((node) => node.id === "group-border-partial-review"),
    ).toMatchObject({
      geometryIssues: [
        {
          code: "below-minimum-boundary-piece",
          affects: "topology",
          message:
            "Group border omitted 2 exterior atomic pieces below the minimum normalized renderer scale (1e-9).",
        },
      ],
    });

    render(
      <PuzzleCanvas
        puzzle={lossyPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );
    const canvas = screen.getByRole("group", { name: "Untitled puzzle" });
    expect(canvas).toHaveAccessibleDescription(/Group omitted-review:.*aux-1/);
    expect(canvas).toHaveAccessibleDescription(
      /Group partial-review:.*below the minimum normalized renderer scale/,
    );
    expect(screen.getByTestId("group-border-partial-review")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("evaluates the public feature threshold before display quantization", () => {
    const projectWidth = (width: number) => {
      const thresholdPuzzle = structuredClone(puzzle);
      thresholdPuzzle.cells = {
        r1c1: {
          ...thresholdPuzzle.cells.r1c1,
          shape: { kind: "rect", x: 0, y: 0, width, height: 1 },
        },
      };
      thresholdPuzzle.groups = {};
      return projectPuzzleScene(thresholdPuzzle, emptySceneView).nodes.find(
        (node) => node.id === "cell-r1c1",
      );
    };

    expect(projectWidth(1e-9 - 2e-13)).toMatchObject({
      path: "",
      geometryIssue: { code: "below-minimum-feature" },
    });
    for (const width of [1e-9, 1e-9 + 2e-13]) {
      expect(projectWidth(width)).toMatchObject({
        path: expect.stringMatching(/^M /),
        geometryIssue: undefined,
      });
    }
  });

  it("renders main and auxiliary cells from native geometry", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getAllByTestId(/^cell-/)).toHaveLength(82);
    expect(screen.getByTestId("cell-aux-1")).toHaveAttribute(
      "data-solver-participation",
      "visualOnly",
    );
  });

  it("renders only the supplied active-context candidate map", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{
          ...emptySceneView,
          candidates: { r1c2: ["1", "4"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getByTestId("candidates-r1c2")).toHaveTextContent("14");
    expect(screen.queryByTestId("candidates-r1c3")).toBeNull();
  });

  it("projects polygon cells through normalized scene geometry", () => {
    const polygonPuzzle = structuredClone(puzzle);
    polygonPuzzle.cells["aux-1"].shape = {
      kind: "polygon",
      points: [
        { x: 10, y: 4 },
        { x: 11, y: 4 },
        { x: 10.5, y: 5 },
      ],
    };

    render(
      <PuzzleCanvas
        puzzle={polygonPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const d =
      screen.getByTestId("cell-aux-1").querySelector("path")?.getAttribute("d") ??
      "";
    expect(d).not.toMatch(/NaN|Infinity/);
    const coordinates = [...d.matchAll(/[\d.]+/g)].map((match) =>
      Number(match[0]),
    );
    expect(coordinates).toHaveLength(6);
    [10, 4, 11, 4, 10.5, 5].forEach((coordinate, index) => {
      expect(coordinates[index]).toBeCloseTo(coordinate, 8);
    });
  });

  it("derives a region border from explicit group membership", () => {
    const groupedPuzzle = structuredClone(puzzle);
    groupedPuzzle.groups["not-id-derived"] = {
      id: "not-id-derived",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={groupedPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const border = screen.getByTestId("group-border-not-id-derived");
    expect(border).toHaveAttribute(
      "d",
      "M 0 0 L 1 0 M 1 1 L 0 1 M 0 1 L 0 0 M 1 0 L 2 0 M 2 0 L 2 1 M 2 1 L 1 1",
    );
  });

  it("uses domain order, labels, and cardinality for candidate placement", () => {
    const labelledPuzzle = structuredClone(puzzle);
    labelledPuzzle.domains["digits-1-9"].values = [
      { id: "stable-1", label: "Alpha" },
      { id: "stable-2", label: "Beta" },
      { id: "stable-3", label: "Gamma" },
      { id: "stable-4", label: "Delta" },
      { id: "stable-5", label: "Epsilon" },
    ];

    render(
      <PuzzleCanvas
        puzzle={labelledPuzzle}
        view={{
          ...emptySceneView,
          candidates: { r1c2: ["stable-4", "stable-1"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getByTestId("candidates-r1c2")).toHaveTextContent(
      "AlphaDelta",
    );
    expect(screen.getByText("Alpha")).toHaveAttribute("x", "1.1666666666666667");
    expect(screen.getByText("Alpha")).toHaveAttribute("y", "0.16666666666666666");
    expect(screen.getByText("Delta")).toHaveAttribute("x", "1.1666666666666667");
    expect(screen.getByText("Delta")).toHaveAttribute("y", "0.5");
  });

  it("falls back to unknown for absent, raw-id, or mismatched cell capabilities", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{
          ...emptySceneView,
          entityCapabilities: {
            "aux-1": {
              entityKind: "cell",
              entityId: "aux-1",
              status: "visualOnly",
            },
            "cell:r1c1": {
              entityKind: "group",
              entityId: "r1c1",
              status: "visualOnly",
            },
          },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getByTestId("cell-aux-1")).toHaveAttribute(
      "data-solver-participation",
      "unknown",
    );
    expect(screen.getByTestId("cell-r1c1")).toHaveAttribute(
      "data-solver-participation",
      "unknown",
    );
  });

  it("renders givens before projected values and suppresses candidates in filled cells", () => {
    const givenPuzzle = structuredClone(puzzle);
    givenPuzzle.givens.r1c1 = "5";

    render(
      <PuzzleCanvas
        puzzle={givenPuzzle}
        view={{
          ...emptySceneView,
          values: { r1c1: "4", r1c2: "3" },
          candidates: { r1c1: ["1"], r1c2: ["2"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getByTestId("given-r1c1")).toHaveTextContent("5");
    expect(screen.getByTestId("value-r1c2")).toHaveTextContent("3");
    expect(screen.queryByTestId("value-r1c1")).toBeNull();
    expect(screen.queryByTestId("candidates-r1c1")).toBeNull();
    expect(screen.queryByTestId("candidates-r1c2")).toBeNull();
  });

  it("renders supplied selections and labelled annotations as scene paths", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{
          ...emptySceneView,
          selectedCellIds: ["r1c1", "aux-1"],
          annotations: [
            {
              id: "focus-link",
              d: "M 0.5 0.5 L 10.5 4.5",
              label: "Focus link",
            },
          ],
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(screen.getByTestId("selection-r1c1")).toHaveAttribute(
      "d",
      "M 0 0 H 1 V 1 H 0 Z",
    );
    expect(screen.getByTestId("selection-aux-1")).toHaveAttribute(
      "d",
      screen
        .getByTestId("cell-aux-1")
        .querySelector("path")
        ?.getAttribute("d") ?? "",
    );
    expect(screen.getByRole("img", { name: "Focus link" })).toHaveAttribute(
      "d",
      "M 0.5 0.5 L 10.5 4.5",
    );
  });

  it("labels the SVG and exposes each cell as a focusable control", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      screen.getByRole("group", { name: "Untitled puzzle" }),
    ).toBeVisible();
    const cell = screen.getByRole("button", { name: "Cell r1c1" });
    expect(cell).toHaveAttribute("tabindex", "0");
    cell.focus();
    expect(cell).toHaveFocus();
  });

  it("delegates pointer selection from cell descendants", () => {
    const selectedCellIds: string[] = [];
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{ ...emptySceneView, candidates: { r1c2: ["1"] } }}
        onSelectCell={(cellId) => selectedCellIds.push(cellId)}
      />,
    );

    fireEvent.pointerUp(screen.getByText("1"));
    fireEvent.pointerUp(screen.getByTestId("group-border-region-1"));

    expect(selectedCellIds).toEqual(["r1c2"]);
  });

  it("activates a focused cell with Enter or Space only", () => {
    const selectedCellIds: string[] = [];
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={emptySceneView}
        onSelectCell={(cellId) => selectedCellIds.push(cellId)}
      />,
    );

    const cell = screen.getByTestId("cell-r1c1");
    cell.focus();
    fireEvent.keyDown(cell, { key: "ArrowRight" });
    fireEvent.keyDown(cell, { key: "Enter" });
    fireEvent.keyDown(cell, { key: " " });

    expect(selectedCellIds).toEqual(["r1c1", "r1c1"]);
  });

  it("traces a tolerance-aware union border across split and reversed edges", () => {
    const groupedPuzzle = structuredClone(puzzle);
    groupedPuzzle.cells.r1c1.shape = {
      kind: "polygon",
      points: [
        { x: 0, y: 0 },
        { x: 2, y: 0 },
        { x: 2, y: 1 },
        { x: 0, y: 1 },
      ],
    };
    groupedPuzzle.cells.r1c2.shape = {
      kind: "polygon",
      points: [
        { x: 0, y: 2 },
        { x: 1, y: 2 },
        { x: 1, y: 1.00000004 },
        { x: 0, y: 1.00000004 },
      ],
    };
    groupedPuzzle.cells.r1c3.shape = {
      kind: "polygon",
      points: [
        { x: 1, y: 0.99999996 },
        { x: 2, y: 0.99999996 },
        { x: 2, y: 2 },
        { x: 1, y: 2 },
      ],
    };
    groupedPuzzle.groups["split-union"] = {
      id: "split-union",
      roles: ["region"],
      cellIds: ["r1c1", "r1c1", "r1c2", "r1c3"],
    };

    render(
      <PuzzleCanvas
        puzzle={groupedPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const segments = readLineSegments(
      screen.getByTestId("group-border-split-union"),
    );
    expect(segments.length).toBeGreaterThan(0);
    for (const segment of segments) {
      const midpoint = {
        x: (segment.from.x + segment.to.x) / 2,
        y: (segment.from.y + segment.to.y) / 2,
      };
      const liesOnOuterBoundary =
        Math.abs(midpoint.x) < 0.000001 ||
        Math.abs(midpoint.x - 2) < 0.000001 ||
        Math.abs(midpoint.y) < 0.000001 ||
        Math.abs(midpoint.y - 2) < 0.000001;
      expect(liesOnOuterBoundary, JSON.stringify(segment)).toBe(true);
    }
  });

  it("splits non-collinear intersections before tracing overlapping rectangle unions", () => {
    const overlapPuzzle = structuredClone(puzzle);
    overlapPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 0,
      y: 0,
      width: 2,
      height: 2,
    };
    overlapPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 1,
      y: 1,
      width: 2,
      height: 2,
    };
    overlapPuzzle.groups.overlap = {
      id: "overlap",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={overlapPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const segments = readLineSegments(screen.getByTestId("group-border-overlap"));
    expect(segments).toHaveLength(8);
    expect(totalSegmentLength(segments)).toBeCloseTo(12, 8);
  });

  it("traces overlapping polygon unions independent of orientation", () => {
    const overlapPuzzle = structuredClone(puzzle);
    const square = [
      { x: 0, y: 0 },
      { x: 2, y: 0 },
      { x: 2, y: 2 },
      { x: 0, y: 2 },
    ];
    const diamond = [
      { x: 1, y: -2 },
      { x: 4, y: 1 },
      { x: 1, y: 4 },
      { x: -2, y: 1 },
    ];
    overlapPuzzle.cells.r1c1.shape = { kind: "polygon", points: square };
    overlapPuzzle.cells.r1c2.shape = {
      kind: "polygon",
      points: [...diamond].reverse(),
    };
    overlapPuzzle.groups["polygon-overlap"] = {
      id: "polygon-overlap",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={overlapPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const original = readLineSegments(
      screen.getByTestId("group-border-polygon-overlap"),
    );
    expect(original).toHaveLength(4);
    expect(totalSegmentLength(original)).toBeCloseTo(12 * Math.SQRT2, 8);
  });

  it("keeps one exterior boundary for identical geometry and duplicate membership", () => {
    const duplicatePuzzle = structuredClone(puzzle);
    const sharedShape = { kind: "rect" as const, x: 0, y: 0, width: 2, height: 1 };
    duplicatePuzzle.cells.r1c1.shape = sharedShape;
    duplicatePuzzle.cells.r1c2.shape = sharedShape;
    duplicatePuzzle.groups.identical = {
      id: "identical",
      roles: ["region"],
      cellIds: ["r1c1", "r1c1", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={duplicatePuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const segments = readLineSegments(screen.getByTestId("group-border-identical"));
    expect(segments).toHaveLength(4);
    expect(totalSegmentLength(segments)).toBeCloseTo(6, 8);
  });

  it("does not let distant geometry erase a nearby thin union member", () => {
    const thinPuzzle = structuredClone(puzzle);
    thinPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 0,
      y: 0,
      width: 0.01,
      height: 1,
    };
    thinPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 0.01,
      y: 0,
      width: 1,
      height: 1,
    };
    thinPuzzle.cells.r1c3.shape = {
      kind: "rect",
      x: 1_000_000,
      y: 0,
      width: 1,
      height: 1,
    };
    thinPuzzle.groups["thin-and-distant"] = {
      id: "thin-and-distant",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2", "r1c3"],
    };

    render(
      <PuzzleCanvas
        puzzle={thinPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const segments = readLineSegments(
      screen.getByTestId("group-border-thin-and-distant"),
    );
    expect(segments).toHaveLength(10);
    expect(totalSegmentLength(segments)).toBeCloseTo(8.02, 7);
  });

  it("retains both outer and hole boundaries in a ring-shaped union", () => {
    const ringPuzzle = structuredClone(puzzle);
    const positions = [
      [0, 0], [1, 0], [2, 0],
      [0, 1],         [2, 1],
      [0, 2], [1, 2], [2, 2],
    ] as const;
    const cellIds = ["r1c1", "r1c2", "r1c3", "r1c4", "r1c5", "r1c6", "r1c7", "r1c8"];
    cellIds.forEach((cellId, index) => {
      const [x, y] = positions[index];
      ringPuzzle.cells[cellId].shape = { kind: "rect", x, y, width: 1, height: 1 };
    });
    ringPuzzle.groups.ring = { id: "ring", roles: ["region"], cellIds };

    render(
      <PuzzleCanvas
        puzzle={ringPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const segments = readLineSegments(screen.getByTestId("group-border-ring"));
    expect(totalSegmentLength(segments)).toBeCloseTo(16, 8);
    const holeBoundaryLength = segments
      .filter((segment) => {
        const midpoint = {
          x: (segment.from.x + segment.to.x) / 2,
          y: (segment.from.y + segment.to.y) / 2,
        };
        return midpoint.x >= 1 && midpoint.x <= 2 && midpoint.y >= 1 && midpoint.y <= 2;
      })
      .reduce((total, segment) => total + segmentLength(segment), 0);
    expect(holeBoundaryLength).toBeCloseTo(4, 8);
  });

  it("keeps value and candidate anchors inside a concave polygon", () => {
    const concavePuzzle = structuredClone(puzzle);
    const lShape = [
      { x: 0, y: 0 },
      { x: 3, y: 0 },
      { x: 3, y: 1 },
      { x: 1, y: 1 },
      { x: 1, y: 3 },
      { x: 0, y: 3 },
    ];
    const translatedLShape = lShape.map((point) => ({
      x: point.x + 10,
      y: point.y,
    }));
    concavePuzzle.cells.r1c1.shape = { kind: "polygon", points: lShape };
    concavePuzzle.cells["aux-1"].shape = {
      kind: "polygon",
      points: translatedLShape,
    };

    render(
      <PuzzleCanvas
        puzzle={concavePuzzle}
        view={{
          ...emptySceneView,
          values: { r1c1: "5" },
          candidates: { "aux-1": ["1", "4", "9"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      isPointInPolygon(readTextPoint(screen.getByTestId("value-r1c1")), lShape),
    ).toBe(true);
    for (const label of ["1", "4", "9"]) {
      expect(
        isPointInPolygon(readTextPoint(screen.getByText(label)), translatedLShape),
      ).toBe(true);
    }
  });

  it("finds content anchors inside a thin concave polygon with a large bounding box", () => {
    const thinConcavePuzzle = structuredClone(puzzle);
    const thinLShape = [
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 0.00001 },
      { x: 0.00001, y: 0.00001 },
      { x: 0.00001, y: 100 },
      { x: 0, y: 100 },
    ];
    const translatedThinLShape = thinLShape.map((point) => ({
      x: point.x + 110,
      y: point.y,
    }));
    thinConcavePuzzle.cells.r1c1.shape = {
      kind: "polygon",
      points: thinLShape,
    };
    thinConcavePuzzle.cells["aux-1"].shape = {
      kind: "polygon",
      points: translatedThinLShape,
    };

    render(
      <PuzzleCanvas
        puzzle={thinConcavePuzzle}
        view={{
          ...emptySceneView,
          values: { r1c1: "5" },
          candidates: { "aux-1": ["1", "4", "9"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      isPointInPolygon(
        readTextPoint(screen.getByTestId("value-r1c1")),
        thinLShape,
      ),
    ).toBe(true);
    for (const label of ["1", "4", "9"]) {
      expect(
        isPointInPolygon(
          readTextPoint(screen.getByText(label)),
          translatedThinLShape,
        ),
      ).toBe(true);
    }
  });

  it("keeps anchors inside an above-threshold large-coordinate sliver", () => {
    const sliverPuzzle = structuredClone(puzzle);
    const x = 1_000_000_000;
    const height = 0.000007;
    for (const cell of Object.values(sliverPuzzle.cells)) {
      if (cell.shape.kind === "rect") {
        cell.shape.x += x;
      } else if (cell.shape.kind === "polygon") {
        cell.shape.points = cell.shape.points.map((point) => ({
          x: point.x + x,
          y: point.y,
        }));
      }
    }
    const valuePolygon = [
      { x, y: 0 },
      { x: x + 100, y: 0 },
      { x: x + 100, y: height },
      { x, y: height },
    ];
    const candidatePolygon = valuePolygon.map((point) => ({
      x: point.x,
      y: point.y + 1,
    }));
    const valueScenePolygon = valuePolygon.map((point) => ({
      x: point.x - x,
      y: point.y,
    }));
    const candidateScenePolygon = candidatePolygon.map((point) => ({
      x: point.x - x,
      y: point.y,
    }));
    sliverPuzzle.cells.r1c1.shape = {
      kind: "polygon",
      points: valuePolygon,
    };
    sliverPuzzle.cells["aux-1"].shape = {
      kind: "polygon",
      points: candidatePolygon,
    };

    render(
      <PuzzleCanvas
        puzzle={sliverPuzzle}
        view={{
          ...emptySceneView,
          values: { r1c1: "5" },
          candidates: { "aux-1": ["1", "4", "9"] },
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      isPointInPolygon(
        readTextPoint(screen.getByTestId("value-r1c1")),
        valueScenePolygon,
      ),
    ).toBe(true);
    const candidatePoints = ["1", "4", "9"].map((label) =>
      readTextPoint(screen.getByText(label)),
    );
    for (const point of candidatePoints) {
      expect(isPointInPolygon(point, candidateScenePolygon)).toBe(true);
    }
    expect(
      new Set(candidatePoints.map((point) => `${point.x},${point.y}`)).size,
    ).toBe(3);
  });

  it("accounts for bounded interior work by vertex count rather than aspect ratio", () => {
    const projectPolygon = (width: number, vertexCount: number) => {
      const elongatedPuzzle = structuredClone(puzzle);
      elongatedPuzzle.cells.r1c1.shape = {
        kind: "polygon",
        points: Array.from({ length: vertexCount }, (_, index) => {
          const angle = (index * Math.PI * 2) / vertexCount;
          return {
            x: width / 2 + (width / 2) * Math.cos(angle),
            y: 0.5 + 0.5 * Math.sin(angle),
          };
        }),
      };
      const metrics = {
        interiorWorkUnits: 0,
        earCandidateScans: 0,
        pointInTriangleTests: 0,
        triangleEvaluations: 0,
        nearestEdgeScans: 0,
        pointInPolygonEdgeScans: 0,
      };
      const scene = projectPuzzleScene(
        elongatedPuzzle,
        { ...emptySceneView, values: { r1c1: "5" } },
        metrics,
      );
      const cell = scene.nodes.find(
        (node) => node.kind === "cell" && node.cellId === "r1c1",
      );
      return { metrics, cell };
    };

    const shortFour = projectPolygon(2, 4);
    const longFour = projectPolygon(100, 4);
    const longEight = projectPolygon(100, 8);
    const longSixteen = projectPolygon(100, 16);

    expect(longFour.metrics).toEqual(shortFour.metrics);
    expect(longFour.metrics).toEqual({
      interiorWorkUnits: 24,
      earCandidateScans: 1,
      pointInTriangleTests: 1,
      triangleEvaluations: 2,
      nearestEdgeScans: 8,
      pointInPolygonEdgeScans: 12,
    });
    for (const metrics of [
      longFour.metrics,
      longEight.metrics,
      longSixteen.metrics,
    ]) {
      expect(metrics.earCandidateScans).toBeGreaterThan(0);
      expect(metrics.pointInTriangleTests).toBeGreaterThan(0);
      expect(metrics.triangleEvaluations).toBeGreaterThan(0);
      expect(metrics.nearestEdgeScans).toBeGreaterThan(0);
      expect(metrics.pointInPolygonEdgeScans).toBeGreaterThan(0);
      expect(metrics.interiorWorkUnits).toBe(
        metrics.earCandidateScans +
          metrics.pointInTriangleTests +
          metrics.triangleEvaluations +
          metrics.nearestEdgeScans +
          metrics.pointInPolygonEdgeScans,
      );
    }
    expect(longEight.metrics.interiorWorkUnits).toBeGreaterThan(
      longFour.metrics.interiorWorkUnits,
    );
    expect(longSixteen.metrics.interiorWorkUnits).toBeGreaterThan(
      longEight.metrics.interiorWorkUnits,
    );
    expect(longSixteen.metrics.interiorWorkUnits).toBeLessThanOrEqual(
      longEight.metrics.interiorWorkUnits * 8,
    );
    expect(longSixteen.metrics.interiorWorkUnits).toBeLessThanOrEqual(2_000);
    expect(
      longSixteen.cell?.kind === "cell" ? longSixteen.cell.content : [],
    ).toHaveLength(1);
  });

  it("isolates degenerate polygon geometry with a deterministic accessible explanation", () => {
    const invalidPuzzle = structuredClone(puzzle);
    invalidPuzzle.cells.r1c1.shape = { kind: "polygon", points: [] };
    invalidPuzzle.cells.r1c2.shape = {
      kind: "polygon",
      points: [
        { x: 0, y: 0 },
        { x: 1, y: 0 },
        { x: 2, y: 0 },
      ],
    };

    render(
      <PuzzleCanvas
        puzzle={invalidPuzzle}
        view={{
          ...emptySceneView,
          values: { r1c1: "5", r1c2: "4" },
          entityCapabilities: {
            ...emptySceneView.entityCapabilities,
            "cell:r1c1": {
              entityKind: "cell",
              entityId: "r1c1",
              status: "fullyVerified",
              reason: "Solver projection remains authoritative.",
            },
          },
        }}
        onSelectCell={() => undefined}
      />,
    );

    const emptyCell = screen.getByRole("button", { name: "Cell r1c1" });
    expect(emptyCell).toHaveAttribute("data-solver-participation", "fullyVerified");
    expect(emptyCell).toHaveAccessibleDescription(
      "Solver participation: fully verified. Solver projection remains authoritative. Presentation geometry: polygon has no usable normalized topology.",
    );
    expect(screen.getByRole("button", { name: "Cell r1c2" })).toHaveAccessibleDescription(
      "Solver participation: unknown. Presentation geometry: polygon has no usable normalized topology.",
    );
    expect(screen.getByTestId("cell-r1c3")).toBeVisible();
    expect(screen.queryByTestId("value-r1c1")).toBeNull();
    expect(screen.queryByTestId("value-r1c2")).toBeNull();
  });

  it("announces cell content, selection, and solver participation without visual duplication", () => {
    const semanticPuzzle = structuredClone(puzzle);
    semanticPuzzle.givens.r1c1 = "5";

    render(
      <PuzzleCanvas
        puzzle={semanticPuzzle}
        view={{
          ...emptySceneView,
          values: { r1c2: "3" },
          candidates: { r1c3: ["1", "4"] },
          selectedCellIds: ["r1c1"],
          entityCapabilities: {
            "cell:r1c1": {
              entityKind: "cell",
              entityId: "r1c1",
              status: "fullyVerified",
              reason: "Exact projection.",
            },
            "cell:r1c2": {
              entityKind: "cell",
              entityId: "r1c2",
              status: "partiallyVerified",
              reason: "Some rules are visual only.",
            },
            "cell:r1c3": {
              entityKind: "cell",
              entityId: "r1c3",
              status: "visualOnly",
              reason: "Not projected.",
            },
          },
        }}
        onSelectCell={() => undefined}
      />,
    );

    const given = screen.getByRole("button", {
      name: "Cell r1c1, given 5",
    });
    expect(given).toHaveAttribute("aria-pressed", "true");
    expect(given).toHaveAccessibleDescription(
      "Solver participation: fully verified. Exact projection.",
    );
    const value = screen.getByRole("button", {
      name: "Cell r1c2, value 3",
    });
    expect(value).toHaveAttribute("aria-pressed", "false");
    expect(value).toHaveAccessibleDescription(
      "Solver participation: partially verified. Some rules are visual only.",
    );
    expect(
      screen.getByRole("button", {
        name: "Cell r1c3, candidates 1, 4",
      }),
    ).toHaveAccessibleDescription(
      "Solver participation: visual only. Not projected.",
    );
    expect(screen.getByTestId("given-r1c1")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("keeps internal, region, and selection strokes visible in CSS pixels", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{ ...emptySceneView, selectedCellIds: ["r1c1"] }}
        onSelectCell={() => undefined}
      />,
    );

    const surface = screen.getByTestId("cell-r1c1").querySelector("path");
    expect(surface).not.toBeNull();
    expect(getComputedStyle(surface as SVGPathElement).strokeWidth).toBe("1px");
    expect(
      getComputedStyle(screen.getByTestId("group-border-region-1")).strokeWidth,
    ).toBe("2px");
    expect(
      getComputedStyle(screen.getByTestId("selection-r1c1")).strokeWidth,
    ).toBe("2px");
  });

  it("deduplicates repeated selection and annotation identifiers", () => {
    const { container } = render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={{
          ...emptySceneView,
          selectedCellIds: ["r1c1", "r1c1"],
          annotations: [
            { id: "same", d: "M 0 0 L 1 1" },
            { id: "same", d: "M 1 0 L 0 1" },
          ],
        }}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      container.querySelectorAll('[data-testid="selection-r1c1"]'),
    ).toHaveLength(1);
    expect(
      container.querySelectorAll('[data-testid="annotation-same"]'),
    ).toHaveLength(1);
  });

  it("fits the SVG responsively without forcing natural-width overflow", () => {
    render(
      <PuzzleCanvas
        puzzle={puzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    const canvas = screen.getByRole("group", { name: "Untitled puzzle" });
    expect(getComputedStyle(canvas).maxInlineSize).toBe("100%");
    expect(getComputedStyle(canvas).blockSize).toBe("auto");
    const [, , width, height] = (canvas.getAttribute("viewBox") ?? "")
      .split(" ")
      .map(Number);
    expect(width / height).toBeCloseTo(11 / 9, 8);
  });

  it("clips long candidate labels visually while preserving their full accessible text", () => {
    const longLabelPuzzle = structuredClone(puzzle);
    const longLabel = "A candidate label that is intentionally very long";
    longLabelPuzzle.domains["digits-1-9"].values[0].label = longLabel;

    render(
      <PuzzleCanvas
        puzzle={longLabelPuzzle}
        view={{ ...emptySceneView, candidates: { r1c1: ["1"] } }}
        onSelectCell={() => undefined}
      />,
    );

    const candidate = screen.getByText(longLabel);
    const viewport = candidate.closest("svg");
    expect(viewport).toHaveClass("puzzle-cell__candidate-viewport");
    expect(viewport).toHaveAttribute("overflow", "hidden");
    expect(Number(viewport?.getAttribute("width"))).toBeGreaterThan(0);
    expect(Number(viewport?.getAttribute("height"))).toBeGreaterThan(0);
    expect(candidate).not.toHaveAttribute("clip-path");
    expect(
      screen.getByRole("button", {
        name: `Cell r1c1, candidates ${longLabel}`,
      }),
    ).toBeVisible();
  });

  it("clips candidates without document-global identifiers across server roots", () => {
    const longLabelPuzzle = structuredClone(puzzle);
    const longLabel = "Complete accessible candidate label";
    longLabelPuzzle.domains["digits-1-9"].values[0].label = longLabel;
    const renderIndependentRoot = () =>
      renderToString(
        <PuzzleCanvas
          puzzle={longLabelPuzzle}
          view={{ ...emptySceneView, candidates: { r1c1: ["1"] } }}
          onSelectCell={() => undefined}
        />,
      );

    const firstRoot = renderIndependentRoot();
    const secondRoot = renderIndependentRoot();
    const combinedMarkup = firstRoot + secondRoot;

    expect(combinedMarkup).not.toMatch(/\sid=/);
    expect(combinedMarkup).not.toContain("url(#");
    expect(combinedMarkup.match(/overflow="hidden"/g)).toHaveLength(2);
    expect(firstRoot).toContain(
      `aria-label="Cell r1c1, candidates ${longLabel}"`,
    );
    expect(secondRoot).toContain(
      `aria-label="Cell r1c1, candidates ${longLabel}"`,
    );
  });
});
