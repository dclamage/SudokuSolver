import { memo, useMemo } from "react";
import type { CSSProperties, KeyboardEvent, PointerEvent } from "react";

import type { CellId, PuzzlePackageV1 } from "../domain/puzzle/types";
import { projectPuzzleScene } from "./projectPuzzleScene";
import type { PuzzleSceneView, SceneCellNode } from "./types";
import "./puzzleCanvas.css";

export interface PuzzleCanvasProps {
  puzzle: PuzzlePackageV1;
  view: PuzzleSceneView;
  onSelectCell: (cellId: CellId) => void;
}

function textNodesEqual(
  first: SceneCellNode["content"][number],
  second: SceneCellNode["content"][number],
) {
  return (
    first.id === second.id &&
    first.x === second.x &&
    first.y === second.y &&
    first.text === second.text &&
    first.role === second.role &&
    first.candidateKind === second.candidateKind &&
    first.candidateTone === second.candidateTone &&
    first.candidateLabel === second.candidateLabel &&
    first.clip?.id === second.clip?.id &&
    first.clip?.x === second.clip?.x &&
    first.clip?.y === second.clip?.y &&
    first.clip?.width === second.clip?.width &&
    first.clip?.height === second.clip?.height
  );
}

function cellNodesEqual(first: SceneCellNode, second: SceneCellNode) {
  return (
    first.id === second.id &&
    first.cellId === second.cellId &&
    first.path === second.path &&
    first.label === second.label &&
    first.description === second.description &&
    first.selected === second.selected &&
    first.solverParticipation === second.solverParticipation &&
    first.fill?.color === second.fill?.color &&
    first.fill?.label === second.fill?.label &&
    first.geometryIssue?.code === second.geometryIssue?.code &&
    first.geometryIssue?.affects === second.geometryIssue?.affects &&
    first.geometryIssue?.message === second.geometryIssue?.message &&
    first.content.length === second.content.length &&
    first.content.every((content, index) =>
      textNodesEqual(content, second.content[index]),
    )
  );
}

const CellNode = memo(function CellNode({ node }: { node: SceneCellNode }) {
  const candidates = node.content.filter(
    (content) => content.role === "candidate",
  );
  const displayedValue = node.content.find(
    (content) => content.role !== "candidate",
  );
  const candidateKinds = [undefined, "corner", "centre"] as const;
  const surfaceStyle =
    node.fill === undefined
      ? undefined
      : ({ "--puzzle-cell-fill": node.fill.color } as CSSProperties);
  const candidateClassName = (candidate: SceneCellNode["content"][number]) =>
    `puzzle-cell__candidate${candidate.candidateKind === undefined ? "" : ` puzzle-cell__candidate--${candidate.candidateKind}`}${candidate.candidateTone === undefined ? "" : ` puzzle-cell__candidate--${candidate.candidateTone}`}`;

  return (
    <g
      className="puzzle-cell"
      data-cell-id={node.cellId}
      data-solver-participation={node.solverParticipation}
      data-cell-fill={node.fill?.label}
      data-testid={node.id}
      role="button"
      tabIndex={0}
      aria-label={node.label}
      aria-description={node.description}
      aria-pressed={node.selected}
    >
      <path className="puzzle-cell__surface" d={node.path} style={surfaceStyle} />
      {displayedValue === undefined ? null : (
        <text
          className={`puzzle-cell__value puzzle-cell__value--${displayedValue.role}`}
          data-testid={displayedValue.id}
          x={displayedValue.x}
          y={displayedValue.y}
          aria-hidden="true"
        >
          {displayedValue.text}
        </text>
      )}
      {candidateKinds.map((candidateKind) => {
        const markedCandidates = candidates.filter(
          (candidate) => candidate.candidateKind === candidateKind,
        );
        if (markedCandidates.length === 0) {
          return null;
        }
        const testId =
          candidateKind === undefined
            ? `candidates-${node.cellId}`
            : `${candidateKind}-candidates-${node.cellId}`;
        return (
          <g data-testid={testId} aria-hidden="true" key={testId}>
            {markedCandidates.map((candidate) =>
              candidate.clip === undefined ? (
                <text
                  className={candidateClassName(candidate)}
                  data-candidate-label={candidate.candidateLabel}
                  key={candidate.id}
                  x={candidate.x}
                  y={candidate.y}
                >
                  {candidate.text}
                </text>
              ) : (
                <svg
                  className="puzzle-cell__candidate-viewport"
                  key={candidate.id}
                  x={candidate.clip.x}
                  y={candidate.clip.y}
                  width={candidate.clip.width}
                  height={candidate.clip.height}
                  viewBox={`${candidate.clip.x} ${candidate.clip.y} ${candidate.clip.width} ${candidate.clip.height}`}
                  overflow="hidden"
                >
                  <text
                    className={candidateClassName(candidate)}
                    data-candidate-label={candidate.candidateLabel}
                    x={candidate.x}
                    y={candidate.y}
                  >
                    {candidate.text}
                  </text>
                </svg>
              ),
            )}
          </g>
        );
      })}
    </g>
  );
}, (previous, next) => cellNodesEqual(previous.node, next.node));

function findCellId(target: EventTarget | null, currentTarget: SVGSVGElement) {
  if (!(target instanceof Element)) {
    return undefined;
  }

  const cell = target.closest<SVGGElement>("[data-cell-id]");
  return cell !== null && currentTarget.contains(cell)
    ? cell.dataset.cellId
    : undefined;
}

export function PuzzleCanvas({
  puzzle,
  view,
  onSelectCell,
}: PuzzleCanvasProps) {
  const scene = useMemo(
    () => projectPuzzleScene(puzzle, view),
    [puzzle, view],
  );
  const style = {
    "--puzzle-scene-width": scene.width,
    "--puzzle-scene-height": scene.height,
    aspectRatio: `${scene.width} / ${scene.height}`,
  } as CSSProperties;

  const selectPointerCell = (event: PointerEvent<SVGSVGElement>) => {
    const cellId = findCellId(event.target, event.currentTarget);
    if (cellId !== undefined) {
      onSelectCell(cellId);
    }
  };

  const selectKeyboardCell = (event: KeyboardEvent<SVGSVGElement>) => {
    if (event.key !== "Enter" && event.key !== " ") {
      return;
    }

    const cellId = findCellId(event.target, event.currentTarget);
    if (cellId !== undefined) {
      event.preventDefault();
      onSelectCell(cellId);
    }
  };

  return (
    <svg
      className="puzzle-canvas"
      viewBox={scene.viewBox}
      style={style}
      role="group"
      aria-label={scene.label}
      aria-description={scene.description}
      onPointerUp={selectPointerCell}
      onKeyDown={selectKeyboardCell}
      preserveAspectRatio="xMidYMid meet"
    >
      {scene.nodes.map((node) =>
        node.kind === "cell" ? (
          <CellNode key={node.id} node={node} />
        ) : (
          <path
            className={`puzzle-scene-path puzzle-scene-path--${node.role}`}
            data-testid={node.id}
            d={node.d}
            key={node.id}
            role={node.label === undefined ? undefined : "img"}
            aria-label={node.label}
            aria-hidden={node.label === undefined ? "true" : undefined}
          />
        ),
      )}
    </svg>
  );
}
