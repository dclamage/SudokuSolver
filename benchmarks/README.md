# Solver benchmarks

A lean, repeatable performance harness for the solver. It runs a curated corpus of puzzles,
times each solve/count (min + median over N iterations, plus bytes allocated), **validates**
each result against an expected value, and can **diff against a saved baseline** to flag
regressions.

This is intentionally small: whole-puzzle timing for tracking solver performance and catching
regressions — not a micro-benchmark framework.

## Running

Always run from the repo root, in Release, and prevent the machine from sleeping mid-run
(macOS suspends processes on idle sleep, which corrupts timings):

```bash
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark
```

Options:

| Option | Meaning |
| --- | --- |
| `[path]` | Corpus file (default `benchmarks/corpus.json`) |
| `--iterations N` | Timed iterations per case (default 3; raise for less noise) |
| `--filter TEXT` | Only cases whose name or category contains TEXT |
| `--multithread` | Force multi-threaded solving for every case |
| `--save FILE` | Write results as JSON (use later as a baseline) |
| `--baseline FILE` | Diff min-time vs a saved baseline; flags regressions > 5% |
| `--bitops` | Run the `BitOperations` vs software micro-benchmark and exit (see below) |
| `--import-iss` | Rebuild `corpus-iss.json` from sigh's CTC index and exit (see below) |

Exit codes: `0` ok, `1` a result failed validation, `3` a regression vs baseline.

## Solver switches for A/B runs

A few solver options can be set from the environment so both arms of a comparison come from one
build — which also sidesteps the stale-`--baseline` problem below:

| Variable | Effect |
| --- | --- |
| `SUDOKU_WEAK_LINK_DISCOVERY` | `always` / `never` / `deferred` (default). See [`docs/weak-link-discovery-tradeoff.md`](../docs/weak-link-discovery-tradeoff.md) |
| `SUDOKU_WEAK_LINK_DEFER_NODES` | Node budget before `deferred` gives up and restarts with discovery. Default 2000 |
| `SUDOKU_SANDWICH_BF_ARM` | `heuristic` (default) / `exact` / `none` — how much `SandwichConstraint` propagates while brute forcing. Logical solving always uses `exact`. See [`docs/pathological-outliers.md`](../docs/pathological-outliers.md) |
| `SUDOKU_CF_TRIGGER=nodes:N` | Defers cell forcing until a search has visited N nodes. Takes it from geomean 1.403x against `off` to ~1.0 on the ISS corpus while keeping most of the pruning. See [`docs/cell-forcing-worklist.md`](../docs/cell-forcing-worklist.md) § Step 7 |
| `SUDOKU_OVC_MODE` | `steplogic` (default) / `cellforcing` — whether kropki/difference/ratio/XV run their own arc-consistency sweep during brute force, or hand their cells to the shared cell-forcing scan. See [`docs/whisper-arc-consistency.md`](../docs/whisper-arc-consistency.md) §5 |

The full inventory of default-off tiers, with the one-line reason each is parked, is in
[`docs/ideas.md`](../docs/ideas.md) § "The default-off inventory".

Beware `never` on `corpus-iss.json`: some puzzles there do not finish without weak-link discovery.

## Baseline workflow

Timings are machine-specific, so baselines are **not** committed. On a known-good commit, save
one; after a change, compare against it:

```bash
# before a change (or on main):
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 7 --save /tmp/base.json
# after your change:
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 7 --baseline /tmp/base.json
```

Timing on a laptop is noisy; prefer a quiet machine and `--iterations 7+`, and treat only
consistent, repeatable deltas as real.

**Scale the iteration count to the corpus's total duration, not by habit.** The number that matters
is how long a whole run takes, because that is what fixed per-process noise is measured against.
`corpus.json` totals ~16 s and is stable at 5 iterations. A 144-case classic corpus totals ~100 ms
and at 5 iterations drifts **±10%** between identical runs — enough to invent a double-digit result
or hide one. At 25 it settles to ~1% (measured: 99.3 / 97.4 / 98.1 / 98.0 ms, same build, same
config). If a corpus is fast, a delta smaller than the spread of four repeated runs is not a
finding, so measure that spread before trusting the delta.

