# True candidates: 11,800x allocation reduction

Date: 2026-08-02

Investigating the 18.8 GB allocated by a single true-candidates call on a blank grid with
non-consecutive. The prior expectation was that the answer might simply be "it does a ton of work"
— non-consecutive is genuinely very constrained, in a way solvers find hard to exploit.

**It does do a ton of work, and that part is legitimate. The allocation was not.** Work and
allocation are independent axes, and the decisive comparison is bytes *per unit of work*:

| operation | work | allocation | bytes/node |
| --- | ---: | ---: | ---: |
| brute force (`kropki-search`) | 1,581,998 guesses | 1.78 MB | **~1.1** |
| true candidates (`tc-blank-nonconsecutive`) | 18,805,867 nodes | ~10 GB | **~537** |

Brute force had been effectively zero-allocation for years. True candidates was allocating ~500x
more per node for the same kind of search.

## Two causes, both structural

Instrumentation over one non-consecutive run: **18.8M nodes, 9.74M solver clones, 21.3M list
allocations, 91.8M list adds**.

1. **Unpooled branch solvers.** `TrueCandidatesInternal` did `solver.Clone(...)` per branch and
   never recycled. `CountSolutions` has used `BruteForceSolverPool` for this since it was written —
   `TrueCandidatesState` simply never got one.
2. **A `List<int>` allocated per node.** The cell-selection loop did `bestCellIndices = []` on
   every node, *and* again each time a better candidate count was found — hence 21.3M allocations
   for 18.8M nodes.

Weak links were already shared rather than cloned (`willRunNonSinglesLogic: false`), so they were
not a factor.

## Fix

Both are mechanical:

- `TrueCandidatesState` now owns a `BruteForceSolverPool`, rents branch solvers, and releases them
  on every path where a branch dies (failed propagation, failed `ClearValue`, failed `SetValue`,
  and solution-found). The pool no-ops `Release` for non-pooled solvers, so a missed release
  degrades to allocation rather than corrupting.
- `bestCellIndices` is hoisted out of the node loop and `Clear()`ed.

Pooling is enabled for **single-threaded** searches only. In the multi-threaded path a solver is
handed to another task and cannot safely be released by this one; that path still clones.

## Result

Allocation, native, same puzzles:

| case | before | after |
| --- | ---: | ---: |
| tc-blank-nonconsecutive | 12,232-18,756 MB | **1.58 MB** |
| tc-blank9 | 19.1-20.6 MB | 1.56 MB |
| tc-escargot-partial | 5.1-319.5 MB | 1.55 MB |
| tc-escargot | 0.11 MB | 1.59 MB |

Allocation is now the pool's one-time preallocation (730 solvers, ~1.5 MB) and essentially nothing
per node — the same profile brute force has. On the worst case that is a **~11,800x** reduction.

Trivial cases pay ~1.5 MB they previously did not, because the pool is sized up front at
`NUM_CANDIDATES + 1` regardless of how little searching follows. That is a fair trade at this size
but could be made lazy.

Speed improved too, on the cases where allocation was not already trivial:

| case | before | after |
| --- | ---: | ---: |
| tc-blank9 | 46-52 ms | **14.7 ms** |
| tc-escargot | 8.5 ms | **1.5 ms** |
| tc-escargot-partial | 17-52 ms | 5.2 ms |

`tc-blank-nonconsecutive` did not clearly improve in wall time (~12 s, against 9.8-18 s before).
Its search is randomized — `TrueCandidatesInternal` picks among equally-good cells with
`RandomNext` — so run-to-run variance is large and swamps the difference. Allocation is the
reliable signal here; timing on that case is not.

