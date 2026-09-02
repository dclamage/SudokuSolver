# Web Frontend Milestone 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a local-first desktop/mobile Sudoku setting and playtesting vertical slice that reads the native puzzle format, runs the C# solver through WASM, keeps candidate contexts exclusive, and walks through one structured logical deduction.

**Architecture:** Add a `SudokuSolverWeb` React/Vite application whose framework-independent TypeScript domain owns native documents, commands, sessions, candidate contexts, and persistence. Add a `SudokuSolverService` .NET library that owns the shared native/legacy protocol and is consumed by both the console websocket host and `SudokuSolverWasm`; the browser talks to that service through a revision-safe worker client. The first logical vertical slice adds a pure naked-single finder plus explicit apply operation beside the existing mutating logical solver.

**Tech Stack:** .NET 10/C#, browser WebAssembly, React 19.2.7, React DOM 19.2.7, TypeScript 7.0.2, Vite 8.2.2, Vitest 4.1.11, Testing Library, native SVG, native IndexedDB, and Playwright 1.62.1.

**Spec:** `docs/superpowers/specs/2026-09-01-web-frontend-architecture-design.md`

## Global Constraints

- Use `codex/web-frontend` in `F:\Git\SudokuSolver\.claude\worktrees\codex-web-frontend`; preserve unrelated user work.
- Set and Playtest are the only primary workspaces. Logical Walkthrough is a focused expansion of the active Logical Solver context.
- Exactly one candidate context supplies candidate marks, highlights, controls, and jobs at a time.
- Ship the three default contexts: Setter Notes, True Candidates, and Logical Solver. Context definitions/configuration persist; generated boards remain disposable.
- True Candidates supports `Automatic` and `On request` refresh plus `Possibility`, `Solution frequency`, and `Logic comparison` displays.
- Only the active context starts context-specific solver work. Every response is checked against request ID, semantic revision, semantic hash, and context ID; document revision is also echoed for diagnostics. A response from an older cosmetic-only document revision remains usable when its semantic revision and hash still match.
- The native schema uses stable IDs, explicit groups/topology, and explicit solver projections. Never infer solver meaning from visual coordinates.
- The current C# solver accepts only a square Latin projection. Auxiliary cells remain fully editable but receive an explicit `visualOnly` capability result.
- Keep the current f-puzzles protocol operational for the userscript and console, but do not use it as native frontend state.
- Do not add runtime state libraries, component kits, or IndexedDB wrappers in milestone 1. Runtime dependencies are React and React DOM only.
- Solver/WASM work never runs on the UI thread. Milestone 1 deliberately publishes the current single-threaded WASM variant into a dedicated worker; cancellation terminates and recreates that worker. Keep `SolverClient` transport-neutral so a future main-thread host for multi-threaded WASM can replace it without changing domain or feature code. Vite development and preview responses include COOP/COEP headers.
- Follow `.editorconfig` and add XML documentation to every new public or internal C# member.
- Use the repository's actual MSTest conventions in `SudokuTests` even though `AGENTS.md` contains an outdated NUnit reference.
- Mobile touch targets are at least 44 CSS pixels; no essential action depends on hover or right-click.
- Do not implement custom-constraint authoring, SCL export, f-puzzles import UI, automation, LLM integration, accounts, or hosting in this milestone.

---

### Task 1: Scaffold the web workspace and quality gates

**Files:**
- Create: `SudokuSolverWeb/package.json`
- Create: `SudokuSolverWeb/package-lock.json`
- Create: `SudokuSolverWeb/index.html`
- Create: `SudokuSolverWeb/tsconfig.json`
- Create: `SudokuSolverWeb/tsconfig.app.json`
- Create: `SudokuSolverWeb/tsconfig.node.json`
- Create: `SudokuSolverWeb/eslint.config.js`
- Create: `SudokuSolverWeb/vite.config.ts`
- Create: `SudokuSolverWeb/src/main.tsx`
- Create: `SudokuSolverWeb/src/app/App.tsx`
- Create: `SudokuSolverWeb/src/app/App.test.tsx`
- Create: `SudokuSolverWeb/src/styles/tokens.css`
- Create: `SudokuSolverWeb/src/styles/global.css`
- Create: `SudokuSolverWeb/src/test/setup.ts`
- Create: `SudokuSolverWeb/playwright.config.ts`
- Create: `.github/workflows/web.yml`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: Node 22.14.0 already installed locally and the repository's .NET 10 SDK.
- Produces: npm scripts `dev`, `typecheck`, `test`, `test:watch`, `build`, `preview`, `e2e`, and `verify`.

- [ ] **Step 1: Generate the pinned React/TypeScript scaffold**

Run each command from the repository root:

~~~powershell
npm create vite@9.2.0 SudokuSolverWeb -- --template react-ts --no-interactive
Set-Location SudokuSolverWeb
npm install --save-exact react@19.2.7 react-dom@19.2.7
npm install --save-dev --save-exact typescript@7.0.2 vite@8.2.2 @vitejs/plugin-react@6.1.0 vitest@4.1.11 jsdom@27.0.0 @testing-library/react@16.3.0 @testing-library/dom@10.4.1 @testing-library/user-event@14.6.1 @testing-library/jest-dom@6.9.1 @playwright/test@1.62.1
~~~

Delete only Vite's generated demo assets and replace its starter component in later steps. Keep the generated ESLint dependency versions and lockfile.

- [ ] **Step 2: Write the failing application-shell test**

`SudokuSolverWeb/src/app/App.test.tsx`:

~~~tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";

describe("App", () => {
  it("renders only Set and Playtest as primary workspaces", () => {
    render(<App />);

    expect(screen.getByRole("tab", { name: "Set" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Playtest" })).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Analyze" })).toBeNull();
  });
});
~~~

- [ ] **Step 3: Configure Vitest and verify the test fails**

Add this test block to `vite.config.ts` and import `defineConfig` from `vitest/config`:

~~~ts
test: {
  environment: "jsdom",
  setupFiles: ["./src/test/setup.ts"],
  css: true,
},
~~~

`src/test/setup.ts`:

~~~ts
import "@testing-library/jest-dom/vitest";
~~~

Run:

~~~powershell
npm run test -- App.test.tsx
~~~

Expected: FAIL because `App` does not expose the required tabs.

- [ ] **Step 4: Add the minimal accessible shell and design tokens**

`src/app/App.tsx`:

~~~tsx
export function App() {
  return (
    <main className="app-shell">
      <header className="top-bar">
        <strong>SudokuSolver</strong>
        <nav aria-label="Workspace" role="tablist">
          <button role="tab" aria-selected="true">Set</button>
          <button role="tab" aria-selected="false">Playtest</button>
        </nav>
      </header>
    </main>
  );
}
~~~

Define charcoal surfaces, true-white canvas, cyan accent, text, borders, 44-pixel controls, and responsive spacing as CSS custom properties in `tokens.css`. Import `tokens.css` and `global.css` from `main.tsx`.

- [ ] **Step 5: Add build, test, and cross-origin-isolation configuration**

Set Vite's `server.headers` and `preview.headers` to:

~~~ts
{
  "Cross-Origin-Opener-Policy": "same-origin",
  "Cross-Origin-Embedder-Policy": "require-corp",
  "Cross-Origin-Resource-Policy": "same-origin",
}
~~~

Add package scripts:

~~~json
{
  "typecheck": "tsc -b",
  "test": "vitest run",
  "test:watch": "vitest",
  "build": "tsc -b && vite build",
  "e2e": "playwright test",
  "verify": "npm run typecheck && npm run test && npm run build"
}
~~~

Configure Playwright's base URL as `http://127.0.0.1:4173` and its web server as `npm run preview -- --host 127.0.0.1`.

- [ ] **Step 6: Add the web CI workflow**

`.github/workflows/web.yml` installs Node 22, runs `npm ci`, `npm run verify`, installs Chromium with `npx playwright install --with-deps chromium`, and runs `npm run e2e -- --project=desktop-chromium`. Do not add WASM yet; Task 6 extends this workflow.

- [ ] **Step 7: Run the scaffold gates**

Run:

~~~powershell
npm run typecheck
npm run test
npm run build
~~~

Expected: all three commands exit 0.

- [ ] **Step 8: Commit**

~~~powershell
git add .gitignore .github/workflows/web.yml SudokuSolverWeb
git commit -m "Scaffold the web frontend" -m "### Added" -m "Added the React and Vite application shell." -m "Added TypeScript, unit-test, browser-test, and CI gates."
~~~

---

### Task 2: Define the native puzzle package and shared fixture

**Files:**
- Create: `test-fixtures/native/classic-with-auxiliary.json`
- Create: `test-fixtures/native/four-by-four-killer.json`
- Create: `test-fixtures/native/semantic-hashes.json`
- Create: `SudokuSolverWeb/src/domain/puzzle/types.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/validatePuzzlePackage.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/canonicalJson.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/computeSemanticHash.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/createStarterPuzzle.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/validatePuzzlePackage.test.ts`