**The run now warns when a baseline does not describe it.** Both mismatches used to be silent and
both have produced fictional numbers here. A baseline saved from a different corpus — or the same
corpus under a different `--filter` — matches no case names at all, so every ratio drops out and the
run prints a bare total that reads as a clean result; it now says so, and names how many of the run's
cases the baseline actually covers. A baseline saved at a different `--iterations` is flagged too,
using the `Iterations` field `--save` records. Baselines written before that field existed report 0
and are not flagged, so re-save rather than trusting an old file.

**And a baseline's iteration count is part of its identity.** `--save` records `MinMs`, and the
minimum of 5 samples is systematically lower than the minimum of 3. Comparing across different
counts produces a confident, entirely fictional number — in one direction or the other depending on
which arm had more samples.

### Read the ratios, not the total

**`total min ms` is a sum, and case times here span five orders of magnitude, so it is dominated by
whichever few cases are slowest.** A change that helps only those looks like a corpus-wide win. The
run prints a per-category share breakdown and flags any single case above a quarter of the total for
exactly this reason — when one line is 40%+ of the sum, the total is measuring that case, not the
solver.

With `--baseline`, the run instead reports **per-case ratios** and aggregates them:

```
vs baseline over 30 cases (per-case ratios, unweighted by duration):
  geomean  1.073x (  7.3%)   median  1.036x
  p10  0.652x   p90  2.294x
  best  0.062x kropki-search-cap50k   worst  4.169x quadruple-16
  12 better, 18 worse, 0 within 1%
  by category (each category weighted equally:  0.998x):
    setting                 0.745x  n=4
    variant                 0.748x  n=6
    variant-hard            1.835x  n=7
```

Three things about that shape are deliberate:

- **Geometric, not arithmetic.** These are ratios, and the geometric mean is the only mean that is
  symmetric under swapping the arms: rerun with the arms exchanged and every figure inverts exactly.
  An arithmetic mean would average 0.5x and 2.0x to 1.25x and call a perfect wash a 25% regression.
- **The distribution is not decoration.** The example above is *flat on average and wildly bimodal* —
  one case 16x faster, another 4x slower. A uniform 0.95x and a "0.5x on three cases, 1.1x elsewhere"
  are completely different changes with nearly the same mean, and only p10/p90 and the
  better/worse counts separate them.
- **Sub-millisecond cases are excluded.** Under equal weighting, a ratio built from two 0.03 ms
  timings is noise given the same vote as a ten-second case. Cases with a baseline under 1 ms are
  dropped and counted.

### Arm position inside a sweep is worth ~7%, so interleave arms and take the min of rounds

**Running arms back to back in one script biases the later ones, and by more than most findings are
made of.** Measured 2026-08-30 on `--filter iss-tune`: `SUDOKU_CF_TRIGGER=nodes:3000` scored geomean
**1.061x** as the 7th and last arm of a sweep and **0.990x** as a fresh isolated process — same build,
same config, same iteration count, and **identical node counts (2,326,271 both times)**, so the whole
7% was measurement. A sweep that walks thresholds in order will therefore manufacture a clean-looking
monotone trend out of nothing.

The protocol that fixes it, and the control that proves it did:

1. **Interleave.** Run every arm once per round, rotating the starting index each round, each in its
   own process. Position drift then lands on every arm equally instead of on the last one.
2. **Take the min across rounds, per case.** `MinMs` is already a min over iterations; min over
   rounds extends the same estimator across process boundaries. Do this yourself from `--save` files
   rather than with `--baseline`, so both arms are treated symmetrically.
3. **Carry a duplicate control arm.** Run the baseline configuration *twice* under different names.
   The second one is your noise floor, and it is the only thing that licenses reading anything else.

