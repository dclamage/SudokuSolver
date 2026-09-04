import { describe, expect, it } from "vitest";

import type {
  SolverResultResponse,
  TrueCandidatesResult,
  TrueCandidatesSolverRequest,
} from "../../solver/protocol";
import { FakeSolverClient } from "../../test/FakeSolverClient";
import { createStarterPuzzle } from "../puzzle/createStarterPuzzle";
import type { PuzzlePackageV1 } from "../puzzle/types";
import { CandidateContextController } from "./CandidateContextController";
import { projectTrueCandidates } from "./trueCandidatesBehavior";

const hash = "sha256:true-candidates";

function result(
  solutionCounts: number[],
  overrides: Partial<TrueCandidatesResult> = {},
): TrueCandidatesResult {
  return {
    cellIds: ["r1c1", "r1c2"],
    valueIdsBySolverValue: ["1", "2", "3"],
    solutionCounts,
    solutionCountCap: 4,
    ...overrides,
  };
}

function responseFor(
  request: TrueCandidatesSolverRequest,
  candidates: TrueCandidatesResult,
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
    trueCandidates: candidates,
  };
}

function nativeResultFor(
  request: TrueCandidatesSolverRequest,
): TrueCandidatesResult {
  const projection = request.puzzle.solverProjections[0];
  const cellIds = projection.cellIdsByRow.flat();
  const counts = Array.from(
    { length: cellIds.length * projection.valueIdsBySolverValue.length },
    (_, index) => (index === 0 ? 1 : 0),
  );
  return {
    cellIds,
    valueIdsBySolverValue: [...projection.valueIdsBySolverValue],
    solutionCounts: counts,
    logicalCandidateMasks:
      request.trueCandidatesOptions.display === "logicComparison"
        ? Array.from({ length: cellIds.length }, () => 0)
        : undefined,
    solutionCountCap: request.trueCandidatesOptions.solutionCountCap,
  };
}

function createHarness(options: {
  active?: boolean;
  refresh?: "automatic" | "onRequest";
} = {}) {
  const document = createStarterPuzzle(() => "true-candidate-test");
  const trueCandidates = document.authoring.candidateContexts.find(
    (context) => context.id === "true-candidates",
  );
  if (trueCandidates?.kind === "trueCandidates" && options.refresh !== undefined) {
    trueCandidates.refresh = options.refresh;
  }
  const solver = new FakeSolverClient();
  const scheduled: { run: () => void; canceled: boolean }[] = [];
  let requestNumber = 0;
  const controller = new CandidateContextController({
    definitions: document.authoring.candidateContexts,
    activeContextId: options.active === true ? "true-candidates" : "setter-notes",
    document,
    semanticHash: hash,
    solver,
    createRequestId: () => `true-candidate-request-${++requestNumber}`,
    schedule: (run) => {
      const pending = { run, canceled: false };
      scheduled.push(pending);
      return () => {
        pending.canceled = true;
      };
    },
  });
  return { controller, document, solver, scheduled };
}

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
}

describe("true candidate projection", () => {
  it("projects only candidates with a positive solution count in possibility mode", () => {
    expect(projectTrueCandidates(result([0, 1, 2, 3, 0, 4]), "possibility"))
      .toMatchObject({
        candidates: { r1c1: ["2", "3"], r1c2: ["1", "3"] },
        legend: [{ tone: "possible", label: "Possible in at least one solution" }],
      });
  });

  it("assigns deterministic low, medium, and high frequency buckets", () => {
    const projection = projectTrueCandidates(
      result([1, 2, 4, 0, 0, 0]),
      "solutionFrequency",
    );

    expect(projection.presentation.r1c1).toEqual({
      "1": { tone: "frequencyLow", label: "Low solution frequency" },
      "2": { tone: "frequencyMedium", label: "Medium solution frequency" },
      "3": { tone: "frequencyHigh", label: "High solution frequency" },
    });
    expect(projection.legend.map((entry) => entry.label)).toEqual([
      "Low solution frequency",
      "Medium solution frequency",
      "High solution frequency",
    ]);
  });

  it("labels the union of brute-force and logical candidates in comparison mode", () => {
    const projection = projectTrueCandidates(
      result([1, 0, 1, 0, 0, 0], { logicalCandidateMasks: [0b011, 0] }),
      "logicComparison",
    );

    expect(projection.candidates.r1c1).toEqual(["1", "2", "3"]);
    expect(projection.presentation.r1c1).toEqual({
      "1": { tone: "both", label: "Possible and logical" },
      "2": { tone: "logicalOnly", label: "Logical candidate only" },
      "3": { tone: "bruteForceOnly", label: "Brute-force possible only" },
    });
  });
});

