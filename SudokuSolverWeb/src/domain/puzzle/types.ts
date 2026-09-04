export type CellId = string;
export type ValueId = string;
export type CandidateContextId = string;

export const manualColorTokens = ["cyan", "green", "yellow", "rose"] as const;
export type ManualColorToken = (typeof manualColorTokens)[number];
export type ManualMarkKind = "corner" | "centre";

export interface ManualCellNotes {
  corner: readonly ValueId[];
  centre: readonly ValueId[];
  color: ManualColorToken | null;
}

export function isManualColorToken(value: unknown): value is ManualColorToken {
  return (
    typeof value === "string" &&
    (manualColorTokens as readonly string[]).includes(value)
  );
}

export type JsonValue =
  | null
  | boolean
  | number
  | string
  | readonly JsonValue[]
  | { readonly [key: string]: JsonValue };

export interface DomainValue {
  id: ValueId;
  label: string;
  numericValue?: number;
}

export type EntityReference = {
  kind: "cell" | "group" | "edge" | "point" | "path";
  id: string;
};

export type CellShape =
  | { kind: "rect"; x: number; y: number; width: number; height: number }
  | { kind: "polygon"; points: readonly { x: number; y: number }[] }
  | { kind: "path"; d: string };

export interface ConstraintInstance {
  id: string;
  typeId: string;
  definitionReleaseId?: string;
  bindings: Readonly<Record<string, readonly EntityReference[]>>;
  parameters: Readonly<Record<string, JsonValue>>;
  styleOverrides?: Readonly<Record<string, JsonValue>>;
}

export interface CandidateContext {
  id: CandidateContextId;
  name: string;
  kind: string;
  [property: string]: JsonValue;
}

export interface ManualCandidateContext extends CandidateContext {
  kind: "manual";
}

export interface TrueCandidatesContext extends CandidateContext {
  kind: "trueCandidates";
  refresh: "automatic" | "onRequest";
  display: "possibility" | "solutionFrequency" | "logicComparison";
  solutionCountCap: number;
}

export interface LogicalSolverCandidateContext extends CandidateContext {
  kind: "logicalSolver";
  followPuzzleRevision: true;
  enabledTechniqueIds: readonly string[];
}

export type KnownCandidateContext =
  | ManualCandidateContext
  | TrueCandidatesContext
  | LogicalSolverCandidateContext;

function hasValidCandidateContextIdentity(context: CandidateContext): boolean {
  return (
    typeof context.id === "string" &&
    context.id.length > 0 &&
    typeof context.name === "string" &&
    context.name.length > 0 &&
    typeof context.kind === "string" &&
    context.kind.length > 0
  );
}

