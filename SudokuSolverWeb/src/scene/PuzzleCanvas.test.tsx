import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../domain/puzzle/createStarterPuzzle";
import { PuzzleCanvas } from "./PuzzleCanvas";
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

    const { container } = render(
      <PuzzleCanvas
        puzzle={longLabelPuzzle}
        view={{ ...emptySceneView, candidates: { r1c1: ["1"] } }}
        onSelectCell={() => undefined}
      />,
    );

    const candidate = screen.getByText(longLabel);
    const clipReference = candidate.getAttribute("clip-path");
    expect(clipReference).not.toBeNull();
    expect(clipReference ?? "").toMatch(/^url\(#candidate-clip-[^)]+\)$/);
    const clipId = clipReference?.slice(5, -1);
    expect(container.querySelector(`clipPath[id="${clipId}"] rect`)).not.toBeNull();
    expect(
      screen.getByRole("button", {
        name: `Cell r1c1, candidates ${longLabel}`,
      }),
    ).toBeVisible();
  });
});
