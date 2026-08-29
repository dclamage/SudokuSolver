# Logical solver audit

The brute-force engine has had years of measured attention. The logical solver has not, and it has
been explicitly *allowed* not to. This document audits its current shape and turns the known
problems into tasks.

Scope: the `isBruteForcing == false` arm — the human-technique solver that produces
`LogicalStepDesc`s for the UI. Related: [`ideas.md`](ideas.md) for brute-force ideas,
[`HANDOFF.md`](HANDOFF.md) § 2 for scheduled work, [`logical-solver-allocation.md`](logical-solver-allocation.md)
for the browser memory problem this arm already causes.

---

## Read this first: a standing instruction has to change

`AGENTS.md` currently tells every contributor, human or agent:

> If `isBruteForcing` is `false`: prioritize clarity of the logical step to the user and
> comprehensiveness in applying human-like solving techniques. Performance is still important but
> secondary to logical rigor and descriptive output.

**Task T2 reverses that**, and it is not optional — it falls out of T1. Enumerating *every*
applicable technique at a position removes the first-match early-out that is currently the only
thing keeping this arm's cost down. Until `AGENTS.md` is updated, any agent doing T2 will be
working against a written instruction it can cite. Update it as part of T2, not after.

The rule that should survive: performance work here may not degrade a step's explainability. That is
a genuine constraint and most of T2 does not touch it — pooling a buffer changes no description.

---

## The spine: discovery and mutation are the same call

Almost everything below is blocked on one structural fact.

`Constraint.StepLogic(solver, descs, isBruteForcing)` and every `Solver.Find*` method **apply their
eliminations as a side effect** and return a `LogicResult`. The dispatcher (`SolverLogic.cs:100+`)
is a first-match cascade: constraints first, then tuples/pointing, cell forcing, fishes, finned
fishes, wings, AIC, contradictions — each returning the moment it changes anything.

That design makes T1 impossible as written, and T3 and T4 awkward. The refactor underneath all of
them is:

```
IEnumerable<Deduction> Find(...)     // pure, no board mutation
bool Apply(Deduction)                // the only thing that writes
```

Do this once, deliberately, before T1/T3/T4. Doing it three times incidentally is the failure mode.

---

## T1 — Enumerate every applicable logical step, not just the first

**Current.** First-match cascade; the first technique to fire wins and the loop restarts.

**Target.** At a given board position, produce the full set of available deductions.

**What this forces, and it is more than it looks.**

- **No early-out means you pay for every technique, every step.** Today the cascade ordering *is*
  the cost control: cheap techniques run first and short-circuit before AIC is ever reached. Remove
  that and AIC runs at every position. This is why T2 stops being optional.
- **Deduplication and subsumption.** A naked pair and a hidden pair frequently produce the same
  eliminations; an AIC will re-derive what a fish already found. A raw list will be full of
  restatements. Decide whether to dedup by resulting elimination set, by technique, or to present
  the redundancy deliberately (it is arguably useful — "three different ways to see this").
- **Ranking is a product decision, not a technical one.** "Which is the best next step" could mean
  cheapest technique, most eliminations, or lowest human difficulty rating. Pick explicitly; the UI
  will need it and the solver currently has no notion of technique difficulty at all.

**Depends on:** the find/apply split.

---

## T2 — The logical arm now has to be fast

**Current.** Unoptimized by policy. `logical-solver-allocation.md` already documents it as a browser
memory problem.

**Target.** Both axes, algorithmic and allocation. T1 makes the cost multiply, so this is load-bearing
rather than tidying.

**Seeds, verified present.**

- The dispatcher allocates `Enumerable.Empty<int>()` twice per constraint-reported invalidity, and
  rewrites the last description via `WithPrefix($"[{constraint.SpecificName}] ")` — string work
  inside control flow.
- `FindDirectCellForcing` allocates a `List<int>` per call and runs a k-way merge-join
  (`InitIntersectWeakLinks` / `IntersectWeakLinks`) over sorted lists.
- Sandwich's `Permutations` enumerates k! to justify eliminations. `HANDOFF.md` classes this as "a
  product call about step explainability, not perf" — **under the new policy that call is reopened.**