What that buys, on the same corpus and machine: a single back-to-back comparison put the `off`-vs-`off`
control at p10 0.915x / p90 1.145x, while three interleaved rounds put it at **p10 0.986x / p90 1.015x,
geomean 1.000x, 22 better / 23 worse**. The same protocol then resolved a 2.3% effect cleanly. Without
it, nothing under about 15% per case is readable.

This composes with, and does not replace, the iteration-count rule above. Three rounds of 5 iterations
is not the same as one round of 15: the rounds are what average out process-level state, and the
iterations are what average out within-process jitter.

### `nodes` is the metric for a pruning change, and the cheapest parity check you have

Every run now prints a `nodes` column and a `total nodes:` line, and with `--baseline` a
`vs baseline nodes` block. This is `Solver.NodesVisited` — search nodes expanded, counted
unconditionally and read off the same solver the op ran on.

**Use it, not milliseconds, to judge pruning.** Branch ordering, a new propagator, conflict
scoring: these change how much of the tree gets explored, and that is what nodes measure. The count
is exact, reproducible run to run, and identical on any machine — none of which a timing is. A
timing change on a laptop needs seven iterations and a squint; a node change is either there or it
is not.

**And it is a free parity check.** A change that is meant to be output-preserving must leave every
count *bit-identical*. One node of drift means the two arms searched different trees, so the timing
comparison is not like-for-like — the run says so explicitly when it sees movement. This is the same
discipline [`docs/weak-link-bitmatrix-exploration.md`](../docs/weak-link-bitmatrix-exploration.md)
arrived at the expensive way (census the outcome buckets before reading any timing); the column just
makes the cheap half of it automatic.

Three things to know before reading the column:

- **`0` is a real answer, not a broken counter.** `CountSolutions` returns early when root setup
  already finished the puzzle, so an easy `count` case never enters the search — `vanilla-u17`
  legitimately reports 0. `FindSolution` has no such early-out and reports 1 for the same board.
  The `logical` op does not brute force at all and is always 0.
- **`estimate` cases are excluded from the comparison**, not merely flagged, and the run says how
  many it dropped. They sample randomly, so their counts differ from themselves run to run; leaving
  them in turns a perfect parity result into "27 identical, 2 fewer, 1 more" and buries the signal.
  They still contribute to `total nodes:`, which is why that line wobbles slightly between runs.
- **Deferral counts the abandoned prefix.** When `WeakLinkDiscoveryMode.Deferred` gives up and
  restarts with discovery, the nodes from the abandoned attempt stay in the total. They were really
  visited, and the point of the deferral trade-off is exactly that they might be wasted.

A baseline saved before this column existed has no node counts in it. They deserialize to zero, so
the run recognizes an all-zero baseline side and says so, rather than reporting every case as
`0 -> N` and flagging the whole corpus as drifted — a fabricated regression on the one signal that
exists to be trusted absolutely.

What a clean run looks like when nothing moved:

```
total nodes: 26,841,719
vs baseline nodes over 33 cases: 33 identical, 0 fewer, 0 more
  (3 "estimate" case(s) excluded: they sample randomly and differ from themselves)
  total 26,830,146 -> 26,830,146 (   0.0%)
```

The two totals differ because `total nodes:` is the whole run and the second line is the compared
subset, i.e. without the `estimate` cases.

A case that expands zero nodes in **both** arms counts as identical rather than being dropped — a
search that never runs in either arm is genuine agreement. That is also why the stale-baseline check
asks whether the baseline is empty *while this run found nodes*: a corpus filtered down to `logical`
cases is legitimately zero on both sides, and that is a real parity result, not a missing baseline.

### What ratios still do not fix: corpus composition

Per-case ratios remove *duration* weighting. They do nothing about *composition* weighting — **N
similar cases still cast N votes.** Add twenty near-identical non-consecutive puzzles and any change
that helps non-consecutive becomes a corpus-wide "win" under the plain geomean, exactly as it would
under the time sum.

