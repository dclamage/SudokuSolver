import type {
  CandidateContext,
  CandidateContextId,
  CellId,
  ValueId,
} from "../puzzle/types";
import type { SceneAnnotation } from "../../scene/types";

export type CandidateContextStatus =
  | "idle"
  | "live"
  | "stale"
  | "calculating"
  | "error";

export interface CandidateContextRuntime {
  readonly contextId: CandidateContextId;
  readonly baseSemanticRevision: number | null;
  readonly baseSemanticHash: string | null;
  readonly status: CandidateContextStatus;
  readonly candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  readonly error: string | null;
}

export interface CandidateSceneProjection {
  readonly contextId: CandidateContextId;
  readonly candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  readonly annotations: readonly SceneAnnotation[];
}

export type CandidatePanelKind = string;

export interface CandidateContextActions {
  readonly manualCandidateEntry: boolean;
}

export interface CandidatePanelDescriptor {
  readonly contextId: CandidateContextId;
  readonly name: string;
  readonly contextKind: string;
  readonly panelKind: CandidatePanelKind;
  readonly status: CandidateContextStatus;
}

export interface CandidateContextSnapshot {
  readonly activeContextId: CandidateContextId;
  readonly definitions: readonly CandidateContext[];
  readonly contexts: Readonly<
    Record<CandidateContextId, CandidateContextRuntime>
  >;
  readonly sceneProjection: CandidateSceneProjection;
  readonly panel: CandidatePanelDescriptor;
  readonly actions: CandidateContextActions;
}

export interface CandidateBehaviorTransition {
  readonly runtime: CandidateContextRuntime;
  readonly requestWork: boolean;
}

export interface CandidateBehaviorInput {
  readonly definition: CandidateContext;
  readonly runtime: CandidateContextRuntime;
  readonly semanticRevision: number;
  readonly semanticHash: string | null;
  readonly active: boolean;
}

export interface CandidateContextBehavior {
  readonly panelKind: CandidatePanelKind;
  readonly actions: CandidateContextActions;
  activate(input: CandidateBehaviorInput): CandidateBehaviorTransition;
  invalidate(input: CandidateBehaviorInput): CandidateBehaviorTransition;
  getSceneProjection(input: CandidateBehaviorInput): CandidateSceneProjection;
}

export type CandidateBehaviorRegistry = Readonly<
  Record<string, CandidateContextBehavior | undefined>
>;
