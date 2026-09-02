# Sudoku Solver Web Frontend Architecture

**Status:** Approved design · **Date:** 2026-09-01 · **Branch:** `codex/web-frontend`, based on
`wasm-prototype`

## Purpose

Build a local-first, eventually hosted web frontend for SudokuSolver. The product is a first-class
puzzle setting tool, a SudokuPad-familiar playtest environment, and a rich interface to the C# WASM
solver. It should retain the best ideas from f-puzzles, SudokuMaker, SudokuPad, Interactive Sudoku
Solver (ISS), and Sudoku Coach without treating any existing file format or UI architecture as its
foundation.

The frontend and solver will be developed together. The solver is not a fixed black box: its input
model, custom-constraint support, logical deduction model, and explanation capabilities will change
where that enables a materially better setting, solving, or analysis experience.

## Product principles

1. **One puzzle, three workspaces.** Set, Playtest, and Analyze operate on one native puzzle
   document and one shared rendering surface.
2. **Local first, hostable later.** Initial persistence is browser-local and file-based. Repository,
   identity, and revision boundaries must support hosted storage, publishing, and accounts later.
3. **The document is more general than today's solver.** The native model can describe arbitrary
   cells, layouts, domains, groups, and constraints. The current C# solver consumes the supported
   square-Latin subset and reports its coverage.
4. **Unsupported does not mean forbidden.** Setters may create, save, playtest, and export puzzles
   with partially supported or visual-only semantics. The UI must never overstate validation.
5. **Custom constraints are first-class.** Their behavior, appearance, parameters, tests, compiled
   releases, and instances are native parts of the product.
6. **Mobile is a primary target.** Setting, playtesting, analysis, and custom authoring receive
   purpose-built phone and tablet interactions rather than a collapsed desktop layout.
7. **Automation is deterministic.** Human and LLM automation produces replayable scripts and
   atomic authoring plans. The LLM translates explicit intent; it is not a creative puzzle setter.
8. **Explanations are structured data.** The solver reports facts, proofs, and walkthrough frames;
   it does not reduce logical work to an eagerly constructed prose string.

## Competitive conclusions

- **f-puzzles** established a low-ceremony, discoverable element workflow and remains an important
  import source, but its format and architecture will not be used as native application state.
- **SudokuPad** is the interaction target for Playtest: familiar digit, corner, centre, and color
  modes; selection behavior; keypad; undo/redo; checking; rules; timer; and mobile controls.
- **SudokuMaker** is the strongest setting-UX reference: searchable elements, persistent element
  layers, direct inspectors, playtesting, custom constraints, and precise cosmetic drawing.
- **ISS** demonstrates the desired solver power and the value of programmable construction,
  custom constraints, arbitrary variables, and solver queries. Its internal-facing interface is a
  warning to expose a deliberate stable API rather than raw solver objects.
- **Sudoku Coach** demonstrates the value of human-oriented technique descriptions, guided
  walkthroughs, and difficulty-aware solving.

## Technical architecture

Use a modular TypeScript platform with a React application shell and an SVG-based scene renderer.
Packages remain internal until reuse justifies publishing them.

- `document`: native schema, migrations, commands, transactions, history, and persistence ports.
- `scene`: scene graph, SVG rendering, layers, hit testing, snapping, guides, and interaction
  annotations.
- `constraints`: built-in/custom definition model, authoring SDK, compiler, verifier, and compiled
  artifacts.
- `solver-client`: typed, versioned communication with the C# WASM solver service.
- `formats`: native package containers, f-puzzles import, and later SudokuPad SCL export.
- `app`: Set, Playtest, Analyze, custom-definition, automation, and settings experiences.

React owns presentation and subscriptions, not puzzle semantics. Domain operations are framework-
independent and testable without rendering components.

## State boundaries

Four kinds of state remain separate:

### Puzzle document

Versioned, portable, authoritative puzzle data: metadata, rules, cells, domains, boards, groups,
adjacency, constraint instances, custom definitions, layouts, styles, scene elements, and embedded
asset references.