describe("true candidate context behavior", () => {
  it("debounces semantic changes and cancels replaced scheduled work", () => {
    const { controller, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:second",
      semantic: true,
    });

    expect(scheduled).toHaveLength(2);
    expect(scheduled[0].canceled).toBe(true);
    scheduled[0].run();
    expect(solver.requests).toHaveLength(0);
    scheduled[1].run();
    expect(solver.requests).toHaveLength(1);
    expect(solver.requests[0]).toMatchObject({
      operation: "trueCandidates",
      semanticRevision: 2,
      trueCandidatesOptions: {
        display: "possibility",
        solutionCountCap: 1,
      },
    });
  });

  it("keeps on-request work stale until Refresh is invoked", () => {
    const { controller, scheduled } = createHarness({
      active: true,
      refresh: "onRequest",
    });

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:second",
      semantic: true,
    });
    expect(scheduled).toHaveLength(0);

    controller.refresh();
    expect(scheduled).toHaveLength(1);
  });

  it("defers inactive automatic work until that context is activated", () => {
    const { controller, scheduled } = createHarness();
    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 2,
      semanticHash: "sha256:second",
      semantic: true,
    });
    expect(scheduled).toHaveLength(0);

    controller.activate("true-candidates");
    expect(scheduled).toHaveLength(1);
  });

  it("retains a live result across cosmetic document revisions", async () => {
    const { controller, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    scheduled[0].run();
    const request = solver.requests[0] as TrueCandidatesSolverRequest;
    solver.resolve(request.requestId, responseFor(request, nativeResultFor(request)));
    await settle();

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 1,
      semanticHash: hash,
      semantic: false,
    });

    expect(controller.getSnapshot().contexts["true-candidates"]).toMatchObject({
      status: "live",
      baseSemanticRevision: 1,
      candidates: { r1c1: ["1"] },
    });
  });

  it("projects correlated progress without marking the revision solved", () => {
    const { controller, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    scheduled[0].run();
    const request = solver.requests[0] as TrueCandidatesSolverRequest;
    const progress = nativeResultFor(request);

    solver.progress(request.requestId, {
      ...responseFor(request, progress),
      kind: "progress",
    });

    expect(controller.getSnapshot().contexts["true-candidates"]).toMatchObject({
      status: "calculating",
      baseSemanticRevision: null,
      candidates: { r1c1: ["1"] },
      progress: { discoveredCandidates: 1, candidateSlots: 729 },
    });
  });

  it("rejects a correlated result with mismatched projected identifiers", async () => {
    const { controller, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    scheduled[0].run();
    const request = solver.requests[0] as TrueCandidatesSolverRequest;
    const mismatched = nativeResultFor(request);
    mismatched.cellIds = [...mismatched.cellIds].reverse();

    solver.resolve(request.requestId, responseFor(request, mismatched));
    await settle();

    expect(controller.getSnapshot().contexts["true-candidates"]).toMatchObject({
      status: "error",
      candidates: {},
      error: "True Candidates returned identifiers outside the requested projection",
    });
  });

  it("cancels work when a different native document replaces the source", () => {
    const { controller, document, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    scheduled[0].run();
    const request = solver.requests[0] as TrueCandidatesSolverRequest;
    const replacement = structuredClone(document);
    replacement.id = "replacement-document";
    replacement.revision = 2;

    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 1,
      semanticHash: hash,
      semantic: false,
      document: replacement,
    });

    expect(solver.canceledRequestIds).toEqual([request.requestId]);
    expect(scheduled).toHaveLength(2);
    expect(controller.getSnapshot().contexts["true-candidates"].status).toBe("calculating");
  });

  it("ignores a retained callback after the active configuration changes", async () => {
    const { controller, document, scheduled, solver } = createHarness({ active: true });
    controller.refresh();
    scheduled[0].run();
    const request = solver.requests[0] as TrueCandidatesSolverRequest;
    const configured = structuredClone(document) as PuzzlePackageV1;
    configured.revision = 2;
    configured.authoring.candidateContexts = configured.authoring.candidateContexts.map(
      (context) => context.id === "true-candidates" && context.kind === "trueCandidates"
        ? { ...context, display: "solutionFrequency", solutionCountCap: 8 }
        : context,
    );
    controller.onPuzzleChanged({
      documentRevision: 2,
      semanticRevision: 1,
      semanticHash: hash,
      semantic: false,
      document: configured,
    });

    expect(() => solver.resolve(
      request.requestId,
      responseFor(request, nativeResultFor(request)),
    )).toThrow(`no pending solver request ${request.requestId}`);
    await settle();
    expect(controller.getSnapshot().contexts["true-candidates"].status).not.toBe("live");
  });
});
