export type CellId = string;
export type ValueId = string;
export type CandidateContextId = string;

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

export type CandidateContext =
  | { id: CandidateContextId; name: string; kind: "manual" }
  | {
      id: CandidateContextId;
      name: string;
      kind: "trueCandidates";
      refresh: "automatic" | "onRequest";
      display: "possibility" | "solutionFrequency" | "logicComparison";
      solutionCountCap: number;
    }
  | {
      id: CandidateContextId;
      name: string;
      kind: "logicalSolver";
      followPuzzleRevision: true;
      enabledTechniqueIds: readonly string[];
    };

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
      Record<CellId, readonly ValueId[]>
    >;
  };
  extensions: Readonly<
    Record<string, { impact: "semantic" | "cosmetic"; data: JsonValue }>
  >;
}