### Solve session

Progress belonging to a particular attempt: entered values, corner/centre marks, colors, timer,
checkpoints, completion, and session-specific settings. Multiple sessions may reference one puzzle
without mutating its definition.

### Editor state

Ephemeral UI state: selection, active tool, viewport, open sheets/panels, hover/focus, and unfinished
gestures. It is not serialized into the puzzle package.

### Generated state

Solver projections, general caches, thumbnails, and other reproducible artifacts are disposable.
The exception is an immutable compiled release for a custom definition: that release travels with
the puzzle so opening a shared document does not execute source code.

## Native topology and cell model

The native model must not make a rectangular matrix foundational.

- **Cell:** stable ID, geometric footprint, domain reference, input capabilities, metadata, and
  optional visual labels.
- **Value domain:** ordered numeric, symbolic, textual, or named values plus optional numeric
  interpretations used by arithmetic constraints.
- **Group:** explicit cell membership and semantic roles such as row, column, region, or
  all-different. Membership is not inferred from coordinates.
- **Adjacency:** explicit or geometry-derived relationships with manual overrides.
- **Board:** a named collection of cells, groups, and presentation bounds, not an implicit matrix.
- **Constraint:** typed references to cells, groups, edges, points, and paths.
- **Layout:** visual geometry, transforms, and layers independent of logical membership.

There is no permanent `core` versus `auxiliary` cell type. Solver participation is projection-
specific. Today a `LatinSquareProjection` maps one compatible set of cells and groups into the
square C# board. Off-grid cells are omitted. A future generalized projection can include the same
unchanged cells.

Auxiliary cells receive the full solving interaction model: entered values, candidate marks,
colors, undo/redo, and optional initial values. They default to the main board's domain but may
reference a custom numeric, textual, or symbolic domain.

Moving a cell does not silently alter its group, adjacency, or solver meaning unless the user
explicitly invokes a geometry-derived topology operation.

## Commands, history, and storage

All puzzle mutations use reversible domain commands. Gestures produce a transaction and commit one
atomic history entry. This applies to values, geometry, styles, layer operations, definitions,
bulk scripts, and imports.

Initial storage uses IndexedDB behind a repository interface with document IDs, revision tokens,
and optimistic concurrency. It provides continuous autosave after committed commands, crash-
recovery journals, named snapshots, recent documents, and independent solve-session storage.

The hosted repository planned for the future will implement the same interface. Stable entity IDs
and transactional commands preserve a path to later collaboration without prematurely adopting a
CRDT.

## Scene graph and precision drawing

One controlled scene graph powers puzzle rendering, custom constraint appearance, cosmetics,
selection, solver annotations, and SCL-oriented SVG export.

Supported primitives include paths, lines, polygons, circles, rectangles, text, markers, cell
fills, clipping, groups, and transforms. Styles use theme-aware tokens with explicit instance
overrides. Custom renderers cannot manipulate the DOM.

Coordinates are continuous canvas coordinates. Precision tools layer optional snapping over them:

- Per-tool or per-element N-by-N cell subdivisions
- Cell centers, corners, edge divisions, and custom anchors
- Existing vertices and intersections
- Alignment and distance guides
- Angle constraints, symmetry, duplication, rotation, and reflection
- Numeric coordinate editing and keyboard nudging
- Free placement when snapping is disabled

Snapped points retain exact rational coordinates where possible; freeform points use normalized
decimals. Guides appear during drawing/editing rather than permanently cluttering the puzzle.
Paths remain editable after creation, including movable vertices and optional curve handles.

Mobile editing uses enlarged screen-space handles and a magnified placement preview so a finger
does not obscure the target.

## Application workspaces

### Set

- Searchable built-in, custom, recent, and favorite element library
- Unbounded pan-and-zoom canvas
- Direct drawing and manipulation with snapping and handles
- Inspector for semantics, parameters, styles, rule text, support, and validation
- Ordered layers with lock, visibility, grouping, and reordering
- Persistent foundational elements such as givens and regions
- Transactional undo/redo and named checkpoints

