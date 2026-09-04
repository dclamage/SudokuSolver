import { useMemo } from "react";

import {
  documentValidationLabel,
  type AppController,
  type EditorTool,
} from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { CandidateContextOutlet } from "../candidates/CandidateContextOutlet";
import { CandidateContextTabs } from "../candidates/CandidateContextTabs";
import { presentManualCellColors } from "../candidates/manualColorPresentation";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView } from "../../scene/types";
import { InspectorPanel } from "./InspectorPanel";

export interface SetWorkspaceProps {
  controller: AppController;
}

function canRunGivenAction(controller: AppController): boolean {
  if (controller.getSnapshot().workspace !== "set") {
    return false;
  }
  const editor = controller.editor.getSnapshot();
  const candidates = controller.candidates.getSnapshot();
  return (
    editor.activeTool === "given" &&
    (!candidates.actions.manualCandidateEntry ||
      editor.setterNotesInputMode === "digit")
  );
}

function runGivenAction(controller: AppController, valueId: string | null) {
  if (!canRunGivenAction(controller)) {
    return;
  }
  const cellId = controller.editor.getSnapshot().selectedCellIds[0];
  const puzzle = controller.puzzle.getSnapshot().document;
  const cell = cellId === undefined ? undefined : puzzle.cells[cellId];
  const domain = cell === undefined ? undefined : puzzle.domains[cell.domainId];
  if (
    cellId === undefined ||
    cell?.input.acceptsValue !== true ||
    domain === undefined ||
    (valueId !== null && !domain.values.some((value) => value.id === valueId))
  ) {
    return;
  }
  controller.puzzle.execute({ type: "setGiven", cellId, valueId });
}

const elementTools: readonly {
  id: EditorTool;
  label: string;
  glyph: string;
  detail: string;
}[] = [
  { id: "given", label: "Given", glyph: "5", detail: "Given digits" },
  { id: "region", label: "Region", glyph: "□", detail: "Regions" },
  {
    id: "auxiliary",
    label: "Auxiliary cell",
    glyph: "A",
    detail: "Auxiliary cell",
  },
  { id: "more", label: "More", glyph: "•••", detail: "More elements" },
];

function ElementButtons({
  controller,
  activeTool,
}: SetWorkspaceProps & { activeTool: EditorTool }) {
  return (
    <div className="element-list">
      {elementTools.map((tool) => (
        <button
          className="element-button"
          key={tool.id}
          type="button"
          aria-label={tool.label}
          aria-pressed={activeTool === tool.id}
          onClick={() => {
            controller.editor.setActiveTool(tool.id);
            controller.editor.setMobileSheet(null);
          }}
        >
          <span className="element-glyph" aria-hidden="true">
            {tool.glyph}
          </span>
          <span>
            <strong>{tool.detail}</strong>
            <small>
              {tool.id === "given"
                ? "Persistent puzzle values"
                : tool.id === "auxiliary"
                  ? "Full input, free placement"
                  : "Available in the element library"}
            </small>
          </span>
          <span aria-hidden="true">›</span>
        </button>
      ))}
    </div>
  );
}