The per-category geomean exists to bound that: each category gets one vote regardless of how many
cases it holds, and the run warns when any single category is more than half the corpus. In the
example above the unweighted number is +7.3% while the category-weighted one is flat, because
`variant-hard` is 7 of 30 cases and the worst performer.

**That is a guard rail, not a solution.** Category is an imperfect proxy for "similar" — four
leave-one-out boards from the same puzzle would sit in one category and be correctly discounted,
but two genuinely independent killer puzzles get discounted the same way. And no aggregate can
rescue a corpus that does not sample what you care about.

The real answer is to use the right corpus for the question:

| | `corpus.json` | `corpus-iss.json` | *(missing)* a hard corpus |
| --- | --- | --- | --- |
| what it is | ~36 hand-picked cases, roughly one per constraint type | 398 real CTC puzzles, **all under 2 s** | the long-running legitimate use cases |
| what it is for | **coverage** — did a change break or move constraint X | **breadth** — does a change help across many styles | **optimisation** — is the solver faster where anyone waits |
| right metric | per-case deltas; the aggregate means nothing | geomean of ratios | **wall time** — seconds saved is the user-facing quantity |

`corpus.json` is a regression suite and was never a uniform sample of anything; treating its geomean
as a population estimate is a category error.

**The missing column is not hypothetical, and it has now produced a wrong answer.** Tuning the
cell-forcing deferral threshold entirely on `corpus-iss.json` concluded that no threshold could win;
on `corpus.json`, whose long searches actually reach a threshold, the same arms are a 4.3% total win
and 7.6–9.1% on `truecandidates` ([`docs/cell-forcing-worklist.md`](../docs/cell-forcing-worklist.md)
§ Step 8). **Before measuring anything that only activates past a threshold, count how many cases
reach it.** If that count is in single digits, the run cannot answer the question however clean its
statistics look — and a tune/holdout split will not warn you, because both halves omit the same
puzzles and therefore agree.

### `corpus-iss.json` excludes the puzzles optimisation is for

This is the important caveat and it is easy to miss. The importer applies **two** filters:

- `--budget-ms` (default **10 s**): a puzzle slower than this is a timeout and is never validated.
- `--corpus-max-ms` (default **2 s**): a puzzle that agrees but is slower than this is validated,
  recorded in the report, and **left out of the corpus**.

The result is a corpus of easy puzzles:

```
398 puzzles   total 13.1 s   mean 32.9 ms
median   1.1 ms      p90  24.9 ms
94% run in under 50 ms;  only 5 exceed 1 s
slowest 1951 ms  <-- against a 2000 ms cap: the distribution is truncated, not naturally ending
```

**The median ISS puzzle solves in 1.1 milliseconds.** Nobody waits on those. So the corpus
self-selects for puzzles that do not need optimising and discards the ones that do, and that biases
every conclusion drawn from it toward changes that help trivial searches.

It also undercuts equal weighting *on this corpus specifically*. A geomean over per-case ratios
gives all 376 sub-50 ms puzzles the same vote as the 5 that take over a second — so it answers "does
this help a typical puzzle", where "typical" means "one that was already instant". The wall-clock
sum, for all its faults, at least weights toward where the time actually goes. Neither is right,
because the corpus is truncated: the puzzles that would settle the question were filtered out at
import.

Use it for what it is good at — breadth across many real puzzles and constraint types, with a
tune/holdout split so a fitted heuristic can be checked — and do not read it as "the solver got
faster".