### Playtest

- SudokuPad-familiar digit, corner, centre, and color modes
- Multi-cell selection, keypad, keyboard shortcuts, undo/redo, checking, rules, and timer
- Responsive touch controls and complete auxiliary-cell input
- Separate solve sessions, restartable from checkpoints, that never mutate the puzzle definition

### Analyze

- Validation, true candidates, solution count, solve estimate, next step, and full logical path
- Solver coverage displayed before results
- Cancellable background jobs
- Structured deduction list, proof details, and frame-by-frame walkthroughs on the shared surface
- Progressive disclosure so advanced diagnostics do not overwhelm ordinary setting

## Mobile interaction model

All pointer, touch, stylus, and keyboard inputs call the same domain commands. No essential action
depends on hover, right-click, or precise mouse placement.

- One-finger gestures use the active tool; two fingers pan and zoom.
- A hand tool enables one-finger panning.
- Long-press provides contextual actions, but all actions also have visible controls.
- Tool handles and hit targets remain usable in screen space at every zoom level.
- Interrupted gestures cancel or commit atomically.
- The canvas occupies nearly the full phone screen.
- Primary modes and keypad live in a thumb-reachable bottom dock.
- Element library, inspector, layers, rules, and analysis use draggable bottom sheets.
- Complex editors may become temporarily full-screen.
- Portrait and landscape preserve document and selection state.
- Safe areas and software keyboards cannot obscure active controls.

Setting supports both tool-first and selection-first workflows. Mobile interaction and performance
are feature acceptance criteria, not a later responsive-design pass.

## Capability reporting

Every semantic entity is evaluated against the active solver implementation and receives a status:

- Fully verified
- Partially verified
- Visual only
- Invalid definition

The report includes reasons and affected entities. Playtest, persistence, and export remain
available with partial support, but solver results state precisely which semantics were represented.
As the solver gains capabilities, unchanged documents may receive stronger support automatically.

## Custom constraint definitions

A custom definition is versioned separately from its puzzle instances.

It contains:

- Identity, name, description, categories, and rule-text templates
- Typed target signature: cells, ordered paths, edges, groups, points, or combinations
- Parameter and style schemas
- Authoring source for semantics and rendering
- User-supplied examples, expected valid/invalid cases, and accepted behavioral properties
- Compiler and SDK versions
- Immutable compiled release with content hashes

An instance stores a definition revision reference, selected targets, parameter values, style
overrides, and optional rule-text overrides.

Opening a shared puzzle uses the compiled release. Source executes only when a user deliberately
opens and rebuilds the definition in the sandboxed authoring environment.

### Semantic intermediate representation

The IR is extensible and composable rather than synonymous with one NFA encoding. Initial forms
include:

- Binary relation tables
- Allowed and forbidden tuples
- Sequence automata
- Typed arithmetic expressions and relations
- Native sum representations
- Composite constraints
- Future propagator representations

Arithmetic is first-class. Expressions include cell, group, path, and parameter values; literals;
sums; weighted sums; differences; counts; ranges; equality/inequality/ordering; and modular
relationships. All-different and repeat policies remain independent predicates.

Compilation selects the most suitable supported backend: native solver sum machinery, bounded
tuple enumeration, binary tables, or prefix-state automata. Large sums must not be forced into
impractical lookup tables or NFAs.

This model supports killer cages, arrows, equal-sum groups, sandwich results stored in another cell,
weighted paths, ranges, and future auxiliary-cell arithmetic. Unsupported auxiliary semantics are
reported without losing the native definition.

### Custom appearance

A definition owns its default renderer, style schema, hit regions, editable handles, layer role,
and visual states for setting, solving, selection, errors, and disabled behavior. Instance values
may override exposed style parameters.

Advanced rendering source produces controlled scene-graph primitives. It has no DOM, network,
clock, or ambient randomness. Custom constraints can therefore have distinctive visuals while
remaining deterministic, theme-aware, selectable, serializable, and exportable.