**Interfaces:**
- Consumes: no earlier domain interface.
- Produces: `PuzzlePackageV1`, `CellId`, `CandidateContextId`, `validatePuzzlePackage(value): PuzzlePackageV1`, `computeSemanticHash(puzzle): Promise<string>`, and `createStarterPuzzle(): PuzzlePackageV1`.

- [ ] **Step 1: Write the cross-language fixture**

The fixture must contain:

- `schemaVersion: 1`, ID `puzzle-classic-aux`, document revision `1`, and semantic revision `1`.
- One numeric domain `digits-1-9` with stable value IDs `1` through `9`, display labels, and numeric interpretations `1` through `9`.
- A `main` board with 81 rectangular cells using stable IDs `r1c1` through `r9c9`.
- Explicit row, column, and region groups.
- A `latin-square` solver projection whose `cellIdsByRow` lists the 81 IDs explicitly.
- One off-grid rectangular cell `aux-1` at canvas coordinate `x: 10, y: 4`, omitted from the projection.
- The three default candidate contexts with IDs `setter-notes`, `true-candidates`, and `logical-solver`.

Do not add variant constraints to this fixture; it is the contract test for the current projection boundary.

Add a smaller `four-by-four-killer.json` contract fixture with the same native shape, a four-value numeric domain, and one `builtin.killer` instance. Bind its `cells` role to stable cell references and store `{ "sum": 3 }` in typed JSON parameters. It need not be solver-supported in milestone 1; it proves arithmetic data survives TypeScript/C# parsing, hashing, persistence, and capability reporting without flattening it into prose or an NFA.

- [ ] **Step 2: Write the failing schema tests**

~~~ts
import fixture from "../../../../test-fixtures/native/classic-with-auxiliary.json";
import { describe, expect, it } from "vitest";
import { validatePuzzlePackage } from "./validatePuzzlePackage";

describe("validatePuzzlePackage", () => {
  it("accepts an explicit Latin projection with an ignored auxiliary cell", () => {
    const puzzle = validatePuzzlePackage(fixture);
    expect(puzzle.solverProjections[0].cellIdsByRow).toHaveLength(9);
    expect(puzzle.cells["aux-1"].domainId).toBe("digits-1-9");
    expect(puzzle.solverProjections[0].cellIdsByRow.flat()).not.toContain("aux-1");
  });

  it("rejects duplicate projected cell IDs", () => {
    const invalid = structuredClone(fixture);
    invalid.solverProjections[0].cellIdsByRow[0][1] = "r1c1";
    expect(() => validatePuzzlePackage(invalid)).toThrow("projection contains duplicate cell r1c1");
  });
});
~~~

- [ ] **Step 3: Run the test and verify it fails**

~~~powershell
npm run test -- validatePuzzlePackage.test.ts
~~~

Expected: FAIL because the types and validator do not exist.

- [ ] **Step 4: Add the discriminated native types**

~~~ts
export type CellId = string;
export type ValueId = string;
export type CandidateContextId = string;

export type JsonValue =
  | null
  | boolean
  | number
  | string
  | readonly JsonValue[]
  | { readonly [key: string]: JsonValue };

export interface DomainValue {
  id: ValueId;
  label: string;
  numericValue?: number;
}

export type EntityReference = {
  kind: "cell" | "group" | "edge" | "point" | "path";
  id: string;
};

export type CellShape =
  | { kind: "rect"; x: number; y: number; width: number; height: number }
  | { kind: "polygon"; points: readonly { x: number; y: number }[] }
  | { kind: "path"; d: string };

export interface ConstraintInstance {
  id: string;
  typeId: string;
  definitionReleaseId?: string;
  bindings: Readonly<Record<string, readonly EntityReference[]>>;
  parameters: Readonly<Record<string, JsonValue>>;
  styleOverrides?: Readonly<Record<string, JsonValue>>;
}

export type CandidateContext =
  | { id: CandidateContextId; name: string; kind: "manual" }
  | {
      id: CandidateContextId;
      name: string;
      kind: "trueCandidates";
      refresh: "automatic" | "onRequest";
      display: "possibility" | "solutionFrequency" | "logicComparison";
      solutionCountCap: number;
    }
  | {
      id: CandidateContextId;
      name: string;
      kind: "logicalSolver";
      followPuzzleRevision: true;
      enabledTechniqueIds: readonly string[];
    };

export interface PuzzlePackageV1 {
  schemaVersion: 1;
  id: string;
  revision: number;
  semanticRevision: number;
  metadata: { title: string; author: string; rules: string };
  domains: Record<string, { id: string; values: readonly DomainValue[] }>;
  cells: Record<CellId, {
    id: CellId;
    domainId: string;
    shape: CellShape;
    input: { acceptsValue: boolean; acceptsCandidates: boolean };
    label?: string;
  }>;
  boards: Record<string, { id: string; name: string; cellIds: readonly CellId[]; groupIds: readonly string[] }>;
  groups: Record<string, { id: string; roles: readonly string[]; cellIds: readonly CellId[] }>;
  adjacency: Record<string, { id: string; kind: string; fromCellId: CellId; toCellId: CellId }>;
  points: Record<string, { id: string; x: number; y: number }>;
  edges: Record<string, { id: string; fromPointId: string; toPointId: string }>;
  paths: Record<string, { id: string; pointIds: readonly string[]; closed: boolean }>;
  givens: Record<CellId, ValueId>;
  constraints: readonly ConstraintInstance[];
  solverProjections: readonly {
    id: string;
    kind: "latin-square";
    boardId: string;
    domainId: string;
    valueIdsBySolverValue: readonly ValueId[];
    cellIdsByRow: readonly (readonly CellId[])[];
  }[];
  presentation: {
    styles: Readonly<Record<string, JsonValue>>;
    sceneElements: readonly JsonValue[];
  };
  source?: JsonValue;
  release?: JsonValue;
  assets?: readonly JsonValue[];
  provenance?: JsonValue;
  authoring: {
    candidateContexts: readonly CandidateContext[];
    manualMarks: Record<CandidateContextId, Record<CellId, readonly ValueId[]>>;
  };
  extensions: Readonly<Record<string, { impact: "semantic" | "cosmetic"; data: JsonValue }>>;
}
~~~

- [ ] **Step 5: Implement exact validation**

`validatePuzzlePackage` must reject unsupported schema versions, missing entity/domain/value references, malformed bindings/parameters, non-square projections, rows of different lengths, duplicate projected cells, a domain size different from the projection size, invalid givens, decreasing/negative revisions, and missing context IDs. The Latin projection's ordered `valueIdsBySolverValue` must cover its domain exactly and every value must have the matching numeric interpretation `1..N`; that is a limitation of this projection, not of the native domain model. Return a newly constructed `PuzzlePackageV1` rather than casting the input. Preserve validated `source`, `release`, `assets`, `provenance`, and namespaced `extensions` sections even though milestone 1 does not author them. Extensions must explicitly declare semantic/cosmetic impact; unknown or malformed impact is rejected rather than silently excluded from solver identity.

`canonicalJson` recursively sorts object keys while retaining array order. For v1, semantic JSON numbers must be finite safe integers; exact fractions use an explicit `{ numerator, denominator }` object. This avoids JavaScript/.NET float-spelling drift without a hidden rounding rule; presentation geometry may still use finite decimals because it is not hashed. `computeSemanticHash` hashes the portable semantic view: schema version; domain/value identities and numeric interpretations; cell domain/input semantics; board/group/adjacency membership; all givens; paths used by constraints; complete constraint bindings/parameters/definition release IDs; referenced release semantics; solver projections; and extensions declared semantic. Exclude document ID/revisions, metadata, cell/point geometry, presentation/style overrides, authoring contexts/manual marks, source/provenance, cosmetic assets, and extensions declared cosmetic. Return lowercase `sha256:<hex>`. This is a document-semantic hash, not merely a hash of what today's Latin projector happens to support.

- [ ] **Step 6: Add the starter factory and pass the tests**

~~~ts
export function createStarterPuzzle(createId: () => string = () => crypto.randomUUID()): PuzzlePackageV1 {
  const puzzle = structuredClone(validatePuzzlePackage(fixture));
  puzzle.id = createId();
  puzzle.metadata.title = "Untitled puzzle";
  return puzzle;
}
~~~

Run:

~~~powershell
npm run test -- validatePuzzlePackage.test.ts
npm run typecheck
~~~

Expected: PASS.

Add tests proving that moving `aux-1` leaves the hash unchanged, setting `r1c1 = "5"` changes it, changing an auxiliary cell's domain changes it, and changing the killer sum changes it.

Record the canonical semantic hashes for both fixtures in `semantic-hashes.json`. The TypeScript tests assert those golden values; Task 4's C# tests assert the same file so neither runtime can silently define its own canonicalization.

- [ ] **Step 7: Commit**

~~~powershell
git add test-fixtures/native SudokuSolverWeb/src/domain/puzzle
git commit -m "Define the native puzzle package" -m "### Added" -m "Added the explicit topology and solver-projection schema." -m "Added a shared classic-grid fixture with an auxiliary cell."
~~~

---

### Task 3: Add reversible commands, history, and an observable puzzle store

