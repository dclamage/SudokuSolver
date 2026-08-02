# Dynamic weak-link discovery: a large, two-sided perf lever

Date: 2026-08-02

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