- [`ideas.md`](ideas.md) idea 6 is a foot-gun audit currently scoped to the brute-force arm on the
  grounds that the logical arm is allowed to be slow. That scope note needs updating alongside
  `AGENTS.md`; its lead item (`ClearCandidates` and the elimination-list pattern) applies here in
  full, and its criterion — *does the helper accept a lossier form than the caller already holds?* —
  is the right lens for this arm too.

**Measure on megabytes as well as milliseconds.** The browser runs this per edit.

---

## T3 — Rich structured deductions; build strings last, or elsewhere

**Current.** `LogicalStepDesc` is *partially* structured already: it carries `highlightCells`,
`sourceCandidates`, `elimCandidates`, `strongLinks`, `weakLinks`, `subSteps` and `isSingle` — **and
an eagerly-built `desc` string**. The structure is there; the string is authored inside each
technique, and it is the string that the UI actually consumes.

Two symptoms of the split being incomplete:

- The dispatcher patches the last description after the fact with a `[ConstraintName] ` prefix,
  because ownership was not part of the record.
- There is a fallback description reading *"reported the board was invalid without specifying why.
  This is a bug: please report it!"* — a runtime admission that constraints routinely fail to
  describe themselves, caught by a string check rather than by the type system.

**Target.** A typed deduction per technique — the conclusion, the premises, and the rule applied —
with rendering as a separate layer.

**What this unlocks that the current design cannot.**

- **Audience-specific text.** Beginner / expert / terse are renderer choices, impossible when the
  string is baked at the deduction site.
- **Localization.** Strings authored inside the solver cannot be translated.
- **A walkthrough UI.** "Show me why" needs premises and rule, not prose. `subSteps` already hints
  at the tree such a UI would walk.
- **Not paying for prose you discard.** Under T1 you will build hundreds of descriptions to display
  one.

---

## T4 — A queryable technique catalog, and toggles that match the techniques

**Current, and it is worse than "not fine-grained enough".**

- Six booleans — `DisableTuples`, `DisablePointing`, `DisableFishes`, `DisableWings`, `DisableAIC`,
  `DisableContradictions` — packed into `uint DisabledLogicFlags` at **hardcoded bit positions**
  (`SolverAccess.cs:5-35`).
- **`DisablePointing` gates two different techniques**: pointing *and* `FindDirectCellForcing`.
- **Constraint logic has no toggle at all.** The `foreach (var constraint in constraints)` loop runs
  unconditionally, before any flag is consulted. Constraint steps and classic steps are not merely
  mixed — one whole category is unswitchable.
- Nothing is queryable. `WebsocketListener.cs:370` and `SolverCommandProcessor.cs:248` both compare
  raw flag bits, so every UI hardcodes the bit meanings and must stay in sync by hand.

**Target.** A registry: stable ID, display name, category (classic / constraint / chaining), default
state, cost tier, owning constraint where applicable. UIs enumerate it. Toggles become a set over
IDs rather than bit positions.

**Compatibility trap.** `SolverFactory.cs:405` writes `DisabledLogicFlags` into `comparableData`, so
the flag representation participates in solver identity/caching. Changing it is not purely internal
— check what consumes that comparison before altering the encoding.

**Bonus:** a catalog with a cost tier is exactly the metadata T1's ranking needs, and the ordering
the cascade currently hardcodes becomes data.

---

## T5 — Gaps in coverage

**Present today** (12 entry points): naked/hidden singles, direct cell forcing, naked tuples and
pointing, unorthodox tuples, fishes, finned fishes, wings, N-wings, AIC (with bivalue/bilocal/ALS
strong links), simple contradictions.

**Genuinely absent.** The uniqueness family — Unique Rectangle, Hidden/Avoidable Rectangle, BUG —
plus Sue de Coq, Empty Rectangle, and franken/mutant fish.

**A nuance that changes the task.** AIC subsumes many named human techniques — W-Wing, Skyscraper,
2-String Kite, Remote Pairs are all chains. Those deductions are *found*; they are reported as "AIC".
So part of T5 is not a missing technique but a **missing name**, fixable under T3 by pattern-matching
the chain shape and reporting the human name. Separate the two before estimating: "we cannot find
this" and "we find it and call it something unhelpful" are different jobs.

