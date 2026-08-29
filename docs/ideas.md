# Ideas backlog

Unvalidated ideas: things worth trying that nobody has measured yet. This is the **front of the
funnel**, and it is deliberately small and skimmable.

How it relates to the other docs:

| stage | lives in | contains |
| --- | --- | --- |
| **idea** | this file | a pitch, a reason it might work, and what would kill it |
| **scheduled work** | `HANDOFF.md` § 2 | scoped, prioritized items with a known shape |
| **answered** | `docs/<topic>-*.md` | an exploration with a verdict, kept whether the answer was yes or no |

An idea graduates when someone commits to working it: write the brief as its own doc, move the
entry's substance there, and leave a one-line link with a status. Do not let a full brief grow in
this file — it stops being skimmable, which is the only thing it is for.

**Status vocabulary:** `open` (nobody has started) · `active` (in progress, link the branch) ·
`graduated` (has its own doc) · `closed` (answered; link the verdict).

**An entry needs four things.** Pitch, why it might work, what would kill it, prerequisites. The
fourth is the one people skip and the one that saves the session — see the cross-cutting
prerequisites below, which currently block four of the six entries.

_Note: `docs/optimization-roadmap.md` predates this file (2026-08-01), is scoped to constraint
allocation work, and has stale macOS paths and a stale `dev` branch name. Treat it as historical._

---

## Cross-cutting prerequisites

### A committed node counter — blocks ideas 1, 2, 4 and 5

Most of the ideas below are **pruning** changes, and pruning is measured in nodes, not
milliseconds. (Idea 5 is the partial exception: if it substitutes for probing it also has a
straight wall-clock payoff, so it can be read on either axis.) Today `nodesVisited` lives in `NodeBudget` (`SolverBruteForce.cs:23`), only counts
when a budget is set, and is never surfaced. Every node figure in `weak-link-representation.md`,
`cell-forcing-worklist.md` and `branch-ordering.md` came from throwaway instrumentation that no
longer exists — so those claims are not currently auditable either.

Shape: a plain `long` per solver instance, summed at join. Do **not** make the existing
`Interlocked.Increment` unconditional — that puts a contended atomic on the hottest path in the
default, unbudgeted case.

Small work, outsized leverage. It is the first thing to do if any pruning idea is next.

### The parity census — cheap, and it stops you chasing ghosts

`weak-link-bitmatrix-exploration.md` established the method: census the deduction's outcome buckets
across the full ISS corpus under both arms and require bit-identical counts *before* reading any
timing. It cost little and converted a suspected hazard into a proven non-issue.

Also from that work: control for `est-*` cases. They sample randomly, so the same arm differs from
itself on `corpus.json` (~0.006%). A difference that size is not a signal.

### Design heuristic: new per-write maintenance usually loses

Ideas 2 and 3 both propose state maintained on the board-write path. The counts control arm in
`weak-link-bitmatrix-exploration.md` answered its query in O(1) with no new structure and **still
gained nothing**, because per-assign maintenance swamped a query that ran rarely. Before adding
anything to the write path, measure the query-to-maintenance ratio.

---

## 1. Conflict scores per candidate, not per cell

**Status:** open · **Prereq:** node counter

Today `conflictScores` is `int[NUM_CELLS]`, bumped through `IncrementConflictScore(int cellIndex)`,
VSIDS-style with halving every 16,384 increments (`CONFLICT_DECAY_INTERVAL`). Failure attribution
arrives as `_lastContradictionCellIndex`.

**Pitch.** Conflict scoring exists to stop thrashing. If the thrashing is caused by a specific
*candidate* rather than the cell as a whole, cell-level aggregation blurs the signal: a cell whose
value 3 is poison scores the same as a diffusely bad cell, and branch ordering cannot tell them
apart.

**Why it might work.** Attribution is already available at candidate resolution — a branch point is
a `(cell, value)` pair, so the failing candidate is known at the site where the score is bumped.
Only the index changes. Cost is trivial: 729 ints instead of 81, and the decay loop is 9× longer but
runs once per 16,384 increments.