**Building the hard corpus.** Re-import with the ceilings raised and write to a separate file, so
the fast corpus stays quick:

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --import-iss     --iss-index /tmp/iss-index.json --iss-dir /tmp/iss-puzzles     --budget-ms 120000 --corpus-max-ms 120000     --out benchmarks/corpus-iss-hard.json --report /tmp/iss-hard-report.json
```

The existing report's "agreed but too slow" list is the candidate set, and it costs nothing but
wall-clock to promote them. Expect a corpus that takes minutes rather than seconds, which is the
point: on it, **total time is the metric**, because a change that removes 30 s from a 60 s puzzle
matters more than the same ratio on a 1 ms one, and only a sum says so.

Even then, coverage bias remains: the index holds only the ~30% our parser supports, so a
per-constraint result generalises only as far as that.

**`alloc MB` is noisy too, and in the same way.** It comes from `GC.GetTotalAllocatedBytes`, which is
**process-wide**, so a case picks up whatever else the runtime allocated during its measured
iteration — enough to have reported **negative** totals (`-3.71`, `-758.75`) on cases that allocate a
fraction of a MB. Corpus-wide alloc sums are therefore not a reliable A/B signal. Re-measure a
suspicious case on its own with `--filter`, where the number is stable, and for attributing
allocation to a *specific* method use `GC.GetAllocatedBytesForCurrentThread()` around it instead —
it is per-thread and does not drift.

## BitOperations micro-benchmark

`--bitops` times each `BitOperations` intrinsic against a hand-written software equivalent *in the
same host*. A ratio below 1 means the intrinsic lost to software, i.e. it is not being lowered to a
real machine instruction. This exists because the WASM port needed to know whether Mono AOT emits
`i32.popcnt` / `i32.clz` / `i32.ctz`; the shared `BitOpsBench.cs` is compiled by both the native
harness and `SudokuSolverWasm`, so the two are directly comparable. Results:
[`docs/wasm-prototype-findings.md`](../docs/wasm-prototype-findings.md).

## Corpus format

`corpus.json` is a list of cases. Each sets exactly one input (`fpuzzles` / `givens` / `iss` /
`blank`) plus how to run it:

```json
{
  "name": "killer-cage",
  "category": "killer",
  "fpuzzles": "N4Ig...",          // OR "givens": "0000...", OR "iss": ".Cage~10~R1C1…",
                                  //   OR "blank": 9
  "constraints": ["arrow:r1c1;r1c2r1c3"],   // optional extra constraints
  "op": "count",                  // "count" (CountSolutions), "solve" (FindSolution),
                                  //   or "logical" (ConsolidateBoard)
  "maxCount": 50000,              // optional cap for "count" (0/absent = uncapped)
  "expected": 1,                  // optional; a mismatch is a validation failure
  "multiThread": false            // optional
}
```

Capped counts are deterministic (`min(actual, cap)`), so they make good bounded, validatable
cases for otherwise-huge search spaces.

## Ops

| op | runs | result value |
| --- | --- | --- |
| `count` | `CountSolutions` | number of solutions (capped by `maxCount`) |
| `solve` | `FindSolution` | 1 if solvable, 0 if not |
| `logical` | `ConsolidateBoard` | candidates still standing across the grid, or -1 if logic proved the board invalid |
| `truecandidates` | `TrueCandidates` | sum of per-candidate solution counts, each clamped to `numSolutionsCap` |
| `estimate` | `EstimateSolutions` | samples completed (`estimateIterations`, default 200) |

`count` and `solve` exercise only brute force. **`logical` is the only op that touches the logical
solver** — `AICSolver`, `SolverLogic`, and the constraints' non-brute-force `StepLogic` — which is
the half a setting UI depends on. Its score is deliberately sensitive to logic changes: a fully
solved grid scores one per cell (81 on a 9x9), and a puzzle where logic stalls scores higher.
Adding or improving a technique will move it, which forces a deliberate re-baseline instead of a
silent drift.

`truecandidates` is **the** setting-UI operation: it runs on every grid edit, usually against a
board that is still under-constrained. That is the opposite of the rest of the corpus, which is
finished puzzles with unique solutions. Set `numSolutionsCap` to the cap the UI uses (1 for plain
true candidates, 8 for the coloured variant).

Clamping matters: the solver returns **raw** counts and callers clamp them (see
`WebsocketListener.SendTrueCandidates`). Unclamped totals are **not deterministic** — they depend
on how many solutions the search happened to enumerate before every candidate was covered — so
never score a `truecandidates` case on raw counts. Clamped, the score is stable and readable: 729
means every candidate on a 9x9 is still live, 81 means the grid is fully resolved.

`estimate` is scored on samples completed rather than the estimate itself, which is stochastic. The
sample count still catches a path that short-circuits or throws; the point of these cases is time
and allocation, since each sample clones one child per open candidate and keeps only one.

### Capped counts on a blank grid, for constraints no real puzzle covers

`xsum-search`, `skyscraper-search` and `sandwich-search` are blank 9x9 grids with a few clues,
counted to a cap. Neither X-Sum nor Skyscraper appears in *any* of the 398 ISS puzzles, so before
these cases nothing in either corpus exercised `XSumConstraint` or `SkyscraperConstraint` at all — changing them would have been
unmeasured, and a regression invisible. A capped count on a blank grid is the cheapest way to cover a
constraint that has no real-world puzzles to import: it needs no hand-authored puzzle, and the cap
makes the score deterministic (the search stops at the cap, it does not sample).

Their expected counts were cross-checked for agreement across all three `WeakLinkDiscoveryMode`
values and single- vs multi-threaded, which is the strongest independent check available without an
external oracle. `sandwich-search` is additionally checked across both of
`SandwichConstraint`'s propagation arms (`SUDOKU_SANDWICH_BF_ARM`), since agreement there is the
invariant that lets the brute-force arm be weaker than the logical one.

Sandwich *is* covered by the ISS corpus (8 puzzles), but it had no case in this corpus, so a change
that moved the ISS total by 7% left the default `--iterations 3` run completely flat. Real-world
coverage in one corpus is not a substitute for a case in the one people run by habit.

The same gap cost renban a 321× win and whispers a 169× win in silence. `renban-vivian` (CTC
`h-ymyScJa2s`, 14 renban lines and two givens) and `whisper-zoomout` (CTC `OqyXKDOhfDA`, four
thermometers and twelve whispers) were added for exactly that reason: they ran in 8.3 s and 19.6 s,
run in 26 ms and 113 ms, and there was previously no renban `count` case and **no whisper case of any
kind**. Unlike the three `*-search` cases these are real uncapped unique-solution puzzles, so they
*are* legitimate tuning targets.

**These three are regression detectors, not tuning targets.** A blank grid counted to a small cap
asks for a few solutions out of an astronomical set, so almost nothing needs pruning and any change
that propagates *less* looks like a win. Tuning `SkyscraperConstraint`'s propagation this way
measured 14–137× faster and was really **209× slower** on a realistic board. Tune propagation
strength on real puzzles, uncapped — see
[`docs/pathological-outliers.md`](../docs/pathological-outliers.md) § "The regime trap".

Pick the cap from the per-solution cost, not a round number. `skyscraper-search` uses a cap of 100
where `xsum-search` uses 5,000, because **Skyscraper costs ~3.3 ms per solution against X-Sum's
0.025 ms** — about 130× more. That gap is a real and so far undiagnosed per-node cost problem in
`SkyscraperConstraint`; see [`docs/pathological-outliers.md`](../docs/pathological-outliers.md) for
the same shape diagnosed in `SandwichConstraint`.

### `nc-4given`, four givens and a negative constraint

Non-consecutive had **no `count` case in either corpus**. `tc-blank-nonconsecutive` is a
`truecandidates` op with `numSolutionsCap: 1`, so it stops as soon as every candidate is covered and
barely searches at all — it does not exercise the tree.

`nc-4given` is a unique-solution 9x9 with four givens (`r1c2=4`, `r2c1=6`, `r3c4=7`, `r3c6=3`) and
nothing but the non-consecutive negative constraint doing the work, so it is close to a pure test of
propagation strength. Its count was cross-checked as 1 under both `--multithread` and single, all
three `SUDOKU_WEAK_LINK_DISCOVERY` values (`deferred`/`always`/`never`) and `SUDOKU_BRANCH_ORDER=static`.

Prefer this over counting a **blank** non-consecutive grid. That has an exact answer (5,287,048) but
takes a very long time, and it is slow even capped — a 200,000-solution cap did not finish a single
arm in ten minutes here, so it is not usable as a corpus case at any cap worth having.

`OrthogonalValueConstraint` sets `WantsBruteForcePropagation => false`, so non-consecutive is
excluded from the brute-force constraint queue entirely and is enforced purely by weak links. That
makes this case a clean probe of the *board-write* path — every weak-link `ClearValue` fires the
hidden-single group marking — with the constraint-queue machinery contributing nothing. It measured
-19.6% on the propagation-worklist packing change while the ISS corpus median was -14.3%.

### `tc-nc-no-r3c4`, and why there is only one of it

Built by removing one given from `nc-4given` and running `truecandidates`. It covers the gap
`nc-4given` does not: **`truecandidates` is the setting-UI operation**, it runs on every grid edit
against an under-constrained board, and the only other non-consecutive case for it is a blank grid
that barely searches. Score 598, agreeing across five arms: default, `--multithread`, all three
`SUDOKU_WEAK_LINK_DISCOVERY` values, and cell forcing on and off.

It discriminates hard — enabling in-search cell forcing moves it **-42%**, and the same
construction on the other three givens moves -44%, -46% and -50%. All four were measured; only the
cheapest is committed. **The other three are redundant and would distort the total.** They move
together within 9 points, so the fourth adds almost no signal over the first, and all four together
cost ~29 s against a corpus that is otherwise ~9 s of everything-except-non-consecutive.

That is the general rule this case exists to illustrate:

> **Do not let one constraint family dominate the total.** Adding N slow cases of one kind makes any
> change that helps that kind look like a corpus-wide win. Non-consecutive was already **54%** of
> `total min ms` before this case was added, on the strength of `tc-blank-nonconsecutive` alone;
> all four leave-one-out boards would have taken it to **82%**.

The run now prints a per-category share breakdown and flags any single case above a quarter of the
total, so this is visible rather than something you have to suspect. `tc-blank-nonconsecutive` is
currently 43% on its own — **read per-case deltas, not the total.**

A three-given non-consecutive board costs about the same as a blank one (4.6 s against ~9 s). Three
givens barely help, which is a fact about how little the negative constraint propagates from givens
alone.

### `quadruple-16`, a real puzzle rather than a blank grid

`quadruple-16` covers `QuadrupleConstraint`, which — like X-Sum and Skyscraper before them — had no
case here at all, so a change worth 14% of quadruple brute-force time was invisible to the default
run. Unlike the three above it is a **real CTC puzzle**, lifted from `corpus-iss.json`
(`iss-jk4n68pZLG8`, 16 quadruples, uncapped, unique solution), which makes it a legitimate tuning
target and not merely a regression detector: it is a finished puzzle with a real search, so
propagating *less* does not automatically look like a win on it. Prefer this shape when the
constraint has real puzzles to import; reach for a capped blank grid only when it does not.

Its expected count comes from ISS's own recorded count, and was re-checked here under
`SUDOKU_WEAK_LINK_DISCOVERY=always` and under `--multithread`. Note `never` does **not** finish on
it, which is the same caveat that applies to `corpus-iss.json` generally.

Two things to know before adding `logical` cases:

- **They are slow.** Logic to exhaustion costs far more than brute force on the same puzzle, and a
  few puzzles are wildly worse (`variant-cloneways` takes ~64 s, `variant-equalsums` ~44 s, versus
  21 ms and 14 ms to brute-force solve). Probe before adding.
- **They allocate enormously** — escargot allocates 0.22 MB to brute-force solve and **281 MB** to
  logic-solve, a ~1300x difference. That matters for the WASM port, which has a 2 GiB heap ceiling
  and a weaker GC.

## The ISS corpus (`corpus-iss.json`)

A second corpus, generated rather than hand-maintained: **398 real CTC puzzles** imported from
[sigh's ISS index](https://sigh.github.io/iss-sudoku-index/), each an uncapped `count` whose
`expected` is the solution count **ISS itself recorded**. It exists because `corpus.json` is 33
hand-picked cases, which is far too few to tune a heuristic against without over-fitting.

The index's `data/mappings.json` also records ISS's own **`guesses` and `solve_ms` per puzzle**, for
all 1,671 — so you can compare tree sizes against ISS without running it. That is the first thing to
look at on a slow puzzle: it says whether you are losing on node count or on cost per node.

```bash
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- benchmarks/corpus-iss.json
```

### The tune/holdout split is the point — respect it

Every case is in category `iss-tune` (280) or `iss-holdout` (118):

```bash
--filter iss-tune       # develop and tune against these
--filter iss-holdout    # only ever to check the result generalises
```

**Never tune a heuristic against `iss-holdout`.** The split is a pure function of the puzzle id
(FNV-1a, not `string.GetHashCode()`, which is randomised per process), so re-importing — with more
puzzles, or after extending the parser — never moves a puzzle from holdout to tune. That property is
what makes the holdout worth anything; a reshuffle would silently contaminate it.

### Regenerating

Needs the index JSON and a directory of `<puzzle_id>.iss` files, both fetched from the index site
(`data/mappings.json` is the machine-readable index; each puzzle is at
`data/puzzles/<puzzle_id>/puzzle.iss`, and ~18 of the 1,671 ids 404):

```bash
curl -sS -o /tmp/iss-index.json https://sigh.github.io/iss-sudoku-index/data/mappings.json
mkdir -p /tmp/iss-puzzles && python3 -c "
import json, urllib.request, os
from concurrent.futures import ThreadPoolExecutor
ids = [r['puzzle_id'] for r in json.load(open('/tmp/iss-index.json'))['rows']]
def get(i):
    p = f'/tmp/iss-puzzles/{i}.iss'
    if os.path.exists(p): return
    try:
        with urllib.request.urlopen(f'https://sigh.github.io/iss-sudoku-index/data/puzzles/{i}/puzzle.iss', timeout=60) as r:
            open(p, 'wb').write(r.read())
    except Exception as e: print(i, e)