export function SetWorkspace({ controller }: SetWorkspaceProps) {
  const puzzleSnapshot = useExternalStore(controller.puzzle);
  const editor = useExternalStore(controller.editor);
  const validation = useExternalStore(controller.validation);
  const candidates = useExternalStore(controller.candidates);
  const selectedCellId = editor.selectedCellIds[0];
  const selectedCell =
    selectedCellId === undefined
      ? undefined
      : puzzleSnapshot.document.cells[selectedCellId];
  const selectedDomain =
    selectedCell === undefined
      ? undefined
      : puzzleSnapshot.document.domains[selectedCell.domainId];
  const showGivenControls =
    !candidates.actions.manualCandidateEntry ||
    editor.setterNotesInputMode === "digit";
  const sceneView = useMemo<PuzzleSceneView>(
    () => ({
      values: candidates.sceneProjection.values ?? {},
      candidates: candidates.sceneProjection.candidates,
      candidateMarks: candidates.sceneProjection.candidateMarks,
      candidatePresentation: candidates.sceneProjection.candidatePresentation,
      cellFills: presentManualCellColors(
        candidates.sceneProjection.cellColors,
      ),
      selectedCellIds: editor.selectedCellIds,
      annotations: candidates.sceneProjection.annotations,
      entityCapabilities: validation.capability?.entities ?? {},
    }),
    [candidates.sceneProjection, editor.selectedCellIds, validation.capability],
  );

  const selectCell = (cellId: string) => {
    controller.editor.selectOnly(cellId);
    if (controller.editor.getSnapshot().activeTool === "auxiliary") {
      controller.editor.setMobileSheet("inspector");
    }
  };

  return (
    <section
      className="workspace workspace--set"
      id="workspace-set"
      role="tabpanel"
      aria-label="Set workspace"
    >
      <div className="workspace-layout workspace-layout--set">
        <aside
          className="workspace-rail workspace-rail--elements"
          data-open={editor.mobileSheet === "elements" ? "true" : "false"}
          aria-label="Elements"
        >
          <div className="rail-heading">
            <h2>Elements</h2>
            <span className="rail-collapse" aria-hidden="true">
              «
            </span>
            <button
              className="mobile-only icon-button"
              type="button"
              aria-label="Close Elements"
              onClick={() => controller.editor.setMobileSheet(null)}
            >
              ×
            </button>
          </div>
          <label className="element-search">
            <span className="visually-hidden">Search elements</span>
            <input type="search" placeholder="Search elements" />
          </label>
          <ElementButtons controller={controller} activeTool={editor.activeTool} />
        </aside>

        <div className="canvas-column">
          <div className="document-heading">
            <div>
              <span className="eyebrow">Set workspace</span>
              <h1>{puzzleSnapshot.document.metadata.title}</h1>
            </div>
            <span className="revision-label">
              Revision {puzzleSnapshot.document.revision}
            </span>
          </div>
          <div className="canvas-stage">
            <PuzzleCanvas
              puzzle={puzzleSnapshot.document}
              view={sceneView}
              onSelectCell={selectCell}
            />
          </div>
          <CandidateContextTabs controller={controller} />
          <CandidateContextOutlet controller={controller} />
          <div className="canvas-toolbar" aria-label="Canvas toolbar">
            <button
              type="button"
              aria-label="Select"
              aria-pressed={editor.activeTool === "select"}
              onClick={() => controller.editor.setActiveTool("select")}
            >
              ↖ <span>Select</span>
            </button>
            <button
              type="button"
              aria-label="Pan"
              aria-pressed={editor.activeTool === "pan"}
              onClick={() => controller.editor.setActiveTool("pan")}
            >
              ✋ <span>Pan</span>
            </button>
            <button type="button" aria-label="Zoom out">
              −
            </button>
            <output aria-label="Zoom level">100%</output>
            <button type="button" aria-label="Zoom in">
              +
            </button>
            <button type="button" aria-label="Fit puzzle">
              ⛶
            </button>
            <button
              className="mobile-only"
              type="button"
              aria-label="Open Elements"
              onClick={() => controller.editor.setMobileSheet("elements")}
            >
              Elements
            </button>
            <button
              className="mobile-only"
              type="button"
              aria-label="Open Inspector"
              onClick={() => controller.editor.setMobileSheet("inspector")}
            >
              Inspector
            </button>
          </div>
          {showGivenControls ? (
            <>
              <div className="keypad" aria-label="Given keypad">
                {(selectedDomain?.values ?? []).map((value) => (
                  <button
                    key={value.id}
                    type="button"
                    aria-label={`Enter ${value.label}`}
                    disabled={editor.activeTool !== "given"}
                    onClick={() => runGivenAction(controller, value.id)}
                  >
                    {value.label}
                  </button>
                ))}
              </div>
              <div className="given-actions">
                <button
                  type="button"
                  aria-label="Clear given"
                  disabled={
                    editor.activeTool !== "given" ||
                    selectedCellId === undefined ||
                    puzzleSnapshot.document.givens[selectedCellId] === undefined
                  }
                  onClick={() => runGivenAction(controller, null)}
                >
                  Clear given
                </button>
              </div>
            </>
          ) : null}
          <div
            className={`puzzle-status puzzle-status--${validation.status}`}
            role="status"
            aria-label="Puzzle status"
          >
            <span aria-hidden="true">✓</span>
            <span>{documentValidationLabel(validation)}</span>
            <span aria-hidden="true">·</span>
            <span>Revision {puzzleSnapshot.document.semanticRevision}</span>
          </div>
        </div>

        <InspectorPanel controller={controller} />
      </div>
    </section>
  );
}
