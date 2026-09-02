import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../domain/puzzle/createStarterPuzzle";
import { PuzzleCanvas } from "./PuzzleCanvas";
import type { PuzzleSceneView } from "./types";

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
});
