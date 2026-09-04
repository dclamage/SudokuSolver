import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import type {
  LogicalApplySolverRequest,
  LogicalCreateSolverRequest,
} from "../../solver/protocol";
import {
  createLogicalHarness,
  logicalResponse,
  logicalState,
  nakedSingle,
} from "../../test/logicalFixtures";

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
}

async function renderLiveLogical() {
  const { controller, solver } = createLogicalHarness();
  const user = userEvent.setup();
  render(<App controller={controller} />);
  await user.click(screen.getByRole("tab", { name: /Logical solver/i }));
  const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
  solver.resolve(
    request.requestId,
    logicalResponse(
      request,
      logicalState({ semanticRevision: 1, deductions: [nakedSingle] }),
    ),
  );
  await settle();
  return { controller, solver, user };
}

describe("Logical Solver panel and walkthrough", () => {
  it("cannot open the walkthrough while another candidate layer is active", () => {
    const { controller } = createLogicalHarness();
    controller.openWalkthrough();
    render(<App controller={controller} />);

    expect(controller.getSnapshot().walkthroughOpen).toBe(false);
    expect(screen.queryByRole("region", { name: "Logical walkthrough" })).toBeNull();
  });

  it.each([
    ["manual", { id: "logical-solver", name: "Replacement notes", kind: "manual" }, "setterNotes"],
    ["true candidates", {
      id: "logical-solver",
      name: "Replacement candidates",
      kind: "trueCandidates",
      refresh: "automatic",
      display: "possibility",
      solutionCountCap: 1,
    }, "trueCandidates"],
  ] as const)("closes when the active context ID is replaced by %s", async (_label, replacement, panelKind) => {
    const { controller, user } = await renderLiveLogical();
    await user.click(screen.getByRole("button", { name: "Open Walkthrough" }));
    screen.getByRole("button", { name: "Next Frame" }).focus();
    const puzzleDocument = structuredClone(controller.puzzle.getSnapshot().document);
    puzzleDocument.authoring.candidateContexts = puzzleDocument.authoring.candidateContexts.map(
      (context) => context.id === "logical-solver"
        ? replacement
        : context,
    );

    act(() => {
      controller.candidates.onPuzzleChanged({
        documentRevision: puzzleDocument.revision + 1,
        semanticRevision: puzzleDocument.semanticRevision,
        semanticHash: "sha256:logical-fixture",
        semantic: false,
        document: puzzleDocument,
      });
    });

    expect(controller.getSnapshot()).toMatchObject({
      workspace: "set",
      walkthroughOpen: false,
    });
    expect(controller.candidates.getPanelDescriptor().panelKind).toBe(panelKind);
    expect(screen.queryByRole("region", { name: "Logical walkthrough" })).toBeNull();
    expect(document.body).toHaveFocus();
  });

  it("keeps Next Step and logical controls out of the DOM until Logical Solver is active", async () => {
    const { controller, user } = await renderLiveLogical();
    expect(screen.getByRole("button", { name: "Next Step" })).toBeVisible();
    expect(screen.queryByText("True Candidates options")).toBeNull();
    expect(screen.queryByRole("button", { name: "Set Corner" })).toBeNull();

    await user.click(screen.getByRole("tab", { name: /Setter notes/i }));
    expect(screen.queryByRole("button", { name: "Next Step" })).toBeNull();
    expect(controller.candidates.getSceneProjection().annotations).toEqual([]);
  });

  it("selects deductions, changes frames without applying, and exposes affected stable entities", async () => {
    const { controller, solver, user } = await renderLiveLogical();

    expect(screen.getByText("basic.naked-single")).toBeVisible();
    expect(screen.getByText(/r1c1/)).toBeVisible();
    const requestCount = solver.requests.length;
    await user.click(screen.getByRole("button", { name: "Open Walkthrough" }));
    await user.click(screen.getByRole("button", { name: "Next Frame" }));

    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.selectedFrameIndex).toBe(1);
    expect(solver.requests).toHaveLength(requestCount);
    await user.click(screen.getByRole("button", { name: "Previous Frame" }));
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.selectedFrameIndex).toBe(0);
  });

  it("opens as a focused expansion, preserves workspace state, and restores focus on Back", async () => {
    const { controller, user } = await renderLiveLogical();
    const opener = screen.getByRole("button", { name: "Open Walkthrough" });
    opener.focus();
    await user.click(opener);

    expect(screen.getByRole("region", { name: "Logical walkthrough" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Set" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Playtest" })).toBeVisible();
    expect(screen.getByText("logical.nakedSingle.place")).toBeVisible();
    expect(screen.getByText("cell: r1c1")).toBeVisible();
    expect(screen.getAllByText("3").some((element) =>
      element.getAttribute("data-logical-emphasis") === "highlight"
    )).toBe(true);
    expect(controller.getSnapshot().workspace).toBe("set");

    await user.click(screen.getByRole("button", { name: "Back to workspace" }));
    expect(screen.queryByRole("region", { name: "Logical walkthrough" })).toBeNull();
    expect(screen.getByRole("button", { name: "Open Walkthrough" })).toHaveFocus();
  });

  it("applies one walkthrough step, renders history, and resets without touching puzzle revision", async () => {
    const { controller, solver, user } = await renderLiveLogical();
    const semanticRevision = controller.puzzle.getSnapshot().document.semanticRevision;
    await user.click(screen.getByRole("button", { name: "Open Walkthrough" }));
    await user.click(screen.getByRole("button", { name: "Next Step" }));
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({
      semanticRevision: 1,
      deductions: [],
      history: [nakedSingle.id],
      positionHash: "sha256:position-2",
    })));
    await settle();

    const history = screen.getByRole("complementary", { name: "Logical history" });
    expect(history).toHaveTextContent("basic.naked-single");
    expect(history).toHaveTextContent("Placement cell r1c1 = value 3");
    expect(history).toHaveTextContent("logical.nakedSingle.place");
    await user.click(screen.getByRole("button", { name: "Reset logical session" }));
    expect(solver.requests.at(-1)).toMatchObject({
      operation: "logical.create",
      logicalCreateOptions: { appliedDeductionIds: [] },
    });
    expect(controller.puzzle.getSnapshot().document.semanticRevision).toBe(semanticRevision);
  });

  it("shows archived revision history as read-only", async () => {
    const { controller, solver, user } = await renderLiveLogical();
    await user.click(screen.getByRole("button", { name: "Open Walkthrough" }));
    await user.click(screen.getByRole("button", { name: "Next Step" }));
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({
      semanticRevision: 1,
      deductions: [],
      history: [nakedSingle.id],
      positionHash: "sha256:position-2",
    })));
    await settle();
    controller.candidates.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:revision-2",
      semantic: true,
    });
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(
      request.requestId,
      logicalResponse(
        request,
        logicalState({ semanticRevision: 2, deductions: [] }),
      ),
    );
    await settle();

    expect(screen.getByText("Revision 1 · archived")).toBeVisible();
    expect(screen.getByText("Read-only history")).toBeVisible();
    const archived = screen.getByText("Revision 1 · archived").closest("section");
    expect(archived).toHaveTextContent("basic.naked-single");
    expect(archived).toHaveTextContent("Placement cell r1c1 = value 3");
    expect(archived).toHaveTextContent("logical.nakedSingle.place");
    expect(archived?.querySelector("button")).toBeNull();
  });
});
