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

function nextUp(value: number) {
  const buffer = new ArrayBuffer(8);
  const view = new DataView(buffer);
  view.setFloat64(0, value);
  view.setBigUint64(0, view.getBigUint64(0) + 1n);
  return view.getFloat64(0);
}

function advanceUp(value: number, count: number) {
  let result = value;
  for (let index = 0; index < count; index += 1) {
    result = nextUp(result);
  }
  return result;
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

function parseLineSegments(d: string) {
  const matches = [
    ...d.matchAll(
      /M ([\d.eE+-]+) ([\d.eE+-]+) L ([\d.eE+-]+) ([\d.eE+-]+)/g,
    ),
  ];
  return matches.map((match) => ({
    from: { x: Number(match[1]), y: Number(match[2]) },
    to: { x: Number(match[3]), y: Number(match[4]) },
  }));
}

function readLineSegments(path: Element) {
  return parseLineSegments(path.getAttribute("d") ?? "");
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

  it("normalizes native annotation coordinates without a flattened affine", () => {
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
      d: expect.stringMatching(/^M /),
    });
    expect(transformedAnnotation).toMatchObject({
      kind: "path",
      d: expect.stringMatching(/^M /),
    });
    if (
      sourceAnnotation?.kind !== "path" ||
      transformedAnnotation?.kind !== "path"
    ) {
      throw new Error("expected projected annotation paths");
    }
    expect(transformedAnnotation.d).toBe(sourceAnnotation.d);
    expect(`${sourceAnnotation.d} ${transformedAnnotation.d}`).not.toMatch(
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
      "d",
      transformedAnnotation.d,
    );
    expect(getByRole("img", { name: "Native" })).not.toHaveAttribute(
      "transform",
    );
  });

  it("normalizes the accepted relative, curve, arc, repeated, and exponent path grammar", () => {
    const pathPuzzle = structuredClone(puzzle);
    pathPuzzle.cells = {
      r1c1: {
        ...pathPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 10, height: 10 },
      },
    };
    pathPuzzle.groups = {};
    const d =
      "M1e0,1e0 2e0,1e0 L3,1 H4 V2 C4,2 4,3 5,3 S6,4 7,5 Q8,6 9,7 T10,8 A1,2 30 0 1 8,8 a.5,.25 45 1 0 1,1 z";
    const annotation = projectPuzzleScene(pathPuzzle, {
      ...emptySceneView,
      annotations: [{ id: "grammar", d, label: "Grammar" }],
    }).nodes.find((node) => node.id === "annotation-grammar");

    expect(annotation).toMatchObject({
      d: "M 0.1 0.1 L 0.2 0.1 L 0.3 0.1 H 0.4 V 0.2 C 0.4 0.2 0.4 0.3 0.5 0.3 S 0.6 0.4 0.7 0.5 Q 0.8 0.6 0.9 0.7 T 1 0.8 A 0.1 0.2 30 0 1 0.8 0.8 A 0.05 0.025 45 1 0 0.9 0.9 Z",
      geometryIssue: undefined,
    });
  });

  it("normalizes huge same-sign annotation coordinates exactly like cell coordinates", () => {
    const hugePuzzle = structuredClone(puzzle);
    const low = 1e308;
    const high = 1.0000000000000002e308;
    hugePuzzle.cells = {
      r1c1: {
        ...hugePuzzle.cells.r1c1,
        shape: {
          kind: "polygon",
          points: [
            { x: low, y: low },
            { x: high, y: low },
            { x: high, y: high },
            { x: low, y: high },
          ],
        },
      },
    };
    hugePuzzle.groups = {};
    const scene = projectPuzzleScene(hugePuzzle, {
      ...emptySceneView,
      annotations: [
        {
          id: "huge",
          d: `M ${low} ${low} L ${high} ${high}`,
          label: "Huge",
        },
      ],
    });

    expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: "M 0 0 L 1 0 L 1 1 L 0 1 Z",
    });
    expect(scene.nodes.find((node) => node.id === "annotation-huge")).toMatchObject({
      d: "M 0 0 L 1 1",
      geometryIssue: undefined,
    });
  });

  it("resolves huge relative path state in native coordinates before normalization", () => {
    const hugePuzzle = structuredClone(puzzle);
    const low = 1e308;
    const high = 1.0000000000000002e308;
    const span = high - low;
    hugePuzzle.cells = {
      r1c1: {
        ...hugePuzzle.cells.r1c1,
        shape: {
          kind: "polygon",
          points: [
            { x: low, y: low },
            { x: high, y: low },
            { x: high, y: high },
            { x: low, y: high },
          ],
        },
      },
    };
    hugePuzzle.groups = {};
    const absolute = `M ${low} ${low} L ${high} ${low} H ${low} V ${high} C ${low} ${low} ${high} ${low} ${high} ${high} S ${low} ${high} ${low} ${low} Q ${high} ${low} ${high} ${high} T ${low} ${low} A ${span} ${span} 0 0 1 ${high} ${high} Z`;
    const relative = `m ${low} ${low} l ${span} 0 h ${-span} v ${span} c 0 ${-span} ${span} ${-span} ${span} 0 s ${-span} 0 ${-span} ${-span} q ${span} 0 ${span} ${span} t ${-span} ${-span} a ${span} ${span} 0 0 1 ${span} ${span} z`;
    const scene = projectPuzzleScene(hugePuzzle, {
      ...emptySceneView,
      annotations: [
        { id: "absolute", d: absolute, label: "Absolute" },
        { id: "relative", d: relative, label: "Relative" },
      ],
    });
    const absoluteNode = scene.nodes.find(
      (node) => node.id === "annotation-absolute",
    );
    const relativeNode = scene.nodes.find(
      (node) => node.id === "annotation-relative",
    );
    const expected =
      "M 0 0 L 1 0 H 0 V 1 C 0 0 1 0 1 1 S 0 1 0 0 Q 1 0 1 1 T 0 0 A 1 1 0 0 1 1 1 Z";

    expect(absoluteNode).toMatchObject({ d: expected, geometryIssue: undefined });
    expect(relativeNode).toMatchObject({
      d: expected,
      geometryIssue: undefined,
    });
    expect(relativeNode?.kind === "path" ? relativeNode.d : "").toContain(
      "A 1 1 0 0 1 1 1",
    );
  });

  it("uses one direct finite native span for unequal huge-axis ULP extents", () => {
    const origin = 1e300;
    const xHigh = advanceUp(origin, 12);
    const yHigh = advanceUp(origin, 13);
    const xSpan = xHigh - origin;
    const ySpan = yHigh - origin;
    const buildScene = (
      minX: number,
      minY: number,
      maxX: number,
      maxY: number,
    ) => {
      const framePuzzle = structuredClone(puzzle);
      framePuzzle.cells = {
        r1c1: {
          ...framePuzzle.cells.r1c1,
          shape: {
            kind: "polygon",
            points: [
              { x: minX, y: minY },
              { x: maxX, y: minY },
              { x: maxX, y: maxY },
              { x: minX, y: maxY },
            ],
          },
        },
      };
      framePuzzle.groups = {};
      return projectPuzzleScene(framePuzzle, {
        ...emptySceneView,
        annotations: [
          {
            id: "frame",
            d: `M ${minX} ${minY} C ${maxX} ${minY} ${minX} ${maxY} ${maxX} ${maxY} M ${minX} ${minY} A ${maxX - minX} ${maxY - minY} 0 0 1 ${maxX} ${maxY}`,
            label: "Frame",
          },
        ],
      });
    };

    const hugeScene = buildScene(origin, origin, xHigh, yHigh);
    const ordinaryScene = buildScene(17, -23, 17 + 12 * 7, -23 + 13 * 7);
    const expectedCell =
      "M 0 0 L 0.923076923077 0 L 0.923076923077 1 L 0 1 Z";
    const expectedAnnotation =
      "M 0 0 C 0.923076923077 0 0 1 0.923076923077 1 M 0 0 A 0.923076923077 1 0 0 1 0.923076923077 1";

    for (const scene of [hugeScene, ordinaryScene]) {
      expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
        path: expectedCell,
      });
      expect(
        scene.nodes.find((node) => node.id === "annotation-frame"),
      ).toMatchObject({ d: expectedAnnotation, geometryIssue: undefined });
    }
    expect(xSpan / ySpan).toBe(12 / 13);
  });

  it("uses one shared scale-first frame when only one axis span overflows", () => {
    const framePuzzle = structuredClone(puzzle);
    framePuzzle.cells = {
      r1c1: {
        ...framePuzzle.cells.r1c1,
        shape: {
          kind: "polygon",
          points: [
            { x: -1e308, y: -8e307 },
            { x: 1e308, y: -8e307 },
            { x: 1e308, y: 8e307 },
            { x: -1e308, y: 8e307 },
          ],
        },
      },
    };
    framePuzzle.groups = {};
    const scene = projectPuzzleScene(framePuzzle, {
      ...emptySceneView,
      annotations: [
        {
          id: "overflow-frame",
          d: "M -1e308 -8e307 L 1e308 8e307 M -1e308 -8e307 A 1e308 1.6e308 0 0 1 1e308 8e307",
          label: "Overflow frame",
        },
      ],
    });

    expect(scene.nodes.find((node) => node.id === "cell-r1c1")).toMatchObject({
      path: "M 0 0 L 1 0 L 1 0.8 L 0 0.8 Z",
    });
    expect(
      scene.nodes.find((node) => node.id === "annotation-overflow-frame"),
    ).toMatchObject({
      d: "M 0 0 L 1 0.8 M 0 0 A 0.5 0.8 0 0 1 1 0.8",
      geometryIssue: undefined,
    });
    expect(JSON.stringify(scene)).not.toMatch(/NaN|Infinity/);
  });

  it("enforces strict SVG separators while preserving valid compact grammar", () => {
    const pathPuzzle = structuredClone(puzzle);
    pathPuzzle.cells = {
      r1c1: {
        ...pathPuzzle.cells.r1c1,
        shape: { kind: "rect", x: -200, y: -200, width: 400, height: 400 },
      },
    };
    pathPuzzle.groups = {};
    const malformed = [
      "M,0 0",
      "M 0,,0",
      "M 0 0,",
      "M\u00a00 0",
      "M\u000b0 0",
      "M\u000c0 0",
      "M 0 0 A 1 1 0 2 0 1 1",
      "M 0 0 Z 1",
    ];
    const valid = [
      "M0-1L2-3",
      "M1e-3-2e+2L0 0",
      "M0 0 1 1 2 2",
      "M0 0L1 1ZM2 2l1 0z",
      "M0 0A1 1 0 011 1",
      "M0.6.5",
      "M1.2.3L4.5.6",
    ];
    const scene = projectPuzzleScene(pathPuzzle, {
      ...emptySceneView,
      annotations: [
        ...malformed.map((d, index) => ({
          id: `malformed-${index}`,
          d,
          label: `Malformed ${index}`,
        })),
        ...valid.map((d, index) => ({
          id: `valid-${index}`,
          d,
          label: `Valid ${index}`,
        })),
      ],
    });
    for (const [index] of malformed.entries()) {
      expect(
        scene.nodes.find((node) => node.id === `annotation-malformed-${index}`),
      ).toMatchObject({
        d: "",
        geometryIssue: { code: "malformed-annotation-path" },
      });
    }
    for (const [index] of valid.entries()) {
      expect(
        scene.nodes.find((node) => node.id === `annotation-valid-${index}`),
      ).toMatchObject({
        d: expect.stringMatching(/^M /),
        geometryIssue: undefined,
      });
    }
  });

  it("isolates malformed annotations and valid annotations without a scene frame", () => {
    const pathPuzzle = structuredClone(puzzle);
    pathPuzzle.cells = {
      r1c1: {
        ...pathPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 10, height: 10 },
      },
    };
    pathPuzzle.groups = {};
    const malformedScene = projectPuzzleScene(pathPuzzle, {
      ...emptySceneView,
      annotations: [
        { id: "malformed", d: "M 0 0 L banana", label: "Malformed" },
      ],
    });
    expect(
      malformedScene.nodes.find((node) => node.id === "annotation-malformed"),
    ).toMatchObject({
      d: "",
      geometryIssue: {
        code: "malformed-annotation-path",
        affects: "topology-and-content",
      },
    });

    const invalidPuzzle = structuredClone(pathPuzzle);
    invalidPuzzle.cells.r1c1.shape = { kind: "polygon", points: [] };
    const invalidFrameScene = projectPuzzleScene(invalidPuzzle, {
      ...emptySceneView,
      annotations: [
        { id: "no-frame", d: "M 0 0 L 1 1", label: "No frame" },
      ],
    });
    expect(
      invalidFrameScene.nodes.find((node) => node.id === "annotation-no-frame"),
    ).toMatchObject({
      d: "",
      geometryIssue: {
        code: "annotation-frame-unavailable",
        affects: "topology-and-content",
      },
    });
    expect(invalidFrameScene.description).toMatch(
      /Annotation no-frame:.*normalization frame is unavailable/,
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

  it("reports omitted native members without diagnosing derived boundary atoms", () => {
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
      geometryIssues: [],
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
    expect(canvas).not.toHaveAccessibleDescription(/Group partial-review:/);
    expect(screen.getByTestId("group-border-partial-review")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("preserves a 2.5e-8 overlap without tolerance-merging its coordinates", () => {
    const overlapPuzzle = structuredClone(puzzle);
    const overlap = 2.5e-8;
    const nativeOffset = overlap * 2;
    overlapPuzzle.cells = {
      r1c1: {
        ...overlapPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
      },
      r1c2: {
        ...overlapPuzzle.cells.r1c2,
        shape: {
          kind: "rect",
          x: nativeOffset,
          y: -1,
          width: 1 - nativeOffset,
          height: 2,
        },
      },
      r1c3: {
        ...overlapPuzzle.cells.r1c3,
        shape: {
          kind: "rect",
          x: nativeOffset,
          y: -1,
          width: 1 - nativeOffset,
          height: 2,
        },
      },
    };
    overlapPuzzle.groups = {
      overlap: {
        id: "overlap",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2"],
      },
    };
    const border = projectPuzzleScene(
      overlapPuzzle,
      emptySceneView,
    ).nodes.find((node) => node.id === "group-border-overlap");
    if (border?.kind !== "path") {
      throw new Error("expected overlap group path");
    }
    const segments = parseLineSegments(border.d);
    const shortSegments = segments.filter(
      (segment) => Math.abs(segmentLength(segment) - overlap) < 1e-12,
    );
    expect(shortSegments).toHaveLength(2);
    expect(shortSegments).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          from: expect.objectContaining({ x: 0 }),
          to: expect.objectContaining({ x: overlap }),
        }),
        expect.objectContaining({
          from: expect.objectContaining({ x: overlap }),
          to: expect.objectContaining({ x: 0 }),
        }),
      ]),
    );
    expect(border.geometryIssues).toEqual([]);
  });

  it("preserves exact-threshold union atoms without diagnosing smaller derived atoms", () => {
    const projectExteriorAtoms = (normalizedFeature: number) => {
      const atomPuzzle = structuredClone(puzzle);
      const nativeOffset = normalizedFeature * 2;
      atomPuzzle.cells = {
        r1c1: {
          ...atomPuzzle.cells.r1c1,
          shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
        },
        r1c2: {
          ...atomPuzzle.cells.r1c2,
          shape: {
            kind: "rect",
            x: nativeOffset,
            y: -1,
            width: 1 - nativeOffset,
            height: 2,
          },
        },
        r1c3: {
          ...atomPuzzle.cells.r1c3,
          shape: {
            kind: "rect",
            x: nativeOffset,
            y: -1,
            width: 1 - nativeOffset,
            height: 2,
          },
        },
      };
      atomPuzzle.groups = {
        atoms: {
          id: "atoms",
          roles: ["region"],
          cellIds: ["r1c1", "r1c2"],
        },
      };
      const border = projectPuzzleScene(atomPuzzle, emptySceneView).nodes.find(
        (node) => node.id === "group-border-atoms",
      );
      if (border?.kind !== "path") {
        throw new Error("expected atomic group path");
      }
      return border;
    };

    const exact = projectExteriorAtoms(1e-9);
    const exactSegments = parseLineSegments(exact.d).filter(
      (segment) => Math.abs(segmentLength(segment) - 1e-9) < 1e-13,
    );
    expect(exactSegments).toHaveLength(2);
    expect(exactSegments).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          from: expect.objectContaining({ x: 0 }),
          to: expect.objectContaining({ x: 1e-9 }),
        }),
        expect.objectContaining({
          from: expect.objectContaining({ x: 1e-9 }),
          to: expect.objectContaining({ x: 0 }),
        }),
      ]),
    );
    expect(exact.geometryIssues).toEqual([]);

    const justBelow = projectExteriorAtoms(1e-9 - 2e-13);
    expect(justBelow.geometryIssues).toEqual([]);

    const subQuantum = projectExteriorAtoms(1e-13);
    expect(subQuantum.geometryIssues).toEqual([]);
  });

  it("does not diagnose sub-threshold split ratios derived during union", () => {
    const projectExteriorAtom = (normalizedFeature: number) => {
      const atomPuzzle = structuredClone(puzzle);
      const nativeOffset = normalizedFeature * 2;
      atomPuzzle.cells = {
        r1c1: {
          ...atomPuzzle.cells.r1c1,
          shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
        },
        r1c2: {
          ...atomPuzzle.cells.r1c2,
          shape: {
            kind: "rect",
            x: nativeOffset,
            y: -1,
            width: 1 - nativeOffset,
            height: 2,
          },
        },
      };
      atomPuzzle.groups = {
        atoms: {
          id: "atoms",
          roles: ["region"],
          cellIds: ["r1c1", "r1c2"],
        },
      };
      const border = projectPuzzleScene(atomPuzzle, emptySceneView).nodes.find(
        (node) => node.id === "group-border-atoms",
      );
      if (border?.kind !== "path") {
        throw new Error("expected atomic group path");
      }
      return border;
    };

    for (const feature of [1e-14, 3e-15, 1e-15]) {
      const border = projectExteriorAtom(feature);
      expect(border.geometryIssues, `feature ${feature}`).toEqual([]);
      expect(border.d).not.toMatch(/NaN|Infinity/);
      const segments = parseLineSegments(border.d);
      const canonicalSegments = segments.map((segment) =>
        [
          `${segment.from.x},${segment.from.y}`,
          `${segment.to.x},${segment.to.y}`,
        ]
          .sort()
          .join("/"),
      );
      expect(new Set(canonicalSegments).size).toBe(canonicalSegments.length);
    }

    const lowerRatio = 0.75;
    const upperRatio = lowerRatio + Number.EPSILON / 2;
    expect(upperRatio).toBeGreaterThan(lowerRatio);
    const adjacentPuzzle = structuredClone(puzzle);
    adjacentPuzzle.cells = {
      r1c1: {
        ...adjacentPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
      },
      r1c2: {
        ...adjacentPuzzle.cells.r1c2,
        shape: { kind: "rect", x: 0, y: 1, width: lowerRatio, height: 1 },
      },
      r1c3: {
        ...adjacentPuzzle.cells.r1c3,
        shape: {
          kind: "rect",
          x: upperRatio,
          y: 1,
          width: 1 - upperRatio,
          height: 1,
        },
      },
    };
    adjacentPuzzle.groups = {
      adjacent: {
        id: "adjacent",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2", "r1c3"],
      },
    };
    const adjacentScene = projectPuzzleScene(
      adjacentPuzzle,
      emptySceneView,
    );
    const adjacentBorder = adjacentScene.nodes.find(
      (node) => node.id === "group-border-adjacent",
    );
    expect(adjacentBorder).toMatchObject({
      geometryIssues: [],
      d: expect.not.stringMatching(/NaN|Infinity/),
    });
  });

  it("does not diagnose a derived adjacent-ratio atom when pointAt collapses", () => {
    const collapsedPuzzle = structuredClone(puzzle);
    const firstCut = 1.75;
    const secondCut = 1.7500000000000002;
    collapsedPuzzle.cells = {
      r1c1: {
        ...collapsedPuzzle.cells.r1c1,
        shape: {
          kind: "polygon",
          points: [
            { x: 1, y: 1 },
            { x: 2, y: 1 },
            { x: 2, y: 0 },
            { x: 1, y: 0 },
          ],
        },
      },
      r1c2: {
        ...collapsedPuzzle.cells.r1c2,
        shape: { kind: "rect", x: 1, y: 1, width: firstCut - 1, height: 1 },
      },
      r1c3: {
        ...collapsedPuzzle.cells.r1c3,
        shape: {
          kind: "rect",
          x: secondCut,
          y: 1,
          width: 2 - secondCut,
          height: 1,
        },
      },
      r1c4: {
        ...collapsedPuzzle.cells.r1c4,
        shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
      },
      r1c5: {
        ...collapsedPuzzle.cells.r1c5,
        shape: {
          kind: "polygon",
          points: [
            { x: 1, y: 0 },
            { x: 2, y: 0 },
            { x: 2, y: 1 },
            { x: 1, y: 1 },
          ],
        },
      },
    };
    collapsedPuzzle.groups = {
      collapsed: {
        id: "collapsed",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2", "r1c3", "r1c5", "r1c1"],
      },
    };
    const border = projectPuzzleScene(
      collapsedPuzzle,
      emptySceneView,
    ).nodes.find((node) => node.id === "group-border-collapsed");
    if (border?.kind !== "path") {
      throw new Error("expected collapsed-ratio group path");
    }

    expect(border.geometryIssues).toEqual([]);
    expect(border.d).not.toMatch(/NaN|Infinity/);
    const segments = parseLineSegments(border.d);
    const canonicalSegments = segments.map((segment) =>
      [
        `${segment.from.x},${segment.from.y}`,
        `${segment.to.x},${segment.to.y}`,
      ]
        .sort()
        .join("/"),
    );
    expect(new Set(canonicalSegments).size).toBe(canonicalSegments.length);
  });

  it("does not report a collapsed interval already covered by a redundant outer boundary", () => {
    const redundantPuzzle = structuredClone(puzzle);
    const firstCut = 1.75;
    const secondCut = 1.7500000000000002;
    redundantPuzzle.cells = {
      r1c1: {
        ...redundantPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 2, height: 2 },
      },
      r1c2: {
        ...redundantPuzzle.cells.r1c2,
        shape: { kind: "rect", x: 0, y: 0, width: firstCut, height: 1 },
      },
      r1c3: {
        ...redundantPuzzle.cells.r1c3,
        shape: {
          kind: "rect",
          x: secondCut,
          y: 0,
          width: 2 - secondCut,
          height: 1,
        },
      },
    };
    redundantPuzzle.groups = {
      redundant: {
        id: "redundant",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2", "r1c3"],
      },
    };

    const scene = projectPuzzleScene(redundantPuzzle, emptySceneView);
    const border = scene.nodes.find(
      (node) => node.id === "group-border-redundant",
    );
    if (border?.kind !== "path") {
      throw new Error("expected redundant group path");
    }

    expect(border.geometryIssues).toEqual([]);
    const segments = parseLineSegments(border.d);
    expect(totalSegmentLength(segments)).toBeCloseTo(4 * scene.width, 12);
    expect(
      segments.every(
        ({ from, to }) =>
          (from.x === 0 && to.x === 0) ||
          (from.x === scene.width && to.x === scene.width) ||
          (from.y === 0 && to.y === 0) ||
          (from.y === scene.height && to.y === scene.height),
      ),
    ).toBe(true);
  });

  it("deduplicates reversed redundant topology without inflating collapsed diagnostics", () => {
    const redundantPuzzle = structuredClone(puzzle);
    const firstCut = 1.75;
    const secondCut = 1.7500000000000002;
    const reversedRectangle = (
      minX: number,
      minY: number,
      maxX: number,
      maxY: number,
    ) => ({
      kind: "polygon" as const,
      points: [
        { x: minX, y: minY },
        { x: minX, y: maxY },
        { x: maxX, y: maxY },
        { x: maxX, y: minY },
      ],
    });
    redundantPuzzle.cells = {
      r1c1: {
        ...redundantPuzzle.cells.r1c1,
        shape: reversedRectangle(0, 0, 2, 2),
      },
      r1c2: {
        ...redundantPuzzle.cells.r1c2,
        shape: reversedRectangle(0, 0, firstCut, 1),
      },
      r1c3: {
        ...redundantPuzzle.cells.r1c3,
        shape: reversedRectangle(secondCut, 0, 2, 1),
      },
      r1c4: {
        ...redundantPuzzle.cells.r1c4,
        shape: reversedRectangle(0, 0, firstCut, 1),
      },
    };
    redundantPuzzle.groups = {
      redundant: {
        id: "redundant",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2", "r1c3", "r1c4", "r1c2", "r1c1"],
      },
    };

    const scene = projectPuzzleScene(redundantPuzzle, emptySceneView);
    const border = scene.nodes.find(
      (node) => node.id === "group-border-redundant",
    );
    if (border?.kind !== "path") {
      throw new Error("expected reversed redundant group path");
    }

    expect(border.geometryIssues).toEqual([]);
    const segments = parseLineSegments(border.d);
    expect(totalSegmentLength(segments)).toBeCloseTo(4 * scene.width, 12);
    const canonicalSegments = segments.map(({ from, to }) =>
      [`${from.x},${from.y}`, `${to.x},${to.y}`].sort().join("/"),
    );
    expect(new Set(canonicalSegments).size).toBe(canonicalSegments.length);
  });

  it("does not diagnose derived sub-threshold atoms in rotated redundant unions", () => {
    const rectangle = (
      minX: number,
      minY: number,
      maxX: number,
      maxY: number,
      reverse = false,
    ) => {
      const points = [
        { x: minX, y: minY },
        { x: maxX, y: minY },
        { x: maxX, y: maxY },
        { x: minX, y: maxY },
      ];
      return reverse ? points.reverse() : points;
    };
    const rotate = (points: readonly TestPoint[], angle: number) =>
      points.map(({ x, y }) => ({
        x: x * Math.cos(angle) - y * Math.sin(angle),
        y: x * Math.sin(angle) + y * Math.cos(angle),
      }));
    const firstCut = 1.75;
    const secondCut = 1.7500000000000002;

    const rotations = [
      {
        angle: 0.1,
        expectedOuter: [
          { x: 0.10845029669402097, y: 0 },
          { x: 1.1893358414402948, y: 0.10845029669402097 },
          { x: 1.0808855447462737, y: 1.1893358414402948 },
          { x: 0, y: 1.0808855447462737 },
        ],
      },
      {
        angle: Math.PI / 6,
        expectedOuter: [
          { x: 0.496143856670665, y: 0 },
          { x: 1.3554902242874278, y: 0.496143856670665 },
          { x: 0.8593463676167628, y: 1.3554902242874278 },
          { x: 0, y: 0.8593463676167628 },
        ],
      },
      {
        angle: Math.PI / 4,
        expectedOuter: [
          { x: 0.7272727272727272, y: 0 },
          { x: 1.4545454545454544, y: 0.7272727272727272 },
          { x: 0.7272727272727273, y: 1.4545454545454544 },
          { x: 0, y: 0.7272727272727273 },
        ],
      },
    ] as const;

    for (const { angle, expectedOuter } of rotations) {
      const redundantPuzzle = structuredClone(puzzle);
      const outer = rotate(rectangle(0, 0, 2, 2, true), angle);
      const firstInner = rotate(rectangle(0, 0, firstCut, 1), angle);
      const secondInner = rotate(rectangle(secondCut, 0, 2, 1, true), angle);
      redundantPuzzle.cells = {
        r1c1: {
          ...redundantPuzzle.cells.r1c1,
          shape: { kind: "polygon", points: outer },
        },
        r1c2: {
          ...redundantPuzzle.cells.r1c2,
          shape: { kind: "polygon", points: firstInner },
        },
        r1c3: {
          ...redundantPuzzle.cells.r1c3,
          shape: { kind: "polygon", points: secondInner },
        },
        r1c4: {
          ...redundantPuzzle.cells.r1c4,
          shape: { kind: "polygon", points: firstInner },
        },
      };
      redundantPuzzle.groups = {
        redundant: {
          id: "redundant",
          roles: ["region"],
          cellIds: ["r1c1", "r1c2", "r1c3", "r1c4", "r1c2", "r1c1"],
        },
      };

      const scene = projectPuzzleScene(redundantPuzzle, emptySceneView);
      const border = scene.nodes.find(
        (node) => node.id === "group-border-redundant",
      );
      if (border?.kind !== "path") {
        throw new Error("expected rotated redundant group path");
      }

      expect(border.geometryIssues, `angle ${angle}`).toEqual([]);
      const segments = parseLineSegments(border.d);
      expect(segments, `angle ${angle}`).toHaveLength(7);
      const expectedEdges = expectedOuter.map((from, index) => ({
        from,
        to: expectedOuter[(index + 1) % expectedOuter.length],
      }));
      const coverageByEdge = expectedEdges.map(() => [] as {
        start: number;
        end: number;
      }[]);
      const tolerance = 1e-9;

      for (const segment of segments) {
        const matches = expectedEdges.flatMap((edge, edgeIndex) => {
          const deltaX = edge.to.x - edge.from.x;
          const deltaY = edge.to.y - edge.from.y;
          const squaredLength = deltaX * deltaX + deltaY * deltaY;
          const edgeLength = Math.sqrt(squaredLength);
          const parameter = (point: TestPoint) =>
            ((point.x - edge.from.x) * deltaX +
              (point.y - edge.from.y) * deltaY) /
            squaredLength;
          const distance = (point: TestPoint) =>
            Math.abs(
              (point.x - edge.from.x) * deltaY -
                (point.y - edge.from.y) * deltaX,
            ) / edgeLength;
          const firstParameter = parameter(segment.from);
          const secondParameter = parameter(segment.to);
          if (
            distance(segment.from) > tolerance ||
            distance(segment.to) > tolerance ||
            firstParameter < -tolerance ||
            firstParameter > 1 + tolerance ||
            secondParameter < -tolerance ||
            secondParameter > 1 + tolerance
          ) {
            return [];
          }
          return [
            {
              edgeIndex,
              start: Math.min(firstParameter, secondParameter),
              end: Math.max(firstParameter, secondParameter),
            },
          ];
        });
        expect(
          matches,
          `angle ${angle}, segment ${JSON.stringify(segment)}`,
        ).toHaveLength(1);
        const [match] = matches;
        expect(match.end - match.start).toBeGreaterThan(tolerance);
        coverageByEdge[match.edgeIndex].push({
          start: match.start,
          end: match.end,
        });
      }

      for (const [edgeIndex, intervals] of coverageByEdge.entries()) {
        intervals.sort((first, second) => first.start - second.start);
        expect(
          intervals.length,
          `angle ${angle}, edge ${edgeIndex}`,
        ).toBeGreaterThan(0);
        let coveredThrough = 0;
        for (const interval of intervals) {
          expect(
            Math.abs(interval.start - coveredThrough),
            `angle ${angle}, edge ${edgeIndex}`,
          ).toBeLessThan(tolerance);
          coveredThrough = interval.end;
        }
        expect(
          Math.abs(coveredThrough - 1),
          `angle ${angle}, edge ${edgeIndex}`,
        ).toBeLessThan(tolerance);
      }
    }
  });

  it("recognizes collapsed boundary coverage split across emitted segments", () => {
    const redundantPuzzle = structuredClone(puzzle);
    const firstCut = 1.75;
    const secondCut = 1.7500000000000002;
    redundantPuzzle.cells = {
      r1c1: {
        ...redundantPuzzle.cells.r1c1,
        shape: { kind: "rect", x: 0, y: 0, width: 2, height: 2 },
      },
      r1c2: {
        ...redundantPuzzle.cells.r1c2,
        shape: { kind: "rect", x: 0, y: 0, width: 1, height: 1 },
      },
      r1c3: {
        ...redundantPuzzle.cells.r1c3,
        shape: {
          kind: "rect",
          x: 1,
          y: 0,
          width: firstCut - 1,
          height: 1,
        },
      },
      r1c4: {
        ...redundantPuzzle.cells.r1c4,
        shape: {
          kind: "rect",
          x: secondCut,
          y: 0,
          width: 2 - secondCut,
          height: 1,
        },
      },
    };
    redundantPuzzle.groups = {
      redundant: {
        id: "redundant",
        roles: ["region"],
        cellIds: ["r1c1", "r1c2", "r1c3", "r1c4"],
      },
    };

    const scene = projectPuzzleScene(redundantPuzzle, emptySceneView);
    const border = scene.nodes.find(
      (node) => node.id === "group-border-redundant",
    );
    if (border?.kind !== "path") {
      throw new Error("expected split-coverage group path");
    }

    expect(border.geometryIssues).toEqual([]);
    const topSegments = parseLineSegments(border.d)
      .filter(({ from, to }) => from.y === 0 && to.y === 0)
      .map(({ from, to }) => [Math.min(from.x, to.x), Math.max(from.x, to.x)] as const)
      .sort(([firstStart], [secondStart]) => firstStart - secondStart);
    expect(topSegments[0]?.[0]).toBe(0);
    expect(topSegments.at(-1)?.[1]).toBe(scene.width);
    expect(
      topSegments.slice(1).every(([start], index) => start <= topSegments[index][1]),
    ).toBe(true);
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

  it.each([
    ["possible", "Possible in at least one solution"],
    ["frequencyLow", "Low solution frequency"],
    ["frequencyMedium", "Medium solution frequency"],
    ["frequencyHigh", "High solution frequency"],
    ["both", "Possible and logical"],
    ["bruteForceOnly", "Brute-force possible only"],
    ["logicalOnly", "Logical candidate only"],
  ] as const)(
    "renders and accessibly describes the %s True Candidates cue",
    (tone, label) => {
      render(
        <PuzzleCanvas
          puzzle={puzzle}
          view={{
            ...emptySceneView,
            candidates: { r1c2: ["1"] },
            candidatePresentation: {
              r1c2: {
                "1": {
                  tone,
                  label,
                },
              },
            },
          }}
          onSelectCell={() => undefined}
        />,
      );

      const candidate = screen.getByText("1", {
        selector: ".puzzle-cell__candidate",
      });
      expect(candidate).toHaveClass(`puzzle-cell__candidate--${tone}`);
      expect(candidate).toHaveAttribute("data-candidate-label", label);
      expect(screen.getByTestId("cell-r1c2")).toHaveAttribute(
        "aria-label",
        `Cell r1c2, candidates 1 (${label})`,
      );
    },
  );

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
    const annotationSegments = readLineSegments(
      screen.getByRole("img", { name: "Focus link" }),
    );
    expect(annotationSegments).toHaveLength(1);
    expect(annotationSegments[0].from.x).toBeCloseTo(0.5, 9);
    expect(annotationSegments[0].from.y).toBeCloseTo(0.5, 9);
    expect(annotationSegments[0].to.x).toBeCloseTo(10.5, 9);
    expect(annotationSegments[0].to.y).toBeCloseTo(4.5, 9);
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

  it("preserves representable gaps while tracing split and reversed edges", () => {
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
    expect(segments.every((segment) => Number.isFinite(segmentLength(segment)))).toBe(
      true,
    );
    expect(
      segments.some((segment) => {
        const midpointY = (segment.from.y + segment.to.y) / 2;
        return Math.abs(midpointY - 1) < 1e-7;
      }),
    ).toBe(true);
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
