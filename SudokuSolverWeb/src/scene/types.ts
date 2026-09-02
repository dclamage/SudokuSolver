import type { CellId, ValueId } from "../domain/puzzle/types";
import type { CapabilityEntityResult } from "../solver/protocol";

export type SolverParticipation =
  | CapabilityEntityResult["status"]
  | "unknown";

export interface SceneTextNode {
  kind: "text";
  id: string;
  x: number;
  y: number;
  text: string;
  role: "given" | "value" | "candidate";
}

export interface SceneCellNode {
  kind: "cell";
  id: string;
  cellId: CellId;
  path: string;
  label: string;
  solverParticipation: SolverParticipation;
  content: readonly SceneTextNode[];
}

export interface ScenePathNode {
  kind: "path";
  id: string;
  d: string;
  role: "grid" | "selection" | "annotation";
  label?: string;
}

export type SceneNode = SceneCellNode | ScenePathNode;

export interface SceneAnnotation {
  id: string;
  d: string;
  label?: string;
}

export interface PuzzleSceneView {
  values: Readonly<Record<CellId, ValueId>>;
  candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  selectedCellIds: readonly CellId[];
  annotations: readonly SceneAnnotation[];
  entityCapabilities: Readonly<Record<string, CapabilityEntityResult>>;
}

export interface PuzzleScene {
  label: string;
  viewBox: string;
  width: number;
  height: number;
  nodes: readonly SceneNode[];
}
