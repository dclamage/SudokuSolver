import { useMemo } from "react";

import type { AppController, EditorTool } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView } from "../../scene/types";
import { InspectorPanel } from "./InspectorPanel";

export interface SetWorkspaceProps {
  controller: AppController;
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

function validationLabel(controller: AppController) {
  const snapshot = controller.validation.getSnapshot();
  switch (snapshot.status) {
    case "validating":
      return "Checking puzzle…";
    case "valid":
      return snapshot.capability === null ? "Valid" : "Valid · Solver ready";
    case "invalid":
      return "Puzzle needs attention";
    case "error":
      return "Validation unavailable";
  }
}

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
  const selectedCellId = editor.selectedCellIds[0];
  const sceneView = useMemo<PuzzleSceneView>(
    () => ({
      values: {},
      candidates: {},
      selectedCellIds: editor.selectedCellIds,
      annotations: [],
      entityCapabilities: validation.capability?.entities ?? {},
    }),
    [editor.selectedCellIds, validation.capability],
  );

  const enterGiven = (valueId: string) => {
    if (selectedCellId === undefined) {
      return;
    }
    controller.puzzle.execute({
      type: "setGiven",
      cellId: selectedCellId,
      valueId,
    });
  };

  const selectCell = (cellId: string) => {
    controller.editor.selectOnly(cellId);
    if (editor.activeTool === "auxiliary") {
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
          <div className="keypad" aria-label="Given keypad">
            {puzzleSnapshot.document.domains["digits-1-9"].values.map(
              (value) => (
                <button
                  key={value.id}
                  type="button"
                  aria-label={`Enter ${value.label}`}
                  onClick={() => enterGiven(value.id)}
                >
                  {value.label}
                </button>
              ),
            )}
          </div>
          <div
            className={`puzzle-status puzzle-status--${validation.status}`}
            role="status"
            aria-label="Puzzle status"
          >
            <span aria-hidden="true">✓</span>
            <span>{validationLabel(controller)}</span>
            <span aria-hidden="true">·</span>
            <span>Revision {puzzleSnapshot.document.semanticRevision}</span>
          </div>
        </div>

        <InspectorPanel controller={controller} />
      </div>
    </section>
  );
}