**Policy flag on uniqueness.** UR/BUG assume the puzzle has exactly one solution. That is invalid
for puzzle *setting*, and for any board not yet known unique. These need an explicit opt-in, and
their interaction with variant constraints needs thought before they are trusted.

**The other half — "constraints that don't find what humans consider obvious"** — is per-constraint
work, and there are two worked templates for it already:
[`whisper-arc-consistency.md`](whisper-arc-consistency.md) and
[`renban-required-values.md`](renban-required-values.md). This half can start immediately; it does
not depend on the spine.

---

## T6 — Let constraints reason about each other

**Current.** Each `Constraint.StepLogic` reasons alone. Human variant logic routinely combines two
or three constraints at once, and nothing in the API supports that.

**Two partial mechanisms already exist, and they suggest the shape.**

- **Weak links are already a shared substrate.** Constraints publish links via `InitLinks`, and AIC
  chains across links from *different* constraints — which is genuine cross-constraint reasoning,
  just limited to the one relation weak links can express.
- **`SolverOptimizer` + `InnieCageConstraint` already synthesize a new constraint from several.**
  Implicit sum constraints derived from killer cage / house overlaps is exactly "read multiple
  constraints, emit a derived one". That is an existence proof, and the model to generalize.

**Target.** A shared property store that constraints both publish to and read from — cell-set sums,
orderings, permutation/required-value sets, equalities — plus a synthesis pass that derives new
constraints from combinations at init. Generalizing the innie/outie optimizer is the concrete first
step, not a greenfield design.

**This is the largest item here and probably wants its own document** once someone starts it.

---

## T7 — Symbolic values ("coloring")

**Naming, first, because it will otherwise cause confusion.** Standard Sudoku "coloring" means
single-digit strong-link parity, which AIC already subsumes. **This is a different and more powerful
thing** — call it *symbolic values* or *clone coloring* in code and docs, never bare "coloring".

**The idea.** Give an unknown value a symbol. Whatever value r1c1 holds, call it green. Where does
green go in box 2? If there is only one spot, that cell is also green. Where does green go in box 4?
Now intersect the possible values of everything green. r1c2 differs from green, so call it purple;
where does purple go in column 1? And so on.

**Why it is tractable: it is a second board with the same rules.** A symbol obeys exactly the house
constraints a digit does — one green per row, column and box. So **singles, tuples and pointing
apply unchanged** to the symbolic board. This is not a new inference engine, it is the existing one
run over a different alphabet.

**Structure.**

- Cells known to share a value form an equivalence class — a union-find over cells.
- Each class carries a value mask: the intersection of its members' candidate masks. Merging two
  classes intersects their masks.
- The link is bidirectional and both directions are deductions: a class's mask narrowing to one
  value places a real digit; a member cell's candidates narrowing tightens the class mask, which
  tightens every other member.

**Two cross-links worth knowing before starting.**

- This is the same structure as [`ideas.md`](ideas.md) idea 5's equivalent-literal / SCC merge.
  `A=v → B=v` for every `v` *is* "A and B are the same color". The implication graph and the color
  classes are two views of one mechanism — whichever gets built first should expose the other.
- Real clone constraints already exist (`variant-cloneways` is in the corpus), and are the natural
  first test: they hand you the equivalence classes for free, with no inference required.

**Where the value is.** Variant sudoku, heavily. This is a technique humans use constantly and the
solver cannot express at all.

---

## Suggested order

1. **The find/apply split.** Everything else is cleaner after it and messier without it.
2. **T3 and T4 together**, riding that refactor — the typed deduction and the catalog are the same
   modeling exercise from two directions, and doing them apart means touching every technique twice.
3. **T2 alongside**, measured per change, with `AGENTS.md` updated first so the policy is not
   ambiguous.
4. **T5's "obvious things constraints miss"** can start now, in parallel, independently.
5. **T6 and T7** last of the big items — both add techniques, and both would otherwise bake more
   eagerly-built strings into the codebase that T3 then has to unpick.
