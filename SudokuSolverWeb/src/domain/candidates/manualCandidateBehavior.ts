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
      const entries = Object.entries(input.manualMarks ?? {});
      const corner = Object.fromEntries(
        entries
          .filter(([, notes]) => notes.corner.length > 0)
          .map(([cellId, notes]) => [cellId, notes.corner]),
      );
      const centre = Object.fromEntries(
        entries
          .filter(([, notes]) => notes.centre.length > 0)
          .map(([cellId, notes]) => [cellId, notes.centre]),
      );
      const cellColors = Object.fromEntries(
        entries.flatMap(([cellId, notes]) =>
          notes.color === null ? [] : [[cellId, notes.color]],
        ),
      );
      return Object.freeze({
        contextId: input.definition.id,
        candidates: corner,
        candidateMarks: Object.freeze({ corner, centre }),
        cellColors,
        annotations: EMPTY_ANNOTATIONS,
      });
    },
  });
