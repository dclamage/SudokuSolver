import { describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../puzzle/createStarterPuzzle";
import type { CandidateContext, PuzzlePackageV1 } from "../puzzle/types";
import type { CountSolverRequest, SolverResultResponse } from "../../solver/protocol";
import { FakeSolverClient } from "../../test/FakeSolverClient";
import {
  CandidateContextController,
  type CandidateContextControllerOptions,
} from "./CandidateContextController";

const initialSemanticHash = "sha256:revision-1";

function createCandidateController(
  options: {
    activeContextId?: string;
    solver?: FakeSolverClient;
    prepareDocument?: (document: PuzzlePackageV1) => void;
  } = {},
) {
  const document = createStarterPuzzle(() => "candidate-test-puzzle");
  options.prepareDocument?.(document);
  const solver = options.solver ?? new FakeSolverClient();
  let requestNumber = 0;
  const controllerOptions: CandidateContextControllerOptions = {
    definitions: document.authoring.candidateContexts,
    activeContextId: options.activeContextId ?? "setter-notes",
    document,
    semanticHash: initialSemanticHash,
    solver,
    createRequestId: () => `candidate-request-${++requestNumber}`,
    schedule: (run) => {
      run();
      return () => undefined;
    },
  };
  return {
    controller: new CandidateContextController(controllerOptions),
    document,
    solver,
  };
}

function resultFor(
  request: CountSolverRequest,
  overrides: Partial<SolverResultResponse> = {},
): SolverResultResponse {
  return {
    protocolVersion: request.protocolVersion,
    requestId: request.requestId,
    operation: request.operation,
    documentRevision: request.documentRevision,
    semanticRevision: request.semanticRevision,
    semanticHash: request.semanticHash,
    contextId: request.contextId,
    kind: "result",
    count: {
      solutionCount: 1,
      maxSolutions: request.countOptions.maxSolutions,
      isClamped: false,
    },
    ...overrides,
  };
}

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
}

