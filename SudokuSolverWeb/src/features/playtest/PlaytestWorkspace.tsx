import { useMemo } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView } from "../../scene/types";
import type { PlaytestInputMode } from "./PlaytestSession";

export interface PlaytestWorkspaceProps {
  controller: AppController;
}

const inputModes: readonly {
  id: PlaytestInputMode;
  label: string;
  glyph: string;
}[] = [
  { id: "digit", label: "Digit", glyph: "▦" },
  { id: "corner", label: "Corner", glyph: "⌜" },
  { id: "centre", label: "Centre", glyph: "⊙" },
  { id: "color", label: "Color", glyph: "◉" },
  { id: "erase", label: "Erase", glyph: "⌫" },
];

function formatElapsed(milliseconds: number) {
  const totalSeconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = String(totalSeconds % 60).padStart(2, "0");
  return `${minutes}:${seconds}`;
}

function mergeCandidates(
  corner: Readonly<Record<string, readonly string[]>>,
  centre: Readonly<Record<string, readonly string[]>>,
) {
  const cellIds = new Set([...Object.keys(corner), ...Object.keys(centre)]);
  return Object.fromEntries(
    [...cellIds].map((cellId) => [
      cellId,
      [...new Set([...(corner[cellId] ?? []), ...(centre[cellId] ?? [])])],
    ]),
  );
}

function inputLabel(mode: PlaytestInputMode, valueLabel: string) {
  switch (mode) {
    case "digit":
      return `Enter ${valueLabel}`;
    case "corner":
      return `Toggle corner ${valueLabel}`;
    case "centre":
      return `Toggle centre ${valueLabel}`;
    case "color":
      return `Color shortcut ${valueLabel}`;
    case "erase":
      return `Erase with ${valueLabel}`;
  }
}

