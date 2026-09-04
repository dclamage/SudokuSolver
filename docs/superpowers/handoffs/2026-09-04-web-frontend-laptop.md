# Web frontend laptop handoff — 2026-09-04

## Branch and intent

- Remote branch to continue: `codex/web-frontend`
- Original base branch: `wasm-prototype`
- Implementation plan: `docs/superpowers/plans/2026-09-02-web-frontend-milestone-1.md`
- Architecture/design decisions: `docs/superpowers/specs/2026-09-01-web-frontend-architecture-design.md`
- Accepted visual references: `docs/superpowers/designs/2026-09-02-web-frontend/`

The project is a local-first, mobile-usable, first-class Sudoku setting and
solving frontend that integrates the native/WASM C# solver. It uses its own
forward-compatible native puzzle format, supports solver-ignored auxiliary
cells and arbitrary presentation geometry, keeps candidate workflows as
exclusive named layers, and is designed to grow toward custom constraints,
arbitrary grids, richer logical explanations, and eventually hosted community
features without requiring those future systems now.

## Completed and independently reviewed

Tasks 1–13 are complete on this branch and passed their independent review
loops:

1. Web workspace and quality gates.
2. Native puzzle package, validation, canonicalization, and shared fixture.
3. Reversible puzzle commands/history/store.
4. Native-to-C# Latin solver projection and capability reporting.
5. Shared native solver service/protocol.
6. Revision-safe single-threaded WASM worker client.
7. Shared SVG puzzle surface under the approved normalized-geometry contract.
8. Responsive Set and Playtest shells.
9. Exclusive candidate-context/layer orchestration.
10. Typed Setter Notes Corner/Centre/Color state and controls.
11. Configurable True Candidates: possibility, frequency, and real logical
    comparison, with native/WASM execution.
12. Pure structured `basic.naked-single` discovery/apply and restart-safe native
    logical sessions.
13. Logical Solver layer and responsive semantic walkthrough with readable
    current/archived step history.

The Task 13 final review baseline was 308 passing web tests. The Task 12 native
baseline was 248 passing .NET tests; later Task 14 work is frontend-only.

## Current stopping point: Task 14 partial

Checkpoint commit: `ee1d41a793feb45ff7b6184f208bedf2673f9011`
(`Checkpoint Task 14 persistence core`).

Completed in that checkpoint:

- Pure v1 migration/validation outcomes, including unsupported-future records.
- Typed `DocumentRepository` contracts and repository errors.
- IndexedDB `sudoku-solver-web` version 1 with `documents`, `journals`, and
  `sessions` stores.
- Strictly-newer transactional saves, recent metadata, selected-session state,
  exact journal clearing, and raw future-schema preservation.
- Autosave snapshot isolation, deterministic 300 ms scheduling, out-of-order
  protection, retry, journal lifetime, status ownership, and dispose behavior.
- Focused persistence suite: 21 passing tests; typecheck and scoped ESLint pass.

Task 14 is deliberately **not complete**. Continue with:

- Intentional `PuzzleStore`/`AppController` document replacement and async
  startup selection.
- Load the selected or most-recent supported document; never overwrite an
  unsupported-only database with a starter.
- Newer/equal/older and malformed/unsupported journal handling with guarded
  Restore/Discard actions.
- Autosave wiring to committed puzzle snapshots, with truthful
  Saving/Saved/Failed/Retry state.
- Accessible responsive Recent Documents and recovery UI.
- App/startup/UI tests and invocation-time ownership guards for delayed actions.
- Real production-build Chromium IndexedDB QA, reload/recovery checks, desktop
  and 390px screenshots, then full Task 14 verification and independent review.

After Task 14 passes review, execute Task 15 exactly as written in the plan.

## Important constraints and ledgered issues

- Use subagent-driven development with `gpt-5.6-sol` at `high`, strict TDD,
  independent review, and the existing maximum-five-fix-round workflow.
- Do not use Chrome browser control or the in-app browser until the user
  explicitly clears the recording constraint. Isolated headless Playwright is
  allowed and has been used successfully.
- Preserve existing user work and the isolated branch. Do not merge to
  `wasm-prototype`, rewrite history, or delete worktrees without explicit user
  direction.
- Repository-wide frontend lint has one known pre-existing finding at
  `SudokuSolverWeb/src/domain/puzzle/validatePuzzlePackage.ts:117`
  (`preserve-caught-error`). Scoped task lint is clean. This must be triaged by
  the final branch review before merge.
- WASM publishing emits four existing trim-analysis warnings in native package
  serialization/hashing and may note missing optional `wasm-tools`; real dev and
  preview runtime probes pass.
- Task 8 deferred two minor accessibility items for final triage: roving/arrow
  navigation for primary Set/Playtest tabs, and focus/Escape lifecycle for
  mobile sheets.
- Task 5 deferred minor deterministic-overlap/concrete-host coverage questions
  for final review.

## Resume checklist

1. Fetch and check out `codex/web-frontend` from `origin` in an isolated
   worktree on the laptop.
2. Confirm `git status --short --branch` is clean and HEAD contains
   `ee1d41a` plus this handoff note.
3. Read `AGENTS.md`, the architecture spec, the implementation plan, and this
   handoff before editing.
4. Run the focused Task 14 persistence tests and `npm run typecheck` to confirm
   the laptop environment.
5. Continue only the remaining Task 14 integration slice, keeping generated
   candidate/logical/Playtest runtime state out of document persistence.
6. Complete Task 14's full gates, real IndexedDB headless QA, report, commit,
   and independent review before beginning Task 15.