### Authoring paths and validation

All authoring paths produce the same definition package:

- Guided visual composer
- Advanced programmable editor
- LLM translation and repair

Validation type-checks, compiles, enforces resource limits, runs examples, performs bounded
enumeration/property checks, checks render determinism, and returns concrete counterexamples. Only
the setter can accept a compiled release.

## Shared native semantic format

The authoritative `PuzzlePackage` is a versioned native format shared by TypeScript and C#. It has
a stable semantic core plus presentation, source, release, asset, provenance, and namespaced
extension sections.

The WASM protocol may transmit only the semantic view or semantic deltas for performance, but those
objects reuse the native schema and stable IDs. It does not define a second f-puzzles-like puzzle
model.

The C# side performs the authoritative projection into the capabilities of the active solver and
returns the capability report. This keeps solver-support logic out of React and avoids divergent
TypeScript/C# projections.

Schema evolution uses explicit migrations. The application preserves the original package until
migration and validation succeed, retains unknown namespaced extensions, and uses deterministic
serialization for hashes, diffs, caching, and reproducible builds.

## Solver service and protocol

The frontend depends on a `SolverService` interface. Browser WASM is the first implementation; a
future hosted implementation may use the same contract.

Initial operations include validation, solve, true candidates, count, estimate, next logical step,
and complete logical path. Jobs carry protocol version, request ID, document/session revisions,
semantic hash, operation options, and cancellation identity. Responses repeat relevant revisions so
stale results are discarded.

Unsupported features, invalid definitions, contradictions, limits, progress, and cancellation are
typed outcomes rather than prose-only errors. Expensive jobs are cancellable and never block the UI
thread. Cosmetic edits retain semantic solver caches.

The existing duplicated console/WASM command-processing behavior should move behind a shared C#
service used by both hosts.

## Logical solver and explanation model

Logical solver modernization is tandem product work, not a deferred adapter enhancement. The plan
in `docs/logical-solver-audit.md` supplies the implementation spine.

### Find/apply separation

Every logical technique moves from mutation during discovery to pure discovery plus explicit
application:

```text
Find(position, options) -> deductions
Apply(position, deduction) -> validated board delta
```

This permits enumeration, ranking, comparison, replay, and UI inspection without mutating the board.

### Technique catalog

A queryable registry supplies stable ID, display metadata, category, default state, human
difficulty, cost tier, applicability conditions, and owning constraint. Technique controls use IDs,
not hard-coded flag bits. Uniqueness-dependent techniques require explicit opt-in when uniqueness
has been established.

### Deduction and proof data

A deduction contains:

- Technique and owning-constraint IDs
- Premises and conclusions
- Placements and eliminations
- Candidate, cell, group, constraint, edge, and path references
- Strong and weak links
- Arithmetic equations and value-set relationships
- Assumptions, contradictions, and nested proof nodes
- A board precondition hash and applicable delta

Text rendering is separate so the UI can provide terse, normal, beginner, expert, and eventually
localized descriptions without the solver constructing every discarded string.

### Walkthrough frames

A default walkthrough is a sequence of semantic frames. A frame can focus or dim entities,
highlight candidates by role, draw links/arrows/outlines/paths/callouts, display typed equations or
rule applications, and reveal a conclusion or board delta.

The solver emits semantic annotations, not SVG or pixel coordinates. The shared scene renderer maps
them to the active geometry, theme, viewport, and device.

Implementation starts with the find/apply foundation and representative vertical slices: a single,
a tuple, a sum-based variant constraint, an AIC, and a custom constraint. Each must be discoverable,
applicable, explainable, drawable, and walkable before the model is migrated across all techniques.
Logical-arm performance and allocation become explicit gates as enumeration replaces first-match
short-circuiting.

## Programmable authoring sandbox

