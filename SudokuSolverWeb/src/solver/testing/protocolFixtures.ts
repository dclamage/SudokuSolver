import fixture from "../../../../test-fixtures/native/classic-with-auxiliary.json";
import { validatePuzzlePackage } from "../../domain/puzzle/validatePuzzlePackage";
import {
  SOLVER_PROTOCOL_VERSION,
  type CountSolverRequest,
  type SolverProgressResponse,
  type SolverErrorResponse,
  type SolverResultResponse,
  type ValidateSolverRequest,
} from "../protocol";

const puzzle = validatePuzzlePackage(fixture);

const requestDefaults: ValidateSolverRequest = {
  protocolVersion: SOLVER_PROTOCOL_VERSION,
  requestId: "validate-request",
  documentRevision: 1,
  semanticRevision: 1,
  semanticHash:
    "sha256:b8489ce1173b87c3b8a004a0c69364b16e4a3994cbbc654489e3cc8077837536",
  contextId: "playtest",
  operation: "validate",
  puzzle,
  validateOptions: { projectionId: "main-latin-square" },
};

export function makeValidateRequest(
  overrides: Partial<ValidateSolverRequest> = {},
): ValidateSolverRequest {
  return {
    ...requestDefaults,
    ...overrides,
    validateOptions:
      overrides.validateOptions ?? requestDefaults.validateOptions,
  };
}

export function makeValidateErrorResponse(
  overrides: Partial<SolverErrorResponse> = {},
): SolverErrorResponse {
  return {
    kind: "error",
    protocolVersion: SOLVER_PROTOCOL_VERSION,
    requestId: requestDefaults.requestId,
    documentRevision: requestDefaults.documentRevision,
    semanticRevision: requestDefaults.semanticRevision,
    semanticHash: requestDefaults.semanticHash,
    contextId: requestDefaults.contextId,
    operation: "validate",
    error: {
      code: "contradiction",
      message: "The projected puzzle is contradictory.",
    },
    ...overrides,
  };
}

export function makeValidateResponse(
  overrides: Partial<SolverResultResponse> = {},
): SolverResultResponse {
  return {
    kind: "result",
    protocolVersion: SOLVER_PROTOCOL_VERSION,
    requestId: requestDefaults.requestId,
    documentRevision: requestDefaults.documentRevision,
    semanticRevision: requestDefaults.semanticRevision,
    semanticHash: requestDefaults.semanticHash,
    contextId: requestDefaults.contextId,
    operation: "validate",
    capability: {
      projectionId: "main-latin-square",
      contradiction: false,
      entities: {},
    },
    ...overrides,
  };
}

export function makeCountRequest(
  overrides: Partial<CountSolverRequest> = {},
): CountSolverRequest {
  return {
    protocolVersion: SOLVER_PROTOCOL_VERSION,
    requestId: "count-request",
    documentRevision: requestDefaults.documentRevision,
    semanticRevision: requestDefaults.semanticRevision,
    semanticHash: requestDefaults.semanticHash,
    contextId: requestDefaults.contextId,
    operation: "count",
    puzzle,
    ...overrides,
    countOptions: overrides.countOptions ?? {
      projectionId: "main-latin-square",
      maxSolutions: 8,
    },
  };
}

export function makeCountProgressResponse(
  overrides: Partial<SolverProgressResponse> = {},
): SolverProgressResponse {
  return {
    kind: "progress",
    protocolVersion: SOLVER_PROTOCOL_VERSION,
    requestId: "count-request",
    documentRevision: requestDefaults.documentRevision,
    semanticRevision: requestDefaults.semanticRevision,
    semanticHash: requestDefaults.semanticHash,
    contextId: requestDefaults.contextId,
    operation: "count",
    count: { solutionCount: 3, maxSolutions: 8, isClamped: false },
    ...overrides,
  };
}

export function makeCountResultResponse(
  overrides: Partial<SolverResultResponse> = {},
): SolverResultResponse {
  return {
    kind: "result",
    protocolVersion: SOLVER_PROTOCOL_VERSION,
    requestId: "count-request",
    documentRevision: requestDefaults.documentRevision,
    semanticRevision: requestDefaults.semanticRevision,
    semanticHash: requestDefaults.semanticHash,
    contextId: requestDefaults.contextId,
    operation: "count",
    count: { solutionCount: 8, maxSolutions: 8, isClamped: true },
    ...overrides,
  };
}