**Files:**
- Create: `SudokuSolverWeb/src/domain/puzzle/commands.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/applyPuzzleCommand.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/PuzzleStore.ts`
- Create: `SudokuSolverWeb/src/domain/puzzle/PuzzleStore.test.ts`

**Interfaces:**
- Consumes: `PuzzlePackageV1` and stable cell/context IDs from Task 2.
- Produces: `PuzzleCommand`, `applyPuzzleCommand`, and `PuzzleStore` with `getSnapshot`, `subscribe`, `execute`, `undo`, and `redo`.

- [ ] **Step 1: Write the failing history tests**

~~~ts
it("commits a gesture as one reversible revision", () => {
  const store = new PuzzleStore(createStarterPuzzle(() => "history-test"));
  store.execute({ type: "setGiven", cellId: "r1c1", valueId: "5" });
  expect(store.getSnapshot().document.givens.r1c1).toBe("5");
  expect(store.getSnapshot().document.revision).toBe(2);
  expect(store.getSnapshot().document.semanticRevision).toBe(2);
  store.undo();
  expect(store.getSnapshot().document.givens.r1c1).toBeUndefined();
  expect(store.getSnapshot().document.revision).toBe(3);
  expect(store.getSnapshot().document.semanticRevision).toBe(3);
});

it("moves an auxiliary cell without changing solver membership", () => {
  const store = new PuzzleStore(createStarterPuzzle(() => "move-test"));
  store.execute({ type: "moveCell", cellId: "aux-1", x: 11.25, y: 4.5 });
  expect(store.getSnapshot().document.cells["aux-1"].shape).toMatchObject({ x: 11.25, y: 4.5 });
  expect(store.getSnapshot().document.semanticRevision).toBe(1);
  expect(store.getSnapshot().document.solverProjections[0].cellIdsByRow.flat()).not.toContain("aux-1");
});
~~~

- [ ] **Step 2: Run the tests and verify they fail**

~~~powershell
npm run test -- PuzzleStore.test.ts
~~~

Expected: FAIL because `PuzzleStore` is missing.

- [ ] **Step 3: Define the command union**

~~~ts
export type PuzzleCommand =
  | { type: "setGiven"; cellId: CellId; valueId: ValueId | null }
  | { type: "moveCell"; cellId: CellId; x: number; y: number }
  | { type: "setManualMarks"; contextId: CandidateContextId; cellId: CellId; valueIds: readonly ValueId[] }
  | { type: "renameCandidateContext"; contextId: CandidateContextId; name: string }
  | { type: "configureTrueCandidates"; contextId: CandidateContextId; refresh: "automatic" | "onRequest"; display: "possibility" | "solutionFrequency" | "logicComparison"; solutionCountCap: number }
  | { type: "addCandidateContext"; context: CandidateContext; index?: number }
  | { type: "duplicateCandidateContext"; sourceContextId: CandidateContextId; contextId: CandidateContextId; name: string; index?: number }
  | { type: "moveCandidateContext"; contextId: CandidateContextId; toIndex: number }
  | { type: "removeCandidateContext"; contextId: CandidateContextId }
  | { type: "restoreCandidateContext"; context: CandidateContext; index: number; manualMarks: Record<CellId, readonly ValueId[]> };
~~~

`restoreCandidateContext` is an internal history command, not exposed through UI/controller APIs. Removing or duplicating a context must round-trip its definition, position, and manual marks exactly; generated solver boards are intentionally not part of undo state. Add a test that removes a populated manual context, undoes, and restores its original index and marks, plus a reorder/undo test.

- [ ] **Step 4: Implement immutable apply and inverse generation**

~~~ts
export interface CommandResult {
  document: PuzzlePackageV1;
  inverse: PuzzleCommand;
  semanticChange: boolean;
}

export function applyPuzzleCommand(document: PuzzlePackageV1, command: PuzzleCommand): CommandResult
~~~

Every successful command increments `revision`. Changes to portable puzzle semantics—domain values, cell input/domain membership, boards/groups/adjacency, givens (including auxiliary givens), constraint bindings/parameters, and solver projections—increment `semanticRevision` and set `semanticChange: true`. Cell/point geometry, presentation, manual marks, candidate-context names/configuration/order, and context add/remove operations are nonsemantic; their behavior controllers invalidate only their own generated state when needed. Reject missing IDs, invalid domain values, removing the last context, and configuring a non-True-Candidates context.

- [ ] **Step 5: Implement the observable store**

`PuzzleStore` keeps separate undo/redo stacks of inverse commands, emits `PuzzleStoreSnapshot` through a stable listener set, and clears redo after a new command. Undo and redo are revision-incrementing commits. The snapshot includes `lastChange: { semantic: boolean; documentRevision: number; semanticRevision: number } | null`; `AppController` computes the hash for that exact immutable snapshot before notifying solver-backed behavior controllers.

- [ ] **Step 6: Run tests**

~~~powershell
npm run test -- PuzzleStore.test.ts
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolverWeb/src/domain/puzzle
git commit -m "Add reversible puzzle commands" -m "### Added" -m "Added immutable native-document commands and inverse history." -m "Added an observable puzzle store with semantic revision events."
~~~

---

### Task 4: Project native packages into the current C# Latin solver

**Files:**
- Create: `SudokuSolver/PuzzleFormats/Native/NativePuzzlePackage.cs`
- Create: `SudokuSolver/PuzzleFormats/Native/NativePuzzleProjector.cs`
- Create: `SudokuSolver/PuzzleFormats/Native/CapabilityReport.cs`
- Create: `SudokuSolver/PuzzleFormats/Native/NativeSemanticHasher.cs`
- Create: `SudokuTests/NativePuzzleProjectionTests.cs`
- Modify: `SudokuTests/SudokuTests.csproj`

**Interfaces:**
- Consumes: the JSON shape and fixture from Task 2.
- Produces: `NativePuzzlePackage`, `NativePuzzleProjector.Project(NativePuzzlePackage, string): NativeProjectionResult`, and explicit entity capability results.

- [ ] **Step 1: Include the shared fixture in the test output**

~~~xml
<ItemGroup>
  <Content Include="..\test-fixtures\native\**" Link="test-fixtures\native\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
~~~

- [ ] **Step 2: Write the failing projection test**

~~~csharp
[TestClass]
public class NativePuzzleProjectionTests
{
    [TestMethod]
    public void ProjectsExplicitLatinGridAndReportsAuxiliaryCellAsVisualOnly()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-fixtures", "native", "classic-with-auxiliary.json"));
        NativePuzzlePackage package = NativePuzzlePackage.Parse(json);
        NativeProjectionResult result = NativePuzzleProjector.Project(package, "main-latin");

        Assert.AreEqual(81, result.Solver.NUM_CELLS);
        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["r1c1"].Status);
        Assert.AreEqual(EntityCapability.VisualOnly, result.Capabilities.Entities["aux-1"].Status);
        Assert.AreEqual("Not included in projection main-latin.", result.Capabilities.Entities["aux-1"].Reason);
    }
}
~~~

- [ ] **Step 3: Run and verify failure**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter NativePuzzleProjectionTests
~~~

Expected: FAIL because the native C# model does not exist.

- [ ] **Step 4: Add source-generated native DTOs and parsing**

Model the v1 fields required by the fixture and retain unknown JSON through `JsonExtensionData` dictionaries. Required public shape:

~~~csharp
public sealed class NativePuzzlePackage
{
    public int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public long Revision { get; init; }
    public long SemanticRevision { get; init; }
    public required Dictionary<string, NativeDomain> Domains { get; init; }
    public required Dictionary<string, NativeCell> Cells { get; init; }
    public required Dictionary<string, NativeGroup> Groups { get; init; }
    public required Dictionary<string, string> Givens { get; init; }
    public required List<NativeConstraintInstance> Constraints { get; init; }
    public required List<NativeSolverProjection> SolverProjections { get; init; }
    public static NativePuzzlePackage Parse(string json);
}
~~~

`NativeDomain` contains ordered `NativeDomainValue` objects with stable string IDs, labels, and optional numeric interpretations. `NativeConstraintInstance` retains `TypeId`, optional `DefinitionReleaseId`, role-named entity bindings, JSON parameters, and style overrides. Use `JsonElement` only at these explicitly open payload boundaries and clone it during parse so its lifetime is owned. Preserve namespaced extensions and other forward-compatible sections through `JsonExtensionData`.

- [ ] **Step 5: Implement authoritative projection and capability reporting**

`Project` validates the projection is square, the domain has matching cardinality, `valueIdsBySolverValue` covers it exactly with numeric interpretations `1..N`, every projected cell exists exactly once, and order is explicit. Construct `new Solver(size, size, size)`, map region groups to `SetRegions`, call `FinalizeConstraints`, then translate stable given value IDs through the projection before applying them. Unsupported constraints—including the milestone's preserved killer instance—are `PartiallyVerified`; unprojected cells are `VisualOnly`.

~~~csharp
public sealed record NativeProjectionResult(
    Solver Solver,
    IReadOnlyDictionary<string, int> CellIndexById,
    IReadOnlyList<string> CellIdByIndex,
    CapabilityReport Capabilities);
