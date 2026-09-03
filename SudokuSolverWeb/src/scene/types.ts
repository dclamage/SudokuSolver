import type { CellId, ValueId } from "../domain/puzzle/types";
import type { CapabilityEntityResult } from "../solver/protocol";

export type SolverParticipation =
  | CapabilityEntityResult["status"]
  | "unknown";

export type SceneGeometryIssueCode =
  | "annotation-frame-unavailable"
  | "below-minimum-feature"
  | "invalid-topology"
  | "member-geometry-omitted"
  | "malformed-annotation-path"
  | "non-finite-geometry"
  | "unusable-content-anchor"
  | "unsupported-path-geometry";

export interface SceneGeometryIssue {
  code: SceneGeometryIssueCode;
  affects: "content" | "topology" | "topology-and-content";
  message: string;
}

export interface SceneTextNode {
  kind: "text";
  id: string;
  x: number;
  y: number;
  text: string;
  role: "given" | "value" | "candidate";
  candidateKind?: "corner" | "centre";
  clip?: SceneClipRect;
}

export interface SceneCellFill {
  color: string;
  label: string;
}

export interface SceneClipRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface SceneCellNode {
  kind: "cell";
  id: string;
  cellId: CellId;
  path: string;
  label: string;
  description: string;
  selected: boolean;
  solverParticipation: SolverParticipation;
  fill?: SceneCellFill;
  geometryIssue?: SceneGeometryIssue;
  content: readonly SceneTextNode[];
}

export interface ScenePathNode {
  kind: "path";
  id: string;
  d: string;
  role: "grid" | "selection" | "annotation";
  label?: string;
  geometryIssue?: SceneGeometryIssue;
  geometryIssues?: readonly SceneGeometryIssue[];
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
  candidateMarks?: {
    corner: Readonly<Record<CellId, readonly ValueId[]>>;
    centre: Readonly<Record<CellId, readonly ValueId[]>>;
  };
  cellFills?: Readonly<Record<CellId, SceneCellFill>>;
  selectedCellIds: readonly CellId[];
  annotations: readonly SceneAnnotation[];
  entityCapabilities: Readonly<Record<string, CapabilityEntityResult>>;
}

export interface PuzzleScene {
  label: string;
  description?: string;
  viewBox: string;
  width: number;
  height: number;
  nodes: readonly SceneNode[];
  clips: readonly SceneClipRect[];
}
