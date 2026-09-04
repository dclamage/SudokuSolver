import { describe, expect, it } from "vitest";

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
import type { LogicalDeduction, SolverResultResponse } from "../../solver/protocol";
import { SolverResponseError } from "../../solver/WasmSolverClient";

const secondSingle: LogicalDeduction = {
  ...nakedSingle,
  id: "sha256:naked-single-r1c2-4",
  preconditionHash: "sha256:position-2",
  delta: { placements: [{ cellId: "r1c2", valueId: "4" }], eliminations: [] },
  frames: [{
    focus: [{ kind: "cell", id: "r1c2" }],
    dim: [],
    highlight: [{ kind: "cell", id: "r1c2" }, { kind: "value", id: "4" }],
    explanation: { key: "logical.nakedSingle.place", arguments: [{ kind: "cell", value: "r1c2" }, { kind: "value", value: "4" }] },
  }],
};

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
}

describe("Logical Solver candidate behavior", () => {
  it("creates automatically only when the logical context becomes active", () => {
    const { controller, solver } = createLogicalHarness();
    expect(solver.requests.filter((request) => request.operation === "logical.create")).toHaveLength(0);

    controller.candidates.activate("logical-solver");

    expect(solver.requests.at(-1)).toMatchObject({
      operation: "logical.create",
      contextId: "logical-solver",
      logicalCreateOptions: {
        projectionId: "main-latin-square",
        appliedDeductionIds: [],
      },
    });
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].status).toBe("calculating");
  });

  it("accepts a typed session and projects stable candidates and only the selected frame", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;

    solver.resolve(request.requestId, logicalResponse(request, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();

    const runtime = controller.candidates.getSnapshot().contexts["logical-solver"];
    expect(runtime).toMatchObject({ status: "live", candidates: { r1c1: ["3"] } });
    expect(runtime.logical?.selectedDeductionId).toBe(nakedSingle.id);
    expect(controller.candidates.getSceneProjection().annotations).toEqual([
      { id: "logical-focus-cell-r1c1", entity: { kind: "cell", id: "r1c1" }, emphasis: "focus", label: "Focus cell r1c1" },
      { id: "logical-highlight-cell-r1c1", entity: { kind: "cell", id: "r1c1" }, emphasis: "highlight", label: "Highlight cell r1c1" },
      { id: "logical-highlight-value-3", entity: { kind: "value", id: "3" }, emphasis: "highlight", label: "Highlight value 3" },
    ]);

    controller.candidates.selectLogicalFrame(1);
    expect(controller.candidates.getSceneProjection().annotations.map((annotation) => annotation.id)).toEqual([
      "logical-dim-group-row-1",
      "logical-highlight-cell-r1c1",
    ]);
  });

  it("applies exactly one step, suppresses duplicates, and leaves puzzle, notes, and Playtest untouched", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.playtest.enterValue("r1c2", "4");
    const puzzleBefore = structuredClone(controller.puzzle.getSnapshot().document);
    const playtestBefore = controller.playtest.getSnapshot();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();

    const first = controller.candidates.nextLogicalStep(nakedSingle.id);
    const second = controller.candidates.nextLogicalStep(nakedSingle.id);
    const applyRequests = solver.requests.filter((request) => request.operation === "logical.apply");
    expect(applyRequests).toHaveLength(1);
    const apply = applyRequests[0] as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({ semanticRevision: 1, deductions: [], history: [nakedSingle.id], positionHash: "sha256:position-2" })));
    await Promise.all([first, second]);

    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.historyDeductionIds).toEqual([nakedSingle.id]);
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.appliedDeductions).toEqual([nakedSingle]);
    expect(controller.puzzle.getSnapshot().document).toEqual(puzzleBefore);
    expect(controller.playtest.getSnapshot()).toEqual(playtestBefore);
  });

  it("applies an explicitly requested available deduction even when another deduction was selected", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({
      semanticRevision: 1,
      deductions: [nakedSingle, { ...secondSingle, preconditionHash: "sha256:position-1" }],
    })));
    await settle();

    const promise = controller.candidates.nextLogicalStep(secondSingle.id);
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({
      semanticRevision: 1,
      deductions: [],
      history: [secondSingle.id],
      positionHash: "sha256:position-3",
    })));
    await promise;

    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.historyDeductionIds).toEqual([secondSingle.id]);
  });

  it("does not supersede the selected deduction while its apply is in flight", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    const alternate = { ...secondSingle, preconditionHash: "sha256:position-1" };
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle, alternate] })));
    await settle();

    const promise = controller.candidates.nextLogicalStep(nakedSingle.id);
    controller.candidates.selectLogicalDeduction(alternate.id);
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.selectedDeductionId).toBe(nakedSingle.id);
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({ semanticRevision: 1, deductions: [], history: [nakedSingle.id], positionHash: "sha256:position-2" })));
    await promise;
  });

  it("resets the logical board when the semantic revision changes", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const createRequest = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(createRequest.requestId, logicalResponse(createRequest, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();
    const applyPromise = controller.candidates.nextLogicalStep(nakedSingle.id);
    const applyRequest = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(applyRequest.requestId, logicalResponse(applyRequest, logicalState({ semanticRevision: 1, deductions: [], history: [nakedSingle.id] })));
    await applyPromise;

    controller.candidates.onPuzzleChanged({ documentRevision: 2, semanticRevision: 2, semanticHash: "sha256:revision-2", semantic: true });

    const runtime = controller.candidates.getSnapshot().contexts["logical-solver"];
    expect(runtime.status).toBe("calculating");
    expect(runtime.logical?.archivedRevisions).toHaveLength(1);
    expect(runtime.logical?.archivedRevisions[0]).toMatchObject({
      semanticRevision: 1,
      historyDeductionIds: [nakedSingle.id],
      appliedDeductions: [nakedSingle],
    });
    expect(solver.requests.at(-1)?.operation).toBe("logical.create");
  });

  it("retains a live session across cosmetic revisions and resets with empty history on request", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();
    const session = controller.candidates.getSnapshot().contexts["logical-solver"].logical?.sessionId;

    controller.candidates.onPuzzleChanged({ documentRevision: 9, semanticRevision: 1, semanticHash: create.semanticHash, semantic: false });
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.sessionId).toBe(session);
    expect(solver.requests.filter((request) => request.operation === "logical.create")).toHaveLength(1);

    controller.candidates.resetLogicalSession();
    expect((solver.requests.at(-1) as LogicalCreateSolverRequest).logicalCreateOptions.appliedDeductionIds).toEqual([]);
  });

  it("recreates stale native sessions with the successfully applied history", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();
    const firstPromise = controller.candidates.nextLogicalStep(nakedSingle.id);
    const firstApply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(firstApply.requestId, logicalResponse(firstApply, logicalState({
      semanticRevision: 1,
      deductions: [secondSingle],
      history: [nakedSingle.id],
      positionHash: "sha256:position-2",
    })));
    await firstPromise;
    const promise = controller.candidates.nextLogicalStep(secondSingle.id);
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, {
      protocolVersion: apply.protocolVersion,
      requestId: apply.requestId,
      documentRevision: apply.documentRevision,
      semanticRevision: apply.semanticRevision,
      semanticHash: apply.semanticHash,
      contextId: apply.contextId,
      operation: apply.operation,
      kind: "error",
      error: { code: "staleContext", message: "session expired" },
    });
    await promise;

    expect(solver.requests.at(-1)).toMatchObject({
      operation: "logical.create",
      logicalCreateOptions: { appliedDeductionIds: [nakedSingle.id] },
    });
    const replay = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(replay.requestId, logicalResponse(replay, logicalState({
      semanticRevision: 1,
      deductions: [secondSingle],
      history: [nakedSingle.id],
      positionHash: "sha256:position-2",
    })));
    await settle();
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].logical?.appliedDeductions).toEqual([nakedSingle]);
  });

  it("recovers when the real client rejects staleContext as a typed error", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();
    const promise = controller.candidates.nextLogicalStep(nakedSingle.id);
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;

    solver.reject(apply.requestId, new SolverResponseError("staleContext", "worker restarted"));
    await promise;

    expect(solver.requests.at(-1)).toMatchObject({
      operation: "logical.create",
      logicalCreateOptions: { appliedDeductionIds: [] },
    });
  });

  it.each([
    ["request", { requestId: "other-request" }],
    ["operation", { operation: "logical.apply" as const }],
    ["document revision", { documentRevision: 99 }],
    ["semantic revision", { semanticRevision: 99 }],
    ["semantic hash", { semanticHash: "sha256:other" }],
    ["context", { contextId: "other-context" }],
  ] as const)("rejects a mismatched %s response", async (_label, mismatch) => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(request.requestId, logicalResponse(
      request,
      logicalState({ semanticRevision: 1, deductions: [nakedSingle] }),
      mismatch as Partial<SolverResultResponse>,
    ));
    await settle();

    expect(controller.candidates.getSnapshot().contexts["logical-solver"]).toMatchObject({
      status: "stale",
      candidates: {},
    });
  });

  it("rejects malformed stable identifiers without publishing their board", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    const malformed = logicalState({ semanticRevision: 1, deductions: [nakedSingle] });
    solver.resolve(request.requestId, logicalResponse(request, {
      ...malformed,
      cells: [{ cellId: "not-a-cell", valueId: null, candidateValueIds: ["3"] }],
    }));
    await settle();

    expect(controller.candidates.getSnapshot().contexts["logical-solver"]).toMatchObject({
      status: "error",
      candidates: {},
      error: expect.stringContaining("requested projection"),
    });
  });

  it.each([
    ["missing", (cells: ReturnType<typeof logicalState>["cells"]) => cells.slice(0, -1)],
    ["extra", (cells: ReturnType<typeof logicalState>["cells"]) => [...cells, cells[0]]],
    ["reordered", (cells: ReturnType<typeof logicalState>["cells"]) => [cells[1], cells[0], ...cells.slice(2)]],
    ["auxiliary", (cells: ReturnType<typeof logicalState>["cells"]) => [
      ...cells.slice(0, -1),
      { cellId: "aux-1", valueId: null, candidateValueIds: ["1"] },
    ]],
  ] as const)("rejects a %s cell sequence outside the requested projection", async (_case, changeCells) => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    const state = logicalState({ semanticRevision: 1, deductions: [nakedSingle] });

    solver.resolve(request.requestId, logicalResponse(request, {
      ...state,
      cells: changeCells(state.cells),
    }));
    await settle();

    expect(controller.candidates.getSnapshot().contexts["logical-solver"]).toMatchObject({
      status: "error",
      candidates: {},
      error: expect.stringContaining("requested projection"),
    });
  });

  it("rejects malformed correlated progress without publishing it", () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    const malformed = logicalState({ semanticRevision: 1, deductions: [nakedSingle] });

    solver.progress(request.requestId, {
      ...logicalResponse(request, malformed),
      kind: "progress",
      logical: {
        ...malformed,
        cells: [{ cellId: "not-a-cell", valueId: null, candidateValueIds: ["3"] }],
      },
    });

    expect(solver.canceledRequestIds).toContain(request.requestId);
    expect(controller.candidates.getSnapshot().contexts["logical-solver"]).toMatchObject({
      status: "error",
      candidates: {},
      error: expect.stringContaining("requested projection"),
    });
  });

  it("rejects an apply result owned by a different logical session", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const create = solver.requests.at(-1) as LogicalCreateSolverRequest;
    solver.resolve(create.requestId, logicalResponse(create, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    await settle();
    const promise = controller.candidates.nextLogicalStep(nakedSingle.id);
    const apply = solver.requests.at(-1) as LogicalApplySolverRequest;
    solver.resolve(apply.requestId, logicalResponse(apply, logicalState({
      semanticRevision: 1,
      deductions: [],
      history: [nakedSingle.id],
      sessionId: "another-session",
      positionHash: "sha256:position-2",
    })));
    await promise;

    expect(controller.candidates.getSnapshot().contexts["logical-solver"]).toMatchObject({
      status: "error",
      error: expect.stringContaining("different session"),
    });
  });

  it("keeps an inactive logical context stale through semantic changes and initializes it on activation", () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.onPuzzleChanged({
      documentRevision: 3,
      semanticRevision: 3,
      semanticHash: "sha256:revision-3",
      semantic: true,
    });

    expect(solver.requests.filter((request) => request.operation === "logical.create")).toHaveLength(0);
    expect(controller.candidates.getSnapshot().contexts["logical-solver"].status).toBe("stale");
    controller.candidates.activate("logical-solver");
    expect(solver.requests.at(-1)).toMatchObject({ operation: "logical.create", semanticRevision: 3 });
  });

  it("ignores stale and cross-context responses without overwriting newer state", async () => {
    const { controller, solver } = createLogicalHarness();
    controller.candidates.activate("logical-solver");
    const request = solver.requests.at(-1) as LogicalCreateSolverRequest;
    controller.candidates.activate("setter-notes");
    expect(solver.canceledRequestIds).toContain(request.requestId);
    expect(() => solver.resolve(request.requestId, logicalResponse(request, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })))).toThrow();
    await settle();
    expect(controller.candidates.getSceneProjection().contextId).toBe("setter-notes");
  });
});