~~~

`NativeSemanticHasher.Compute` uses the same sorted-key semantic view as TypeScript. Assert both golden hashes from `semantic-hashes.json`. Add projection tests showing geometry/metadata changes do not change the hash, a projected given does, and the killer's numeric sum parameter round-trips unchanged while receiving a precise partially-supported capability result.

- [ ] **Step 6: Pass projection and regression tests**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter "NativePuzzleProjectionTests|SolverEngineTests"
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolver/PuzzleFormats/Native SudokuTests/NativePuzzleProjectionTests.cs SudokuTests/SudokuTests.csproj
git commit -m "Add native Latin puzzle projection" -m "### Added" -m "Added native package DTOs and explicit square-Latin projection." -m "Added entity-level capability reporting for auxiliary cells."
~~~

---

### Task 5: Extract a shared solver service and add the native protocol

**Files:**
- Create: `SudokuSolverService/SudokuSolverService.csproj`
- Create: `SudokuSolverService/Protocol/SolverRequest.cs`
- Create: `SudokuSolverService/Protocol/SolverResponse.cs`
- Create: `SudokuSolverService/Protocol/LegacyProtocolDtos.cs`
- Create: `SudokuSolverService/Protocol/ProtocolJsonContext.cs`
- Create: `SudokuSolverService/SolverCommandProcessor.cs`
- Create: `SudokuSolverService/NativeOperationRunner.cs`
- Create: `SudokuTests/SolverCommandProcessorTests.cs`
- Create: `SudokuTests/Helpers/NativeRequestFixtures.cs`
- Modify: `SudokuSolver.sln`
- Modify: `SudokuTests/SudokuTests.csproj`
- Modify: `SudokuSolverConsole/SudokuSolverConsole.csproj`
- Modify: `SudokuSolverConsole/WebsocketListener.cs`
- Modify: `SudokuSolverWasm/SudokuSolverWasm.csproj`
- Modify: `SudokuSolverWasm/SolverInterop.cs`
- Delete: `SudokuSolverWasm/Responses.cs`
- Delete: `SudokuSolverWasm/SolverCommandProcessor.cs`

**Interfaces:**
- Consumes: `NativePuzzleProjector` and the existing legacy f-puzzles operations.
- Produces: one transport-neutral `SolverCommandProcessor.Handle(string, Action<string>, CancellationToken)` used by console and WASM.

- [ ] **Step 1: Write correlation and compatibility tests**

~~~csharp
[TestMethod]
public void NativeValidateEchoesRevisionHashAndContext()
{
    string request = NativeRequestFixtures.Validate("request-1", documentRevision: 7, semanticRevision: 4, contextId: "true-candidates");
    List<string> responses = [];
    new SolverCommandProcessor(singleThreaded: true).Handle(request, responses.Add, CancellationToken.None);

    SolverResponse response = SolverResponse.Parse(responses.Single());
    Assert.AreEqual("request-1", response.RequestId);
    Assert.AreEqual(7, response.DocumentRevision);
    Assert.AreEqual(4, response.SemanticRevision);
    Assert.AreEqual(NativeRequestFixtures.SemanticHash, response.SemanticHash);
    Assert.AreEqual("true-candidates", response.ContextId);
    Assert.AreEqual("validate", response.Operation);
}

[TestMethod]
public void LegacySolveStillReturnsNonceBasedSolvedResponse()
{
    string request = JsonSerializer.Serialize(new { nonce = 41, command = "solve", dataType = "fpuzzles", data = NativeRequestFixtures.LegacyPuzzle });
    List<string> responses = [];
    new SolverCommandProcessor(singleThreaded: true).Handle(request, responses.Add, CancellationToken.None);
    StringAssert.Contains(responses.Single(), "\"nonce\":41");
    StringAssert.Contains(responses.Single(), "\"type\":\"solved\"");
}
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter SolverCommandProcessorTests
~~~

Expected: FAIL because `SudokuSolverService` is absent.

- [ ] **Step 3: Define the AOT-safe native envelope**

`SolverRequest` has required `ProtocolVersion`, `RequestId`, `DocumentRevision`, `SemanticRevision`, `SemanticHash`, `ContextId`, `Operation`, `Puzzle`, and strongly typed option properties. `SolverResponse` repeats correlation fields and has optional typed `Capability`, `Solve`, `Count`, `TrueCandidates`, `Logical`, and `Error` properties plus `Kind` (`progress`, `result`, `error`, or `canceled`). Use lower camel case source generation; do not serialize `object`.

`NativeRequestFixtures` reads the shared native fixture, computes `SemanticHash` with `NativeSemanticHasher`, exposes the existing known-good f-puzzles `LegacyPuzzle`, and emits fully correlated request JSON. Add a test that a deliberately incorrect hash returns `semanticHashMismatch`; never merely echo an unverified client hash.

- [ ] **Step 4: Move the legacy processor into the service**

Move current WASM behavior without semantic changes. Change the boundary to:

~~~csharp
public sealed class SolverCommandProcessor(bool singleThreaded)
{
    public void Handle(string messageJson, Action<string> sendJson, CancellationToken cancellationToken);
}
~~~

Detect native messages by `protocolVersion` and route everything else to legacy handling. Preserve legacy request/response fields, nonce/type semantics, progress/final sequencing, and cache behavior; golden compatibility tests should compare parsed JSON so harmless property-order changes are not treated as protocol breaks.

- [ ] **Step 5: Implement native validate, solve, and bounded count**

`NativeOperationRunner` projects every request. `validate` returns capability and contradiction state; `solve` returns values keyed by stable cell ID; `count` requires `maxSolutions >= 1` and returns progress plus a final clamped count. Error codes are `unsupportedVersion`, `invalidPackage`, `unsupportedProjection`, `contradiction`, and `internalError`.

- [ ] **Step 6: Replace both duplicated hosts**

`SolverInterop` passes `DispatchResponse` to the shared processor. `WebsocketListener` retains client/cancellation ownership but delegates operation execution with a client-specific JSON sink. Remove the copied WASM processor and response files.

- [ ] **Step 7: Add project references**

Add `SudokuSolverService` to `SudokuSolver.sln` and reference it from console, WASM, and tests. Do not add `SudokuSolverWasm` to the solution because ordinary CI does not install the workload.

- [ ] **Step 8: Run native, legacy, and WASM gates**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter "SolverCommandProcessorTests|NativePuzzleProjectionTests"
dotnet test SudokuTests/SudokuTests.csproj
dotnet build SudokuSolverConsole/SudokuSolverConsole.csproj
dotnet build SudokuSolverWasm/SudokuSolverWasm.csproj -c Debug
~~~

Expected: all commands exit 0 and the existing 152-test baseline does not regress.

- [ ] **Step 9: Commit**

~~~powershell
git add SudokuSolverService SudokuSolverConsole SudokuSolverWasm SudokuTests SudokuSolver.sln
git commit -m "Share the solver command service" -m "### Changed" -m "Replaced duplicated console and WASM processors with one service." -m "Preserved the legacy f-puzzles protocol and added the native protocol."
~~~

---

### Task 6: Build the revision-safe WASM worker client

**Files:**
- Create: `SudokuSolverWeb/scripts/build-wasm.mjs`
- Create: `SudokuSolverWeb/src/solver/protocol.ts`
- Create: `SudokuSolverWeb/src/solver/SolverClient.ts`
- Create: `SudokuSolverWeb/src/solver/WorkerTransport.ts`
- Create: `SudokuSolverWeb/src/solver/worker/solver.worker.ts`
- Create: `SudokuSolverWeb/src/solver/WasmSolverClient.ts`
- Create: `SudokuSolverWeb/src/solver/WasmSolverClient.test.ts`
- Create: `SudokuSolverWeb/src/solver/dotnet.d.ts`
- Create: `SudokuSolverWeb/src/solver/testing/FakeWorkerTransport.ts`
- Create: `SudokuSolverWeb/src/solver/testing/protocolFixtures.ts`
- Create: `SudokuSolverWeb/src/test/FakeSolverClient.ts`
- Modify: `SudokuSolverWeb/package.json`
- Modify: `SudokuSolverWeb/vite.config.ts`
- Modify: `.github/workflows/web.yml`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: Task 5's native contract.
- Produces: `SolverClient.start(request): SolverJob` and `createWasmSolverClient(): SolverClient`.

- [ ] **Step 1: Write stale-response and cancellation tests**

~~~ts
it("rejects a response that does not match its submitted document revision", async () => {
  const transport = new FakeWorkerTransport();
  const client = new WasmSolverClient(transport);
  const job = client.start(makeValidateRequest({ requestId: "r1", documentRevision: 4, semanticRevision: 2 }));
  transport.emit(makeValidateResponse({ requestId: "r1", documentRevision: 3, semanticRevision: 2 }));
  await expect(job.result).rejects.toThrow("stale solver response");
});

