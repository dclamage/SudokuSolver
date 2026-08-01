# Solver optimization roadmap

A handoff document so each improvement below can be picked up in a **fresh session** with no prior
context. Read the "Orientation" and "Playbook" sections first, then jump to the one improvement you
want to work on.

_Last updated: 2026-08-01._

---

## Orientation (read first)

- **Repo:** this repo (`/Users/d.clamage/git/SudokuSolver`). Work off `dev` (the main branch).
- **Reference solver:** `/Users/d.clamage/git/Interactive-Sudoku-Solver` (ISS) — a fast open-source
  JS solver. There is permission to reuse its ideas/code. Its search engine (`js/solver/engine.js`)
  and constraint handlers (`js/solver/handlers.js`) are the model for low-allocation, no-enumeration
  propagation.
- **Machine note:** this laptop idle-sleeps and suspends processes, corrupting long timings. Wrap
  anything over a minute in `caffeinate -i <cmd>`.

### What's already landed on `dev`
- **Brute-force engine:** unified zero-allocation sum-constraint registry (`SumConstraintRegistry`),
  hidden-single scan early-out, zero-allocation pooled search, constraint **propagation queue**,
  conflict-score branch heuristic, arrow tuple support, and pool-exhaustion fallbacks.
- **Benchmark harness:** `benchmarks/SudokuSolverBenchmark` + `benchmarks/corpus.json` (see Playbook).
- **Skyscraper rewrite** (the template for the constraint work below): commit
  `01adeac` — ~6× faster and 1.7 GB → 0.26 MB on the Renban Skyscrapers puzzle.

### Parked / not landed
- **`feat/derived-sum-discovery`** branch: a correct, inert-by-default prototype that discovers
  combined sum constraints (M1 plumbing, M2 static scorer, M3 combined little killers, + range
  support). **Investigation concluded it does not currently pay off** — weak-link discovery already
  solves the puzzles it would help, so it's parked, not merged. Don't revive without a puzzle that
  genuinely branches *and* where a sum-combo beats weak-link probing.
- **Weak-link discovery incremental re-probing:** investigated and shelved. Pass-capping was proven
  (via controlled benchmark A/B) to regress killer-cage/lk10 — the passes are load-bearing. A correct
  incremental version needs propagation-dependency tracking; it's a real project, not a quick win.

### Stale local branches (safe to delete)
`perf/solver-optimizations`, `perf/benchmark-harness`, `perf/skyscraper` are fully merged into `dev`.
`perf/weak-link-discovery` is a stale investigation branch. Prune with `git branch -d` / `-D`.

---

## Playbook: optimizing a hot constraint (the repeatable recipe)

This is exactly how the Skyscraper win was done. Each constraint improvement below follows it.

