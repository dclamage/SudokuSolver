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

Exit codes: `0` ok, `1` a result failed validation, `3` a regression vs baseline.

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

## BitOperations micro-benchmark

`--bitops` times each `BitOperations` intrinsic against a hand-written software equivalent *in the
same host*. A ratio below 1 means the intrinsic lost to software, i.e. it is not being lowered to a
real machine instruction. This exists because the WASM port needed to know whether Mono AOT emits
`i32.popcnt` / `i32.clz` / `i32.ctz`; the shared `BitOpsBench.cs` is compiled by both the native
harness and `SudokuSolverWasm`, so the two are directly comparable. Results:
[`docs/wasm-prototype-findings.md`](../docs/wasm-prototype-findings.md).

## Corpus format

`corpus.json` is a list of cases. Each sets exactly one input (`fpuzzles` / `givens` / `blank`)
plus how to run it:

```json
{
  "name": "killer-cage",
  "category": "killer",
  "fpuzzles": "N4Ig...",          // OR "givens": "0000...", OR "blank": 9
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

Two things to know before adding `logical` cases:

- **They are slow.** Logic to exhaustion costs far more than brute force on the same puzzle, and a
  few puzzles are wildly worse (`variant-cloneways` takes ~64 s, `variant-equalsums` ~44 s, versus
  21 ms and 14 ms to brute-force solve). Probe before adding.
- **They allocate enormously** — escargot allocates 0.22 MB to brute-force solve and **281 MB** to
  logic-solve, a ~1300x difference. That matters for the WASM port, which has a 2 GiB heap ceiling
  and a weaker GC.

## Adding cases

Prefer puzzles that exercise real search (killers, little killers, arrows, hard vanilla) and
give each an `expected` value so the run doubles as a correctness check. Keep any single case
under a few seconds so a full run stays quick; cap large counts with `maxCount`.