it("cancels by restarting the single-threaded worker", async () => {
  const transport = new FakeWorkerTransport();
  const client = new WasmSolverClient(transport);
  const job = client.start(makeValidateRequest({ requestId: "r2" }));
  job.cancel();
  expect(transport.restartCount).toBe(1);
  await expect(job.result).rejects.toThrow("solver worker restarted");
});
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- WasmSolverClient.test.ts
~~~

Expected: FAIL.

`FakeWorkerTransport` implements the production transport interface with `sent`, `emit`, `fail`, `restart`, and `restartCount` controls. `protocolFixtures.ts` exports fully correlated `makeValidateRequest` and `makeValidateResponse` builders with valid puzzle/hash defaults. `FakeSolverClient` records typed requests and exposes `progress(requestId, response)`, `resolve(requestId, response)`, and `reject(requestId, error)`; later candidate/UI tests reuse it rather than creating incompatible fakes.

- [ ] **Step 3: Define the client boundary**

~~~ts
export interface SolverJob<TResponse extends SolverResponse = SolverResponse> {
  readonly result: Promise<TResponse>;
  cancel(): void;
  subscribe(listener: (response: TResponse) => void): () => void;
}

export interface SolverClient {
  start<TResponse extends SolverResponse>(request: SolverRequest): SolverJob<TResponse>;
  dispose(): void;
}
~~~

Validate protocol version, request ID, request operation, semantic revision, semantic hash, and context ID before publishing a response. Document revision must equal the submitted request's revision, but the caller may still accept it after cosmetic edits when current semantic revision/hash match.

- [ ] **Step 4: Implement worker and transport**

The Vite module worker imports `/solver/_framework/dotnet.js` dynamically, registers `sendResponse`, calls `Initialize`, queues requests until ready, and emits `ready`, `response`, `done`, and `error`. Build only the single-threaded WASM variant for this milestone. Cancellation terminates the worker, rejects its outstanding jobs, creates a fresh worker, and lazily replays document/session requests as their owning controllers reactivate. Do not attempt to boot the current multi-threaded runtime inside this worker.

- [ ] **Step 5: Add deterministic WASM build**

`build-wasm.mjs` resolves only `SudokuSolverWeb/.wasm-build` and `SudokuSolverWeb/public/solver`, runs a Debug publish, verifies `dotnet.js`, and replaces only `public/solver/_framework`. Ignore both generated directories. Add `wasm:build` and `dev:wasm` scripts.

- [ ] **Step 6: Run tests and build**

~~~powershell
npm run test -- WasmSolverClient.test.ts
npm run wasm:build
npm run typecheck
npm run build
~~~

Expected: PASS and `public/solver/_framework/dotnet.js` exists.

- [ ] **Step 7: Extend CI**

Install `wasm-tools`, run `npm run wasm:build` before build, and cache npm/NuGet/workload downloads. Do not upload the generated runtime.

- [ ] **Step 8: Commit**

~~~powershell
git add .gitignore .github/workflows/web.yml SudokuSolverWeb
git commit -m "Add the WASM solver client" -m "### Added" -m "Added the single-threaded worker-hosted .NET runtime build and transport." -m "Added revision, hash, context, and cancellation guards."
~~~

---

### Task 7: Render the shared SVG puzzle surface

**Files:**
- Create: `SudokuSolverWeb/src/scene/types.ts`
- Create: `SudokuSolverWeb/src/scene/projectPuzzleScene.ts`
- Create: `SudokuSolverWeb/src/scene/PuzzleCanvas.tsx`
- Create: `SudokuSolverWeb/src/scene/PuzzleCanvas.test.tsx`
- Create: `SudokuSolverWeb/src/scene/puzzleCanvas.css`

**Interfaces:**
- Consumes: `PuzzlePackageV1`, the C# entity-capability report, and value/candidate/selection/annotation projections.
- Produces: `PuzzleCanvas` and `projectPuzzleScene`; React owns no puzzle semantics.

- [ ] **Step 1: Write scene tests**

~~~tsx
const puzzle = createStarterPuzzle(() => "scene");
const emptySceneView: PuzzleSceneView = {
  values: {},
  candidates: {},
  selectedCellIds: [],
  annotations: [],
  entityCapabilities: { "aux-1": { status: "visualOnly", reason: "Not included in projection main-latin." } },
};

it("renders main and auxiliary cells from native geometry", () => {
  render(<PuzzleCanvas puzzle={puzzle} view={emptySceneView} onSelectCell={() => undefined} />);
  expect(screen.getAllByTestId(/^cell-/)).toHaveLength(82);
  expect(screen.getByTestId("cell-aux-1")).toHaveAttribute("data-solver-participation", "visualOnly");
});

it("renders only the supplied active-context candidate map", () => {
  render(<PuzzleCanvas puzzle={puzzle} view={{ ...emptySceneView, candidates: { r1c2: ["1", "4"] } }} onSelectCell={() => undefined} />);
  expect(screen.getByTestId("candidates-r1c2")).toHaveTextContent("14");
  expect(screen.queryByTestId("candidates-r1c3")).toBeNull();
});
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- PuzzleCanvas.test.tsx
~~~

Expected: FAIL.

- [ ] **Step 3: Define scene nodes**

~~~ts
export type SceneNode =
  | { kind: "cell"; id: string; cellId: CellId; path: string; solverParticipation: "fullyVerified" | "partiallyVerified" | "visualOnly" | "unsupported" | "unknown" }
  | { kind: "text"; id: string; x: number; y: number; text: string; role: "given" | "value" | "candidate" }
  | { kind: "path"; id: string; d: string; role: "grid" | "selection" | "annotation" };

export interface PuzzleSceneView {
  values: Readonly<Record<CellId, ValueId>>;
  candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  selectedCellIds: readonly CellId[];
  annotations: readonly SceneAnnotation[];
  entityCapabilities: Readonly<Record<string, EntityCapabilityDto>>;
}
~~~

- [ ] **Step 4: Implement geometry projection**

Convert rects/polygons to SVG paths. Derive borders from explicit groups, never cell ID text. Read solver participation only from the capability map, falling back to `unknown` while validation is pending; never infer it from projection membership in TypeScript. Candidate positions use domain order and an N-by-N subgrid based on `Math.ceil(Math.sqrt(domain.values.length))` and render domain labels rather than value IDs.

- [ ] **Step 5: Implement semantic SVG**

Render a labelled `svg` with cell groups, `data-cell-id` hit targets, candidate text, selection, and annotations. Use event delegation and keyboard activation for focused cells.

- [ ] **Step 6: Pass tests**

~~~powershell
npm run test -- PuzzleCanvas.test.tsx
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolverWeb/src/scene
git commit -m "Render the native SVG puzzle scene" -m "### Added" -m "Added geometry-driven SVG cells, values, candidates, and annotations." -m "Added solver-participation and auxiliary-cell rendering."
~~~

---

### Task 8: Build responsive Set and Playtest shells

**Files:**
- Create: `SudokuSolverWeb/src/app/AppController.ts`
- Create: `SudokuSolverWeb/src/app/useExternalStore.ts`
- Create: `SudokuSolverWeb/src/features/set/SetWorkspace.tsx`
- Create: `SudokuSolverWeb/src/features/set/InspectorPanel.tsx`
- Create: `SudokuSolverWeb/src/features/playtest/PlaytestSession.ts`
- Create: `SudokuSolverWeb/src/features/playtest/PlaytestWorkspace.tsx`
- Create: `SudokuSolverWeb/src/features/playtest/PlaytestWorkspace.test.tsx`
- Create: `SudokuSolverWeb/src/test/createTestAppController.ts`
- Create: `SudokuSolverWeb/src/app/appShell.css`
- Modify: `SudokuSolverWeb/src/app/App.tsx`

**Interfaces:**
- Consumes: `PuzzleStore`, `PuzzleCanvas`, and injected `SolverClient`.
- Produces: `AppController` with independent puzzle/editor/playtest state and two responsive workspaces.

- [ ] **Step 1: Write workspace isolation test**

~~~tsx
it("playtest entries never mutate puzzle givens", async () => {
  const controller = createTestAppController();
  render(<App controller={controller} />);
  await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
  await userEvent.click(screen.getByTestId("cell-r1c1"));
  await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));
  expect(controller.playtest.getSnapshot().values.r1c1).toBe("5");
  expect(controller.puzzle.getSnapshot().document.givens.r1c1).toBeUndefined();
});
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- PlaytestWorkspace.test.tsx
~~~

Expected: FAIL.

`createTestAppController` builds a starter puzzle with deterministic ID, in-memory persistence, fixed clock/UUID functions, and the shared `FakeSolverClient`. Return the controller plus its test dependencies so later tasks can resolve jobs without loading WASM.

- [ ] **Step 3: Implement separate state owners**

`AppController` owns `puzzle`, `editor`, `playtest`, `validation`, and `solver` plus `setWorkspace("set" | "playtest")`, `openWalkthrough`, and `closeWalkthrough`. It runs document-level native validation after load and semantic commits, correlates it by semantic revision/hash, and supplies the returned capability map to both workspaces. This ambient validation is separate from candidate contexts. `EditorStore` keeps selection, viewport, active tool/context, and mobile sheet. `PlaytestSession` keeps values, manual candidates, colors, history, and timer without executing puzzle commands.

