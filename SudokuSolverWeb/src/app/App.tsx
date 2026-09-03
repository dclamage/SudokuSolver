import type { AppController } from "./AppController";
import { useExternalStore } from "./useExternalStore";
import { PlaytestWorkspace } from "../features/playtest/PlaytestWorkspace";
import { SetWorkspace } from "../features/set/SetWorkspace";
import "./appShell.css";

export interface AppProps {
  controller: AppController;
}

function HistoryActions({
  controller,
  workspace,
}: AppProps & { workspace: "set" | "playtest" }) {
  return workspace === "set" ? (
    <div className="history-actions">
      <button type="button" aria-label="Undo" onClick={() => controller.puzzle.undo()}>
        <span aria-hidden="true">↶</span>
        <span>Undo</span>
      </button>
      <button type="button" aria-label="Redo" onClick={() => controller.puzzle.redo()}>
        <span aria-hidden="true">↷</span>
        <span>Redo</span>
      </button>
    </div>
  ) : (
    <PlaytestHistoryActions controller={controller} />
  );
}

function PlaytestHistoryActions({ controller }: AppProps) {
  const playtest = useExternalStore(controller.playtest);
  return (
    <div className="history-actions">
      <button
        type="button"
        aria-label="Undo"
        disabled={!playtest.canUndo}
        onClick={() => controller.playtest.undo()}
      >
        <span aria-hidden="true">↶</span>
        <span>Undo</span>
      </button>
      <button
        type="button"
        aria-label="Redo"
        disabled={!playtest.canRedo}
        onClick={() => controller.playtest.redo()}
      >
        <span aria-hidden="true">↷</span>
        <span>Redo</span>
      </button>
    </div>
  );
}

function AppHeader({
  controller,
  workspace,
}: AppProps & { workspace: "set" | "playtest" }) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const isSet = workspace === "set";

  return (
    <header className="top-bar">
      <strong className="wordmark">
        Sudoku<span>Solver</span>
      </strong>
      <div className="workspace-navigation">
        <nav aria-label="Workspace" role="tablist">
          <button
            aria-controls="workspace-set"
            aria-selected={isSet}
            role="tab"
            type="button"
            onClick={() => controller.setWorkspace("set")}
          >
            <span aria-hidden="true">✎</span>
            Set
          </button>
          <button
            aria-controls="workspace-playtest"
            aria-selected={!isSet}
            role="tab"
            type="button"
            onClick={() => controller.setWorkspace("playtest")}
          >
            <span aria-hidden="true">▷</span>
            Playtest
          </button>
        </nav>
        <button
          className="mobile-only mobile-layers-button"
          type="button"
          aria-label="Layers"
          onClick={() => controller.editor.setMobileSheet("layers")}
        >
          <span aria-hidden="true">◇</span>
          Layers
        </button>
      </div>
      <div className="document-title" aria-label="Current puzzle">
        {puzzle.metadata.title}
      </div>
      <HistoryActions controller={controller} workspace={workspace} />
      <div className="save-state">
        <span aria-hidden="true">{controller.hasPersistence ? "☁" : "•"}</span>
        {controller.hasPersistence
          ? "Saved locally"
          : "Session only · not saved"}
      </div>
    </header>
  );
}

function MobileLayersSheet({ controller }: AppProps) {
  const editor = useExternalStore(controller.editor);
  const candidateState = useExternalStore(controller.candidates);
  if (editor.mobileSheet !== "layers") {
    return null;
  }
  return (
    <section className="mobile-tool-sheet" role="region" aria-label="Layers">
      <div className="rail-heading">
        <div>
          <span className="eyebrow">Active candidate context</span>
          <h2>Layers</h2>
        </div>
        <button
          className="icon-button"
          type="button"
          aria-label="Close Layers"
          onClick={() => controller.editor.setMobileSheet(null)}
        >
          ×
        </button>
      </div>
      <div className="layer-list">
        {candidateState.definitions.map((context) => (
          <button
            key={context.id}
            type="button"
            aria-label={context.name}
            aria-pressed={candidateState.activeContextId === context.id}
            onClick={() => controller.candidates.activate(context.id)}
          >
            <span>{context.name}</span>
            <small>{context.kind === "manual" ? "Manual" : "Solver"}</small>
          </button>
        ))}
      </div>
    </section>
  );
}

export function App({ controller }: AppProps) {
  const app = useExternalStore(controller);
  return (
    <main className="app-shell">
      <AppHeader controller={controller} workspace={app.workspace} />
      {app.workspace === "set" ? (
        <SetWorkspace controller={controller} />
      ) : (
        <PlaytestWorkspace controller={controller} />
      )}
      <MobileLayersSheet controller={controller} />
    </main>
  );
}
