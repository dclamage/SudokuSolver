import type {
  CandidateContext,
  CandidateContextId,
  CellId,
  ManualCellNotes,
  ManualColorToken,
  ValueId,
} from "../puzzle/types";
import type { SceneAnnotation } from "../../scene/types";
import type { LogicalDeduction } from "../../solver/protocol";

export type CandidateContextStatus =
  | "idle"
  | "live"
  | "stale"
  | "calculating"
  | "error";

export type ManualInputMode =
  | "digit"
  | "corner"
  | "centre"
  | "color"
  | "erase";

export interface CandidateContextRuntime {
  readonly contextId: CandidateContextId;
  readonly baseSemanticRevision: number | null;
  readonly baseSemanticHash: string | null;
  readonly status: CandidateContextStatus;
  readonly candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  readonly values?: Readonly<Record<CellId, ValueId>>;
  readonly candidatePresentation?: TrueCandidatePresentationMap;
  readonly trueCandidates?: TrueCandidateRuntimeResult | null;
  readonly progress?: TrueCandidateProgress | null;
  readonly error: string | null;
  readonly logical?: LogicalCandidateRuntime;
}

export interface ArchivedLogicalRevision {
  readonly semanticRevision: number;
  readonly semanticHash: string;
  readonly historyDeductionIds: readonly string[];
  readonly deductions: readonly LogicalDeduction[];
}

export interface LogicalCandidateRuntime {
  readonly sessionId: string | null;
  readonly positionHash: string | null;
  readonly candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  readonly values: Readonly<Record<CellId, ValueId>>;
  readonly availableDeductions: readonly LogicalDeduction[];
  readonly historyDeductionIds: readonly string[];
  readonly selectedDeductionId: string | null;
  readonly selectedFrameIndex: number;
  readonly applyingDeductionId: string | null;
  readonly archivedRevisions: readonly ArchivedLogicalRevision[];
}

export type TrueCandidateTone =
  | "possible"
  | "frequencyLow"
  | "frequencyMedium"
  | "frequencyHigh"
  | "both"
  | "bruteForceOnly"
  | "logicalOnly";

export interface TrueCandidatePresentation {
  readonly tone: TrueCandidateTone;
  readonly label: string;
}

export type TrueCandidatePresentationMap = Readonly<
  Record<CellId, Readonly<Record<ValueId, TrueCandidatePresentation>>>
>;

export interface TrueCandidateLegendEntry {
  readonly tone: TrueCandidateTone;
  readonly label: string;
}

export interface TrueCandidateProgress {
  readonly discoveredCandidates: number;
  readonly candidateSlots: number;
}

export interface TrueCandidateRuntimeResult {
  readonly solutionCounts: readonly number[];
  readonly logicalCandidateMasks?: readonly number[];
  readonly solutionCountCap: number;
  readonly legend: readonly TrueCandidateLegendEntry[];
}

export interface CandidateSceneProjection {
  readonly contextId: CandidateContextId;
  readonly candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  readonly values?: Readonly<Record<CellId, ValueId>>;
  readonly candidateMarks?: {
    readonly corner: Readonly<Record<CellId, readonly ValueId[]>>;
    readonly centre: Readonly<Record<CellId, readonly ValueId[]>>;
  };
  readonly cellColors?: Readonly<Record<CellId, ManualColorToken>>;
  readonly candidatePresentation?: TrueCandidatePresentationMap;
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
  readonly manualMarks?: Readonly<Record<CellId, ManualCellNotes>>;
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