- [ ] **Step 4: Implement Set**

Render Elements, canvas, Inspector, toolbar, undo/redo, and quiet puzzle status. Milestone elements are Given, Region, Auxiliary cell, and More; Given editing and auxiliary movement work.

- [ ] **Step 5: Implement Playtest**

Render the shared canvas, digit/corner/centre/color/erase modes, keypad, undo/redo, rules, and check. Follow familiar interaction ordering without copying external code/assets.

- [ ] **Step 6: Add responsive CSS**

Below 720px, hide desktop rails, keep canvas primary, render Set/Playtest in the bottom dock, and move tools to labelled sheets. Keep controls at least 44px.

- [ ] **Step 7: Run tests**

~~~powershell
npm run test -- PlaytestWorkspace.test.tsx App.test.tsx
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 8: Commit**

~~~powershell
git add SudokuSolverWeb/src/app SudokuSolverWeb/src/features SudokuSolverWeb/src/styles
git commit -m "Add Set and Playtest workspaces" -m "### Added" -m "Added responsive setting and independent playtest flows." -m "Added desktop rails and mobile bottom-sheet layouts."
~~~

---

### Task 9: Implement exclusive candidate-context orchestration

**Files:**
- Create: `SudokuSolverWeb/src/domain/candidates/types.ts`
- Create: `SudokuSolverWeb/src/domain/candidates/CandidateContextController.ts`
- Create: `SudokuSolverWeb/src/domain/candidates/CandidateContextController.test.ts`
- Create: `SudokuSolverWeb/src/features/candidates/CandidateContextTabs.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/CandidateContextOutlet.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/CandidateContextTabs.test.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/candidateContexts.css`
- Modify: `SudokuSolverWeb/src/app/AppController.ts`
- Modify: `SudokuSolverWeb/src/features/set/SetWorkspace.tsx`
- Modify: `SudokuSolverWeb/src/features/playtest/PlaytestWorkspace.tsx`

**Interfaces:**
- Consumes: context definitions, semantic revisions, and `SolverClient`.
- Produces: one `CandidateSceneProjection` and panel descriptor for the active context.

- [ ] **Step 1: Write exclusivity and focus tests**

~~~ts
it("does not run an inactive automatic context", () => {
  const solver = new FakeSolverClient();
  const controller = createCandidateController({ activeContextId: "setter-notes", solver });
  controller.onPuzzleChanged({ documentRevision: 2, semanticRevision: 2, semanticHash: "sha256:revision-2", semantic: true });
  expect(controller.getSnapshot().contexts["true-candidates"].status).toBe("stale");
  expect(solver.requests).toHaveLength(0);
});

it("starts stale automatic work when activated", () => {
  const solver = new FakeSolverClient();
  const controller = createCandidateController({ activeContextId: "setter-notes", solver });
  controller.onPuzzleChanged({ documentRevision: 2, semanticRevision: 2, semanticHash: "sha256:revision-2", semantic: true });
  controller.activate("true-candidates");
  expect(solver.requests[0].contextId).toBe("true-candidates");
});
~~~

The React test asserts a Setter Notes panel has no `Next step` or `Refresh now` button.

`createCandidateController` is a test-local helper that constructs `CandidateContextController` with the three starter context definitions, the current starter document/hash, an injected `FakeSolverClient`, and an immediate deterministic scheduler.

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- CandidateContext
~~~

Expected: FAIL.

- [ ] **Step 3: Define runtime state**

~~~ts
export type CandidateContextStatus = "idle" | "live" | "stale" | "calculating" | "error";

export interface CandidateContextRuntime {
  contextId: CandidateContextId;
  baseSemanticRevision: number | null;
  baseSemanticHash: string | null;
  status: CandidateContextStatus;
  candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  error: string | null;
}

export interface CandidateSceneProjection {
  contextId: CandidateContextId;
  candidates: Readonly<Record<CellId, readonly ValueId[]>>;
  annotations: readonly SceneAnnotation[];
}
~~~

- [ ] **Step 4: Implement behavior registry**

Each behavior supplies `activate`, `invalidate`, `getSceneProjection`, and `panelKind`. Resolve only the active behavior. Cancel old jobs on context or semantic revision changes; ignore correlation mismatches. Cosmetic document revisions retain generated candidates when the semantic revision/hash still match.

- [ ] **Step 5: Implement tabs and outlet**

Render named contexts, a separate settings chevron where applicable, and Add Layer. Resolve panels through a `panelKind` registry. Inactive panels must be absent from the DOM, not CSS-hidden.

- [ ] **Step 6: Wire workspaces and pass tests**

~~~powershell
npm run test -- CandidateContext
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolverWeb/src/domain/candidates SudokuSolverWeb/src/features/candidates SudokuSolverWeb/src/app/AppController.ts SudokuSolverWeb/src/features/set/SetWorkspace.tsx SudokuSolverWeb/src/features/playtest/PlaytestWorkspace.tsx
git commit -m "Add exclusive candidate contexts" -m "### Added" -m "Added focus-driven candidate orchestration and panel registration." -m "Prevented inactive contexts from rendering marks or starting jobs."
~~~

---

### Task 10: Implement Setter Notes

**Files:**
- Create: `SudokuSolverWeb/src/domain/candidates/manualCandidateBehavior.ts`
- Create: `SudokuSolverWeb/src/features/candidates/SetterNotesPanel.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/SetterNotesPanel.test.tsx`
- Modify: `SudokuSolverWeb/src/domain/candidates/CandidateContextController.ts`
- Modify: `SudokuSolverWeb/src/features/candidates/CandidateContextOutlet.tsx`

**Interfaces:**
- Consumes: `setManualMarks` and behavior registry.
- Produces: manual candidate projection and manual tools only while active.

- [ ] **Step 1: Write failing persistence/isolation test**

~~~tsx
it("preserves manual marks while another context is active", async () => {
  const controller = createTestAppController();
  controller.candidates.activate("setter-notes");
  controller.editor.selectOnly("r1c2");
  render(<SetterNotesPanel controller={controller} />);
  await userEvent.click(screen.getByRole("button", { name: "Mark 4" }));
  controller.candidates.activate("true-candidates");
  expect(controller.candidates.getSceneProjection().candidates.r1c2).toBeUndefined();
  controller.candidates.activate("setter-notes");
  expect(controller.candidates.getSceneProjection().candidates.r1c2).toEqual(["4"]);
});
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- SetterNotesPanel.test.tsx
~~~

Expected: FAIL.

- [ ] **Step 3: Implement manual behavior and controls**

In Set, write through `PuzzleStore.execute`. In Playtest, use `PlaytestSession.setManualMarks`. Sort and deduplicate by domain order. Render Digit, Corner, Centre, Color, Erase, and keypad; emit no solver request.

- [ ] **Step 4: Register and pass tests**

~~~powershell
npm run test -- SetterNotesPanel.test.tsx CandidateContextTabs.test.tsx
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add SudokuSolverWeb/src/domain/candidates SudokuSolverWeb/src/features/candidates
git commit -m "Add Setter Notes context" -m "### Added" -m "Added persistent setter-controlled candidate marks." -m "Added manual marking controls without solver actions."
~~~

---

### Task 11: Implement configurable True Candidates end to end

**Files:**
- Create: `SudokuTests/NativeTrueCandidatesProtocolTests.cs`
- Modify: `SudokuSolverService/Protocol/SolverRequest.cs`
- Modify: `SudokuSolverService/Protocol/SolverResponse.cs`
- Modify: `SudokuSolverService/NativeOperationRunner.cs`
- Create: `SudokuSolverWeb/src/domain/candidates/trueCandidatesBehavior.ts`
- Create: `SudokuSolverWeb/src/domain/candidates/trueCandidatesBehavior.test.ts`
- Create: `SudokuSolverWeb/src/features/candidates/TrueCandidatesPanel.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/TrueCandidatesOptionsPopover.tsx`
- Create: `SudokuSolverWeb/src/features/candidates/TrueCandidatesPanel.test.tsx`
- Modify: `SudokuSolverWeb/src/features/candidates/CandidateContextTabs.tsx`
- Modify: `SudokuSolverWeb/src/features/candidates/CandidateContextOutlet.tsx`

**Interfaces:**
- Consumes: `Solver.TrueCandidates`, the projection's stable cell/value mappings, and context configuration.
- Produces: explicit `cellIds`, `valueIdsBySolverValue`, `solutionCounts`, and optional `logicalCandidateMasks` in stable projected order.

- [ ] **Step 1: Write native mode tests**

~~~csharp
[DataRow("possibility", 1, false)]
[DataRow("solutionFrequency", 8, false)]
[DataRow("logicComparison", 1, true)]
[TestMethod]
public void TrueCandidateModesReturnCountsAndOptionalLogicalMasks(string display, int cap, bool expectLogicalMasks)
{
    SolverResponse response = RunTrueCandidates(display, cap);
    Assert.AreEqual(cap, response.TrueCandidates.SolutionCountCap);
    Assert.AreEqual(81 * 9, response.TrueCandidates.SolutionCounts.Length);
    Assert.AreEqual(expectLogicalMasks, response.TrueCandidates.LogicalCandidateMasks is not null);
}
~~~