The sandbox is a first-class feature for both human automation and LLM-generated scripts. It exposes
a stable, versioned `PuzzleAuthoringAPI`, not React state, DOM objects, or raw solver internals.

Scripts may:

- Query boards, cells, groups, adjacency, selections, geometry, and existing elements
- Create, update, transform, replicate, and remove constraint instances
- Create auxiliary cells, paths, cosmetics, text, and scene elements
- Define and compile custom definition revisions
- Generate givens or solution data according to explicit algorithms
- Invoke validation, solving, counting, candidates, logical paths, and difficulty analysis
- Use rows, columns, regions, rays, blocks, overlays, rotations, reflections, and pattern stamping
- Use explicit seeded randomness for reproducible operations

A run receives immutable input and returns an `AuthoringPlan` containing proposed commands, solver
results, diagnostics, provenance, warnings, and before/after preview data. Applying it is one atomic,
undoable command.

Scripts cannot access the DOM, network, storage, clipboard, ambient time, or application globals.
They execute inside an embedded capability-limited JavaScript runtime, not browser `eval`,
`new Function`, or an ordinary worker with ambient web APIs. CPU time, memory, solver work, emitted
operations, and output size are bounded.

## LLM authoring boundary

The governing rule is:

> The LLM translates explicit intent into a replayable script; it never supplies puzzle-design
> judgment during execution.

The pipeline is:

```text
explicit instruction -> deterministic authoring script -> preview and validation
```

The script may call solver operations, branch, iterate, and use a specified seed, but it cannot call
the LLM. Given the same document, parameters, seed, native schema, compiler, and solver versions, it
must produce the same plan.

The LLM may translate a fully specified custom rule, place mechanically selected constraints,
calculate clues from a provided solution, automate transformations, generate exact cosmetics, and
repair compilation/test failures while preserving the specification.

It may not choose a theme, constraint palette, placements, givens, intended deductions, difficulty,
aesthetic direction, or which puzzle content to alter for uniqueness. When execution would require
subjective judgment or an unspecified design choice, it identifies the missing input and stops.

Each planned change must trace to an instruction clause. Unexplained changes fail plan validation.
If a repair cannot satisfy the specification exactly, the system reports the conflict rather than
creatively changing the puzzle.

The LLM remains outside script execution. It may receive structured compiler/test diagnostics and
produce a revised deterministic script or definition as a new draft. The setter reviews and accepts
every LLM-generated plan or compiled release.

A provider-neutral service isolates model APIs. Local development may use a configured development
endpoint; future hosting may add authenticated access, budgets, rate limits, and abuse controls
without changing the authoring model. Core setting and automation work without an LLM.

## Import, export, and packaging

The logical native schema is JSON-compatible and inspectable. Physical containers may later add
compression and content-addressed binary assets without altering the document model.

f-puzzles is import-only at the product layer. Existing solver support may remain, but an imported
puzzle is converted into native state once. Adapters emit an itemized conversion report:

- Exact
- Approximated
- Preserved as visual-only
- Preserved as opaque source data
- Unsupported or omitted

No adapter silently drops an element.

SudokuPad export will target SCL rather than f-puzzles. The later SCL-specific design will
investigate its SVG capabilities and determine how semantic elements, auxiliary cells, generated
rule text, and scene-graph visuals are represented. The export report distinguishes appearance from
semantics SudokuPad can enforce.

## Reliability and security

- Opening shared puzzles executes no source code.
- Sandboxed code has explicit capabilities and bounded resources.
- Failed migration, compilation, import, or script execution leaves the last valid document intact.
- Custom releases include source/compiler/artifact hashes and version metadata.
- Autosave, journals, and snapshots make recovery visible rather than silently resetting state.
- Solver results are revision-safe and coverage-qualified.
- Imported/embedded content is treated as untrusted data.

## Verification strategy

### Document and geometry

- Schema migration and deterministic serialization fixtures
- Stable identity and unknown-extension preservation
- Command inversion, transaction, undo/redo, and crash-recovery tests
- Snapping, path editing, transforms, hit testing, and arbitrary cell geometry tests
- Property tests for round trips and inverse operations