function isJsonValue(value: unknown, ancestors = new Set<object>()): boolean {
  if (
    value === null ||
    typeof value === "boolean" ||
    typeof value === "string"
  ) {
    return true;
  }
  if (typeof value === "number") {
    return Number.isFinite(value);
  }
  if (typeof value !== "object") {
    return false;
  }
  if (ancestors.has(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  if (
    !Array.isArray(value) &&
    prototype !== Object.prototype &&
    prototype !== null
  ) {
    return false;
  }
  ancestors.add(value);
  const items = Array.isArray(value) ? Array.from(value) : Object.values(value);
  const valid = items.every((item) => isJsonValue(item, ancestors));
  ancestors.delete(value);
  return valid;
}

function hasValidTrueCandidatesConfiguration(
  candidate: Record<string, unknown>,
): boolean {
  return (
    (candidate.refresh === "automatic" || candidate.refresh === "onRequest") &&
    (candidate.display === "possibility" ||
      candidate.display === "solutionFrequency" ||
      candidate.display === "logicComparison") &&
    typeof candidate.solutionCountCap === "number" &&
    Number.isSafeInteger(candidate.solutionCountCap) &&
    candidate.solutionCountCap >= 1 &&
    candidate.solutionCountCap <= 1024
  );
}

function hasValidTechniqueIds(value: unknown): value is readonly string[] {
  return (
    Array.isArray(value) &&
    value.every(
      (techniqueId) =>
        typeof techniqueId === "string" && techniqueId.length > 0,
    )
  );
}

export function isManualCandidateContext(
  context: CandidateContext,
): context is ManualCandidateContext {
  return hasValidCandidateContextIdentity(context) && context.kind === "manual";
}

export function isTrueCandidatesContext(
  context: CandidateContext,
): context is TrueCandidatesContext {
  const candidate = context as unknown as Record<string, unknown>;
  return (
    hasValidCandidateContextIdentity(context) &&
    context.kind === "trueCandidates" &&
    hasValidTrueCandidatesConfiguration(candidate)
  );
}

export function isLogicalSolverCandidateContext(
  context: CandidateContext,
): context is LogicalSolverCandidateContext {
  const candidate = context as unknown as Record<string, unknown>;
  return (
    hasValidCandidateContextIdentity(context) &&
    context.kind === "logicalSolver" &&
    candidate.followPuzzleRevision === true &&
    hasValidTechniqueIds(candidate.enabledTechniqueIds)
  );
}

export function assertValidCandidateContext(
  context: unknown,
): asserts context is CandidateContext {
  if (typeof context !== "object" || context === null || Array.isArray(context)) {
    throw new Error("candidate context must be an object");
  }
  const candidate = context as Record<string, unknown>;
  if (typeof candidate.id !== "string" || candidate.id.length === 0) {
    throw new Error("candidate context ID must be a non-empty string");
  }
  if (typeof candidate.name !== "string" || candidate.name.length === 0) {
    throw new Error("candidate context name must be a non-empty string");
  }
  if (typeof candidate.kind !== "string" || candidate.kind.length === 0) {
    throw new Error("candidate context kind must be a non-empty string");
  }
  if (!isJsonValue(candidate)) {
    throw new Error(`candidate context ${candidate.id} must be valid JSON`);
  }
  if (candidate.kind === "manual") {
    return;
  }
  if (candidate.kind === "trueCandidates") {
    if (candidate.refresh !== "automatic" && candidate.refresh !== "onRequest") {
      throw new Error(
        `candidate context ${candidate.id} refresh must be automatic or onRequest`,
      );
    }
    if (
      candidate.display !== "possibility" &&
      candidate.display !== "solutionFrequency" &&
      candidate.display !== "logicComparison"
    ) {
      throw new Error(`candidate context ${candidate.id} display is invalid`);
    }
    if (
      typeof candidate.solutionCountCap !== "number" ||
      !Number.isSafeInteger(candidate.solutionCountCap) ||
      candidate.solutionCountCap < 1 ||
      candidate.solutionCountCap > 1024
    ) {
      throw new Error("solutionCountCap must be an integer between 1 and 1024");
    }
    return;
  }
  if (candidate.kind === "logicalSolver") {
    if (candidate.followPuzzleRevision !== true) {
      throw new Error(
        `candidate context ${candidate.id} followPuzzleRevision must be true`,
      );
    }
    if (
      !hasValidTechniqueIds(candidate.enabledTechniqueIds)
    ) {
      throw new Error(
        `candidate context ${candidate.id} enabledTechniqueIds must be an array of non-empty strings`,
      );
    }
  }
}

export interface PuzzlePackageV1 {
  schemaVersion: 1;
  id: string;
  revision: number;
  semanticRevision: number;
  metadata: { title: string; author: string; rules: string };
  domains: Record<string, { id: string; values: readonly DomainValue[] }>;
  cells: Record<
    CellId,
    {
      id: CellId;
      domainId: string;
      shape: CellShape;
      input: { acceptsValue: boolean; acceptsCandidates: boolean };
      label?: string;
    }
  >;
  boards: Record<
    string,
    {
      id: string;
      name: string;
      cellIds: readonly CellId[];
      groupIds: readonly string[];
    }
  >;
  groups: Record<
    string,
    { id: string; roles: readonly string[]; cellIds: readonly CellId[] }
  >;
  adjacency: Record<
    string,
    {
      id: string;
      kind: string;
      fromCellId: CellId;
      toCellId: CellId;
    }
  >;
  points: Record<string, { id: string; x: number; y: number }>;
  edges: Record<
    string,
    { id: string; fromPointId: string; toPointId: string }
  >;
  paths: Record<
    string,
    { id: string; pointIds: readonly string[]; closed: boolean }
  >;
  givens: Record<CellId, ValueId>;
  constraints: readonly ConstraintInstance[];
  solverProjections: readonly {
    id: string;
    kind: "latin-square";
    boardId: string;
    domainId: string;
    valueIdsBySolverValue: readonly ValueId[];
    cellIdsByRow: readonly (readonly CellId[])[];
  }[];
  presentation: {
    styles: Readonly<Record<string, JsonValue>>;
    sceneElements: readonly JsonValue[];
  };
  source?: JsonValue;
  release?: JsonValue;
  assets?: readonly JsonValue[];
  provenance?: JsonValue;
  authoring: {
    candidateContexts: readonly CandidateContext[];
    manualMarks: Record<
      CandidateContextId,
      Record<CellId, ManualCellNotes>
    >;
  };
  extensions: Readonly<
    Record<string, { impact: "semantic" | "cosmetic"; data: JsonValue }>
  >;
}