In the same test class, implement `RunTrueCandidates` by calling `NativeRequestFixtures.TrueCandidates(display, cap, contextId: "true-candidates")`, executing one `SolverCommandProcessor`, parsing the single final `result` response, and asserting that no `error` response was emitted.

- [ ] **Step 2: Run and verify failure**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter NativeTrueCandidatesProtocolTests
~~~

Expected: FAIL.

- [ ] **Step 3: Add explicit native options/results**

`TrueCandidatesOptionsDto` has `Display` and `SolutionCountCap`; validate display names and cap `1..1024`. Return the projection's ordered stable cell/value IDs with flattened non-negative counts and `logicalCandidateMasks` only for comparison. Do not expose legacy `-1` sentinels or make the frontend infer stable IDs from row/column coordinates.

- [ ] **Step 4: Write frontend tests**

Cover automatic debounce/cancel, on-request staleness, possibility projection, frequency buckets, logic-comparison labels, and split-control access to Refresh/Display.

- [ ] **Step 5: Implement behavior**

Use an injected 250ms scheduler. Store counts, logical masks, revision, and progress. Cosmetic edits do not invalidate; inactive automatic contexts become stale without scheduling.

- [ ] **Step 6: Implement split control and panel**

The selected control reads `True Candidates | Auto` or `True Candidates | On request`. Its popover contains two refresh choices, three display choices, and cap when relevant. The panel contains only calculation state, last revision, refresh/cancel, and legend.

- [ ] **Step 7: Run all focused tests**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter NativeTrueCandidatesProtocolTests
npm run test -- trueCandidates TrueCandidates
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 8: Commit**

~~~powershell
git add SudokuSolverService SudokuTests/NativeTrueCandidatesProtocolTests.cs SudokuSolverWeb/src/domain/candidates SudokuSolverWeb/src/features/candidates
git commit -m "Add configurable True Candidates" -m "### Added" -m "Added native possibility, frequency, and logic-comparison results." -m "Added automatic/on-request behavior and split controls."
~~~

---

### Task 12: Add the first pure structured deduction and apply operation

**Files:**
- Create: `SudokuSolver/Logical/TechniqueDescriptor.cs`
- Create: `SudokuSolver/Logical/LogicalDeduction.cs`
- Create: `SudokuSolver/Logical/DeductionDelta.cs`
- Create: `SudokuSolver/Logical/WalkthroughFrame.cs`
- Create: `SudokuSolver/Logical/LogicalPosition.cs`
- Create: `SudokuSolver/Logical/NakedSingleFinder.cs`
- Create: `SudokuSolver/Logical/LogicalDeductionService.cs`
- Create: `SudokuTests/LogicalDeductionServiceTests.cs`
- Create: `SudokuTests/NativeLogicalProtocolTests.cs`
- Modify: `SudokuSolverService/Protocol/SolverRequest.cs`
- Modify: `SudokuSolverService/Protocol/SolverResponse.cs`
- Modify: `SudokuSolverService/NativeOperationRunner.cs`

**Interfaces:**
- Consumes: a finalized projected `Solver`.
- Produces: pure `FindAvailable`, validated `Apply` for `basic.naked-single`, and native `logical.create`/`logical.apply`.

- [ ] **Step 1: Write pure-find and stale-apply tests**

~~~csharp
[TestMethod]
public void FindAvailableDoesNotMutateTheBoard()
{
    LogicalPosition position = CreatePosition();
    Solver solver = position.Solver;
    string before = solver.CandidateString;
    IReadOnlyList<LogicalDeduction> deductions = LogicalDeductionService.FindAvailable(position);
    Assert.AreEqual(before, solver.CandidateString);
    Assert.AreEqual("basic.naked-single", deductions.Single().TechniqueId);
}

[TestMethod]
public void ApplyRejectsAChangedPosition()
{
    LogicalPosition position = CreatePosition();
    LogicalDeduction deduction = LogicalDeductionService.FindAvailable(position).Single();
    DeductionPlacement placement = deduction.Delta.Placements.Single();
    Assert.IsTrue(position.Solver.SetValue(0, 0, position.GetSolverValue(placement.ValueId)));
    Assert.ThrowsException<InvalidOperationException>(() => LogicalDeductionService.Apply(position, deduction));
}
~~~

`CreatePosition` takes `Puzzles.uniqueClassics[0].Item2`, replaces its first digit with `.`, creates a solver from those givens, maps flat indices to `r1c1` through `r9c9`, and maps solver values to stable value IDs `"1"` through `"9"`. It returns `new LogicalPosition(solver, cellIds, valueIdsBySolverValue, semanticHash: "sha256:logical-test")`; no undeclared candidate-string constant is used.

- [ ] **Step 2: Run and verify failure**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter LogicalDeductionServiceTests
~~~

Expected: FAIL.

- [ ] **Step 3: Define typed data**

`LogicalDeduction` has required `Id`, `TechniqueId`, `OwningConstraintId`, `PreconditionHash`, `Premises`, `Delta`, and `Frames`. Delta contains placements/eliminations expressed with stable cell and value IDs; `LogicalPosition.GetSolverValue` is the explicit boundary back to current integer masks. Frames contain semantic focus/dim/highlight references and typed explanation keys, never SVG coordinates.

- [ ] **Step 4: Implement pure naked-single discovery**

Scan the flat board, emit one deduction per unset one-candidate cell, and never call mutation methods. IDs are SHA-256 of technique ID, precondition hash, and canonical delta.

- [ ] **Step 5: Implement validated apply**

Verify the position hash and re-run the finder to confirm the exact deduction, then apply through solver methods. Return the new hash and candidate board.

- [ ] **Step 6: Add native logical sessions**

Store sessions by `(contextId, semanticRevision, semanticHash)`. `logical.create` projects fresh state and returns candidates/deductions. `logical.apply` requires deduction ID, applies it, appends history, and returns the next state. Mismatch returns `staleContext`. Worker restart recreates and replays persisted IDs.

- [ ] **Step 7: Add protocol tests**

Test create, apply, source puzzle immutability, stale rejection, and a frame referencing `r1c1` rather than coordinates.

- [ ] **Step 8: Run focused and regression tests**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj --filter "LogicalDeductionServiceTests|NativeLogicalProtocolTests"
dotnet test SudokuTests/SudokuTests.csproj
~~~

Expected: PASS; existing `StepLogic` is unchanged.

- [ ] **Step 9: Commit**

~~~powershell
git add SudokuSolver/Logical SudokuSolverService SudokuTests
git commit -m "Add structured naked-single deductions" -m "### Added" -m "Added pure discovery and validated apply for the first logical technique." -m "Added revision-safe logical sessions and semantic walkthrough frames."
~~~

---

### Task 13: Implement Logical Solver context and Walkthrough

**Files:**
- Create: `SudokuSolverWeb/src/domain/candidates/logicalSolverBehavior.ts`
- Create: `SudokuSolverWeb/src/domain/candidates/logicalSolverBehavior.test.ts`
- Create: `SudokuSolverWeb/src/features/candidates/LogicalSolverPanel.tsx`
- Create: `SudokuSolverWeb/src/features/walkthrough/LogicalWalkthrough.tsx`
- Create: `SudokuSolverWeb/src/features/walkthrough/LogicalWalkthrough.test.tsx`
- Create: `SudokuSolverWeb/src/test/logicalFixtures.ts`
- Create: `SudokuSolverWeb/src/features/walkthrough/walkthrough.css`
- Modify: `SudokuSolverWeb/src/features/candidates/CandidateContextOutlet.tsx`
- Modify: `SudokuSolverWeb/src/app/App.tsx`
- Modify: `SudokuSolverWeb/src/app/AppController.ts`

**Interfaces:**
- Consumes: native logical operations.
- Produces: stateful Logical Solver candidates, context-only step controls, and expanded walkthrough.

- [ ] **Step 1: Write reset and visibility tests**

~~~ts
it("resets the logical board when the semantic revision changes", async () => {
  const { controller, solver } = createLogicalHarness();
  controller.candidates.activate("logical-solver");
  const createRequest = solver.requests.at(-1)!;
  solver.resolve(createRequest.requestId, logicalState({ semanticRevision: 1, deductions: [nakedSingle] }));
  await controller.candidates.nextLogicalStep(nakedSingle.id);
  controller.onPuzzleChanged({ documentRevision: 2, semanticRevision: 2, semanticHash: "sha256:revision-2", semantic: true });
  expect(controller.getSnapshot().contexts["logical-solver"].status).toBe("calculating");
  expect(solver.requests.at(-1)?.operation).toBe("logical.create");
});
~~~

Also assert Next Step is absent until Logical Solver is active.

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- logicalSolver LogicalWalkthrough
~~~

Expected: FAIL.

