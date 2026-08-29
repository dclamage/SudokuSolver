# Handoff: solver performance & the browser port

Last updated 2026-08-19. Branch `wasm-prototype`, working tree clean, pushed. Read this first, then
the linked docs as needed.

---

## 1. Where things stand

The goal behind all of this is a **browser-hosted Sudoku setting site** backed by this C# solver,
exporting to SudokuPad — a SudokuMaker rival with a much stronger solver. See
[`wasm-prototype-findings.md`](wasm-prototype-findings.md).

**The strategic question is settled: the WASM port is viable and a TypeScript rewrite is not
indicated.** The evidence, in one table — exhaustive count, same machine, same Node/V8:

| puzzle | ISS (JS) | ours native C# | ours WASM | WASM tax | ISS vs ours |
| --- | ---: | ---: | ---: | ---: | ---: |
| escargot | 0.5 ms | 3.49 | 3.48 | **1.00×** | 7.0× |
| platinum-blonde | 0.6 ms | 4.87 | 4.64 | **0.95×** | 8.1× |

The WASM tax on core search is **~1.0×**; the corpus-wide 3.84× is confined to the *constraint layer*.
And on these two puzzles ISS beats us 5–8× **in native C# too**, so whatever gap exists is algorithmic
rather than a hosting or language problem. That is the part not to re-litigate — full argument in
[`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md).

**Don't read the 5–8× as a general verdict, though.** Those are two hard *vanilla* classics. Across
402 real CTC puzzles imported from the ISS index, **we are faster than ISS on 84% of them and ~2.6×
faster at the median** (p50 0.38×, p90 1.39×). The deficit is confined to hard vanilla and to a thin
pathological tail (12 puzzles >5× slower, worst >3,300×). Since the product runs variant puzzles, the
relevant distribution is the favourable one. Data and caveats:
[`iss-corpus-import.md`](iss-corpus-import.md) §2.

### Infrastructure you now have

- **34-case corpus**, 5 ops: `count`, `solve`, `logical`, `truecandidates`, `estimate`.
  `benchmarks/README.md` documents each. `truecandidates` is the operation a setting UI actually
  runs on every edit; `logical` is the half the product leans on hardest.
- **398-case ISS corpus** in `benchmarks/corpus-iss.json`, generated from sigh's CTC index with
  `--import-iss` and validated against the solution counts ISS recorded — **402 agree, 0 disagree**.
  Split `iss-tune` (280) / `iss-holdout` (118) by a hash of the puzzle id, so re-importing never
  reassigns a puzzle. **Never tune against the holdout.**
- **`SolverFactory.CreateFromIss`** — parses the ISS text format, now covering 30% of the index.
- **ISS's own node counts, for free.** `data/mappings.json` carries `guesses` and `solve_ms` for all
  1,671 puzzles, so you can compare tree sizes against ISS without running it. Use this before
  diagnosing any slow puzzle: it tells you immediately whether you are losing on nodes or on cost
  per node.
- **ISS itself is checked out** at `/Users/d.clamage/git/Interactive-Sudoku-Solver`, and reading its
  handler for whichever constraint you are chasing is the highest-leverage first move on any
  propagation gap. `js/solver/handlers.js` holds them. Doing exactly this for sandwich (its
  `Lunchbox` handler) answered the question immediately and produced a 7% corpus-wide win: it
  memoizes a combination table per grid size where we enumerated per node, represents a combination
  as a bitmask rather than a `List<int>`, and replaces exact placement with a cardinality test. Note
  ISS also has an `Or`/`And`/`Var` DSL and we have `NFAConstraint`, either of which may be how it
  gets stronger line deductions than we do.
- **WASM prototype** in `SudokuSolverWasm/` (not in the .sln). Same JSON protocol as the websocket
  server. `run-node.mjs` for headless 1T, `drive-chrome.mjs` for MT (MT WASM refuses to run outside
  a browser).

### What landed in this session

**The Renban propagation gap is closed.** `h-ymyScJa2s` went **8,293 ms → 26 ms (321×)** and from
14.0 M search nodes to essentially none — we now beat ISS on it by 4×. `blPgSzctUMg` 26 → 4 ms.
Full 398-puzzle ISS corpus: **p50 1.00, geomean 0.967, 0 result mismatches**; 32-case corpus −2.8%.
Write-up: [`renban-required-values.md`](renban-required-values.md).

The deduction is ISS's `BinaryPairwise` **required values**, which we had no form of: the
*intersection* of a renban's still-valid range masks are values that must appear on the line, so one
remaining cell for one is a hidden single and two or more lets you eliminate it from every cell that
sees them all. One extra `&=` in a loop `RenbanConstraint.StepLogic` was already running, plus
`ApplyRequiredValues` / `EliminateSeenByAll`.

Four things it turned up that matter beyond renban:

1. **ISS records its own node counts.** `data/mappings.json` has `guesses` and `solve_ms` per
   puzzle, for all 1,671, without running ISS. That turns the group-A diagnosis into an exchange
   rate: **ISS spends ~29 µs/node against our ~1 µs and explores 2,700× fewer nodes.**
2. **Our brute-force loop propagates singles and `constraint.StepLogic` — and nothing else.** So a
   constraint that says "weak links enforce me" (whispers, dots) contributes *nothing* at
   candidate level during brute force, because weak links only fire on `SetValue`. That, not
   anything about renban, is why the corpus statistic put `Thermo` at 0.20× (it has a real
   `StepLogic`) and `Renban`/`Whisper` at 4.50×/2.89×.
3. **Do not enable `FastAdvancedStrategies` during brute force.** It takes `h-ymyScJa2s` from 8,426
   to 226 ms and, with discovery on, solves it with **zero** search nodes — and costs **p50 2.24×,
   geomean 2.19× on `iss-tune`**. Pointing-only is *worse* than pointing-plus-everything (geomean
   2.59, one puzzle 345×): the strategies are not additive, because each changes candidate counts and
   so the `score/count` branch ranking. **Deferral does not rescue it either** — simulated at every
   threshold from 5 to 800 ms it is a 3.1× loss, because of the 14 `iss-tune` puzzles over 100 ms, 8
   improve and 6 get worse (worst 20.9×).
4. **`corpus-iss.json` systematically excludes the puzzles escalation exists for.** Membership is
   wall-clock gated, so `h-ymyScJa2s` (8.3 s) and `1HuNjcLWlPE` are not in it. Any "escalate on
   expensive searches" measurement against that corpus is honest about the corpus and silent about
   the tail. Keep a pathological side-corpus when judging one.

Two puzzles regressed and both are the known second-order effects, not bugs: `iss-NjpUueqFEHY`
241 → 985 ms on **4.5× more nodes** (branch ordering — stronger propagation is not monotone in tree
size), and `iss--Uj9xZPyzM4` 1,314 → 1,898 ms on **17% fewer** nodes (more propagation rounds per
node, not the 81-cell scan — capping holders changes it by under 2%). Together they are the entire
reason the corpus *total* rose 7.3% while every distribution statistic improved.

Also added: `renban-vivian` to `corpus.json` (the corpus had no real renban `count` case at all, so
a 321× win was invisible in the run people make by habit) and `SudokuTests/RenbanTests.cs`.

**Then the Whisper half, from the scoping the renban write-up left behind.**
[`whisper-arc-consistency.md`](whisper-arc-consistency.md). `WhispersConstraint.StepLogic` returned
`None` unconditionally — "weak links enforce it" — so it propagated nothing at candidate level. It
now does pairwise arc consistency (ISS's `BinaryConstraint.enforceConsistency`), one forward and one
backward sweep per call, off a per-value `compatibleMask` table built in the constructor.

- **`OqyXKDOhfDA` 19,566 → 116 ms (169×)**, against ISS's 411 ms — we are now 3.6× faster than ISS.
- **`1HuNjcLWlPE` finishes for the first time**: 18.9 s and the right answer, having previously not
  completed in 28 minutes. Still 263× off ISS, but finite, which is what lets it be worked on.
- ISS corpus **geomean 0.771, 0 mismatches**, and the **holdout gains more than the tune split**
  (0.708 vs 0.799) — the cleanest generalisation signal in this file. `corpus.json` flat.
- Worst regression anywhere is 2.94×, and `iss-NjpUueqFEHY` — the puzzle the renban change cost
  4.08× — *improves* 978 → 569 ms here. Two independent propagation additions partially cancelling on
  one puzzle is a good reason not to chase an individual branch-ordering casualty.

Also added `whisper-zoomout` to `corpus.json` (there was no whisper case of *any* kind) and
`SudokuTests/WhispersTests.cs`. The interesting test is negative: the "cell fixed to an unsupported
value" branch is unreachable in normal play, because `SetValue` applies the pair's weak links first —
which is the half of the old "weak links enforce it" comment that was correct.

### What landed in the session before that

Eight commits. **ISS corpus 13,618 → 11,585 ms (−15%)**. Green at the end: 121 tests, 31-case corpus
0 FAIL, 398/398 ISS puzzles 0 FAIL.

Four changes shipped:

1. **Logical-solver allocation halved** — 2,593 → 1,289 MB over the five `logical` cases, time −8%.
   `Extensions.CombinationsBuffered` (borrowed buffer, adopted only at audited non-retaining sites)
   plus reusing one `HashSet` in `FindFinnedFishes` instead of allocating 429k of them per solve.
   [`logical-solver-allocation.md`](logical-solver-allocation.md).
2. **Weak links apply per cell, not per candidate** — `SetValue` walks a compiled
   `candidate → (cell, mask)` table instead of calling `ClearValue` per target. **ISS corpus −5.6%.**
   [`weak-link-representation.md`](weak-link-representation.md).
3. **Sandwich combinations come from a table** (`ValueCombinationTable`) instead of being enumerated
   and sum-filtered at every node, which is how ISS's `Lunchbox` does it.
4. **Sandwich stopped over-propagating while brute forcing** — **ISS corpus −7.0%**, and
   `blPgSzctUMg` went from 49× off ISS to ~1.9×, closing it as an outlier.
   [`pathological-outliers.md`](pathological-outliers.md).

Four things were investigated and deliberately **not** changed, which matters as much:

- **The bilocal branch-ordering tier stays off.** It scores 0.83× on the 28-case corpus and **7.7×
  worse** on `iss-tune` — the two corpora disagree on the *sign* of the effect.
- **Discovery raising node count is not a defect**, so don't hunt for a bug; it is a second-order
  consequence of stronger propagation.
- **Capping discovery passes is the wrong fix** — passes ≥2 yield 39% of all discovered links.
- **`SkyscraperConstraint`'s DFS is fine — do not "fix" it.** Skipping it measures 14–137× *faster*
  on blank capped grids and is **209× slower** on realistic ones.

**The rule the last two produced. If you read one thing here before tuning propagation, read this:**

> `StepLogic` is propagation only — `EnforceConstraint` plus the weak links own correctness — so the
> brute-force and logical arms may legitimately differ, and a *weaker* brute-force arm cannot change
> a solution count. But **never tune propagation strength on blank-grid capped counts.** They ask for
> a handful of solutions out of an astronomical set, so almost nothing needs pruning and any arm that
> propagates less looks better. Tune on real puzzles, uncapped. This nearly shipped a 209×
> regression.

Also added `xsum-search`, `skyscraper-search` and `sandwich-search` to `corpus.json`: X-Sum and
Skyscraper appear in **zero** of the 398 ISS puzzles and had no case anywhere, so changes to them
were unmeasurable. They are regression detectors, **not** tuning targets — see the rule above.

### And the session before that

**Deferred weak-link discovery** — the Priority 1 item — is built, tuned and **on by default**.
`WeakLinkDiscoveryMode` (`Always`/`Never`/`Deferred`) is now a real solver option with
`Solver.WeakLinkDiscoveryNodeThreshold` beside it, default N=2000, chosen by sweeping `iss-tune` and
confirmed on `iss-holdout` (0 result mismatches on either). Full write-up:
[`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) § "Deferred discovery, as built".

