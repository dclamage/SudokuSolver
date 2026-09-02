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

function canonicalSegments(
  segments: readonly { from: TestPoint; to: TestPoint }[],
) {
  return segments
    .map((segment) => {
      const points = [segment.from, segment.to].sort(
        (first, second) => first.x - second.x || first.y - second.y,
      );
      return `${points[0].x},${points[0].y}|${points[1].x},${points[1].y}`;
    })
    .sort();
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

  it("projects polygon cells without replacing their native geometry", () => {
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

    expect(
      screen.getByTestId("cell-aux-1").querySelector("path"),
    ).toHaveAttribute("d", "M 10 4 L 11 4 L 10.5 5 Z");
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
      "M 10 4 H 11 V 5 H 10 Z",
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
      expect(liesOnOuterBoundary).toBe(true);
    }
  });

  it("keeps geometry tolerance independent of the canvas coordinate offset", () => {
    const translatedPuzzle = structuredClone(puzzle);
    translatedPuzzle.cells.r1c1.shape = {
      kind: "rect",
      x: 1_000_000_000,
      y: 1_000_000_000,
      width: 1,
      height: 1,
    };
    translatedPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 1_000_000_001,
      y: 1_000_000_000,
      width: 1,
      height: 1,
    };
    translatedPuzzle.groups["translated-region"] = {
      id: "translated-region",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={translatedPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    expect(
      readLineSegments(screen.getByTestId("group-border-translated-region")),
    ).toHaveLength(6);
  });

  it("preserves exact representable thin boundaries across offsets and a noisy overlap", () => {
    const thinPuzzle = structuredClone(puzzle);
    const width = 0.00001;
    const cases = [
      { cellId: "r1c1", groupId: "thin-zero", offset: 0 },
      { cellId: "r1c2", groupId: "thin-million", offset: 1_000_000 },
      { cellId: "r1c3", groupId: "thin-billion", offset: 1_000_000_000 },
    ] as const;
    for (const { cellId, groupId, offset } of cases) {
      thinPuzzle.cells[cellId].shape = {
        kind: "rect",
        x: offset,
        y: 0,
        width,
        height: 1,
      };
      thinPuzzle.groups[groupId] = {
        id: groupId,
        roles: ["region"],
        cellIds: [cellId],
      };
    }

    const overlapOffset = 1_000_000;
    const overlapRight = overlapOffset + width;
    const neighborStart = overlapRight - 0.00000496;
    const neighborEnd = neighborStart + 0.5;
    thinPuzzle.cells.r1c4.shape = {
      kind: "polygon",
      points: [
        { x: neighborEnd, y: 1 },
        { x: neighborEnd, y: 0 },
        { x: neighborStart, y: 0 },
        { x: neighborStart, y: 1 },
      ],
    };
    thinPuzzle.groups["thin-noisy-overlap"] = {
      id: "thin-noisy-overlap",
      roles: ["region"],
      cellIds: ["r1c2", "r1c4", "r1c2"],
    };

    render(
      <PuzzleCanvas
        puzzle={thinPuzzle}
        view={emptySceneView}
        onSelectCell={() => undefined}
      />,
    );

    for (const { groupId, offset } of cases) {
      const right = offset + width;
      const segments = readLineSegments(
        screen.getByTestId(`group-border-${groupId}`),
      );
      expect(canonicalSegments(segments)).toEqual(
        canonicalSegments([
          { from: { x: offset, y: 0 }, to: { x: right, y: 0 } },
          { from: { x: right, y: 0 }, to: { x: right, y: 1 } },
          { from: { x: right, y: 1 }, to: { x: offset, y: 1 } },
          { from: { x: offset, y: 1 }, to: { x: offset, y: 0 } },
        ]),
      );
      expect(totalSegmentLength(segments)).toBeCloseTo(
        2 * ((right - offset) + 1),
        12,
      );
    }

    const overlapSegments = readLineSegments(
      screen.getByTestId("group-border-thin-noisy-overlap"),
    );
    expect(canonicalSegments(overlapSegments)).toEqual(
      canonicalSegments([
        { from: { x: overlapOffset, y: 0 }, to: { x: neighborStart, y: 0 } },
        { from: { x: neighborStart, y: 0 }, to: { x: overlapRight, y: 0 } },
        { from: { x: overlapRight, y: 0 }, to: { x: neighborEnd, y: 0 } },
        { from: { x: neighborEnd, y: 0 }, to: { x: neighborEnd, y: 1 } },
        { from: { x: overlapOffset, y: 1 }, to: { x: neighborStart, y: 1 } },
        { from: { x: neighborStart, y: 1 }, to: { x: overlapRight, y: 1 } },
        { from: { x: overlapRight, y: 1 }, to: { x: neighborEnd, y: 1 } },
        { from: { x: overlapOffset, y: 0 }, to: { x: overlapOffset, y: 1 } },
      ]),
    );
    expect(totalSegmentLength(overlapSegments)).toBeCloseTo(
      2 * ((neighborEnd - overlapOffset) + 1),
      12,
    );
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

  it("traces overlapping polygon unions independent of orientation and translation", () => {
    const overlapPuzzle = structuredClone(puzzle);
    const square = [
      { x: 0, y: 0 },
      { x: 2, y: 0 },
      { x: 2, y: 2 },
      { x: 0, y: 2 },
    ];
    const diamond = [
      { x: 1, y: -1 },
      { x: 3, y: 1 },
      { x: 1, y: 3 },
      { x: -1, y: 1 },
    ];
    const translateAndReverse = (points: readonly TestPoint[]) =>
      [...points]
        .reverse()
        .map((point) => ({
          x: point.x + 1_000_000_000,
          y: point.y - 1_000_000_000,
        }));
    overlapPuzzle.cells.r1c1.shape = { kind: "polygon", points: square };
    overlapPuzzle.cells.r1c2.shape = { kind: "polygon", points: diamond };
    overlapPuzzle.cells.r1c3.shape = {
      kind: "polygon",
      points: translateAndReverse(square),
    };
    overlapPuzzle.cells.r1c4.shape = {
      kind: "polygon",
      points: translateAndReverse(diamond),
    };
    overlapPuzzle.groups["polygon-overlap"] = {
      id: "polygon-overlap",
      roles: ["region"],
      cellIds: ["r1c1", "r1c2"],
    };
    overlapPuzzle.groups["translated-polygon-overlap"] = {
      id: "translated-polygon-overlap",
      roles: ["region"],
      cellIds: ["r1c3", "r1c4"],
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
    const translated = readLineSegments(
      screen.getByTestId("group-border-translated-polygon-overlap"),
    );
    expect(original).toHaveLength(8);
    expect(translated).toHaveLength(8);
    expect(totalSegmentLength(original)).toBeCloseTo(8 * Math.SQRT2, 8);
    expect(totalSegmentLength(translated)).toBeCloseTo(8 * Math.SQRT2, 8);
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
      width: 0.001,
      height: 1,
    };
    thinPuzzle.cells.r1c2.shape = {
      kind: "rect",
      x: 0.001,
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
    expect(totalSegmentLength(segments)).toBeCloseTo(8.002, 8);
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

  it("bounds polygon interior work independently of elongated geometry size", () => {
    const projectElongatedPolygon = (width: number) => {
      const elongatedPuzzle = structuredClone(puzzle);
      elongatedPuzzle.cells.r1c1.shape = {
        kind: "polygon",
        points: [
          { x: 0, y: 0 },
          { x: width, y: 0 },
          { x: width, y: 1 },
          { x: 0, y: 1 },
        ],
      };
      const metrics = { interiorWorkUnits: 0 };
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

    const short = projectElongatedPolygon(2);
    const long = projectElongatedPolygon(100);

    expect(short.metrics.interiorWorkUnits).toBeGreaterThan(0);
    expect(long.metrics.interiorWorkUnits).toBe(short.metrics.interiorWorkUnits);
    expect(long.metrics.interiorWorkUnits).toBeLessThanOrEqual(8);
    expect(short.cell?.kind === "cell" ? short.cell.content : []).toHaveLength(1);
    expect(long.cell?.kind === "cell" ? long.cell.content : []).toHaveLength(1);
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
      "Solver participation: fully verified. Solver projection remains authoritative. Visual geometry unavailable: polygon has no usable interior.",
    );
    expect(screen.getByRole("button", { name: "Cell r1c2" })).toHaveAccessibleDescription(
      "Solver participation: unknown. Visual geometry unavailable: polygon has no usable interior.",
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
    expect(canvas).toHaveStyle({ aspectRatio: "11 / 9" });
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
