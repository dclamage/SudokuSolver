import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";

export interface InspectorPanelProps {
  controller: AppController;
}

export function InspectorPanel({ controller }: InspectorPanelProps) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const editor = useExternalStore(controller.editor);
  const selectedCellId = editor.selectedCellIds[0];
  const selectedCell =
    selectedCellId === undefined ? undefined : puzzle.cells[selectedCellId];
  const selectedRect =
    selectedCell?.shape.kind === "rect" ? selectedCell.shape : undefined;

  const moveSelectedCell = (deltaX: number, deltaY: number) => {
    if (selectedCell === undefined || selectedCell.shape.kind !== "rect") {
      return;
    }
    controller.puzzle.execute({
      type: "moveCell",
      cellId: selectedCell.id,
      x: selectedCell.shape.x + deltaX,
      y: selectedCell.shape.y + deltaY,
    });
  };

  return (
    <aside
      className="workspace-rail workspace-rail--inspector"
      data-open={editor.mobileSheet === "inspector" ? "true" : "false"}
      aria-label="Inspector"
    >
      <div className="rail-heading">
        <h2>Inspector</h2>
        <button
          className="mobile-only icon-button"
          type="button"
          aria-label="Close Inspector"
          onClick={() => controller.editor.setMobileSheet(null)}
        >
          ×
        </button>
      </div>
      {selectedCell === undefined ? (
        <p className="empty-panel-copy">Select a cell to inspect it.</p>
      ) : (
        <div className="inspector-fields">
          <p className="eyebrow">Selected item</p>
          <div className="selected-item-card">
            <span aria-hidden="true" className="element-glyph">
              {selectedCellId === "aux-1" ? "A" : "#"}
            </span>
            <span>
              <strong>
                {selectedCellId === "aux-1" ? "Auxiliary cell" : "Cell"}
              </strong>
              <small>{selectedCell.label ?? selectedCell.id}</small>
            </span>
          </div>
          <dl className="detail-list">
            <div>
              <dt>Domain</dt>
              <dd>1–9</dd>
            </div>
            <div>
              <dt>Input enabled</dt>
              <dd>{selectedCell.input.acceptsValue ? "Yes" : "No"}</dd>
            </div>
          </dl>
          {selectedCellId === "aux-1" && selectedRect !== undefined ? (
            <fieldset className="position-editor">
              <legend>Position</legend>
              <label>
                X
                <input
                  inputMode="decimal"
                  type="number"
                  value={selectedRect.x}
                  step="0.25"
                  onChange={(event) => {
                    const x = event.currentTarget.valueAsNumber;
                    if (Number.isFinite(x)) {
                      controller.puzzle.execute({
                        type: "moveCell",
                        cellId: selectedCell.id,
                        x,
                        y: selectedRect.y,
                      });
                    }
                  }}
                />
              </label>
              <label>
                Y
                <input
                  inputMode="decimal"
                  type="number"
                  value={selectedRect.y}
                  step="0.25"
                  onChange={(event) => {
                    const y = event.currentTarget.valueAsNumber;
                    if (Number.isFinite(y)) {
                      controller.puzzle.execute({
                        type: "moveCell",
                        cellId: selectedCell.id,
                        x: selectedRect.x,
                        y,
                      });
                    }
                  }}
                />
              </label>
              <div className="nudge-grid" aria-label="Move auxiliary cell">
                <button
                  type="button"
                  aria-label="Move up"
                  onClick={() => moveSelectedCell(0, -0.25)}
                >
                  ↑
                </button>
                <button
                  type="button"
                  aria-label="Move left"
                  onClick={() => moveSelectedCell(-0.25, 0)}
                >
                  ←
                </button>
                <button
                  type="button"
                  aria-label="Move right"
                  onClick={() => moveSelectedCell(0.25, 0)}
                >
                  →
                </button>
                <button
                  type="button"
                  aria-label="Move down"
                  onClick={() => moveSelectedCell(0, 0.25)}
                >
                  ↓
                </button>
              </div>
            </fieldset>
          ) : null}
        </div>
      )}
    </aside>
  );
}