**It is a latency/throughput dial, not the free win the plan assumed.** Total corpus time and median
puzzle latency want opposite thresholds: raising N makes the typical puzzle much faster and a
handful of puzzles much slower. At the shipped N=2000 the median ISS puzzle is **~1.5× faster** while
total corpus time is **unchanged** (0.994× tune, 1.003× holdout), and the 28-case corpus is flat
(0.99–1.00×) once the untimeable randomised case is excluded. N≈5000, the value this file previously
suggested, is past the point where the total turns negative.

Three things it turned up that affect work elsewhere:

1. **Search-tree clones share `conflictScores` by reference**, so the abandoned attempt's branch
   ordering leaked into the retry — a 431% regression on `variant-orbit` until it was rolled back.
   Anything else that reruns a search on the same `Solver` needs `SnapshotConflictState`.
2. **`Never` cannot finish the ISS tune split at all** — killed after 8 minutes on one puzzle. Real
   CTC puzzles are effectively unsolvable without discovery, which is why the *bounded prefix* shape
   is right and a predictive on/off classifier is not: a misclassification there hangs.
3. **It fixed `Wb5YT1b-U9Q`**, one of the five pathological outliers in Priority 5 below: 308 ms →
   21 ms against ISS's 7.4 ms. Worth re-measuring the others before diagnosing them.