**The part that is bigger than it looks.** Per-candidate scores imply a *value* order as well as a
*cell* order — the sudoku analog of phase saving. Decide deliberately whether to use them for value
ordering or to aggregate back up to cells for branching only. These are different experiments, and
mixing them makes the result unreadable.

**Prior art cuts both ways.** Original VSIDS scored literals; MiniSat moved to variable activity plus
phase saving. Per-cell matches modern practice, so this is a real question, not a free win.

**What would kill it.** Node counts don't improve, or improve only where the difference is noise.

**Measurement note.** This deliberately changes branch order, so the parity census is not the check
here. `SUDOKU_BRANCH_ORDER=static` is — use it to separate propagator effects from ordering effects.

---

## 2. Make pointing (locked candidates) cheap enough for in-search use

**Status:** open · **Prereq:** node counter

Today `FastFindPointing` (`SolverBruteForceLogic.cs:943`) runs only inside `FastAdvancedStrategies`,
i.e. at root setup. It iterates every group in `maxValueGroups` and, per group, rescans every cell to
rebuild its set-values and candidate-pool masks from scratch. No incrementality, no dirty set.

**Pitch.** Same shape as cell forcing: a deduction that helps specific puzzle types, currently priced
out of the search. But pointing may price *better* than cell forcing did, because the state its
trigger needs already exists and is already maintained.

**Why it might work — the specific asymmetry against cell forcing.**

- `_candidateCountsPerGroupValue` is already updated incrementally on every write by
  `TrackHiddenSingles` (`SolverModification.cs:506`). Pointing's trigger — a `(group, value)` with
  few remaining placements — is readable straight off that array with **zero new maintenance**.
- `_checkGroupForHiddens` is already a dirty-group bitset on the same path. Pointing can ride it
  rather than inventing a parallel one.
- So the enqueue side — where cell forcing bled, with 57.7% of pops firing on an already-dead
  target — starts from existing state instead of a purpose-built filter.

**The trap, stated up front.** Do **not** maintain per-`(group, value)` placement bitmasks. That is
new per-write maintenance, and it is exactly the shape the counts control arm already falsified. Use
the existing counts as the trigger, then scan for placements only in the few groups that pass.

**What would kill it.** The count-based trigger isn't selective enough, so most pops still scan and
find nothing; or the eliminations it finds are ones the constraint propagators already produce.

---

## 3. Defer reacting to candidate clearing

**Status:** open

**A correction to the premise first, because it changes the idea.** The *deduction* is already
deferred. A board write does not run naked-single or cell-forcing logic inline — it pushes to
worklists (`pendingNakedSingles`, `pendingCellForcing`) and sets bits (`_constraintQueued` via
`cellToConstraintMask`, and `_checkGroupForHiddens`) that the propagation stages drain later.

What is **eager** is the *bookkeeping*:

- `TrackHiddenSingles` walks `CellToGroupsLookup[cellIndex]` — 3+ groups — and decrements a
  per-group-value counter for every cleared value, on every write. `weak-link-representation.md`
  calls this "the sleeper" cost.
- `EnqueueCellForcing` (`SolverModification.cs:14`) does its filter lookups on every write.

**So the real idea is: batch the bookkeeping, not the deduction.** Accumulate dirty cells during a
propagation burst and reconcile group counts once. A cell cleared several times within one step
currently pays the group walk each time; coalescing pays it once.

**Why it might work.** The grouped weak-link change already banked a win from precisely this shape —
per-cell bookkeeping once rather than once per cleared candidate. This moves the same lever one
level out, from per-clear to per-step.

**What would kill it.**

- **Low coalescing rate.** If cells are rarely cleared twice within a step there is nothing to save.
  Measure this first — it is cheap, and it is the entire premise.
- Reconciliation must touch every dirty cell anyway, so you may only be trading N cheap eager walks
  for one pass over the same N.
- **A correctness constraint that may leave no room:** nothing may read
  `_candidateCountsPerGroupValue` or `_checkGroupForHiddens` between deferral and reconciliation.
  Hidden singles read both. That pins the reconcile point to the top of the hidden-single stage,
  which is close to where the work already happens.

