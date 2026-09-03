import { useEffect, useMemo, useState } from "react";

import {
  documentValidationLabel,
  type AppController,
} from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView, SceneCellFill } from "../../scene/types";
import type { PlaytestInputMode } from "./PlaytestSession";
import type { PlaytestSession } from "./PlaytestSession";

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

const cellFillPalette: Readonly<Record<string, SceneCellFill>> = Object.freeze({
  cyan: Object.freeze({ color: "#b9efff", label: "cyan" }),
  green: Object.freeze({ color: "#c9f5d5", label: "green" }),
  yellow: Object.freeze({ color: "#fff0b3", label: "yellow" }),
  rose: Object.freeze({ color: "#ffd1dc", label: "rose" }),
});

function formatElapsed(milliseconds: number) {
  const totalSeconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = String(totalSeconds % 60).padStart(2, "0");
  return `${minutes}:${seconds}`;
}

function ElapsedTimer({ session }: { session: PlaytestSession }) {
  const [elapsed, setElapsed] = useState(() =>
    session.getElapsedMilliseconds(),
  );
  useEffect(() => {
    const timerId = window.setInterval(() => {
      setElapsed(session.getElapsedMilliseconds());
    }, 1_000);
    return () => window.clearInterval(timerId);
  }, [session]);
  return <time aria-label="Elapsed time">{formatElapsed(elapsed)}</time>;
}

function presentCellFills(
  colors: Readonly<Record<string, readonly string[]>>,
): Readonly<Record<string, SceneCellFill>> {
  return Object.fromEntries(
    Object.entries(colors).flatMap(([cellId, colorIds]) => {
      const fill = cellFillPalette[colorIds[0]];
      return fill === undefined ? [] : [[cellId, fill]];
    }),
  );
}

function inputLabel(
  mode: "digit" | "corner" | "centre",
  valueLabel: string,
) {
  switch (mode) {
    case "digit":
      return `Enter ${valueLabel}`;
    case "corner":
      return `Toggle corner ${valueLabel}`;
    case "centre":
      return `Toggle centre ${valueLabel}`;
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
  const cellFills = useMemo(
    () => presentCellFills(playtest.colors),
    [playtest.colors],
  );
  const sceneView = useMemo<PuzzleSceneView>(
    () => ({
      values: playtest.values,
      candidates: {},
      candidateMarks: playtest.manualCandidates,
      cellFills,
      selectedCellIds: editor.selectedCellIds,
      annotations: [],
      entityCapabilities: validation.capability?.entities ?? {},
    }),
    [
      cellFills,
      editor.selectedCellIds,
      playtest.manualCandidates,
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
      case "erase":
        break;
    }
  };

  const rulesOpen = editor.mobileSheet === "rules";
  const keypadMode =
    playtest.inputMode === "digit" ||
    playtest.inputMode === "corner" ||
    playtest.inputMode === "centre"
      ? playtest.inputMode
      : null;
  const statusMessage =
    playtest.checkMessage ?? documentValidationLabel(validation);

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
        <ElapsedTimer session={controller.playtest} />
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
            {playtest.inputMode === "erase" ? (
              <button
                className="erase-selected-button"
                type="button"
                aria-label="Erase selected cell"
                disabled={
                  selectedCellId === undefined ||
                  puzzle.givens[selectedCellId] !== undefined
                }
                onClick={() => {
                  if (selectedCellId !== undefined) {
                    controller.playtest.erase(selectedCellId);
                  }
                }}
              >
                Erase selected cell
              </button>
            ) : keypadMode !== null ? (
              <div className="keypad" aria-label="Playtest keypad">
                {puzzle.domains["digits-1-9"].values.map((value) => (
                  <button
                    key={value.id}
                    type="button"
                    aria-label={inputLabel(keypadMode, value.label)}
                    onClick={() => enter(value.id)}
                  >
                    {value.label}
                  </button>
                ))}
              </div>
            ) : null}
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