**True candidates was made reproducible, then had a 64× latency cliff removed.** Its branch choice
drew from a time-seeded thread-static `Random`; it now uses a stream scoped to the invocation, which
took `tc-blank-nonconsecutive` from a ~40% run-to-run swing to ~1% and made the path measurable for
the first time. Sweeping the now-fixed seed exposed a cliff: `tc-escargot-partial` ran 5.99–6.82 ms
on eight of ten seeds and **76.8 ms and 381.3 ms** on the other two.

The cause was not the tie-break heuristic. Instrumenting the coverage timeline showed the DFS
**trapped in its first root branch** — it spent 301,545 of 303,137 nodes there on the worst seed,
then finished within 1,600 nodes of escaping, and `nFirstShallow ≈ n90` in every run to within 1%.
A DFS cannot cover candidates that need a different root branch until the first subtree is
exhausted, and no branch-ordering rule fixes that. The fix is to stop guessing: after
`TrueCandidatesStallLimit` (default 100) consecutive solutions that cover nothing new, each
remaining candidate is settled directly by forcing it and running a capped `CountSolutions`. Worst
case **381 → 14.7 ms**, seed spread **64× → 3.7×**, healthy seeds untouched.

Two things to carry forward:

- **Treat every pre-2026-08-03 `tc-*` timing as optimistic.** Each benchmark iteration used to be a
  fresh draw from a wide distribution and the harness reports `min ms`, so old figures fall as
  iteration count rises. The 28-case corpus total rose 15,887 → 17,243 ms purely from removing that
  bias, before any of the endgame work.
- **A cap of 1 cannot detect a whole class of error here.** The first version of the endgame
  double-counted overlapping directed subtrees. Every corpus `tc-*` case uses `numSolutionsCap: 1`,
  where any positive count clamps to the right answer, so all four passed while the API default of 8
  was silently wrong. Only `TrueCandidatesMatchesBruteForceOracle` — which checks every candidate
  against a forced-cell `CountSolutions` at caps 1 *and* 8 — caught it. Test above a cap of 1.

### Earlier: the ISS corpus import

`benchmarks/corpus-iss.json`, 398 cases, 402 agree / 0 disagree, tune/holdout split in place.
Write-up: [`iss-corpus-import.md`](iss-corpus-import.md). Two things it turned up:

1. **The self-validation found four silent-wrong-answer defects** in `IssParser`, plus one trap in
   `CountSolutions` — cancellation returns a partial count that looks completed, which can bite any
   caller with a timeout. All fixed and covered by tests;
   [`iss-corpus-import.md`](iss-corpus-import.md) §3 lists them.
2. **It produced the corrected ISS speed picture above**, and with it four new pathological outliers
   that are now the most promising perf targets in the file (Priority 5).

Note that the raw ISS data is **not in the repo**: the importer needs `mappings.json` and a directory
of `.iss` files fetched from the index site. `benchmarks/README.md` has the fetch commands; a fresh
machine has to run them before `--import-iss` will work.

---

## 2. Recommended order of work

**Start here.** Several priorities below are now resolved and kept only for their findings —
**1b, 1c and 3 are closed or done**, and Priority 1's two items were both investigated and answered.
What is genuinely open, roughly by value:

> Unvalidated ideas — no shape, no schedule — live in [`ideas.md`](ideas.md), not here. An idea
> earns a row in this table once someone has scoped it. Note that `ideas.md` currently lists a
> **committed node counter** as a prerequisite blocking four of its six entries.
>
> [`logical-solver-audit.md`](logical-solver-audit.md) covers the *logical* arm — seven tasks, and a
> standing `AGENTS.md` instruction ("performance is secondary" for the non-brute-force arm) that its
> task T2 deliberately reverses.

| what | where | shape |
| --- | --- | --- |
| **Give `OrthogonalValueConstraint` brute-force propagation.** Kropki/difference/ratio/XV have the same defect renban and whisper had — `WantsBruteForcePropagation => false` plus an `isBruteForcing` short-circuit — and the deduction is *already written* in its `StepLogic`. Deleting both guards takes `1HuNjcLWlPE` from 18.9 s to **6.35 s**, but allocation from 1.0 to 7.8 GB. | [`whisper-arc-consistency.md`](whisper-arc-consistency.md) §5 | Contained; the work is making the existing pass allocation-free and queue-driven |
| **`1HuNjcLWlPE` is still 263× off ISS** (18.9 s vs 71.8 ms) — the last pathological outlier, and now finite enough to iterate on. After the dots, the lever is ISS's required-value exclusion for a *binary pair*. | [`whisper-arc-consistency.md`](whisper-arc-consistency.md) §5 | Open-ended, but with two named next steps |
| **Sandwich's `Permutations` in the *logical* arm.** Still enumerates k! to justify eliminations. | [`pathological-outliers.md`](pathological-outliers.md) | A product call about step explainability, not perf |
| **`XSumConstraint` allocates 53 MB for a 51 ms count.** | Priority 5 | Contained, but build a many-clue *uncapped* case first |
| **Pool the grouped weak-link arrays** (~116 KB per compiled search). | [`weak-link-representation.md`](weak-link-representation.md) | Contained; matters for per-edit true candidates in the browser |
| **Extend ISS coverage to NFA/Pair**, +237 puzzles. | Priority 4 | Plumbing; corpus size is what protects every heuristic here from over-fitting, and it misled us twice last session |
| **True-candidates leftovers** — the ~2.9× seed spread, and a node-relative stall trigger. | Priority 2 | Smaller |

