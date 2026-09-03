import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import { AppController } from "./app/AppController";
import { createStarterPuzzle } from "./domain/puzzle/createStarterPuzzle";
import { PuzzleStore } from "./domain/puzzle/PuzzleStore";
import { createWasmSolverClient } from "./solver/WasmSolverClient";
import "./styles/tokens.css";
import "./styles/global.css";

const controller = new AppController({
  puzzle: new PuzzleStore(createStarterPuzzle()),
  solver: createWasmSolverClient(),
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App controller={controller} />
  </StrictMode>,
);
