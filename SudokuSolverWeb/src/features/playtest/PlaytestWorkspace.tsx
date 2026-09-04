import { useEffect, useMemo, useState } from "react";

import {
  documentValidationLabel,
  type AppController,
} from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView, SceneCellFill } from "../../scene/types";
import { CandidateContextOutlet } from "../candidates/CandidateContextOutlet";
import { CandidateContextTabs } from "../candidates/CandidateContextTabs";
import type { PlaytestSession } from "./PlaytestSession";

export interface PlaytestWorkspaceProps {
  controller: AppController;
}

const independentInputModes = Object.freeze([
  Object.freeze({ id: "digit" as const, label: "Digit", glyph: "▦" }),
]);
const EMPTY_CANDIDATES = Object.freeze({});
const EMPTY_CELL_FILLS = Object.freeze({});

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

export function PlaytestWorkspace({ controller }: PlaytestWorkspaceProps) {
  const puzzleSnapshot = useExternalStore(controller.puzzle);
  const editor = useExternalStore(controller.editor);
  const playtest = useExternalStore(controller.playtest);
  const validation = useExternalStore(controller.validation);
  const candidates = useExternalStore(controller.candidates);
  const puzzle = puzzleSnapshot.document;
  const selectedCellId = editor.selectedCellIds[0];
  const manualCandidateEntry = candidates.actions.manualCandidateEntry;
  const inputMode =
    !manualCandidateEntry && playtest.inputMode !== "digit"
      ? "digit"
      : playtest.inputMode;
  const cellFills = useMemo(
    () =>
      manualCandidateEntry
        ? presentCellFills(playtest.colors)
        : EMPTY_CELL_FILLS,
    [manualCandidateEntry, playtest.colors],
  );
  const sceneView = useMemo<PuzzleSceneView>(
    () => ({
      values: candidates.sceneProjection.values ?? playtest.values,
      candidates: manualCandidateEntry
        ? EMPTY_CANDIDATES
        : candidates.sceneProjection.candidates,
      candidateMarks: manualCandidateEntry
        ? playtest.manualCandidates
        : undefined,
      candidatePresentation: manualCandidateEntry
        ? undefined
        : candidates.sceneProjection.candidatePresentation,
      cellFills,
      selectedCellIds: editor.selectedCellIds,
      annotations: candidates.sceneProjection.annotations,
      entityCapabilities: validation.capability?.entities ?? {},
    }),
    [
      cellFills,
      candidates.sceneProjection,
      editor.selectedCellIds,
      manualCandidateEntry,
      playtest.manualCandidates,
      playtest.values,
      validation.capability,
    ],
  );

  const selectCell = (cellId: string) => {
    controller.editor.selectOnly(cellId);
    if (manualCandidateEntry && inputMode === "erase") {
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
    if (inputMode === "digit") {
      controller.playtest.enterValue(selectedCellId, valueId);
    }
  };

  const rulesOpen = editor.mobileSheet === "rules";
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
            {!manualCandidateEntry ? (
              <div
                className="mode-picker mode-picker--single"
                aria-label="Input mode"
              >
                {independentInputModes.map((mode) => (
                  <button
                    key={mode.id}
                    type="button"
                    aria-label={mode.label}
                    aria-pressed={inputMode === mode.id}
                    onClick={() => controller.playtest.setInputMode(mode.id)}
                  >
                    <span aria-hidden="true">{mode.glyph}</span>
                    <span>{mode.label}</span>
                  </button>
                ))}
              </div>
            ) : null}
            <CandidateContextTabs controller={controller} />
            <CandidateContextOutlet controller={controller} />
            {!manualCandidateEntry ? (
              <div className="keypad" aria-label="Playtest keypad">
                {(selectedCellId === undefined
                  ? []
                  : puzzle.domains[puzzle.cells[selectedCellId]?.domainId]
                      ?.values ?? []
                ).map((value) => (
                  <button
                    key={value.id}
                    type="button"
                    aria-label={`Enter ${value.label}`}
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