**Measurement note.** Deferral changes *when* a naked single or an invalidity is noticed, which can
change which deduction fires first. Run the parity census before trusting any timing.

---

## 4. Ramp up machinery only when a puzzle proves it needs it

**Status:** open · **Prereq:** node counter

There is already one instance of this: `WeakLinkDiscoveryMode.Deferred` skips weak-link probing and
retries with it when the cheap attempt doesn't pan out. Its design has a property worth reusing —
`SnapshotConflictState` / `RestoreConflictState` roll back the branch-ordering learning so a deferred
retry is *exactly* an undeferred search, making the only cost of deferral a bounded wasted prefix.

**Pitch.** Generalize that from one flag to a ladder. Start every puzzle with the cheapest propagator
set and escalate — pointing, cell forcing, weak-link discovery, richer branch ordering — only when
the search passes a threshold. Easy puzzles, which are most puzzles, never pay for machinery they
don't need.

**Why it might work.** Several tiers are already built and default-off precisely because they lose on
average while winning on specific puzzle classes: cell forcing (`SUDOKU_CF_TRIGGER`, default `Off`)
and the bilocal branch-ordering tier (`BILOCAL_SEARCH_WEIGHT_DEFAULT = 0`). A ramp changes the
question from "does this pay on average" to "does this pay on the puzzles that reach tier N" — which
is the question those tiers can actually win.

**Decisions to make explicitly.**

- **Restart vs escalate in place.** Restart inherits `Deferred`'s clean semantics — the retry is
  equivalent to a full-machinery search, so the cost is a bounded prefix. Escalating in place is
  cheaper but leaves a tree already shaped by the weak configuration, and gives up that equivalence.
  Prior art: SAT restart strategies (Luby, geometric).
- **Threshold in nodes, not milliseconds** — for reproducibility across machines, and for
  comparability with every other benchmark in this repo.

**What would kill it.** Thresholds that can't be tuned without overfitting; or a prefix cost that
isn't actually bounded in the escalate-in-place variant.

**Tuning hazard specific to this repo.** `corpus-iss.json` samples only easy puzzles (commit
`6cac28d`). That is exactly the population that should never leave tier 0, so it *cannot* validate a
ramp — it can only confirm that the cheap tier is cheap. Tuning needs the hard cases from the
28-case corpus (`tc-blank-nonconsecutive`, `kropki-search-cap50k`), and ideally puzzles that are hard
for different reasons.

---

## 5. The missing binary implication: `a → b`

**Status:** open · **Prereq:** node counter (partly — see below)

Over literals there are three binary forms. Only one is genuinely missing:

| form | clause | name | status in the brute-force search |
| --- | --- | --- | --- |
| `a → ¬b` | `¬a ∨ ¬b` | weak link | First class — `weakLinks` plus two compiled CSR views. |
| `¬a → b` | `a ∨ b` | strong link | **Already propagated. Nothing to do — see below.** |
| `a → b` | `¬a ∨ b` | implication | **Not represented anywhere.** This is the idea. |

**Why `¬a → b` needs nothing, so nobody re-proposes it.** A strong link fires on an *elimination*
and yields an *assignment*. For a bivalue cell that is precisely a naked single; for a bilocal it is
precisely a hidden single. Those are stages 1 and 2 of `StepBruteForceLogic` — the binary
strong-link propagator is already built, and `FindHiddenSingle` even covers the constraint-derived
case via `group.FromConstraint.MustContainValue`. Storing them would also be actively wrong: they
are **dynamic**, a property of the current board rather than of the puzzle. A cell is not bivalue at
root; it becomes bivalue partway down. So there is nothing to precompute and anything stored would
need per-write maintenance. (`AICSolver.FindStrongLinks` builds them per run for the *logical*
solver, where they feed chains. The uncovered cases there — ALS — are not binary.)

**Pitch.** Give the search the one form it lacks: `a → b`, an assignment that forces another
assignment.