`logicalFixtures.ts` exports one typed `nakedSingle` deduction, `logicalState({ semanticRevision, deductions })`, and `createLogicalHarness()` built from `createTestAppController` plus `FakeSolverClient`. Use these exact helpers in behavior and walkthrough tests.

- [ ] **Step 3: Implement logical behavior**

Create/restore revision-matched sessions on activation. Store board, deductions, applied steps, selected frame, and archived revision history. Semantic changes cancel and archive old state; initialize only when active or next selected.

- [ ] **Step 4: Implement context panel**

Render technique, affected entities, deductions, Next Step, Reset, and Open Walkthrough. Do not render true-candidate or manual options.

- [ ] **Step 5: Implement Walkthrough**

Keep Set/Playtest in the header, add Back, render history/canvas/explanation columns, map semantic frames to scene annotations, and let Prev/Next change frames without applying. Next Step applies one deduction.

- [ ] **Step 6: Pass tests**

~~~powershell
npm run test -- logicalSolver LogicalWalkthrough CandidateContext
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolverWeb/src/domain/candidates SudokuSolverWeb/src/features/candidates SudokuSolverWeb/src/features/walkthrough SudokuSolverWeb/src/app
git commit -m "Add the Logical Solver walkthrough" -m "### Added" -m "Added a revision-following logical candidate context." -m "Added context-only step controls and semantic walkthrough."
~~~

---

### Task 14: Add IndexedDB autosave, migrations, and recovery

**Files:**
- Create: `SudokuSolverWeb/src/domain/persistence/DocumentRepository.ts`
- Create: `SudokuSolverWeb/src/domain/persistence/IndexedDbDocumentRepository.ts`
- Create: `SudokuSolverWeb/src/domain/persistence/migrations.ts`
- Create: `SudokuSolverWeb/src/domain/persistence/AutosaveCoordinator.ts`
- Create: `SudokuSolverWeb/src/domain/persistence/AutosaveCoordinator.test.ts`
- Create: `SudokuSolverWeb/src/domain/persistence/RecentDocuments.tsx`
- Modify: `SudokuSolverWeb/src/app/AppController.ts`
- Modify: `SudokuSolverWeb/src/app/App.tsx`

**Interfaces:**
- Consumes: committed snapshots and v1 validation.
- Produces: `load`, `save`, `listRecent`, `saveJournal`, `loadJournal`, and recovery UI.

- [ ] **Step 1: Write save ordering and journal tests**

~~~ts
it("never lets an older save overwrite a newer revision", async () => {
  const repository = new DeferredRepository();
  const coordinator = new AutosaveCoordinator(repository);
  const first = coordinator.schedule(documentAtRevision(2));
  const second = coordinator.schedule(documentAtRevision(3));
  repository.resolveSave(3);
  repository.resolveSave(2);
  await Promise.all([first, second]);
  expect(repository.current?.revision).toBe(3);
});

it("keeps a journal until save succeeds", async () => {
  const repository = new DeferredRepository();
  const coordinator = new AutosaveCoordinator(repository);
  const pending = coordinator.schedule(documentAtRevision(2));
  expect(repository.journal?.revision).toBe(2);
  repository.resolveSave(2);
  await pending;
  expect(repository.journal).toBeNull();
});
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
npm run test -- AutosaveCoordinator.test.ts
~~~

Expected: FAIL.

Define `DeferredRepository` and `documentAtRevision` as private helpers in `AutosaveCoordinator.test.ts`. The repository keeps pending resolvers keyed by document revision, stores `current` only when the incoming revision is newer, and exposes its current `journal`; this makes out-of-order completion deterministic.

- [ ] **Step 3: Define repository/migration contracts**

Use database `sudoku-solver-web` version 1 with `documents`, `journals`, and `sessions` stores. Records carry ID, schema version, revision, updated UTC ISO string, and serialized data. Unsupported future packages are preserved and reported, never overwritten.

- [ ] **Step 4: Implement conditional saves**

In one readwrite transaction, write only when incoming revision is greater. Journal first; clear only after matching document save completes. Surface quota, blocked-upgrade, and parse errors as typed errors.

- [ ] **Step 5: Wire startup and autosave**

Create starter when empty. If journal is newer, show Restore/Discard. Autosave committed puzzle changes after 300ms and show Saving/Saved/Failed.

- [ ] **Step 6: Pass tests**

~~~powershell
npm run test -- AutosaveCoordinator.test.ts
npm run typecheck
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add SudokuSolverWeb/src/domain/persistence SudokuSolverWeb/src/app
git commit -m "Add local document persistence" -m "### Added" -m "Added revision-safe IndexedDB documents and recovery journals." -m "Added startup migration, recent documents, and autosave status."
~~~

---

### Task 15: Prove the desktop/mobile vertical slice

**Files:**
- Create: `SudokuSolverWeb/e2e/setting.spec.ts`
- Create: `SudokuSolverWeb/e2e/candidate-contexts.spec.ts`
- Create: `SudokuSolverWeb/e2e/logical-walkthrough.spec.ts`
- Create: `SudokuSolverWeb/e2e/mobile.spec.ts`
- Create: `SudokuSolverWeb/e2e/wasm-smoke.spec.ts`
- Create: `SudokuSolverWeb/e2e/__screenshots__/`
- Modify: `SudokuSolverWeb/playwright.config.ts`
- Modify: `.github/workflows/web.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: the complete milestone.
- Produces: executable acceptance coverage and local run instructions.

- [ ] **Step 1: Configure desktop/mobile projects**

Define `desktop-chromium` at 1440×960 and `mobile-chromium` with Pixel 7. Keep WebKit opt-in until real WASM passes; CI gates both Chromium projects.

- [ ] **Step 2: Write setting/autosave acceptance**

Set `r1c1 = 5`, move `aux-1`, verify auxiliary is visual-only, reload, and verify edits survive.

- [ ] **Step 3: Write candidate isolation acceptance**

Add a manual mark; switch to True Candidates and verify it disappears; choose On request and Solution frequency; refresh; switch back and verify the manual mark. Assert Next Step is absent outside Logical Solver.

- [ ] **Step 4: Write walkthrough acceptance**

Load the near-complete fixture, activate Logical Solver, apply naked single, open Walkthrough, verify stable reference/highlight, change frames, return to Set, and verify context/step remains selected.

- [ ] **Step 5: Write mobile acceptance**

At Pixel 7, verify Set/Playtest/Layers dock, Layers sheet, True Candidates-only options, Setter Notes-only tools, and unobscured controls.

- [ ] **Step 6: Write real WASM smoke**

Send native validate, bounded count, True Candidates, `logical.create`, and `logical.apply` without mocks. Assert correlation fields and prove UI responsiveness by cancelling a longer count.

- [ ] **Step 7: Capture deterministic visual baselines**

Use `toHaveScreenshot` for four approved states. Compare composition to the files in `docs/superpowers/designs/2026-09-02-web-frontend`. Generated concepts are direction references; Playwright images are pixel baselines.

- [ ] **Step 8: Run complete gate**

~~~powershell
dotnet test SudokuTests/SudokuTests.csproj
dotnet build SudokuSolverConsole/SudokuSolverConsole.csproj -c Release
dotnet build SudokuSolverWasm/SudokuSolverWasm.csproj -c Debug
Set-Location SudokuSolverWeb
npm ci
npm run wasm:build
npm run verify
npx playwright install chromium
npm run e2e -- --project=desktop-chromium --project=mobile-chromium
~~~

Expected: all commands exit 0.

- [ ] **Step 9: Perform browser-assisted visual QA**

Start `npm run dev:wasm` and inspect the actual local app with the in-app Browser at desktop and Pixel 7 sizes. Check focus order, visible focus, 200% zoom, touch sizes, console errors, and worker errors. Fix reproducible defects with a failing regression test first.

- [ ] **Step 10: Update README**

Document Node/.NET/WASM prerequisites, install/build/dev/test commands, cross-origin isolation, ignored runtime outputs, and the square-Latin/auxiliary boundary.

- [ ] **Step 11: Commit**

~~~powershell
git add .github/workflows/web.yml README.md SudokuSolverWeb
git commit -m "Verify the web frontend vertical slice" -m "### Added" -m "Added real-WASM desktop and mobile acceptance coverage." -m "Added visual regressions and local development documentation."
~~~

---

## Milestone exit criteria

- A native `PuzzlePackageV1` round-trips through TypeScript, C#, IndexedDB, and WASM without f-puzzles as app state.
- Console and WASM use one processor while legacy userscript compatibility remains.
- Set and Playtest share one SVG canvas and work on desktop and Pixel 7.
- The 9×9 grid is verified; `aux-1` is editable and visual-only.
- Exactly one candidate context contributes marks/controls; inactive contexts preserve state and start no jobs.
- True Candidates supports both refresh policies and all three display modes at the layer control.
- Logical Solver owns candidates, deductions, Next Step, and Walkthrough; naked single is pure, structured, applied, and drawable.
- Semantic edits invalidate by revision; cosmetic edits do not; stale responses never render.
- Autosave, recovery, history, cancellation, mobile, and visual browser gates pass.
- Existing solver tests and WASM build remain green.