1. **Branch:** `git switch -c perf/<name> dev`.
2. **Get a measurable puzzle.** The benchmark only proves a win if a corpus case exercises the
   constraint. Look in `SudokuTests/Puzzles.cs` (`uniqueVariantFPuzzles`, 35 puzzles with validated
   solutions), `SudokuTests/CustomConstraintTests.cs`, `SudokuTests/SumConstraintTests.cs`. To find
   slow ones, time them (a throwaway `[TestMethod]` that loops the collection and `Assert.Fail`s a
   sorted-by-time table — see how Skyscraper's renban puzzle was found). If none exists, construct
   one with `-c <consoleName>:<options>` on a givens grid. Add it to `benchmarks/corpus.json` with an
   `expected` value (a capped count or `solve`→1). Then commit that corpus addition first.
3. **Save a baseline in the SAME session** (baselines drift across machine states — never compare
   against an old file):
   `caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 7 --save /tmp/base.json`
4. **Fix scheduling** if the constraint is in the always-run bucket: give it
   `public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;` (precompute an
   `int[] cellIndices` in the ctor). This makes brute-force propagation re-run it only when a watched
   cell changes. Deduction-neutral (only affects the BF queue, not logical solving). Modest alone.
5. **Rewrite `StepLogic` allocation-free** (this is usually the big win):
   - Use `stackalloc` scratch (buffers are `MAX_VALUE ≤ 31`); **clear** them.
   - **Never allocate per call** (no `new List`, `.ToList`, `.Permutations()`, `new T[]`) and **do
     not call `CanPlaceDigits`** — it allocates a `List<int>`. Replicate its check with the
     allocation-free `solver.IsWeakLink(candA, candB)` (it enforces cross-constraint weak links,
     e.g. Renban's `|v0-v1| >= lineLength`).
   - Replace any permutation/subset enumeration with a DP or pruned DFS with **early termination**
     ("stop once every candidate has support").
   - Compute every keep-mask from **one board snapshot** before applying any `KeepMask`.
   - **Never store mutable scratch on the constraint instance** — constraints are shared by reference
     across all search-tree clones AND across threads (see Invariants).
   - Only allocate the elimination list when `logicalStepDescription != null` (i.e. not brute force).
6. **Correctness = solution counts unchanged.** A sound `StepLogic` only removes candidates that
   can't be in any solution; the rewrite may be a *relaxation* (weaker) but must never eliminate a
   valid candidate. Validate:
   - Full test suite: `caffeinate -i dotnet test -c Release`.
   - **Count differential vs `dev`:** `git worktree add --detach <tmp> dev`, build its console, and
     compare `CountSolutions` (`-n -x <cap>`) for several puzzles using the constraint across all
     variants (directions, partial lines, edge clues). Every count must match. (See the Skyscraper
     commit's approach.)
7. **Benchmark A/B:**
   `caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 7 --baseline /tmp/base.json`
   Expect a large drop on the target; treat sub-~10 ms and unaffected-constraint deltas as noise.
8. **Commit**, then land: `git switch dev && git merge --ff-only perf/<name>`, retest, `git push origin dev`.

### Invariants a fresh session must respect
- **Constraints are shared by reference** across every cloned `Solver` in the search tree and across
  threads in multi-threaded solving. `StepLogic`/`EnforceConstraint` must be re-entrant and hold no
  mutable per-call state on the instance. Precomputed immutable data + stack/thread-local scratch only.
- **`CellIndicesForPropagationQueue`** only affects the brute-force propagation queue. Returning
  `null` (the default when `Group` is null) puts the constraint in the "always-run" bucket that fires
  every propagation step.
- **Weak links are frozen during a solve** (added at setup by `DiscoverWeakLinks`), so queue-driven
  scheduling keyed on cell changes is correct during brute force.

---

## The audit (source of the improvements below)

All of these constraints have brute-force `StepLogic`. Two anti-patterns: no `Group`+no
`CellIndicesForPropagationQueue` ⇒ always-run bucket; and per-call allocation in `StepLogic`.

| Constraint | Group | PropQueue | allocs in StepLogic |
|---|:-:|:-:|:-:|
| NFAConstraint | – | – | 7 |
| SandwichConstraint | – | – | 5 |
| OrthogonalValueConstraint | – | – | 3 |
| BinaryLookupConstraint | – | – | 2 |
| XSumConstraint | – | – | 1 |
| DiagonalNonconsecutive / Quadruple | – | – | 1 |
| BetweenLine, Even, Odd, Maximum, Minimum, Whispers, SlowThermometer, Taxicab, SelfTaxicab, Indexer, Chess, Palindrome | – | – | 0 |
| (already good) KillerCage, Renban, Thermometer, ArrowSum, InnieCage, LittleKiller, EqualSums, Skyscraper | ✓ or PQ | | 0 |

Regenerate this table any time with the grep in the git history of this file's creation, or:
`grep -lE 'override LogicResult StepLogic' SudokuSolver/Constraints/*.cs` then check each for
`override List<(int, int)> Group`, `CellIndicesForPropagationQueue`, and `new List`/`.Permutations`.

---

## Improvement 1 — `OrthogonalValueConstraint` ✅ DONE (commit `13b31bc`, 2026-08-01)

**Key finding that reframed this one:** `OrthogonalValueConstraint.StepLogic` returns `None`
*unconditionally* while brute forcing (guard at the top of the method) — the constraint is enforced
entirely by the weak links it adds in `InitLinks`. So its `StepLogic` **allocations never fire during
brute force** (they only fire in logical solving); the audit's "3 allocs" is a logical-solve cost, not
a brute-force one. The real brute-force cost was pure scheduling: it sat in the always-run bucket,
paying a virtual no-op call per instance on every propagation step.

**What landed (deduction-neutral, brute-force scheduling only):**
- New `Constraint.WantsBruteForcePropagation` virtual (default `true`). When `false`, the constraint is
  left out of the brute-force propagation queue **entirely** — neither always-run nor cell-scheduled —
  wired in `SolverInitialization.FinalizeConstraints` (the queue-build loop `continue`s on it).
- `OrthogonalValueConstraint` overrides it to `false`.
- **Dead end avoided:** first tried `CellIndicesForPropagationQueue` = marker cells (cell-scheduling).
  That *regressed* ~8% — marker cells change constantly, so per-cell-change enqueue bookkeeping costs
  more than the always-run no-op it removes. For a no-op-during-BF constraint the only win is to not be
  in the queue at all; hence the new opt-out flag.
- **Result:** ~5–6% faster on a 16-instance kropki search (`kropki-search-cap50k`, added to corpus).
  Win scales with the number of `OrthogonalValueConstraint` instances; ~noise on 1–2 instances. Full
  suite (94) passes; counts match `dev` across difference/ratio/sum markers, negative constraints, and
  a unique negative-kropki puzzle.

**Follow-up for a later session:** the `StepLogic` *logical-solve* allocations are still there
(`GetRelatedConstraints(...).SelectMany(...).ToHashSet()` per call at the top of `StepLogic` + the
per-call `valInstances` array). Only worth it if a `solve`/logical corpus case is dominated by this
constraint. Also: `WantsBruteForcePropagation => false` is reusable by any other constraint whose
`StepLogic` short-circuits on `isBruteForcing` — worth auditing (grep `if (isBruteForcing)` in
`Constraints/*.cs`) as part of Improvement 5.

## Improvement 2 — `SandwichConstraint`

- **File:** `SudokuSolver/Constraints/SandwichConstraint.cs` (~669 lines). Allocates 5× in `StepLogic`;
  always-run. Sol flagged it. Membership is dynamic (cells strictly between the 1 and the 9 on a line),
  so it's more involved than Skyscraper.
- **Approach:** precompute per-clue the valid crust positions / filling combinations at construction;
  at step time, bitmask-validate against current candidates with `stackalloc` scratch and early-out.
  Check ISS for a sandwich handler (`grep -i sandwich js/solver/handlers.js`).
- **Measure with:** a sandwich puzzle (construct via `-c sandwich:<sum>r<X>c<Y>` on givens, or find one).

## Improvement 3 — `XSumConstraint`

- **File:** `SudokuSolver/Constraints/XSumConstraint.cs`. Uses `SumGroup` directly; allocates; always-run.
  The prefix length is variable (= the first cell's value), so it's a natural fit for a small
  allocation-free per-value fast path. Consider whether it can register with `SumConstraintRegistry`
  (see the sum-registry adoption note below).
- **Measure with:** an X-sum puzzle (`-c xsum:<sum>r<X>c<Y>`).

## Improvement 4 — `NFAConstraint` (highest raw allocation: 7)

- **File:** `SudokuSolver/Constraints/NFAConstraint.cs` (regex/automaton line constraint). Highest
  per-call allocation in the audit, but **niche** — only matters on regex-constraint puzzles, so
  measure first to confirm it's worth it. The NFA transition tables should be precomputed and the
  per-step run should be an allocation-free automaton walk over the line.

## Improvement 5 — Broad scheduling-only pass (low risk, general)

- Add `CellIndicesForPropagationQueue` (precomputed `int[]` of watched cells) to the cell-local
  always-run constraints in the audit (BetweenLine, Even, Odd, Maximum, Minimum, Whispers,
  SlowThermometer, DiagonalNonconsecutive, Taxicab, SelfTaxicab, Indexer, Palindrome, …). Skip any
  that legitimately watch the whole board (e.g. Chess king/knight negative modes) unless you enumerate
  their real watched pairs. Deduction-neutral; verify with the full test suite. Value is modest per
  constraint (~3% seen on Skyscraper's scheduling half) but reduces per-step overhead broadly on
  puzzles that stack many of these.

---

## Non-constraint follow-ups (separate sessions)

- **Sum-registry adoption:** migrate remaining sum-shaped constraints (X-Sum, Sandwich, arrows'
  sum side) onto `SumConstraintRegistry` for the zero-allocation BF sum path. Note: the registry
  handles fixed size/sum via `SumCellsHelper`'s list path (killer cages already rely on it), and the
  64-bit sum-mask fast path is capped at sum ≤ 63 / ~7 cells.
- **`-s` determinism:** the conflict-score heuristic can return a different (still valid, still
  deterministic) solution than before for *multi-solution* puzzles. Decide: document it, or keep the
  single-solution path order-stable. Low effort.
- **Branch cleanup:** decide the fate of `feat/derived-sum-discovery` (keep parked / delete / a short
  README documenting the negative result) and prune the merged branches.

---

## Using the benchmark (quick reference)

```bash
# full run
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark
# a subset + save a baseline (same session as the comparison!)
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- \
  --iterations 7 --filter <substr> --save /tmp/base.json
# after a change, compare
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- \
  --iterations 7 --filter <substr> --baseline /tmp/base.json
```
Exit codes: 0 ok, 1 a result failed its `expected` validation, 3 a >5% regression vs baseline.
See `benchmarks/README.md` for the corpus format and the timing-noise caveat (always compare within
one session; treat only consistent, repeatable deltas on non-trivial cases as real).