### Priority 1 — The two remaining weak-link-discovery levers

Deferral is done (see above). Two follow-ups from
[`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) remain, and they are
*complementary* to it rather than superseded — both attack the **cost** side, which is exactly what
deferral cannot: a puzzle that exceeds the budget still pays full price for discovery.

Both were investigated on 2026-08-04 and **both conclusions changed**. Full write-up:
[`branch-ordering.md`](branch-ordering.md).

1. **The node-count increase is real but it is not a defect — don't hunt for a bug.** Confirmed
   `never`→`always`: `littlekiller-10` 5,104 → 15,090 nodes (2.96×), `killer-cage` 5,450 → 6,303
   (1.16×). Two plausible mechanisms were ruled out by inspection: it is *not* conflict-score
   pollution (discovery's scratch clone shares `conflictScores` by reference, but
   `IncrementConflictScore` is only reachable from the search loops, which the scratch solver never
   enters) and *not* bilocals (disabled at every search call site). The real mechanism is that
   `SetValue` propagates along weak links (`SolverModification.cs:123`), so more links change
   candidate counts, which changes the `score/count` ranking. It is a second-order consequence of
   stronger propagation — the same two-sided trade-off as deferral itself.
   **Also: take `escargot` off this list.** At the shipped `Deferred` default it never triggers
   discovery at all (16 nodes, 0.23 ms); only `Always` hurts it, by 32×.
2. **Aborting an unproductive pass: counted at last, and capping passes is the wrong fix.** On the
   258 `iss-tune` puzzles that trigger discovery, passes ≥2 are 56.5% of probes but still yield
   **39.4% of all links** — and `variant-equalsums` gets 4× more links from later passes than from
   pass 1. The provable waste is narrower: **8.3% of probes on `iss-tune`** (27.9% on the 28-case
   corpus, 66% on `killer-cage`) go to passes that yield zero links, and 93 of 251 multi-pass puzzles
   gain nothing from their extra passes. The output-preserving fix is a **targeted re-probe** — only
   candidates probed before the last board change in a pass are stale — not a pass cap. Watch that
   `AddWeakLink` can itself mutate the board, so use a version counter rather than counting elims.

If you revisit the deferral threshold itself, **tune against `--filter iss-tune` and confirm on
`--filter iss-holdout`**, and score the *ratio distribution*, not `total min ms` — the total alone
picks N=250, which makes the median puzzle slower. The 28-case corpus is too small to separate a
threshold honestly and is flat across the whole range.

### Priority 1b — The bilocal branch-ordering tier: **closed, answered "no"**

`GetLeastCandidateCell` has a bilocal tier that explicitly "mirrors ISS `CandidateFinders.House`" and
was unexplained dead code in the `solve`/`count` searches. It is now a weight
(`BilocalSearchWeightPercent`, env `SUDOKU_BILOCAL_WEIGHT`) that **defaults to 0 because enabling it
is measurably worse**, not because it was never tried.

At ISS's own weight of 50, over the 221 non-trivial `iss-tune` puzzles: **7.71× total nodes**, p50
1.000×, p90 2.56×, **worst 180×**. Weight 75 is worse still (13.7×).

**The cautionary tale is the reason to read this one.** On the 28-case corpus the same change scores
**0.832× total** — a 17% win, with `kropki-search-cap50k` at 0.27× — and that is what the first pass
of this investigation reported. The two corpora disagree about the *sign* of the effect by an order of
magnitude in each direction. This is the concrete demonstration of the warning already in this file
that the 28-case corpus is too small to separate a heuristic honestly: **take any branch-ordering or
threshold result to `iss-tune` before believing it.**

Don't re-run the static sweep. The write-up in [`branch-ordering.md`](branch-ordering.md) explains why
the dial is coarse (the weight really encodes "how many candidates must the conflict-score cell have
before a 2-way bilocal beats it", so there is no setting that keeps the wins and drops the losses),
and sketches the only shape that could still work: a *deferred* bilocal arm reusing the
`WeakLinkDiscoveryMode.Deferred` machinery, since a restart only fires on already-expensive searches,
which is where the 0.03× puzzles live.

### Priority 1c — Grouped weak-link application — **DONE**, −5.6% on the ISS corpus

`SetValue` used to apply weak links by walking `weakLinks[cand]` and calling `ClearValue` per target
candidate. It now walks a compiled `candidate → (cell, mask)` table and clears a whole cell at once.
**ISS corpus 13,618 → 12,862 ms (−5.6%)**; `tc-blank-nonconsecutive` −8.8%, `kropki` −11.7%,
`killer-innie` −9.3%, `arrow-search` −8.5% (the last three paired at 15 iterations).
Write-up: [`weak-link-representation.md`](weak-link-representation.md).

Three findings from it worth carrying:

1. **The win is mostly *not* the clustering it was aimed at.** 70–95% of weak-link iterations find
   the target already gone, so the real lever is dismissing a whole cell with one
   `board[cell] & mask` test; plus `TrackHiddenSingles` used to walk `CellToGroupsLookup[cell]` once
   per *cleared candidate* and now runs once per cell. Vanilla has **zero** clustering yet still
   improves 4–8%.
2. **Vanilla clusters not at all, structurally.** Same-cell exclusivity isn't stored as weak links —
   `SetValue` wipes the cell directly — so vanilla targets are all "same digit, peer cell", one per
   cell. Clustering comes only from constraints linking *different* digits across cells
   (nonconsecutive, kropki, killer, clones) and from discovered links.
3. **`EstimateSolutions` runs a nested `CountSolutions` per sample** (~400 attempts for 200 samples).
   Anything that does per-search setup work must be idempotent or it gets multiplied by the sample
   count — the first version doubled `est-escargot-6clue`'s time and allocation this way.

Left undone: the table is ~116 KB of *additional* memory per compiled search (+0.12 MB per call,
`tc-escargot` 0.03 → 0.15 MB), which pooling the two arrays would mostly remove — worth doing for the
browser, where true candidates runs on every edit.

### Priority 2 — What remains on true candidates

The 64× branch-order cliff here is **fixed** (see above). Two smaller things are left:

1. **`tc-blank-nonconsecutive` has an unexplained ~2.9× seed spread** that the endgame does not
   touch (seed 2: 24.1 → 19.1 s; seed 5: 28.7 → 29.0 s). Its 8.6–12.6M *invalid* nodes are genuine
   constraint search, not a coverage hunt, so this is a propagation question. It is also the single
   largest case in the corpus at ~10 s, so it dominates any `truecandidates` measurement.
2. **The stall trigger counts solutions**, which is a poor proxy where solutions are rare — only
   ~1,300 appear across 18M nodes on `tc-blank-nonconsecutive`, so it fires almost incidentally. It
   does no harm there, but a node-relative trigger would be better founded. Sweep
   `SUDOKU_TC_STALL_LIMIT` if you revisit it.

**Do not tune the RNG seed** — the shipped value is the natural counter origin, chosen before any of
this was measured, and it is not what made the cliff go away.

**Keep the randomisation itself.** True candidates is a coverage problem, and a deterministic DFS
yields consecutive solutions differing only in their last few assignments, so each covers almost no
new candidates.

### Priority 3 — Buffer-reusing `Combinations` — **DONE**, and it found something bigger

Logical-solve allocation is **down 50%** across the five `logical` cases (2,593 → 1,289 MB), with
time down 8% as a side effect. Full write-up:
[`logical-solver-allocation.md`](logical-solver-allocation.md).

`Extensions.CombinationsBuffered` exists and is adopted at the six audited-safe call sites; the
other 12 stay on fresh-list `Combinations` because a counter showed they yield **nothing** on the
logical corpus, so there was no benefit to weigh the retention risk against.

**But the scoped item was only 5% of it.** The plan's arithmetic was wrong in three ways — it named
`FindFishes` (which yields almost nothing), counted the outer fish loop rather than the 10×-larger
inner one, and multiplied by the `StepLogic` call count instead of the fish-search call count. The
real win, found by the same counter, was next door: **`FindFinnedFishes` allocated a fresh
`HashSet<int>` per inner combination** (429k per `killer-innie` solve, ~1.1 KB each because it grew
through the 3→7→17→37→89 resize sequence). Hoisting it to one per call and reusing via `Clear()` is
where 90% of the reduction came from — `killer-innie` 831 → 317 MB.

Two things worth carrying forward:

1. **`FindWings` never called `Combinations`.** `FindNWing` hand-rolls its combination walk over a
   reused array, so it was already doing the right thing. The bisect's 49% wings attribution on
   `renban-sky` therefore remains unexplained.
2. **In a hot loop, look for the container that *grows*, not the one that is merely numerous.** The
   `HashSet` was 15× the per-instance cost of the `List` it sat beside, which is the whole story of
   why the scoped item under-delivered and its neighbour over-delivered.

The `[CallerFilePath]`/`[CallerLineNumber]` trick is worth reusing: it attributes yields per call
site without editing a single call site, so the probe is one file plus one revert.

### Priority 4 — Extend ISS coverage to NFA/Pair

Optional, and not blocking anything — but it is the one remaining lever on *corpus size*, which is
what protects every heuristic above from over-fitting. Worth **237 more puzzles**, roughly doubling
the corpus. Coverage is currently 30% of the index and this is most of the reachable remainder.

`NFAConstraint` already deserializes ISS's own `NFASerializer` base64url format, so this is plumbing
rather than invention. The blocker is structural: it has `ConsoleName = null` and a constructor taking
`(solver, int[] cellIndices, string serializedNFA, name)`, so it cannot be built from a constraint
string — and `IssParser` produces strings for `SolverFactory.CreateFromGivens`, which calls
`FinalizeConstraints()` internally, after which constraints can no longer be added. The f-puzzles path
at `SolverFactory.cs:1017` already does the programmatic version; give `IssParser` that shape.

Check two things first: that ISS's own serialized strings round-trip through `NFADeserializer`
(compatibility is by construction but untested against ISS's output), and how `.Pair`'s
`~~`-separated groups and `_named-relation` segments are meant to be read. Many `.NFA` instances
address ISS variables rather than cells and stay out of reach regardless.

Also cheap while you are in there: `GreaterThan` with 3–5 arguments (6 puzzles) is probably a
descending chain, i.e. one reversed thermometer. It is ambiguous against "greater than each of", so
implement the chain reading and **let the import adjudicate** — a wrong reading over-constrains and
will show up as a disagreement. Don't chase ISS's `Var`/`Or`/`And`/`Replicate` DSL (it has block
structure with `.End` terminators), weighted `Sum`, or `LittleKiller` (ISS records no direction, so it
cannot be inferred). Details in [`iss-corpus-import.md`](iss-corpus-import.md) §5.

### Priority 5 — Smaller, well-defined items

- **`FindHiddenSingle` is allocation-free now, and the next brute-force allocation is named.** Its
  only garbage was the `List<(int, int)>` returned by `Constraint.CellsMustContain` — **62.1 MB over
  the ISS corpus, a flat 88 bytes on each of 702,836 hits**. The caller never wanted the list: it
  already knows the single cell the value would go in, because it only asks when the group has
  exactly one candidate cell left. `Constraint.MustContainValue` answers the same question as a
  `bool`; the killer-cage, Renban and Quadruple paths override it allocation-free, and the default
  delegates so unconverted constraints stay correct. Call and hit counts are **identical** before and
  after (19,769,754 / 702,836), so the deductions did not change, and all 429 corpus cases return the
  same results. Two things worth carrying forward:
  - **A `bool`-returning twin is worth having wherever a hot path asks a yes/no question of a
    collection-returning API.** Nothing here got cleverer; the list was pure garbage at the call site.
  - **`Quadruple` reached `CellsMustContainByRunningLogic`, which *clones the whole solver*, from
    inside brute force** (106 clones / 229 KB on the corpus). It never needed to: a required value
    must appear among a quadruple's cells by definition, and `Group` is only non-null once those
    cells have been restricted to `requiredMask`, so the direct answer always applies there. Worth
    checking whether any other constraint reaches that clone from a hot path.
- **That next allocation — `QuadrupleConstraint.EnforceConstraint`'s `requiredValues.ToList()` — is
  now done**; see the `QuadrupleConstraint` item below. One correction worth keeping: a plain `uint`
  mask does **not** replace the list, because a quadruple may require the same digit twice
  (`.Quad~R3C3~1~9~1~4` is in the corpus) and a distinct-values mask silently accepts a board holding
  only one copy. It takes a count per value, not a bit.
- **`renban-sky-logical` is still the allocation odd-one-out**, now 331 MB in only 4
  `ConsolidateBoard` passes and 220 `StepLogic` calls. Its dominant `Combinations` site is
  `IsBoardValid` (496,661 yields, >2× any other case) — a *contradiction check*, not a deduction
  step, so ask why that check costs so much here. The bisect's 49% wings attribution is also still
  open now that `FindWings` is known not to use `Combinations`.
- **`platinum-blonde` is still 8.9× off ISS** even with discovery disabled (the other hard classics
  drop to 1.6–2.2×). It's the cleanest remaining outlier, and it explores 3.4× more nodes than ISS.
- **The pathological ISS outliers are re-measured and split by cause** — see
  [`pathological-outliers.md`](pathological-outliers.md). **Two of the four are now fixed**:
  `h-ymyScJa2s` 8,293 → **26 ms** and `blPgSzctUMg` 26 → **4 ms**, both by renban required-value
  exclusion ([`renban-required-values.md`](renban-required-values.md)); `Wb5YT1b-U9Q` was already
  closed by deferral. **The whisper pair is not fixed**: `OqyXKDOhfDA` is 19.0 s against ISS's
  411 ms and `1HuNjcLWlPE` still does not finish against ISS's 72 ms. Both are whisper-dominated and
  the fix is scoped in [`renban-required-values.md`](renban-required-values.md) §6.
  A node-rate probe splits them into **two opposite diseases**, and they should not be worked as one
  list:
  - **Propagation strength** (`1HuNjcLWlPE`, `OqyXKDOhfDA`, `h-ymyScJa2s`): ~1M nodes/sec, which is
    *healthy* — there are just far too many nodes. We are missing deductions ISS makes; no inner-loop
    work will help. Corpus-wide, **`Renban` puzzles have a 4.50× slower median and `Whisper` 2.89×**
    (n=97 and 134, with a constraint-count control), while `Thermo` puzzles are **0.20×** — five times
    *faster*. `1HuNjcLWlPE` is the extreme: whispers, renban and dots with **no givens at all**.
  - **Cost per node** (`blPgSzctUMg`): only **2,870 nodes** yet 680 ms — 280 µs/node against ~1 µs
    in the group above, and 533 KB allocated per node. This one is `SandwichConstraint`, and partly
    fixed (below).
- **`SandwichConstraint` is fixed, and the lesson generalises.** Its brute-force arm was
  *over*-propagating: exact placement enumeration bought at most 32% fewer nodes for 12–29× the time.
  Swapping it for a set-level union took **ISS corpus 12,772 → 11,872 ms (−7.0%)** and `blPgSzctUMg`
  from 49× off ISS to **1.9×**. `StepLogic` is propagation only — `EnforceConstraint` and the weak
  links own correctness — so **the brute-force and logical arms can and should differ**, and a weaker
  brute-force arm cannot change a solution count. `SUDOKU_SANDWICH_BF_ARM` switches between them.
  **`SkyscraperConstraint` was checked with the same lens and is fine — do not "fix" it.** Skipping
  its DFS looks 14–137× faster on blank grids with a cap, and is **209× slower** on a 36-clue board
  and blows up to 227M nodes when counted exhaustively. Its cost is propagation that repays itself.
  **The lesson generalises and nearly cost a 209× regression: never tune propagation strength on
  blank-grid capped counts** — including the three `*-search` cases in `corpus.json`, which are good
  regression detectors and bad tuning targets, because they systematically favour propagating less.
  Tune on real puzzles, uncapped. The sandwich change survives that test (an 18-clue constructed
  puzzle: heuristic 12.7 ms vs exact 103.9 ms, both 364), which is why it shipped.
  Still open with the same lens, but measure it properly: **`XSumConstraint`** allocates 53 MB for a
  51 ms count. Details: [`pathological-outliers.md`](pathological-outliers.md).
- **`QuadrupleConstraint` brute force is now allocation-free**, and this one was *not* a propagation
  question — it was pure per-call garbage. `EnforceConstraint` and `StepLogic` each rebuilt the
  outstanding-values multiset with `requiredValues.ToList()`, collected `List<(int,int)>` of
  candidate cells, and — the dominant cost — ran `remainingValues.Count(value => value == v)` inside
  the hidden-single loop, allocating a closure, a delegate and a boxed `List<int>` enumerator **per
  candidate digit per call**. Rewritten onto a `stackalloc` outstanding-count span plus cell indices:
  measured over 27 quadruple puzzles from `corpus-iss.json`, `StepLogic` went from ~50 MB to **0 bytes**
  across 124k calls and `EnforceConstraint` to **0 bytes** across 814k calls, worst-case allocation
  **26.7 MB → 0.8 MB**, total time **−13.7%**. Semantics are unchanged, which is the point: the arms
  did not move, so no re-tuning was needed.
  Two things worth carrying forward. **The constraint instance is shared across cloned solvers and
  threads** (`constraints = other.constraints` in the copy constructor), so scratch state has to be
  `stackalloc`, never a reusable instance field. And **`CellsMustContain` was left alone
  deliberately.** `MustContainValue` (above) already keeps brute force off its clone, and replacing
  the clone in `CellsMustContain` itself measured **140.8 vs 141.2 ms — a wash** — because it is
  called only 0–42 times per puzzle. It also *does* change what the constraint concludes: the direct
  answer includes cells `CellsMustContainByRunningLogic` filters out, which is a propagation change
  and wants its own measured commit, not a ride-along in an allocation one.
- **Historical note on the same item, partly superseded.** `SandwichConstraint.cs:544` alone yielded
  **4.2M combinations in a single count** of `blPgSzctUMg` (1,469 per node). Buffered enumeration plus
  replacing `combination.Sum()` (which boxes a `List<int>` struct enumerator 4.2M times) took it
  1,529 → 1,229 MB, −19.6%. **The remaining 1.2 GB is `Extensions.Permutations`** — a recursive
  iterator yielding k! permutations with nested iterator state per level, called per surviving
  combination. The loop only wants "which values can appear at which position", which is bipartite
  matching; enumerating k! placements is the wrong algorithm rather than a slow one. Best-defined
  remaining perf item in this file.
- **The constraint-layer WASM tax**: vanilla is 1.0×, `killer-innie` is 5.73×. Two dispatch fixes
  already took 37% off corpus-wide. Same defect class is worth hunting: comparer/delegate dispatch in
  inner loops is mildly costly natively and severe under Mono AOT.
- **WASM startup cost is unmeasured** (runtime boot + module instantiation). ~270 ms and 2.8 MB
  brotli measured informally in Node; never measured properly in a browser. Matters for the product.
- **`EstimateTrueCandidates` is unpooled** — same shape as `EstimateSolutions`, which is now pooled,
  but it has no benchmark case, so changing it would be unmeasured. Add a case first.
- **`SkyscraperConstraint` costs ~130× more per solution than `XSumConstraint`** — 3.3 ms against
  0.025 ms, measured on blank 9x9 grids with a couple of clues. Setup is not the problem (21 ms of a
  16.6 s run), so this is per-node cost, the same disease as `SandwichConstraint` in
  [`pathological-outliers.md`](pathological-outliers.md) but worse. Undiagnosed; `skyscraper-search`
  now covers it.
- **`xsum-search` allocates 53 MB for a 51 ms count**, which is out of proportion and unexplained.
  Probably the `SumGroup` path, which has no memoization of its own.
- **MT pooling** stays off for `TrueCandidates`/`FindSolution`. Per-invocation local caches are **not
  worth building**: the pool already hits 99.99% (9.68M rents, 272 misses), so they'd only address
  lock contention, which remains unproven. See [`solver-pooling-audit.md`](solver-pooling-audit.md).

---

## 3. Working with Sol (GPT-5.6-sol via Codex)

Sol has earned its keep here: it found a bug that had been missed entirely, and corrected three claims
that had been made too strongly. Use it for **design review and hard diagnosis**, not bulk work — the
Codex token window is shared and capped.

### Mechanics

```bash
# Prompt MUST go via stdin. Passing it as an argument makes codex block on stdin and time out.
codex exec -s read-only -c model=gpt-5.6-sol -c model_reasoning_effort=high < /tmp/prompt.md
```

Run it backgrounded and poll — a `high`-effort review takes 10–20 minutes. It reads the repo itself
(read-only sandbox), so give it file paths and line numbers rather than pasting code.

### What made the consultations work

1. **Measure first, then ask.** Both useful reviews came from bringing real numbers. Sol's best
   catch came directly from a ratio I'd reported without understanding (17–19 bytes per pool rent →
   "that's a display-class allocation").
2. **Ask it explicitly to say where you're wrong.** It did, three times, and was right each time.
3. **Give it the product context.** "Target is a browser, 2 GiB heap, most calls are trivial" changed
   its recommendation materially.
4. **Structure as numbered questions** and it answers them in order.

### Its track record here, for calibration

- **Found** the `PushSolver` closure bug: the `Task.Run` lambda captures `solver`, so the compiler
  builds the display class on method entry, allocating on every *declined* offer. This was the biggest
  MT win to date, and it had been misdiagnosed as accepted-task churn.
- **Corrected** "cross-thread release doesn't happen" (it does, via `PushSolver`), so thread-local
  pools would have been unsound.
- **Corrected** "lock contention causes the MT slowdown" — plausible but unproven, since the path is
  randomised.
- **Corrected** "converges to zero steady-state allocation" — only within one invocation; pools are
  call-scoped.
- **Right call** on copying the board in `ReportSolution` rather than special-casing "never release
  the winner", which would rot in every cleanup path.
- Also spotted the `EstimateSolutions` wasted clone and the `solutionEvent` aliasing hazard.

---

## 4. Methodology, hard-won

**Measure before optimising.** Confident guesses have been wrong here at least four times, and each
time a five-minute counter settled it:

- `FastFindPairs`/`FastFindTriples` lambda sorts looked like a perfect delegate target. They run
  **2–6 times per solve** — `doAdvancedStrategies` is false at every brute-force call site.
- Recursive `Span<T>`/`stackalloc` was the confident explanation for the 23× skyscraper outlier. It
  was `List<int>.BinarySearch`'s comparer dispatch.
- MT allocation "must be" accepted-task churn. It was declined-offer closures.
- Two ISS puzzles "disagreed" with their known solution counts, which looked like translation bugs.
  Both were just slow, and the real bug was in the harness reading a cancelled count as a completed
  one.

Technique that works: drop a temporary `XxxCounters.cs` static class in `SudokuSolver/`, increment at
the sites in question, print from the harness, `git checkout --` to revert. Cheap and decisive.

The same applies to *semantics*, not just performance: when it mattered whether a constraint means
the same thing after a translation, the decisive move was an exhaustive count on a 6x6 — small enough
to enumerate fully, big enough to be a real puzzle — comparing the two forms. That settled four line
types in minutes and contradicted the intuition on two of them.

### Benchmarking traps in this repo

- **A blank grid counted to a small cap is the wrong workload for propagation strength.** It asks for
  a handful of solutions out of an astronomical set, so almost nothing needs pruning and any arm that
  propagates *less* wins. Skipping `SkyscraperConstraint`'s DFS measured **14–137× faster** that way
  and is **209× slower** on a 36-clue board, exploding to 227M nodes when counted exhaustively. Tune
  on real puzzles, uncapped. The three `*-search` corpus cases are regression detectors only.
- **The 28-case corpus can disagree with `iss-tune` about the *sign* of a heuristic.** Enabling the
  bilocal branch tier scores 0.83× on the small corpus and **7.7× worse** on `iss-tune` (p90 2.56×,
  worst 180×). Take any branch-ordering or threshold result to `iss-tune` before believing it.
- **`min ms` is an order statistic, so compare medians on high-variance cases.** `Wb5YT1b-U9Q` read
  as a 26% regression at 3 iterations (min 21.78 against its own median of 79.00) and was flat at 15.
  `killer-innie` and `littlekiller-10` both read as *large* regressions at 3 iterations and are fine
  at 15. Use ≥15 iterations before believing any delta on those.
- **`killer-innie` is high-variance**: 106 ms at 5 iterations vs 56 ms at 15. Use ≥15 iterations or
  its numbers mislead. It produced a fake "+46% REGRESSION" once.
- **`--iterations 1` measures tier-0 JIT code**, not steady state. The harness's single warm-up call
  does not escape tiered compilation, so anything under ~100 ms reads several times too slow. This
  produced *two fabricated findings* in one sitting — a "6.4× spread" on `tc-escargot` and a "3.4×"
  on `tc-blank9`, both of which are actually flat within 1.1×. Beware the specific trap of reasoning
  that the workload is deterministic so one iteration is enough: determinism removes *path* variance,
  not warm-up. Use ≥10 iterations for anything sub-100 ms regardless.
- **`truecandidates` timing is now trustworthy** (fixed 2026-08-03) — it used to be the loudest trap
  here. Its branch choice is still randomised, but from a stream scoped to the invocation instead of
  a time-seeded global, so runs repeat. Beware old numbers: each benchmark iteration used to be a
  fresh draw from a very wide distribution, so `min ms` was an order statistic that got
  systematically *lower* the more iterations you ran. Any pre-2026-08-03 `tc-*` timing is optimistic
  and not comparable to a current one.
- **MT timing is noisy**; allocation is the reliable MT signal too.
- **Always A/B paired** via `git stash push -- SudokuSolver/`, measure, `git stash pop`. Stale
  `--baseline` files caused three false regressions in one run.
- **`truecandidates` returns unclamped counts** and callers clamp them, so score clamped. Raw counts
  now reproduce run to run, but they still depend on how many solutions the search happened to
  enumerate, which is a search-order property rather than an answer.
- **A cancelled `CountSolutions` is indistinguishable from a completed one.** It swallows
  `OperationCanceledException` and returns the partial count. Any timeout-bounded caller must check the
  token itself, or a timed-out count silently reads as a real answer — which is exactly how two fake
  "disagreements" got reported during the ISS import.
- **`corpus-iss.json` membership is wall-clock gated**, so it is not bit-reproducible: regenerating on
  a slower machine quietly drops the slowest cases. Treat the committed file as the artefact.
- Wrap anything long in `caffeinate -i`; this laptop idle-sleeps and silently suspends builds.

### Validation checklist before committing and pushing

```bash
dotnet test -c Release SudokuTests/SudokuTests.csproj                                    # 121 tests
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3        # 0 FAIL
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3 --multithread
dotnet build -c Release SudokuSolver.sln                                                 # sln excludes the WASM project
```

Add the ISS corpus to that list for anything touching the solver or `IssParser` — 398 puzzles with
known answers is the strongest correctness signal available, and it takes about 15 s per iteration:

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- benchmarks/corpus-iss.json --iterations 1
```

For WASM changes, republish and re-diff — `dotnet publish SudokuSolverWasm -c Release -o /tmp/wasm`
takes 7–10 minutes, so budget for it. `compare.js` flags result mismatches, not just timing.

**Then push.** `git push` once the checklist is green, rather than stacking commits locally to the end
of a session — sessions get interrupted, and this branch is the only record of a lot of measurement
that is expensive to reproduce. Keeping the header line of this file accurate about the branch state
is part of the same habit.

---

## 5. Reading order for a cold start

1. This file.
2. [`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md) — why WASM isn't the problem.
3. [`iss-corpus-import.md`](iss-corpus-import.md) — the 398-puzzle corpus, where we actually stand
   against ISS, and the coverage ceiling.
4. [`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) — the biggest lever.
4b. [`branch-ordering.md`](branch-ordering.md) — what decides the branch cell, the disabled bilocal
   tier, and why discovery raises node count.
4c. [`weak-link-representation.md`](weak-link-representation.md) — how weak links are applied in the
   search, and why the grouped form is faster.
4d. [`pathological-outliers.md`](pathological-outliers.md) — the worst puzzles in the corpus, split
   into a propagation-strength group and a cost-per-node group.
4e. [`renban-required-values.md`](renban-required-values.md) — what our brute-force loop actually
   propagates, and why "weak links enforce it" is not enough.
4f. [`whisper-arc-consistency.md`](whisper-arc-consistency.md) — the same lesson applied to whispers,
   and the dots, which still have the defect.
4g. [`weak-link-bitmatrix-exploration.md`](weak-link-bitmatrix-exploration.md) — why a general
   bitmatrix view was rejected, the one place it wins, and the parity-census method.
5. [`logical-solver-allocation.md`](logical-solver-allocation.md) — the browser memory problem.
6. [`solver-pooling-audit.md`](solver-pooling-audit.md) + [`truecandidates-allocation.md`](truecandidates-allocation.md) — what's already pooled and why MT is deliberately off.
7. [`wasm-prototype-findings.md`](wasm-prototype-findings.md) — the .NET-WASM constraints that dictate host architecture.
8. `SudokuSolverWasm/README.md` — how to build and run the browser prototype.

`docs/optimization-roadmap.md` predates this work and covers the earlier per-session solver-perf
plan; it is still accurate but narrower in scope.
