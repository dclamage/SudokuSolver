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

`tc-blank-nonconsecutive` is the one case with a genuinely broad spread rather than a clean bimodal
split. It is also the only case where the median seed (~11.7 s) is much better than the shipped one
(10.0 s is near the good end, so this corpus flatters the default).

**Do not tune the seed.** The shipped value is the natural counter origin, chosen before any of this
was measured. Picking a seed because it dodges the bad mode on these four cases says nothing about
the puzzles a user will actually open.

### A caveat this fix introduces

Fixing the seed converts a per-call lottery into a per-puzzle constant. A puzzle that lands in the
pathological mode is now stuck there on **every** call instead of one in five. That is a real
regression in the worst case, and it is accepted deliberately: a reproducible bad case can be found
and fixed, whereas an intermittent one cannot even be measured. It does raise the priority of the
work below from "optimisation" to "removing a latency cliff".

## What to try

Now that a baseline reproduces, a replacement can be evaluated — which it could not before.
Reordered by what the corrected data supports:

1. **Detect and escape the bad mode.** This is now the main event, not a nicety. The search already
   knows when it has stopped making coverage progress, so restarting the affected subtree with a
   different choice would cut the tail without touching the 80% of cases that are already fine.
   Bounding it the way `WeakLinkDiscoveryMode.Deferred` bounds its prefix would keep the cost
   self-limiting.
2. **Systematic rotation instead of random** — `bestCellIndices[counter++ % count]` covers the tied
   set evenly rather than in expectation. Cheap to try, and it may simply not reach the bad mode.
3. **A real coverage objective**: pick the branch maximising newly-covered candidates instead of
   sampling proportional to them. Most speculative, and the flat cases suggest the current weighting
   is already adequate when it does not fall over.

Start by finding what distinguishes `tc-escargot-partial`'s two bad seeds from its eight good ones —
with a fixed seed that is now a reproducible experiment.

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
