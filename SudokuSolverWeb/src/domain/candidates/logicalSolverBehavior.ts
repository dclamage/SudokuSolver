import type { PuzzlePackageV1 } from "../puzzle/types";
import type {
  LogicalDeduction,
  LogicalEntityReference,
  LogicalResult,
} from "../../solver/protocol";
import type {
  CandidateBehaviorInput,
  CandidateContextBehavior,
  CandidateSceneProjection,
  LogicalCandidateRuntime,
} from "./types";

const EMPTY_ACTIONS = Object.freeze({ manualCandidateEntry: false });
const EMPTY_CANDIDATES = Object.freeze({});
const EMPTY_VALUES = Object.freeze({});
const EMPTY_ANNOTATIONS = Object.freeze([]);

function nonempty(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function assertCellValue(
  document: PuzzlePackageV1,
  projectionCellIds: ReadonlySet<string>,
  projectionValueIds: ReadonlySet<string>,
  cellId: string,
  valueId: string,
) {
  const cell = document.cells[cellId];
  const domain = cell === undefined ? undefined : document.domains[cell.domainId];
  if (
    cell === undefined ||
    domain === undefined ||
    !projectionCellIds.has(cellId) ||
    !projectionValueIds.has(valueId) ||
    !domain.values.some((value) => value.id === valueId)
  ) {
    throw new Error(`Logical Solver returned unknown cell/value ${cellId}/${valueId}`);
  }
}

function assertEntity(
  document: PuzzlePackageV1,
  projectionCellIds: ReadonlySet<string>,
  projectionValueIds: ReadonlySet<string>,
  entity: LogicalEntityReference,
) {
  if (!nonempty(entity.kind) || !nonempty(entity.id)) {
    throw new Error("Logical Solver returned an invalid entity reference");
  }
  const valid =
    (entity.kind === "cell" && projectionCellIds.has(entity.id)) ||
    (entity.kind === "group" && document.groups[entity.id] !== undefined) ||
    (entity.kind === "constraint" && document.constraints.some((item) => item.id === entity.id)) ||
    (entity.kind === "value" && projectionValueIds.has(entity.id));
  if (!valid) {
    throw new Error(`Logical Solver returned unknown ${entity.kind} ${entity.id}`);
  }
}

export function validateLogicalResult(
  document: PuzzlePackageV1,
  projection: PuzzlePackageV1["solverProjections"][number],
  result: LogicalResult,
  expectedHistory: readonly string[],
): void {
  if (!nonempty(result.sessionId) || !nonempty(result.positionHash)) {
    throw new Error("Logical Solver returned an invalid session identity");
  }
  if (
    result.historyDeductionIds.length !== expectedHistory.length ||
    result.historyDeductionIds.some((id, index) => !nonempty(id) || id !== expectedHistory[index])
  ) {
    throw new Error("Logical Solver returned unexpected history");
  }
  const expectedCellIds = projection.cellIdsByRow.flat();
  if (
    result.cells.length !== expectedCellIds.length ||
    result.cells.some((cell, index) => cell.cellId !== expectedCellIds[index])
  ) {
    throw new Error(
      "Logical Solver returned cells outside the requested projection",
    );
  }
  const projectionValueIds = new Set(projection.valueIdsBySolverValue);
  const projectionCellIds = new Set(expectedCellIds);
  const seenCells = new Set<string>();
  for (const cell of result.cells) {
    if (!nonempty(cell.cellId) || seenCells.has(cell.cellId) || document.cells[cell.cellId] === undefined) {
      throw new Error("Logical Solver returned invalid cell identities");
    }
    seenCells.add(cell.cellId);
    if (cell.valueId !== null) {
      assertCellValue(
        document,
        projectionCellIds,
        projectionValueIds,
        cell.cellId,
        cell.valueId,
      );
      if (cell.candidateValueIds.length > 0) {
        throw new Error("Logical Solver returned candidates for a placed cell");
      }
    }
    const seenValues = new Set<string>();
    for (const valueId of cell.candidateValueIds) {
      assertCellValue(
        document,
        projectionCellIds,
        projectionValueIds,
        cell.cellId,
        valueId,
      );
      if (seenValues.has(valueId)) {
        throw new Error("Logical Solver returned duplicate candidates");
      }
      seenValues.add(valueId);
    }
  }
  const seenDeductions = new Set<string>();
  for (const deduction of result.availableDeductions) {
    validateDeduction(
      document,
      projectionCellIds,
      projectionValueIds,
      result.positionHash,
      deduction,
    );
    if (seenDeductions.has(deduction.id)) {
      throw new Error("Logical Solver returned duplicate deductions");
    }
    seenDeductions.add(deduction.id);
  }
}

function validateDeduction(
  document: PuzzlePackageV1,
  projectionCellIds: ReadonlySet<string>,
  projectionValueIds: ReadonlySet<string>,
  positionHash: string,
  deduction: LogicalDeduction,
) {
  if (
    !nonempty(deduction.id) ||
    !nonempty(deduction.techniqueId) ||
    !nonempty(deduction.owningConstraintId) ||
    deduction.preconditionHash !== positionHash ||
    deduction.frames.length === 0
  ) {
    throw new Error("Logical Solver returned an invalid deduction");
  }
  for (const placement of deduction.delta.placements) {
    assertCellValue(
      document,
      projectionCellIds,
      projectionValueIds,
      placement.cellId,
      placement.valueId,
    );
  }
  for (const elimination of deduction.delta.eliminations) {
    assertCellValue(
      document,
      projectionCellIds,
      projectionValueIds,
      elimination.cellId,
      elimination.valueId,
    );
  }
  for (const premise of deduction.premises) {
    if (!nonempty(premise.kind)) {
      throw new Error("Logical Solver returned an invalid premise");
    }
    if (premise.cellId !== null && premise.valueId !== null) {
      assertCellValue(
        document,
        projectionCellIds,
        projectionValueIds,
        premise.cellId,
        premise.valueId,
      );
    }
  }
  for (const frame of deduction.frames) {
    if (!nonempty(frame.explanation.key)) {
      throw new Error("Logical Solver returned an invalid explanation");
    }
    for (const entity of [...frame.focus, ...frame.dim, ...frame.highlight]) {
      assertEntity(
        document,
        projectionCellIds,
        projectionValueIds,
        entity,
      );
    }
    for (const argument of frame.explanation.arguments) {
      if (!nonempty(argument.kind) || !nonempty(argument.value)) {
        throw new Error("Logical Solver returned an invalid explanation argument");
      }
    }
  }
}

export function projectLogicalBoard(result: LogicalResult) {
  const candidates: Record<string, readonly string[]> = {};
  const values: Record<string, string> = {};
  for (const cell of result.cells) {
    if (cell.valueId === null) {
      candidates[cell.cellId] = Object.freeze([...cell.candidateValueIds]);
    } else {
      values[cell.cellId] = cell.valueId;
    }
  }
  return { candidates: Object.freeze(candidates), values: Object.freeze(values) };
}

export function projectLogicalRuntime(
  contextId: string,
  logical: LogicalCandidateRuntime | null | undefined,
): CandidateSceneProjection {
  const selected = logical?.availableDeductions.find(
    (deduction) => deduction.id === logical.selectedDeductionId,
  );
  const frame = selected?.frames[logical?.selectedFrameIndex ?? 0];
  const annotations = frame === undefined
    ? EMPTY_ANNOTATIONS
    : Object.freeze(
        ([
          ...frame.focus.map((entity) => ({ entity, emphasis: "focus" as const })),
          ...frame.dim.map((entity) => ({ entity, emphasis: "dim" as const })),
          ...frame.highlight.map((entity) => ({ entity, emphasis: "highlight" as const })),
        ]).map(({ entity, emphasis }) => Object.freeze({
          id: `logical-${emphasis}-${entity.kind}-${entity.id}`,
          entity,
          emphasis,
          label: `${emphasis[0].toUpperCase()}${emphasis.slice(1)} ${entity.kind} ${entity.id}`,
        })),
      );
  return Object.freeze({
    contextId,
    candidates: logical?.candidates ?? EMPTY_CANDIDATES,
    values: logical?.values ?? EMPTY_VALUES,
    annotations,
  });
}

export const logicalSolverBehavior: CandidateContextBehavior = Object.freeze({
  panelKind: "logicalSolver",
  actions: EMPTY_ACTIONS,
  activate(input: CandidateBehaviorInput) {
    if (input.runtime.status === "live" && input.runtime.baseSemanticRevision === input.semanticRevision && input.runtime.baseSemanticHash === input.semanticHash) {
      return { runtime: input.runtime, requestWork: false };
    }
    return {
      runtime: Object.freeze({ ...input.runtime, status: input.semanticHash === null ? "stale" : "calculating", error: null }),
      requestWork: input.semanticHash !== null,
    };
  },
  invalidate(input: CandidateBehaviorInput) {
    const logical = input.runtime.logical;
    return {
      runtime: Object.freeze({
        ...input.runtime,
        baseSemanticRevision: null,
        baseSemanticHash: null,
        status: input.active && input.semanticHash !== null ? "calculating" : "stale",
        candidates: EMPTY_CANDIDATES,
        values: EMPTY_VALUES,
        error: null,
        logical: logical === undefined ? undefined : Object.freeze({
          ...logical,
          sessionId: null,
          positionHash: null,
          candidates: EMPTY_CANDIDATES,
          values: EMPTY_VALUES,
          availableDeductions: Object.freeze([]),
          historyDeductionIds: Object.freeze([]),
          appliedDeductions: Object.freeze([]),
          selectedDeductionId: null,
          selectedFrameIndex: 0,
          applyingDeductionId: null,
        }),
      }),
      requestWork: input.active && input.semanticHash !== null,
    };
  },
  getSceneProjection(input: CandidateBehaviorInput) {
    return projectLogicalRuntime(input.definition.id, input.runtime.logical);
  },
});