**Why `a → b` *is* storable, unlike the above.** It is static and monotone. If setting `a` forces
`b` at root, it still does at any descendant — the board only loses candidates, so propagation from
`a` yields at least as much. Same property that makes weak links compile-once, and it means the
whole thing can be built at root and shared by reference across clones.

Bonus deduction from the monotone argument: a stored `a → b` whose `b` has since been eliminated
yields `¬a` immediately. Note the contrast with cell forcing, where "fired, target already gone" was
57.7% of pops and pure waste — here the same situation is informative.

**Why this is a different kind of idea from 1–4.** Its payoff is at **root time**, not on the
write path. Transitive closure, strongly-connected components, equivalent-literal detection: all
one-time costs with permanent payoff. That structurally dodges the maintenance heuristic above,
which is what threatens ideas 2 and 3.

**The strongest concrete lever: `DiscoverWeakLinks` already computes implications and throws them
away.** The probe sets a candidate, runs full propagation, then walks every other cell — and the
loop is explicitly labeled "find new eliminations and form the proper weak links". If the
propagation *set* a cell, that is an `a → b` implication, already paid for and discarded. Worse, it
gets recorded as the 8 weak links that say the same thing less directly. Harvesting the positive
consequences is close to free marginal cost on a pass that already runs.

That matters because discovery is this repo's stated biggest lever *and* expensive enough to warrant
a whole `Deferred` mode. If `a → b` and `b → ¬c`, then `a → ¬c` — a weak link obtained algebraically
instead of by probing. The question worth answering is what fraction of discovery's links the
closure reproduces, at what fraction of the cost.

**Storage shape.** Directed, so it cannot ride `weakLinks`' symmetry: a graph over 1,458 literals
(729 candidates × 2 polarities), compiled at root and invalidated on the same events as the existing
CSR views.

**What would kill it — and this repo has already measured the failure mode.** More weak links is
*not* automatically better: `branch-ordering.md` records `killer-cage` paying **64% more time for
16% more nodes** when discovery added 4,132 links, because application cost scales with link count.
Closure-derived links inherit that risk directly, and a closure can produce a lot of them. Any
version of this needs a cap and a measured links-vs-time curve, not just a correctness argument.

**Where the payoff lives.** Vanilla sudoku has essentially no `a → b` at root — setting one
candidate removes at most one candidate from each peer, so no peer collapses to a single.
Implications come from *constraints*: clones (`A=v ↔ B=v`, a 2-cycle in the graph), two-cell killer
cages, kropki/XV pairs, thermometer ends, nonconsecutive. So the payoff population is variant
puzzles — `variant-cloneways` is the obvious first test case, and the same corpus cases that cell
forcing and pointing target. Treat that overlap as a yellow flag: those two failed to pay. The
mechanism here is different enough (root-time, not per-pop) that the failure mode isn't shared, but
the population being hard to help is a real signal.

**Scope discipline.** Keep this root-only until it has earned more. Propagating implications
*during* the search is per-write maintenance and walks straight into the wall that ideas 2 and 3 are
trying to avoid. Equivalent-literal *merging* is also likely too invasive to be worth it — the board
is a flat `uint[NUM_CELLS]` and groups hold cell indices — so the realizable form of the SCC win is
probably "transfer weak links across an equivalence class", not "merge the cells".

---

## 6. Audit and remove authoring-convenience constructs from the hot path

**Status:** open · **Prereq:** none (this one reads on wall clock, not nodes)

The solver was hand-written over many years without AI assistance, and some constructs exist because
they made *writing* the code fast, not because they make it *run* fast. Audit those and rewrite the
callers.

**First, a correction that saves the audit from starting in the wrong place.** It is not LINQ.
`SolverBruteForceLogic.cs`, `SolverModification.cs` and `SolverBruteForce.cs` currently contain
**zero** LINQ calls; `SolverLogic.cs` has 8, and those want checking against the arm they sit in
(see scope, below). Someone grepping for `.Select(` will conclude there is nothing here.

