import type {
  CandidateContext,
  CandidateContextId,
  CellId,
  ManualCellNotes,
  ManualColorToken,
  ManualMarkKind,
  ValueId,
} from "./types";

export type PuzzleCommand =
  | { type: "setGiven"; cellId: CellId; valueId: ValueId | null }
  | { type: "moveCell"; cellId: CellId; x: number; y: number }
  | {
      type: "setManualMarks";
      contextId: CandidateContextId;
      cellId: CellId;
      kind?: ManualMarkKind;
      valueIds: readonly ValueId[];
    }
  | {
      type: "setManualColor";
      contextId: CandidateContextId;
      cellId: CellId;
      color: ManualColorToken | null;
    }
  | {
      type: "clearManualCell";
      contextId: CandidateContextId;
      cellId: CellId;
    }
  | {
      type: "restoreManualCell";
      contextId: CandidateContextId;
      cellId: CellId;
      notes: ManualCellNotes | null;
    }
  | {
      type: "renameCandidateContext";
      contextId: CandidateContextId;
      name: string;
    }
  | {
      type: "configureTrueCandidates";
      contextId: CandidateContextId;
      refresh: "automatic" | "onRequest";
      display: "possibility" | "solutionFrequency" | "logicComparison";
      solutionCountCap: number;
    }
  | {
      type: "addCandidateContext";
      context: CandidateContext;
      index?: number;
    }
  | {
      type: "duplicateCandidateContext";
      sourceContextId: CandidateContextId;
      contextId: CandidateContextId;
      name: string;
      index?: number;
    }
  | {
      type: "moveCandidateContext";
      contextId: CandidateContextId;
      toIndex: number;
    }
  | {
      type: "removeCandidateContext";
      contextId: CandidateContextId;
    }
  | {
      type: "restoreCandidateContext";
      context: CandidateContext;
      index: number;
      manualMarks: Record<CellId, ManualCellNotes>;
    };
