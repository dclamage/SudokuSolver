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
`expected` is the solution count **ISS itself recorded**. It exists because `corpus.json` is 28
hand-picked cases, which is far too few to tune a heuristic against without over-fitting.

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