**The test to apply: does the helper accept a lossier form of data than the caller already holds?**
That is the real foot-gun, and it is not fixable by optimizing the helper — a convenient function
that takes the degraded form will keep attracting callers who degrade their data to reach it. The
question to ask of every such helper is not "can this be faster" but **"can the current callers do
it better, and should the helper exist at all?"** The lead item below is the worked example.

The recurring surface is `List<T>` and reference-typed collections standing in for flat arrays or
masks. That is the convenience — Lists are pleasant to build incrementally — and the cost lands as
pointer chasing, bounds checks, interface indirection, and per-element bookkeeping where per-cell
would do. This repo has already hit it and fixed it **three times**, which is what makes this a
pattern rather than a hunch:

- `IsWeakLink` — `List<T>.BinarySearch` dispatched through `Comparer<int>.Default`; measurably bad
  natively and *catastrophic* under Mono WASM AOT, which emits a `call_indirect` per comparison
  step. Replaced with a hand-rolled search.
- Grouped weak links — `List<int>[]` walked per target candidate, replaced by a compiled CSR view.
  **−5.6%** on the ISS corpus.
- `CombinationsBuffered` — the original `Combinations` returns `IEnumerable<List<T>>`, allocating a
  `List` per combination.

**Lead item — "build a `List<int>` of eliminations, hand it to a helper" (repo owner's nomination).**
Expected to be logical-arm only; it is not.

**The question is not how to make the helper faster. It is whether the helper should be reachable
from these callers at all** — a helper that accepts a lossy form of data the caller already holds in
a better form will keep attracting callers no matter how well it is optimized.

- **The warning already exists and is already being ignored.** `ClearCandidates(IEnumerable<int>)`
  (`SolverModification.cs:214`) carries an XML remark reading, verbatim, *"**Never use this while
  brute forcing, or anywhere else speed matters.**"* It names both costs — per-*candidate*
  bookkeeping instead of per-cell, plus boxing the `List<T>` struct enumerator — and prescribes the
  fix: *"group its eliminations by cell and use `ClearMaskFromCell` instead."* `ClearMaskFromCell`
  exists (`SolverModification.cs:159`). And `SolverBruteForceLogic.cs` — the brute-forcing file —
  calls `ClearCandidates` at `:800`, `:935`, `:1003` and `:1122` regardless. **Do not "fix" this by
  adding a faster overload; that entrenches a call the codebase already forbids here.**
- **Three of the four callers hold a mask already, and throw it away.** `FastFindPairs` calls
  `CalcElims(mask0, [cellIndex0, cellIndex1])` (`:797`) and `FastFindTriples` calls
  `CalcElims(p_i.TargetTripleMask, ...)` (`:932`). Each takes a **mask**, expands it into candidate
  indices, and then `ClearCandidates` clears those one bit at a time. A mask → list → bit round
  trip whose natural form — `ClearMaskFromCell(cell, mask)` per affected cell — is the input the
  caller started with. (`[cellIndex0, cellIndex1]` also allocates an array per iteration of the pair
  loop.)
- **The fourth caller is a stale fallback.** `FastFindCellForcing`'s main path already migrated —
  `:1190` and `:1208` use `ClearMaskFromCell`. `:1122` is the pre-table path taken when `cfOffsets`
  is null at root setup, still on the old merge-join-and-list idiom. Its targets are arbitrary
  candidates, but `weakLinks` lists are sorted by candidate index so targets sharing a cell are
  already adjacent — grouping the run is exactly what `CompileCellForcingTable`'s sort comment says
  the ordering was chosen to enable.
- **Third instance of a stalled migration.** `CalcElims` already has both an allocating form
  (`:797`, `:932`) and a fill-a-caller-buffer form (`:1000`). Compare `CombinationsBuffered` (10
  unbuffered call sites vs 6 buffered) and `_cellForcingElims ??= []` applied at one site. The
  recurring shape is *someone fixed this correctly once and the migration stopped* — a useful
  signature to grep for.
- **Enforcement beats documentation.** A remark saying "never use this while brute forcing" has
  already failed to prevent four such uses. Consider a `Debug.Assert(!isBruteForcing)` inside
  `ClearCandidates`, which turns the convention into a failing test across the existing 121-test
  suite, or split the logical-only entry point under a name that makes the misuse obvious.
- **Couples to idea 2.** If pointing moves in-search, `FastFindPointing`'s two per-invocation
  allocations (`:946-947`) become two per propagation step.

**On the instances that genuinely are logical-arm only: not exempt, just measured differently.**
`logical-solver-allocation.md` exists because allocation in that arm is a *browser memory* problem.
Milliseconds are the metric for the brute-force arm; **megabytes are the metric for the logical
arm.** Do not use the `AGENTS.md` clarity rule to wave off allocation findings — that rule protects
step explainability, not garbage. Rewriting a `List` into a pooled buffer changes no step
description.

> **Scope note, since this changed:** [`logical-solver-audit.md`](logical-solver-audit.md) task T2
> makes the logical arm a performance target in its own right, which retires the "allowed to be
> slow" premise this entry was originally scoped around. The criterion above — *does the helper
> accept a lossier form than the caller already holds?* — applies to both arms.

**Seed list (verified present, not yet verified as wins).**

- **`CellToGroupsLookup` is `List<SudokuGroup>[]`** (`Solver.cs:256`) and `TrackHiddenSingles` walks
  it on **every board write**, then reads `group.Index`, `group.Cells.Count` and
  `group.FromConstraint` off each entry. That is a pointer chase per group per write on the hottest
  path in the solver. A CSR of group indices plus parallel flat arrays of the three fields actually
  read would be dense and prefetchable. Strongest candidate on the list.
- **The `CombinationsBuffered` migration is incomplete** — 10 unbuffered `.Combinations(` call sites
  remain against 6 buffered. The replacement is already written; the work is deciding which of the
  10 are brute-force reachable (some are Sandwich/logical and out of scope).
- **`weakLinks` as `List<int>[729]`** — `CloneWeakLinks` clones 729 `List`s, a cost
  `weak-link-representation.md` names explicitly while noting the grouped table is *additional*
  memory rather than a replacement.
- **27 `ToSortedList()` / `ToEnumerable()` call sites** — census which are on brute-force paths
  before touching any.

**Method: census first, rewrite second.** The bitmatrix exploration is the cautionary tale — a brief
(mine) guessed that `IsWeakLink` traffic was spread across bilocal search, pairs and triples. The
census found **92.7M calls at one site** and 110K across every other site in the entire corpus, and
the guessed-hot site had *zero* calls because its tier is disabled by default. This codebase's cost
is extremely concentrated and human intuition about where has already been wrong once. Instrument,
then rewrite what the counter points at.

**Scope guard — do not sweep the whole codebase.** `AGENTS.md` states that the non-brute-force arm
should prioritize clarity of the logical step and comprehensiveness over speed. Rewriting the
logical solver for performance is against project policy, not just out of scope — `HANDOFF.md`
classes the Sandwich `Permutations` item as "a product call about step explainability, not perf."
The target is `isBruteForcing == true` paths only. An agent turned loose on "remove convenience
helpers" will otherwise gut the explainable arm, which is the product.

**Why this idea is riskier than the others, and the rule that follows.** Every other entry here
fails *cheaply* — the hypothesis doesn't pan out and you delete a prototype. This one fails
**expensively**: large mechanical diffs that make hand-written code harder to read for no measured
gain. So: one item per commit, each with its own paired A/B at 15 iterations, and revert anything
that doesn't move the benchmark. "It's obviously faster" is exactly the reasoning that produced the
row-AND result.

---

## Observation worth keeping: the default-off tiers

Cell forcing and the bilocal tier are both fully built, measured, and disabled by default. Reading
the code overstates what actually runs, which is a standing source of drift between the codebase and
anyone's mental model of it.

If a third tier joins them, this should become a real inventory — knob, default, and the one-line
reason it is parked. A known footgun for that list already: `SUDOKU_CF_ORDER=popcount` silently
breaks the CSR/row-AND order parity documented in `weak-link-bitmatrix-exploration.md`.