describe("CandidateContextController", () => {
  it("does not run an inactive automatic context", () => {
    const solver = new FakeSolverClient();
    const { controller } = createCandidateController({
      activeContextId: "setter-notes",
      solver,
    });

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:revision-2",
      semantic: true,
    });

    expect(controller.getSnapshot().contexts["true-candidates"].status).toBe(
      "stale",
    );
    expect(solver.requests).toHaveLength(0);
  });

  it("starts stale automatic work when activated", () => {
    const solver = new FakeSolverClient();
    const { controller } = createCandidateController({
      activeContextId: "setter-notes",
      solver,
    });
    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:revision-2",
      semantic: true,
    });

    controller.activate("true-candidates");

    expect(solver.requests[0].contextId).toBe("true-candidates");
    expect(controller.getSnapshot().contexts["true-candidates"].status).toBe(
      "calculating",
    );
  });

  it("leaves an on-request context stale until explicit refresh exists", () => {
    const { controller, solver } = createCandidateController({
      prepareDocument: (document) => {
        const definition = document.authoring.candidateContexts.find(
          (context) => context.id === "true-candidates",
        );
        if (definition?.kind === "trueCandidates") {
          definition.refresh = "onRequest";
        }
      },
    });

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:revision-2",
      semantic: true,
    });
    controller.activate("true-candidates");

    expect(controller.getSnapshot().contexts["true-candidates"].status).toBe(
      "stale",
    );
    expect(solver.requests).toHaveLength(0);
  });

  it("cancels automatic work when its native definition becomes on-request", () => {
    const { controller, document, solver } = createCandidateController();
    controller.activate("true-candidates");
    const request = solver.requests[0];
    const configuredDocument = structuredClone(document);
    configuredDocument.revision = 2;
    configuredDocument.authoring.candidateContexts =
      configuredDocument.authoring.candidateContexts.map((context) =>
        context.id === "true-candidates" && context.kind === "trueCandidates"
          ? { ...context, refresh: "onRequest" }
          : context,
      );

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 1,
      semanticHash: initialSemanticHash,
      semantic: false,
      document: configuredDocument,
    });

    expect(solver.canceledRequestIds).toEqual([request.requestId]);
    expect(controller.getSnapshot().contexts["true-candidates"].status).toBe(
      "stale",
    );
    expect(solver.requests).toHaveLength(1);
  });

  it("cancels active work when switching context and projects only the new focus", () => {
    const { controller, solver } = createCandidateController();
    controller.activate("true-candidates");
    const request = solver.requests[0];

    controller.activate("setter-notes");

    expect(solver.canceledRequestIds).toEqual([request.requestId]);
    expect(controller.getSceneProjection()).toEqual({
      contextId: "setter-notes",
      candidates: {},
      annotations: [],
    });
    expect(controller.getPanelDescriptor().contextId).toBe("setter-notes");
  });

  it.each([
    {
      semanticRevision: 2,
      semanticHash: initialSemanticHash,
      changeKind: "semantic revision",
    },
    {
      semanticRevision: 1,
      semanticHash: "sha256:changed-hash",
      changeKind: "semantic hash",
    },
  ] as const)(
    "cancels and replaces active automatic work when the $changeKind changes",
    ({ semanticRevision, semanticHash }) => {
      const { controller, solver } = createCandidateController();
      controller.activate("true-candidates");
      const firstRequest = solver.requests[0];

      controller.onPuzzleChanged({
        documentRevision: 2,
        semanticRevision,
        semanticHash,
        semantic: true,
      });

      expect(solver.canceledRequestIds).toEqual([firstRequest.requestId]);
      expect(solver.requests).toHaveLength(2);
      expect(solver.requests[1]).toMatchObject({
        contextId: "true-candidates",
        semanticRevision,
        semanticHash,
      });
    },
  );

  it("accepts a live result across cosmetic document revisions", async () => {
    const { controller, solver } = createCandidateController();
    controller.activate("true-candidates");
    const request = solver.requests[0] as CountSolverRequest;

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: request.semanticRevision,
      semanticHash: request.semanticHash,
      semantic: false,
    });
    solver.resolve(
      request.requestId,
      resultFor(request, { documentRevision: 1_000 }),
    );
    await settle();

    expect(solver.canceledRequestIds).toEqual([]);
    expect(controller.getSnapshot().contexts["true-candidates"]).toMatchObject({
      baseSemanticRevision: request.semanticRevision,
      baseSemanticHash: request.semanticHash,
      status: "live",
      error: null,
    });
  });

  it.each([
    ["request ID", { requestId: "other-request" }],
    ["protocol", { protocolVersion: 2 as 1 }],
    ["operation", { operation: "solve" as const }],
    ["semantic revision", { semanticRevision: 99 }],
    ["semantic hash", { semanticHash: "sha256:other" }],
    ["context ID", { contextId: "other-context" }],
  ] as const)("rejects a response with a mismatched %s", async (_field, mismatch) => {
    const { controller, solver } = createCandidateController();
    controller.activate("true-candidates");
    const request = solver.requests[0] as CountSolverRequest;

    solver.resolve(request.requestId, resultFor(request, mismatch));
    await settle();

    expect(controller.getSnapshot().contexts["true-candidates"]).toMatchObject({
      status: "stale",
      candidates: {},
      error: null,
    });
  });

  it("resolves behavior and panel kinds from native definitions rather than labels", () => {
    const { controller } = createCandidateController({
      prepareDocument: (document) => {
        const renamed: CandidateContext[] = document.authoring.candidateContexts.map(
          (context) => ({ ...context, name: `Renamed ${context.id}` }),
        );
        document.authoring.candidateContexts = renamed;
      },
    });

    expect(controller.getPanelDescriptor()).toMatchObject({
      contextId: "setter-notes",
      panelKind: "setterNotes",
      name: "Renamed setter-notes",
    });
    controller.activate("logical-solver");
    expect(controller.getPanelDescriptor()).toMatchObject({
      contextId: "logical-solver",
      panelKind: "logicalSolver",
      name: "Renamed logical-solver",
    });
  });

  it("projects current native Setter Notes marks after document changes", () => {
    const { controller, document } = createCandidateController({
      prepareDocument: (candidateDocument) => {
        candidateDocument.authoring.manualMarks["setter-notes"].r1c2 = ["4"];
      },
    });

    expect(controller.getSceneProjection().candidates).toEqual({
      r1c2: ["4"],
    });

    const updatedDocument = structuredClone(document);
    updatedDocument.revision = 2;
    updatedDocument.authoring.manualMarks["setter-notes"].r1c2 = ["4", "7"];
    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 1,
      semanticHash: initialSemanticHash,
      semantic: false,
      document: updatedDocument,
    });

    expect(controller.getSceneProjection().candidates).toEqual({
      r1c2: ["4", "7"],
    });
  });

  it("keeps an unknown context inert and preserves its opaque definition", () => {
    const opaqueContext = {
      id: "future-candidates",
      name: "Future candidates",
      kind: "futureCandidates",
      refreshPolicy: { mode: "future", delay: 12 },
    } as unknown as CandidateContext;
    const { controller, solver } = createCandidateController({
      prepareDocument: (document) => {
        document.authoring.candidateContexts = [
          ...document.authoring.candidateContexts,
          opaqueContext,
        ];
        document.authoring.manualMarks[opaqueContext.id] = {};
      },
    });

    controller.activate(opaqueContext.id);

    expect(controller.getSnapshot().definitions.at(-1)).toEqual(opaqueContext);
    expect(controller.getPanelDescriptor()).toMatchObject({
      contextId: opaqueContext.id,
      panelKind: "unsupported",
      contextKind: "futureCandidates",
      status: "idle",
    });
    expect(controller.getSceneProjection()).toEqual({
      contextId: opaqueContext.id,
      candidates: {},
      annotations: [],
    });
    expect(solver.requests).toHaveLength(0);
  });
});
