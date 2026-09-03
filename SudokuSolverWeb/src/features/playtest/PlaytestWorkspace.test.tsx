import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

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

  it("keeps rules and checking available without another workspace", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByRole("button", { name: "Rules" }));
    expect(screen.getByRole("region", { name: "Rules" })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Check" }));
    expect(screen.getByRole("status")).toHaveTextContent("No conflicts found");
    expect(screen.queryByRole("tab", { name: "Analyze" })).toBeNull();
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
});
