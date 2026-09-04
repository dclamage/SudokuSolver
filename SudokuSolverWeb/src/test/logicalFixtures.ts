import type {
  LogicalDeduction,
  LogicalResult,
  SolverRequest,
  SolverResultResponse,
} from "../solver/protocol";
import { createTestAppController } from "./createTestAppController";

export const nakedSingle: LogicalDeduction = Object.freeze({
  id: "sha256:naked-single-r1c1-3",
  techniqueId: "basic.naked-single",
  owningConstraintId: "builtin:latin-square",
  preconditionHash: "sha256:position-1",
  premises: Object.freeze([]),
  delta: Object.freeze({
    placements: Object.freeze([{ cellId: "r1c1", valueId: "3" }]),
    eliminations: Object.freeze([]),
  }),
  frames: Object.freeze([
    Object.freeze({
      focus: Object.freeze([{ kind: "cell", id: "r1c1" }]),
      dim: Object.freeze([]),
      highlight: Object.freeze([
        { kind: "cell", id: "r1c1" },
        { kind: "value", id: "3" },
      ]),
      explanation: Object.freeze({
        key: "logical.nakedSingle.place",
        arguments: Object.freeze([
          { kind: "cell", value: "r1c1" },
          { kind: "value", value: "3" },
        ]),
      }),
    }),
    Object.freeze({
      focus: Object.freeze([]),
      dim: Object.freeze([{ kind: "group", id: "row-1" }]),
      highlight: Object.freeze([{ kind: "cell", id: "r1c1" }]),
      explanation: Object.freeze({
        key: "logical.nakedSingle.confirm",
        arguments: Object.freeze([{ kind: "cell", value: "r1c1" }]),
      }),
    }),
  ]),
});

const MAIN_CELL_IDS = Array.from(
  { length: 9 },
  (_, row) => Array.from({ length: 9 }, (_value, column) => `r${row + 1}c${column + 1}`),
).flat();
const DIGIT_VALUE_IDS = Array.from({ length: 9 }, (_value, index) => `${index + 1}`);

export function logicalState({
  semanticRevision,
  deductions,
  history = [],
  sessionId = `logical-session-${semanticRevision}`,
  positionHash = deductions[0]?.preconditionHash ?? `sha256:position-${semanticRevision}`,
}: {
  semanticRevision: number;
  deductions: readonly LogicalDeduction[];
  history?: readonly string[];
  sessionId?: string;
  positionHash?: string;
}): LogicalResult {
  return {
    sessionId,
    positionHash,
    cells: MAIN_CELL_IDS.map((cellId) => ({
      cellId,
      valueId: cellId === "r1c1" && history.length > 0 ? "3" : null,
      candidateValueIds:
        cellId === "r1c1" && history.length === 0
          ? ["3"]
          : cellId === "r1c1"
            ? []
            : DIGIT_VALUE_IDS,
    })),
    availableDeductions: [...deductions],
    historyDeductionIds: [...history],
  };
}

export function logicalResponse(
  request: SolverRequest,
  logical: LogicalResult,
  overrides: Partial<SolverResultResponse> = {},
): SolverResultResponse {
  return {
    protocolVersion: request.protocolVersion,
    requestId: request.requestId,
    documentRevision: request.documentRevision,
    semanticRevision: request.semanticRevision,
    semanticHash: request.semanticHash,
    contextId: request.contextId,
    operation: request.operation,
    kind: "result",
    logical,
    ...overrides,
  };
}

export function createLogicalHarness() {
  const controller = createTestAppController();
  const solver = controller.testDependencies.solver;
  const document = controller.puzzle.getSnapshot().document;
  controller.candidates.onPuzzleChanged({
    documentRevision: document.revision,
    semanticRevision: document.semanticRevision,
    semanticHash: "sha256:logical-fixture",
    semantic: true,
    document,
  });
  return { controller, solver };
}
