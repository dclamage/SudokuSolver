export function App() {
  return (
    <main className="app-shell">
      <header className="top-bar">
        <strong>SudokuSolver</strong>
        <nav aria-label="Workspace" role="tablist">
          <button aria-selected="true" role="tab" type="button">
            Set
          </button>
          <button aria-selected="false" role="tab" type="button">
            Playtest
          </button>
        </nav>
      </header>
    </main>
  );
}
