import type { PuzzlePackageV1 } from "../domain/puzzle/types";

export const SOLVER_PROTOCOL_VERSION = 1 as const;

export type SolverOperation =
  | "validate"
  | "solve"
  | "count"
  | "trueCandidates"
  | "logical.create"
  | "logical.apply";

interface SolverRequestEnvelope {
  protocolVersion: typeof SOLVER_PROTOCOL_VERSION;
  requestId: string;
  documentRevision: number;
  semanticRevision: number;
  semanticHash: string;
  contextId: string;
  puzzle: PuzzlePackageV1;
}

export interface ValidateSolverRequest extends SolverRequestEnvelope {
  operation: "validate";
  validateOptions: { projectionId: string };
}

export interface SolveSolverRequest extends SolverRequestEnvelope {
  operation: "solve";
  solveOptions: { projectionId: string };
}

export interface CountSolverRequest extends SolverRequestEnvelope {
  operation: "count";
  countOptions: { projectionId: string; maxSolutions: number };
}

export interface TrueCandidatesSolverRequest extends SolverRequestEnvelope {
  operation: "trueCandidates";
  trueCandidatesOptions: {
    projectionId: string;
    display: "possibility" | "solutionFrequency" | "logicComparison";
    solutionCountCap: number;
  };
}

export interface LogicalCreateSolverRequest extends SolverRequestEnvelope {
  operation: "logical.create";
  logicalCreateOptions: {
    projectionId: string;
    appliedDeductionIds: readonly string[];
  };
}

export interface LogicalApplySolverRequest extends SolverRequestEnvelope {
  operation: "logical.apply";
  logicalApplyOptions: {
    projectionId: string;
    sessionId: string;
    positionHash: string;
    deductionId: string;
  };
}

export type SolverRequest =
  | ValidateSolverRequest
  | SolveSolverRequest
  | CountSolverRequest
  | TrueCandidatesSolverRequest
  | LogicalCreateSolverRequest
  | LogicalApplySolverRequest;

interface SolverResponseEnvelope {
  protocolVersion: typeof SOLVER_PROTOCOL_VERSION;
  requestId: string;
  documentRevision: number;
  semanticRevision: number;
  semanticHash: string;
  contextId: string;
  operation: SolverOperation;
}

export type CapabilityStatus =
  | "invalidDefinition"
  | "fullyVerified"
  | "partiallyVerified"
  | "visualOnly";

export interface CapabilityEntityResult {
  entityKind: string;
  entityId: string;
  status: CapabilityStatus;
  reason?: string;
}

export interface CapabilityResult {
  projectionId: string;
  contradiction: boolean;
  entities: Record<string, CapabilityEntityResult>;
}

export interface SolveResult {
  valuesByCellId: Record<string, string>;
}

export interface CountResult {
  solutionCount: number;
  maxSolutions: number;
  isClamped: boolean;
}

export interface TrueCandidatesResult {
  cellIds: string[];
  valueIdsBySolverValue: string[];
  solutionCounts: number[];
  logicalCandidateMasks?: number[];
  solutionCountCap: number;
}

export interface LogicalCellState {
  cellId: string;
  valueId: string | null;
  candidateValueIds: readonly string[];
}

export interface LogicalEntityReference {
  kind: string;
  id: string;
}

export interface LogicalExplanationArgument {
  kind: string;
  value: string;
}

export interface LogicalWalkthroughFrame {
  focus: readonly LogicalEntityReference[];
  dim: readonly LogicalEntityReference[];
  highlight: readonly LogicalEntityReference[];
  explanation: {
    key: string;
    arguments: readonly LogicalExplanationArgument[];
  };
}

export interface LogicalDeduction {
  id: string;
  techniqueId: string;
  owningConstraintId: string;
  preconditionHash: string;
  premises: readonly {
    kind: string;
    cellId: string | null;
    valueId: string | null;
  }[];
  delta: {
    placements: readonly { cellId: string; valueId: string }[];
    eliminations: readonly { cellId: string; valueId: string }[];
  };
  frames: readonly LogicalWalkthroughFrame[];
}

export interface LogicalResult {
  sessionId: string;
  positionHash: string;
  cells: readonly LogicalCellState[];
  availableDeductions: readonly LogicalDeduction[];
  historyDeductionIds: readonly string[];
}

export interface SolverError {
  code: string;
  message: string;
}

export interface SolverResultResponse extends SolverResponseEnvelope {
  kind: "result";
  capability?: CapabilityResult;
  solve?: SolveResult;
  count?: CountResult;
  trueCandidates?: TrueCandidatesResult;
  logical?: LogicalResult;
}

export interface SolverProgressResponse extends SolverResponseEnvelope {
  kind: "progress";
  count?: CountResult;
  trueCandidates?: TrueCandidatesResult;
  logical?: LogicalResult;
}

export interface SolverErrorResponse extends SolverResponseEnvelope {
  kind: "error";
  error: SolverError;
}

export interface SolverCanceledResponse extends SolverResponseEnvelope {
  kind: "canceled";
}

export type SolverResponse =
  | SolverResultResponse
  | SolverProgressResponse
  | SolverErrorResponse
  | SolverCanceledResponse;