### Cross-language contracts

- Shared native fixtures deserialize and validate in TypeScript and C#
- Semantic hashing and entity mapping agree across runtimes
- Protocol compatibility and migration fixtures cover supported version ranges

### Custom constraints and sandbox

- Compiler and IR fixtures for binary, tuple, automaton, arithmetic, and sum forms
- Bounded enumeration, property checks, and counterexample fixtures
- Render determinism and scene validation
- Resource-limit, capability-denial, reproducibility, and plan-provenance tests
- Compiled-release compatibility across supported compiler/solver versions

### Solver

- Existing correctness and performance corpora remain gates
- Every deduction validates its precondition, declared facts, and resulting delta
- Applying a deduction preserves board validity and matches its explanation
- Proof/walkthrough fixtures cover representative classic, variant, chain, arithmetic, and custom
  deductions
- Logical enumeration receives explicit allocation and runtime budgets

### Application

- Desktop and mobile browser flows for setting, gestures, playtesting, analysis, persistence,
  recovery, and conversion
- Visual regression across representative puzzles, themes, viewports, zoom levels, and display
  densities
- Accessibility tests for keyboard navigation, focus, labels, and non-color-only state

## Performance strategy

- Solver and compiler work never blocks the UI thread.
- The WASM solver loads lazily and is cached for offline reuse.
- Cosmetic edits do not invalidate semantic caches.
- Scene updates are localized to affected nodes and overlays.
- Continuous gestures avoid document commits until completion.
- Solver jobs are debounced, cancellable, and revision-safe.
- Mobile startup, memory, rendering, and interaction latency are measured from the first milestone.

## Delivery sequence

### 1. End-to-end foundation

Create the TypeScript/React workspace, native schema, commands, IndexedDB persistence, shared SVG
surface, a 9x9 board plus auxiliary cell, native C# reader/protocol, basic Set and Playtest flows,
solve/validate/count/true candidates, one structured deduction walkthrough, and desktop/phone shells.

### 2. Useful setting beta

Add the searchable element registry, most-used built-in constraints, unbounded canvas, layers,
styles, adaptive precision drawing, auxiliary domains, capability reporting, f-puzzles import,
SudokuPad-familiar playtesting, and mobile setting flows.

### 3. Custom-constraint studio

Implement definition packages, typed semantics and arithmetic, binary/tuple/automaton/native-sum
compilation, custom scene rendering, guided and programmable editors, validation, counterexamples,
compiled releases, and matching C# representations.

### 4. Rich logical solving and analysis

Continue the stream begun in milestone 1: complete find/apply separation, technique catalog,
structured proof/walkthrough coverage, Analyze workspace, migration of existing logical techniques,
variant-human-logic improvements, and required performance work.

### 5. Deterministic authoring automation

Deliver the versioned `PuzzleAuthoringAPI`, capability-limited JavaScript sandbox, geometry and
replication helpers, solver calls, authoring-plan preview/diff/apply, reproducible seeds, and resource
budgets.

### 6. LLM translation and repair

Add natural-language-to-definition/script translation, compiler/test repair, provenance checks,
non-creative-executor enforcement, and a provider-neutral integration.

### 7. Interchange and release hardening

Investigate and implement SCL/SVG export, native package download and compressed sharing, offline
caching, migrations, and cross-browser/mobile/visual/performance release gates.

Accounts, community publishing, collaboration, and hosted storage are explicitly deferred. Their
future needs are represented by the repository, revision, identity, package, and service boundaries.

## Deferred decisions

- Exact native file extension and compressed container
- Exact embedded JavaScript runtime and editor implementation
- SCL feature mapping and SudokuPad semantic limitations
- Hosted storage, account, publishing, moderation, and collaboration design
- Model provider, pricing, authentication, and quota policy
- General solver topology beyond the current square Latin projection

These decisions should be made in their relevant milestone specifications rather than guessed in
the initial architecture.