> **Update 2026-08-03: the timing is now reliable, and the variance turned out to be the story.**
> See [§ Branch order is worth up to 16x](#branch-order-is-worth-up-to-16x) below. The numbers in
> this section were taken with the old time-seeded generator and are optimistic — see the caveat
> there before comparing anything to them.

## Why this matters more for WASM than native

Natively, 18 GB of gen0 churn is largely absorbed by the GC — which is why this went unnoticed.
WASM is 32-bit with a **2 GiB heap ceiling** and a weaker collector, so the same churn is far more
dangerous there. Post-fix the WASM numbers are healthy: correct results at **1.46 MB**, roughly 3x
native, with no heap pressure.

This also revises the earlier finding that allocation is uncorrelated with the WASM tax
(Spearman -0.04, `wasm-prototype-findings.md`). That measurement covered brute-force cases only,
which barely allocate. It never held for true candidates or the logical solver.

## Still open

The remaining cost on `tc-blank-nonconsecutive` is genuine search: ~18.8M nodes. Non-consecutive is
very constrained but not in a form the solver can exploit cheaply, so a large search is expected.
Reducing it is a propagation/heuristic question, not an allocation one.

Worth noting the logical solver has the same untested smell — `escargot-logical` allocates 284 MB
and `killer-innie-logical` 855 MB. Nothing there has been examined for pooling.

---

# Branch order is a tail risk, not a broad cost

Date: 2026-08-03.

## The determinism fix

`TrueCandidatesInternal` picks among equally-good cells at random, weighted by how many of each
cell's candidates are still uncovered. **That randomisation is deliberate and should stay.** True
candidates is a *coverage* problem — the search runs until every candidate has been seen
`numSolutionsCap` times — and a deterministic DFS produces consecutive solutions that differ only
in their last few assignments, so each one covers almost no new candidates. Randomised branching is
the standard fix for diverse-solution enumeration.

What was accidental was the *entropy source*: `SolverUtility.RandomNext`, a `[ThreadStatic]`
`Random` seeded from a time-seeded global. The stream is now scoped to the invocation
(`SearchRandom`, a counter-based SplitMix64), so single-threaded runs repeat exactly. It stays
counter-based rather than stateful because the multi-threaded search draws from one instance across
tasks; atomic counter advance keeps that race-free, though MT is still not reproducible because
task scheduling decides who gets which draw.

`tc-blank-nonconsecutive` across three separate processes went from **7,700–10,652 ms** to
**9,933 / 9,991 / 10,039 ms** — from a ~40% swing to ~1%.

**Old `tc-*` timings are optimistic and must not be compared to new ones.** Every benchmark
iteration used to be a fresh draw from a wide distribution, and the harness reports `min ms`, so the
published figures are order statistics that fall as iteration count rises. This is why the case
"did not clearly improve" above, and why it made the whole 28-case corpus read 26% slower during
the weak-link deferral work until it was excluded by hand.

## What the fix exposed

With the stream seeded from a constant, sweeping that constant measures what *branch order alone*
costs. Ten seeds, `--iterations 12`, min ms:

| case | seeds 0–9 | verdict |
| --- | --- | --- |
| `tc-escargot` | 0.73–1.02 ms | **insensitive** |
| `tc-blank9` | 13.3–14.4 ms | **insensitive** |
| `tc-escargot-partial` | eight seeds 5.99–6.82; then **76.8** and **381.3** | **heavy tail: 12× and 64×** |
| `tc-blank-nonconsecutive` | 9.1, 9.1, 11.0, 11.3, 12.1, 15.3, 24.1, 28.7 s | broad, **2.9×** |

So the weighted-random tie-break is **not** uniformly weak — it is fine on most puzzles. The problem
is shaped differently: a minority of searches have a **pathological branch-order mode**, and
`tc-escargot-partial` falls into it on 2 of 10 seeds at 12× and 64× the normal cost. For an
interactive UI that is worse than a uniform slowdown would be: under the old time-seeded generator
you rolled this dice on *every* edit, so roughly one edit in five stuttered for no visible reason.

*(Diagnosed and fixed the same day — see the next two sections. The cause is not the tie-break at
all.)*

`tc-blank-nonconsecutive` is the one case with a genuinely broad spread rather than a clean bimodal
split. It is also the only case where the median seed (~11.7 s) is much better than the shipped one
(10.0 s is near the good end, so this corpus flatters the default).

**Do not tune the seed.** The shipped value is the natural counter origin, chosen before any of this
was measured. Picking a seed because it dodges the bad mode on these four cases says nothing about
the puzzles a user will actually open.

### A caveat this fix introduced, since resolved

Fixing the seed converted a per-call lottery into a per-puzzle constant: a puzzle in the
pathological mode would be stuck there on **every** call instead of one in five. That was accepted
deliberately — a reproducible bad case can be found and fixed, an intermittent one cannot even be
measured — and it stood for exactly as long as it took to do that. The directed endgame below
removes the mode itself, so neither the lottery nor the constant applies any more.

## The cause: the DFS is trapped in its first root branch

Instrumenting the coverage timeline settled this immediately. Comparing the node at which the search
first backtracks to the top of the stack (`nFirstShallow`) against the node at which the last 10% of
candidates get covered (`n90`):

| seed | total nodes | solutions | n50 | n90 | nFirstShallow | n100 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 (good) | 5,117 | 898 | 558 | 3,585 | **3,609** | 5,091 |
| 4 (good) | 4,751 | 862 | 485 | 3,134 | **3,059** | 4,725 |
| 3 (bad) | 56,397 | 5,486 | 773 | 54,987 | **54,963** | 56,373 |
| 1 (worst) | 303,137 | 26,446 | 525 | 301,575 | **301,545** | 303,109 |

`nFirstShallow ≈ n90` in every run, to within 1%. And `n100 ≈ total nodes`, so the search stops
almost the moment the last candidate is covered — the entire runtime *is* the coverage hunt.

The mechanism follows: true candidates is a DFS, so it commits to one root branch and **cannot cover
anything requiring a different one until that whole subtree is exhausted**. The worst seed spent
301,545 of 303,137 nodes inside the first branch, enumerating 26,446 solutions that kept re-covering
the same candidates, then finished within 1,600 nodes of escaping. `n50 ≈ 500` for every seed: the
first half is always easy, and all the variance is in how long the first branch takes to exhaust.

Note this is *not* a tie-break quality problem, so the rotation and coverage-objective ideas
originally sketched here would not have fixed it. No branch-ordering rule avoids being stuck in a
subtree that simply lacks the candidates you still need.

## The fix: stop guessing, ask directly

When the search goes `TrueCandidatesStallLimit` consecutive solutions (default 100) without covering
anything new, the undirected search is abandoned and each remaining candidate is settled on purpose:
force it, and count its solutions with a capped `CountSolutions`. A capped count is the answer; an
empty one proves it is not a true candidate. Every pass settles exactly the candidate it targeted,
so this is bounded at one pass per candidate and the result is identical to a full enumeration.

`tc-escargot-partial`, ten seeds, `--iterations 12`:

| seed | before | after |
| ---: | ---: | ---: |
| 0 | 6.68 ms | 6.10 ms |
| 4 | 5.99 ms | 5.87 ms |
| 2 | 6.46 ms | 5.99 ms |
| 5 | 6.82 ms | 6.40 ms |
| 3 | **76.82 ms** | **21.80 ms** |
| 1 | **381.31 ms** | **14.72 ms** |

Worst case **26× faster**, and the seed-to-seed spread collapses from **64× to 3.7×**. Healthy seeds
are untouched, as intended — the threshold sits an order of magnitude above their peak barren run.

### The bug this nearly shipped with

The first version re-entered the shared true-candidates search for each target instead of counting
separately, so it could credit incidental coverage. That **double-counts**: directed subtrees
overlap, since every solution contains a whole grid's worth of candidates, so the same solution is
tallied once per candidate that targeted it.

At a cap of 1 this is invisible — any positive count clamps to the right answer — so the entire
28-case corpus passed, along with all four `tc-*` expected values. It is silently wrong above a cap
of 1, which is the API default. Caught only by `TrueCandidatesMatchesBruteForceOracle`, which checks
every candidate against a forced-cell `CountSolutions` at caps 1 and 8: it reported 8 where the
oracle said 4. **Any future change here needs a cap > 1 in its test**, because the corpus cannot see
this class of error at all.

## What is still open

`tc-blank-nonconsecutive` is a different problem and is barely affected (seed 2: 24.1 → 19.1 s;
seed 5: 28.7 → 29.0 s). Its 8.6–12.6M *invalid* nodes are genuine constraint search, not a coverage
hunt — consistent with the "Still open" note above. Its ~2.9× seed spread is real search-size
variance and remains unexplained.

The stall trigger counts *solutions*, which is a poor proxy where solutions are rare: on
`tc-blank-nonconsecutive` only ~1,300 solutions appear across 18M nodes, so the trigger fires almost
incidentally. It does no harm there, but a node-relative trigger would be better founded.

### Reproducing the sweep

Seed `SearchRandom.counter` from an environment variable (two lines) and run
`--filter tc-escargot --iterations 12` per seed.

**Use ≥10 iterations, not 1.** It is tempting to reason that a fixed seed makes every iteration
follow the identical path, so one iteration suffices — that is wrong, and it produced two entirely
fabricated findings on the first attempt. The harness's single warm-up call is not enough to escape
tiered JIT, so a lone timed iteration measures tier-0 code: `tc-escargot` read 0.84–5.41 ms
(a fake "6.4× spread") against 0.73–1.02 ms measured properly, and `tc-blank9` read 15.9–54.7 ms
(a fake "3.4×") against 13.3–14.4 ms. Both cases are actually flat. Only
`tc-blank-nonconsecutive`, at ~10 s per iteration, was unaffected.