export function PlaytestWorkspace({ controller }: PlaytestWorkspaceProps) {
  const puzzleSnapshot = useExternalStore(controller.puzzle);
  const editor = useExternalStore(controller.editor);
  const playtest = useExternalStore(controller.playtest);
  const validation = useExternalStore(controller.validation);
  const puzzle = puzzleSnapshot.document;
  const selectedCellId = editor.selectedCellIds[0];
  const activeContextName =
    puzzle.authoring.candidateContexts.find(
      (context) => context.id === editor.activeContextId,
    )?.name ?? "Setter notes";
  const candidates = useMemo(
    () =>
      mergeCandidates(
        playtest.manualCandidates.corner,
        playtest.manualCandidates.centre,
      ),
    [playtest.manualCandidates],
  );
  const sceneView = useMemo<PuzzleSceneView>(
    () => ({
      values: playtest.values,
      candidates,
      selectedCellIds: editor.selectedCellIds,
      annotations: [],
      entityCapabilities: validation.capability?.entities ?? {},
    }),
    [
      candidates,
      editor.selectedCellIds,
      playtest.values,
      validation.capability,
    ],
  );

  const selectCell = (cellId: string) => {
    controller.editor.selectOnly(cellId);
    if (playtest.inputMode === "erase") {
      controller.playtest.erase(cellId);
    }
  };

  const enter = (valueId: string) => {
    if (
      selectedCellId === undefined ||
      puzzle.givens[selectedCellId] !== undefined
    ) {
      return;
    }
    switch (playtest.inputMode) {
      case "digit":
        controller.playtest.enterValue(selectedCellId, valueId);
        break;
      case "corner":
      case "centre":
        controller.playtest.toggleCandidate(
          playtest.inputMode,
          selectedCellId,
          valueId,
        );
        break;
      case "color":
        controller.playtest.applyColor(selectedCellId, "cyan");
        break;
      case "erase":
        controller.playtest.erase(selectedCellId);
        break;
    }
  };

  const rulesOpen = editor.mobileSheet === "rules";
  const statusMessage =
    playtest.checkMessage ??
    (validation.status === "validating"
      ? "Checking puzzle…"
      : validation.status === "invalid"
        ? "Puzzle definition needs attention"
        : validation.status === "error"
          ? "Validation unavailable"
          : "Playable");

  return (
    <section
      className="workspace workspace--playtest"
      id="workspace-playtest"
      role="tabpanel"
      aria-label="Playtest workspace"
    >
      <div className="playtest-heading">
        <button
          type="button"
          aria-label="Rules"
          aria-expanded={rulesOpen}
          onClick={() =>
            controller.editor.setMobileSheet(rulesOpen ? null : "rules")
          }
        >
          <span aria-hidden="true">▤</span>
          Rules
        </button>
        <div>
          <span className="eyebrow">Playtest</span>
          <h1>{puzzle.metadata.title}</h1>
        </div>
        <time aria-label="Elapsed time">
          {formatElapsed(controller.playtest.getElapsedMilliseconds())}
        </time>
        <button
          type="button"
          aria-label="Check"
          onClick={() => controller.playtest.check(puzzle)}
        >
          <span aria-hidden="true">✓</span>
          Check
        </button>
      </div>

      <div className="playtest-layout">
        <div className="playtest-canvas-column">
          <div className="canvas-stage canvas-stage--playtest">
            <PuzzleCanvas
              puzzle={puzzle}
              view={sceneView}
              onSelectCell={selectCell}
            />
          </div>
          <div className="playtest-controls" aria-label="Playtest tools">
            <div className="mode-picker" aria-label="Input mode">
              {inputModes.map((mode) => (
                <button
                  key={mode.id}
                  type="button"
                  aria-label={mode.label}
                  aria-pressed={playtest.inputMode === mode.id}
                  onClick={() => controller.playtest.setInputMode(mode.id)}
                >
                  <span aria-hidden="true">{mode.glyph}</span>
                  <span>{mode.label}</span>
                </button>
              ))}
            </div>
            {playtest.inputMode === "color" ? (
              <div className="color-picker" aria-label="Cell colors">
                {[
                  ["cyan", "#39c6f4"],
                  ["green", "#4bd37b"],
                  ["yellow", "#f2c94c"],
                  ["rose", "#ee6c8a"],
                ].map(([name, color]) => (
                  <button
                    key={name}
                    type="button"
                    aria-label={`Apply ${name}`}
                    style={{ "--swatch-color": color } as React.CSSProperties}
                    onClick={() => {
                      if (selectedCellId !== undefined) {
                        controller.playtest.applyColor(selectedCellId, name);
                      }
                    }}
                  >
                    <span aria-hidden="true" />
                  </button>
                ))}
              </div>
            ) : null}
            <div className="context-strip" aria-label="Active layer">
              <strong>Layer: {activeContextName}</strong>
              <span>Manual playtest marks</span>
            </div>
            <div className="keypad" aria-label="Playtest keypad">
              {puzzle.domains["digits-1-9"].values.map((value) => (
                <button
                  key={value.id}
                  type="button"
                  aria-label={inputLabel(playtest.inputMode, value.label)}
                  onClick={() => enter(value.id)}
                >
                  {value.label}
                </button>
              ))}
            </div>
          </div>
          <div
            className={`puzzle-status puzzle-status--${validation.status}`}
            role="status"
            aria-label="Puzzle status"
          >
            <span aria-hidden="true">✓</span>
            <span>{statusMessage}</span>
            <span aria-hidden="true">·</span>
            <span>{playtest.historyLength} moves</span>
          </div>
        </div>

        <aside
          className="playtest-side-sheet"
          data-open={rulesOpen ? "true" : "false"}
          aria-label="Playtest details"
        >
          {rulesOpen ? (
            <section role="region" aria-label="Rules">
              <div className="rail-heading">
                <h2>Rules</h2>
                <button
                  type="button"
                  className="icon-button"
                  aria-label="Close rules"
                  onClick={() => controller.editor.setMobileSheet(null)}
                >
                  ×
                </button>
              </div>
              <p>
                {puzzle.metadata.rules ||
                  "Place each digit once in every row, column, and region."}
              </p>
            </section>
          ) : (
            <div className="desktop-only">
              <h2>Playtest</h2>
              <p>
                Values, notes, colors, history, and the timer belong to this
                solve session.
              </p>
              <p className="support-note">
                Puzzle revision {puzzle.semanticRevision} · {statusMessage}
              </p>
            </div>
          )}
        </aside>
      </div>
    </section>
  );
}
