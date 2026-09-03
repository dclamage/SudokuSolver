import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { App } from "../../app/App";
import { createTestAppController } from "../../test/createTestAppController";

describe("Playtest workspace", () => {
  it("playtest entries never mutate puzzle givens", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c1"));
    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));

    expect(controller.playtest.getSnapshot().values.r1c1).toBe("5");
    expect(
      controller.puzzle.getSnapshot().document.givens.r1c1,
    ).toBeUndefined();
  });

  it("keeps candidate marks, colors, and history in the solve session", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c2"));
    await userEvent.click(screen.getByRole("button", { name: "Corner" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Toggle corner 4" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Color" }));
    await userEvent.click(screen.getByRole("button", { name: "Apply cyan" }));

    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toEqual(["4"]);
    expect(controller.playtest.getSnapshot().colors.r1c2).toEqual(["cyan"]);
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();

    await userEvent.click(screen.getByRole("button", { name: "Undo" }));
    expect(controller.playtest.getSnapshot().colors.r1c2).toBeUndefined();
    await userEvent.click(screen.getByRole("button", { name: "Redo" }));
    expect(controller.playtest.getSnapshot().colors.r1c2).toEqual(["cyan"]);
  });

  it("renders corner marks, centre marks, and cell colors distinctly", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c2"));
    await userEvent.click(screen.getByRole("button", { name: "Corner" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Toggle corner 4" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Centre" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Toggle centre 5" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Color" }));
    await userEvent.click(screen.getByRole("button", { name: "Apply cyan" }));

    expect(screen.getByTestId("corner-candidates-r1c2")).toHaveTextContent(
      "4",
    );
    expect(screen.getByTestId("centre-candidates-r1c2")).toHaveTextContent(
      "5",
    );
    expect(screen.getByTestId("cell-r1c2")).toHaveAttribute(
      "data-cell-fill",
      "cyan",
    );
    expect(screen.getByRole("button", { name: /Cell r1c2/ })).toHaveAccessibleName(
      /corner candidates 4; centre candidates 5; color cyan/i,
    );
  });

  it("hides the digit keypad when color or erase is active", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByRole("button", { name: "Color" }));
    expect(screen.queryByLabelText("Playtest keypad")).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Color shortcut 1" }),
    ).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Erase" }));
    expect(screen.queryByLabelText("Playtest keypad")).toBeNull();
    expect(
      screen.getByRole("button", { name: "Erase selected cell" }),
    ).toBeVisible();
  });

  it("removes and guards manual candidate modes outside Setter Notes", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c2"));
    await userEvent.click(screen.getByRole("button", { name: "Corner" }));
    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));

    expect(screen.queryByRole("button", { name: "Corner" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Centre" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Erase" })).toBeNull();
    expect(screen.getByRole("button", { name: "Color" })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Enter 4" }));
    expect(controller.playtest.getSnapshot().values.r1c2).toBe("4");
    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toBeUndefined();
  });

  it(
    "keeps persisted Setter Notes marks out of the Playtest session",
    async () => {
      const controller = createTestAppController({
        preparePuzzle: (puzzle) => {
          puzzle.authoring.manualMarks["setter-notes"].r1c2 = ["4"];
        },
      });
      render(<App controller={controller} />);

      expect(screen.getByTestId("candidates-r1c2")).toHaveTextContent("4");
      await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));

      expect(screen.queryByTestId("candidates-r1c2")).toBeNull();
      await userEvent.click(screen.getByTestId("cell-r1c2"));
      await userEvent.click(screen.getByRole("button", { name: "Corner" }));
      await userEvent.click(
        screen.getByRole("button", { name: "Toggle corner 7" }),
      );
      expect(screen.getByTestId("corner-candidates-r1c2")).toHaveTextContent(
        "7",
      );
      expect(
        controller.puzzle.getSnapshot().document.authoring.manualMarks[
          "setter-notes"
        ].r1c2,
      ).toEqual(["4"]);
    },
  );

  it("starts on entering Playtest, ticks visibly, and pauses in Set", () => {
    vi.useFakeTimers();
    try {
      const controller = createTestAppController();
      controller.testDependencies.clock.advance(30_000);
      act(() => controller.setWorkspace("playtest"));
      render(<App controller={controller} />);

      expect(screen.getByLabelText("Elapsed time")).toHaveTextContent("0:00");

      act(() => {
        controller.testDependencies.clock.advance(61_000);
        vi.advanceTimersByTime(1_000);
      });
      expect(screen.getByLabelText("Elapsed time")).toHaveTextContent("1:01");

      act(() => controller.setWorkspace("set"));
      controller.testDependencies.clock.advance(60_000);
      act(() => controller.setWorkspace("playtest"));
      expect(screen.getByLabelText("Elapsed time")).toHaveTextContent("1:01");
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps rules and checking available without another workspace", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByRole("button", { name: "Rules" }));
    expect(screen.getByRole("region", { name: "Rules" })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Check" }));
    expect(screen.getByRole("status")).toHaveTextContent(
      "Basic check: no duplicate values in uniqueness groups. Other constraints are not checked.",
    );
    expect(screen.queryByRole("tab", { name: "Analyze" })).toBeNull();
  });

  it("does not treat a presentation-only group as a uniqueness rule", async () => {
    const controller = createTestAppController({
      preparePuzzle: (puzzle) => {
        puzzle.groups.guide = {
          id: "guide",
          roles: ["presentation-guide"],
          cellIds: ["r1c1", "r4c4"],
        };
      },
    });
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c1"));
    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));
    await userEvent.click(screen.getByTestId("cell-r4c4"));
    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));
    await userEvent.click(screen.getByRole("button", { name: "Check" }));

    expect(screen.getByRole("status")).toHaveTextContent(
      "Basic check: no duplicate values in uniqueness groups. Other constraints are not checked.",
    );
  });

  it("starts ambient validation for load and semantic puzzle commits only", async () => {
    const controller = createTestAppController();

    const solver = controller.testDependencies.solver;
    await waitFor(() => expect(solver.requests).toHaveLength(1));
    const loadRequest = solver.requests[0];
    expect(loadRequest).toMatchObject({
      operation: "validate",
      contextId: "document-validation",
      documentRevision: 1,
      semanticRevision: 1,
    });
    expect(loadRequest.semanticHash).toMatch(/^sha256:[0-9a-f]{64}$/);

    controller.puzzle.execute({
      type: "moveCell",
      cellId: "aux-1",
      x: 10.25,
      y: 4,
    });
    await Promise.resolve();
    expect(solver.requests).toHaveLength(1);

    controller.puzzle.execute({
      type: "setGiven",
      cellId: "r1c1",
      valueId: "5",
    });
    await waitFor(() => expect(solver.requests).toHaveLength(2));
    expect(solver.canceledRequestIds).toEqual([loadRequest.requestId]);
    expect(solver.requests[1]).toMatchObject({
      operation: "validate",
      documentRevision: 3,
      semanticRevision: 2,
    });
  });

  it("shares the correlated document capability map across both canvases", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    const solver = controller.testDependencies.solver;
    await waitFor(() => expect(solver.requests).toHaveLength(1));
    const request = solver.requests[0];
    solver.resolve(request.requestId, {
      protocolVersion: request.protocolVersion,
      requestId: request.requestId,
      documentRevision: request.documentRevision,
      semanticRevision: request.semanticRevision,
      semanticHash: request.semanticHash,
      contextId: request.contextId,
      operation: request.operation,
      kind: "result",
      capability: {
        projectionId: "main-latin-square",
        contradiction: false,
        entities: {
          "cell:r1c1": {
            entityKind: "cell",
            entityId: "r1c1",
            status: "fullyVerified",
            reason: "Validated in the document projection.",
          },
        },
      },
    });

    await waitFor(() =>
      expect(screen.getByTestId("cell-r1c1")).toHaveAttribute(
        "data-solver-participation",
        "fullyVerified",
      ),
    );

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    expect(screen.getByTestId("cell-r1c1")).toHaveAttribute(
      "data-solver-participation",
      "fullyVerified",
    );
  });

  it.each([
    ["partiallyVerified", "Validation limited · Partial solver coverage"],
    ["visualOnly", "Validation limited · Visual-only semantics"],
    ["invalidDefinition", "Invalid puzzle definition"],
  ] as const)(
    "announces %s capability precisely in Set and Playtest",
    async (capabilityStatus, expectedLabel) => {
      const controller = createTestAppController();
      render(<App controller={controller} />);
      const solver = controller.testDependencies.solver;
      await waitFor(() => expect(solver.requests).toHaveLength(1));
      const request = solver.requests[0];
      solver.resolve(request.requestId, {
        protocolVersion: request.protocolVersion,
        requestId: request.requestId,
        documentRevision: request.documentRevision,
        semanticRevision: request.semanticRevision,
        semanticHash: request.semanticHash,
        contextId: request.contextId,
        operation: request.operation,
        kind: "result",
        capability: {
          projectionId: "main-latin-square",
          contradiction: false,
          entities: {
            "constraint:reviewed": {
              entityKind: "constraint",
              entityId: "reviewed",
              status: capabilityStatus,
            },
          },
        },
      });

      await waitFor(() =>
        expect(
          screen.getByRole("status", { name: "Puzzle status" }),
        ).toHaveTextContent(expectedLabel),
      );
      await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
      expect(
        screen.getByRole("status", { name: "Puzzle status" }),
      ).toHaveTextContent(expectedLabel);
    },
  );
});
