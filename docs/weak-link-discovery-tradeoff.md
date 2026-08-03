# Dynamic weak-link discovery: a large, two-sided perf lever

Date: 2026-08-02. **Updated 2026-08-03**: the deferral design proposed at the bottom of this file
has been built, tuned and shipped as the default — see
[§ Deferred discovery, as built](#deferred-discovery-as-built) for the results, which are more
two-sided than expected.

Tested the hypothesis that for classic sudoku, `DiscoverWeakLinks` is both slow and unnecessary
compared to just brute-forcing.

**Confirmed for classic, and it generalises much further than expected — but it is not safe to
disable globally.** Discovery is worth up to **22× on some puzzles and costs up to 17× on others**.
It is the single largest perf lever found so far, in both directions.

Nothing was changed in the solver. This is a measurement of the existing
`SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY` gate.

## Background

`DiscoverWeakLinks` runs once per brute-force operation, immediately after the root clone. It
probes every candidate of every unset cell — cloning a scratch solver and propagating — to find
weak links that ordinary propagation misses.

It already early-exits when the initial propagate *completes* the puzzle. That is why
`vanilla-u17` costs 0.08 ms (logic solves it outright, discovery never runs) while `escargot` pays
full price. That asymmetry is what made the earlier per-node analysis in
[`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md) look strange.

## Result

Full corpus, 10 iterations, native Release. `OFF speedup` > 1 means disabling discovery is faster.

| case | ON ms | OFF ms | OFF speedup |
| --- | ---: | ---: | ---: |
| variant-cloneways | 20.42 | 0.90 | **22.63×** |
| variant-equalsums | 14.33 | 0.69 | **20.74×** |
| littlekiller-8 | 2.93 | 0.26 | **11.13×** |
| escargot | 0.99 | 0.12 | **8.54×** |
| variant-killerblister | 4.58 | 0.64 | **7.20×** |
| littlekiller-10 | 55.01 | 16.54 | **3.33×** |
| killer-cage | 39.58 | 31.32 | 1.26× |
| blank4 | 0.76 | 0.67 | 1.13× |
| variant-renban-sky | 108.46 | 104.19 | 1.04× |
| killer-innie | 62.88 | 61.67 | 1.02× |
| arrow-search | 19.62 | 19.42 | 1.01× |
| *(5 logical cases)* | — | — | 0.98–1.03× |
| blank6-cap5M | 2325.72 | 2396.47 | 0.97× |
| **kropki-search-cap50k** | **2225.04** | 10630.12 | **0.21×** |
| **variant-orbit** | **17.37** | 303.97 | **0.06×** |
| **corpus total** | **6829.5** | 15533.2 | — |

Seven cases get faster by disabling, two get dramatically slower, the rest are flat. **The corpus
total favours keeping it on**, but only because `kropki` and `orbit` dominate the sum. Every
result still validated (`ok`), so this is purely a cost/benefit question, not a correctness one.

Logical-solve cases are unaffected, as expected — `ConsolidateBoard` does not call
`DiscoverWeakLinks`.

## The mechanism, via guess counts

Counting branch points with and without makes the three regimes obvious:

| regime | example | guesses ON | guesses OFF | reading |
| --- | --- | ---: | ---: | --- |
| **pure cost** | killer-innie | 50,112 | 50,112 | discovery changed the search not at all |
| | arrow-search | 20,052 | 20,052 | |
| | blank6-cap5M | 5,000,012 | 5,000,012 | |
| **essential** | kropki-search | 1,581,998 | 8,873,294 | 5.6× fewer nodes |
| | variant-orbit | 1,635 | 53,438 | **32.7× fewer nodes** |
| **actively harmful** | littlekiller-10 | 7,550 | **2,554** | discovery *tripled* the search |
| | killer-cage | 3,171 | **2,740** | |
| | escargot | 18 | **10** | |

The third regime is the surprising one. On several puzzles discovery does not merely fail to pay
for itself — it makes the search *worse*, presumably by perturbing the conflict-score branch
ordering that the discovered links feed. `littlekiller-10` explores 3× more nodes with discovery
enabled and still costs 3.3× more wall time.

There is a fourth flavour worth noting: on `variant-cloneways`, `variant-equalsums` and
`variant-killerblister`, discovery drives guesses to **0** — it solves the puzzle outright — and is
*still* 7–22× slower than simply brute-forcing. That is precisely the "slow and unnecessary
compared to brute force" case.

## Competitive impact

This is most of the gap against Interactive Sudoku Solver on classic puzzles. Exhaustive count,
minimum ms:

| puzzle | ISS | ours, discovery ON | ours, discovery OFF | gap ON | gap OFF |
| --- | ---: | ---: | ---: | ---: | ---: |
| escargot | 0.5 | 3.65 | 1.09 | 7.0× | **2.2×** |
| golden-nugget | 0.7 | 3.15 | 1.11 | 4.6× | **1.6×** |
| platinum-blonde | 0.6 | 4.69 | 5.36 | 8.1× | 8.9× |

Two of the three hard classic puzzles go from 5–7× off ISS to **1.6–2.2×**. `platinum-blonde` is
the exception — discovery genuinely helps there — and is now the clearest remaining outlier to
chase.

## What to do about it

*(Written 2026-08-02, before the deferral work. Options 1 and 3 are now done; see the next
section. Options 2 and 4 are still open.)*

Do **not** flip the default. Globally disabling costs 2.3× on the corpus total, because `kropki`
and `orbit` depend on it.

The useful framing is that discovery is an unconditional up-front payment for a benefit that varies
from 32× to negative. Options, roughly in order of effort:

1. **Make it a real solver option**, not just an env var. It is currently reachable only through
   `SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY`, so callers cannot choose per operation. A setting
   UI doing interactive work on a classic grid wants it off; a hard variant count wants it on.
2. **Abort early when unproductive.** Discovery loops `do { … } while (innerResult == Changed)`.
   The pure-cost cases might be detectable after a partial pass — if few links are being found,
   stop. Needs data on how many links each puzzle class actually yields.
3. **Budget it against expected search size.** Paying ~3 ms of discovery to save nothing is fine on
   a 10-second count and disastrous on a 0.12 ms solve. Scaling the probe budget to the work
   already done would cap the downside.
4. **Investigate the harmful cases** (`littlekiller-10`, `killer-cage`, `escargot`), where
   discovery increases node count. That looks like a defect in how discovered links feed branch
   ordering, not an inherent trade-off, and fixing it would remove the downside on that whole
   group.

Note this is entirely a **native** finding — nothing here is WASM-specific. It benefits both
targets equally, and it is a larger lever than every WASM-specific fix so far combined.

## Reproducing

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 10 --save /tmp/disc-on.json
SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY=1 \
    dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 10 --baseline /tmp/disc-on.json
```

Guess counts came from a temporary counter at the `GetLeastCandidateCell` branch points in
`FindSolution`/`CountSolutions`, mirroring `BruteForceSolveStats.IncrementGuesses` on the
`benchmarking-languages` branch. Reverted after measuring.

Caveat: `escargot` is high-variance at these timescales (0.99–3.65 ms with discovery on across
runs). The direction and rough magnitude are stable across repeats; treat single-digit-millisecond
absolutes as indicative.

---

# Deferred discovery, as built

Date: 2026-08-03. Shipped and **on by default**.

`WeakLinkDiscoveryMode` (`Always` / `Never` / `Deferred`) is now a real solver option, with
`Solver.WeakLinkDiscoveryNodeThreshold` alongside it. `Deferred` is the default and works as
proposed: start brute force *without* discovery; if the search exceeds N nodes, throw it away, run
discovery, and start over. Discovery is then only paid for by puzzles that have already proven
expensive without it. `FindSolution`, `CountSolutions` and `TrueCandidates` honour it; the sampling
estimators treat it as `Always`, and so does `CountSolutions` when a `solutionEvent` handler is
attached, because a restart would report the same solutions twice.

The env vars are still there for A/B sweeps without a rebuild — `SUDOKU_WEAK_LINK_DISCOVERY=` and
`SUDOKU_WEAK_LINK_DEFER_NODES=`. The old `SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY=1` still means
`Never`.

## The headline: it is a latency/throughput dial, not a free win

The threshold does not have a value that is best on every axis. Sweeping it over the **iss-tune**
split (280 puzzles; ratios over the 271 above the timer floor, deferred ÷ always, <1 is faster):

| N | corpus total | p10 | **p50** | p90 | p99 | max | cases >1.5× slower |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 250 | **0.961** | 0.088 | 1.041 | 1.164 | 1.40 | 1.4 | 0 |
| 500 | 0.969 | 0.085 | 1.048 | 1.265 | 1.64 | 1.8 | 9 |
| 1000 | 0.968 | 0.088 | 1.014 | 1.388 | 2.01 | 2.4 | 16 |
| **2000** | 0.994 | 0.082 | **0.639** | 1.587 | 2.16 | 2.8 | 35 |
| 5000 | 1.063 | 0.078 | 0.450 | 2.058 | 3.64 | 5.0 | 55 |
| 10000 | 1.186 | 0.079 | 0.439 | 2.604 | 5.56 | 7.3 | 60 |
| 25000 | 1.384 | 0.080 | 0.411 | 4.148 | 10.5 | 14.2 | 60 |
| 100000 | 2.523 | 0.078 | 0.427 | 12.96 | 40.8 | 53.4 | 65 |

**Total time and median latency want opposite thresholds.** Raising N converts more puzzles from
"pays a wasted prefix" to "never pays for discovery at all", which is why p50 falls from 1.04 to
0.41 — but the puzzles that still need discovery pay a prefix proportional to N, and a handful of
them are expensive per node, which is what destroys the total at the bottom of the table.

Two things that do *not* vary: **p10 is ~0.08 at every threshold**, so the biggest wins are already
captured by a budget of 250 nodes — those are puzzles brute force cracks almost immediately while
discovery grinds. And the wasted prefix is bounded by construction, so the downside is capped in a
way that `Never` is not.

**N = 2000 is the shipped default**: the median puzzle gets ~1.5× faster while total corpus time is
unchanged (0.994×), and no puzzle is worse than 2.8×. N=250 has a slightly better total but taxes
the median puzzle by 4% to get it, which is the wrong trade for a UI that runs this on every edit.

## Confirmation on the holdout

`iss-holdout` (118 puzzles) was not looked at until the threshold had been chosen. Same shape:

| N | corpus total | p10 | p50 | p90 | max | result mismatches |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1000 | 0.984 | 0.108 | 0.830 | 1.382 | 2.14 | **0** |
| 2000 | 1.003 | 0.103 | 0.680 | 1.609 | 4.72 | **0** |
| 5000 | 1.019 | 0.087 | 0.527 | 2.033 | 3.85 | **0** |

At the shipped N=2000: total neutral, median puzzle 1.47× faster. The 4.72× worst case is
`3dv4CIY5Mk8`, 6.5 → 30.7 ms; the worst *absolute* regression is `-Uj9xZPyzM4` at 1271 → 1319 ms.

## The 28-case corpus is flat

Excluding `tc-blank-nonconsecutive` — whose search is randomised, so its wall time is not
comparable (see `HANDOFF.md` §4; it swings 7700–10652 ms across thresholds with no trend) — the
curated corpus total is **0.99–1.00× at every threshold up to 25000**. `kropki-search` and
`variant-orbit`, the two cases that depend on discovery, are protected: both stay within 1.08×,
while `variant-equalsums` (0.05×), `littlekiller-8` (0.10×), `escargot` (0.13×) and
`variant-killerblister` (0.15×) collect the wins that motivated this work.

## It fixes one of the known pathological outliers

`Wb5YT1b-U9Q`, listed in `HANDOFF.md` Priority 4 as 308 ms against ISS's 7.4 ms, goes to **21 ms**
— a 14× improvement that closes most of that gap without anyone diagnosing the puzzle. Deferral
appears to be the explanation for at least part of that outlier class: the puzzle was paying for
discovery it did not need.

## Two things worth knowing before touching this

**1. The conflict-score leak was a real bug, and a 5× one.** Search-tree clones share
`conflictScores` with the root *by reference*, so the abandoned attempt's branch-ordering learning
leaked into the retry, making the retry explore a different tree than an undeferred search would.
This is not a wrong-answer bug — the scores only choose which cell to branch on — but on
`variant-orbit` it cost 91 ms against a 17 ms baseline, a **431% regression** that vanished to 1.08×
once `SnapshotConflictState`/`RestoreConflictState` rolled it back. Rolling back is what buys the
property that makes deferral easy to reason about: **a deferred retry is exactly an undeferred
search**, so the only cost of deferral is its bounded prefix.

**2. `Never` cannot finish the ISS tune split at all.** The attempt to measure a `never` reference
arm was killed after 8 minutes on a single puzzle. The 28-case corpus's 2.3× figure understates
this badly: real CTC puzzles exist that are effectively unsolvable without discovery. That is the
strongest argument for the deferral shape over any *predictive* on/off classifier — a
misclassification under a predictor hangs, whereas a blown budget costs a bounded prefix.

## Costs and limits, stated plainly

- **Pure-cost puzzles pay twice.** `killer-innie` and `blank6` gain nothing from discovery but still
  exceed the budget, so they pay the prefix and then the discovery anyway. The damage is
  proportional to N ÷ total nodes, so it is negligible for big searches (`blank6`, 5M nodes) and
  worst for searches only slightly larger than the budget.
- **A restart discards partial work**, so `CountSolutions` recounts from zero. That is why
  `progressEvent` is withheld from the budgeted attempt (its count would visibly rewind) and why
  deferral opts out entirely when `solutionEvent` is attached.
- **Not bit-identical to `Always` for `FindSolution`**, which may now return a different — still
  valid, still deterministic — solution on multi-solution boards.
- **Options 2 and 4 above are still open** and are complementary, not superseded. Aborting an
  unproductive discovery pass early would cut the cost side; fixing the cases where discovery
  *increases* node count would cut it further.

## Reproducing the sweep

```bash
for n in 250 500 1000 2000 5000 10000 25000 100000; do
  SUDOKU_WEAK_LINK_DISCOVERY=deferred SUDOKU_WEAK_LINK_DEFER_NODES=$n \
    caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- \
      benchmarks/corpus-iss.json --filter iss-tune --iterations 3 --save /tmp/tune-$n.json
done
```

Compare against a `SUDOKU_WEAK_LINK_DISCOVERY=always` run of the same command. Score the *ratio
distribution*, not just `total min ms` — the total alone points at N=250 and hides the fact that it
makes the typical puzzle slower.
