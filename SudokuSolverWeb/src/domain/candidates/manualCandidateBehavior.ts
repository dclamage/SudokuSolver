import type {
  CandidateBehaviorInput,
  CandidateContextBehavior,
  CandidateContextRuntime,
} from "./types";

const EMPTY_ANNOTATIONS = Object.freeze([]);
const MANUAL_ACTIONS = Object.freeze({ manualCandidateEntry: true });

function withLiveStatus(
  input: CandidateBehaviorInput,
): CandidateContextRuntime {
  return Object.freeze({
    ...input.runtime,
    baseSemanticRevision: input.semanticRevision,
    baseSemanticHash: input.semanticHash,
    status: "live",
    error: null,
  });
}

export const manualCandidateBehavior: CandidateContextBehavior =
  Object.freeze({
    panelKind: "setterNotes",
    actions: MANUAL_ACTIONS,
    activate(input: CandidateBehaviorInput) {
      return { runtime: withLiveStatus(input), requestWork: false };
    },
    invalidate(input: CandidateBehaviorInput) {
      return input.active
        ? { runtime: withLiveStatus(input), requestWork: false }
        : { runtime: input.runtime, requestWork: false };
    },
    getSceneProjection(input: CandidateBehaviorInput) {
      return Object.freeze({
        contextId: input.definition.id,
        candidates: input.runtime.candidates,
        annotations: EMPTY_ANNOTATIONS,
      });
    },
  });