list(ThreadPoolExecutor(8).map(get, ids))
"
```

Then:

```bash
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --import-iss \
    --iss-index /tmp/iss-index.json --iss-dir /tmp/iss-puzzles \
    --out benchmarks/corpus-iss.json --report /tmp/iss-report.json
```

A puzzle is imported **only if our count equals ISS's**. Disagreements are printed, excluded, and
make the run exit non-zero, because a disagreement means either the parser mistranslated something
or the solver is wrong. The report explains every skipped puzzle, and its
`unsupported constraints, most-blocking first` tally is the list to work from when extending
`IssParser`. Coverage is capped at roughly 30% of the index by ISS's general constraint DSL — see
[`docs/iss-corpus-import.md`](../docs/iss-corpus-import.md).

Two flags worth knowing: `--budget-ms` is the wall-clock ceiling per puzzle while validating (a
puzzle that exceeds it is reported as a timeout and left out, since an unfinished count proves
nothing), and `--corpus-max-ms` keeps a full corpus run quick by admitting only puzzles that counted
faster than it. Agreeing-but-slower puzzles are listed in the report for deliberate promotion.

Because both gates are wall-clock, membership is **not** bit-reproducible: a puzzle sitting near
either threshold can fall on the other side of it on a different machine or a noisier run. Which
puzzles are in is therefore a committed artefact, not something to be regenerated casually — and
regenerating on a slower machine will quietly drop the slowest cases.

## Adding cases

Prefer puzzles that exercise real search (killers, little killers, arrows, hard vanilla) and
give each an `expected` value so the run doubles as a correctness check. Keep any single case
under a few seconds so a full run stays quick; cap large counts with `maxCount`.
