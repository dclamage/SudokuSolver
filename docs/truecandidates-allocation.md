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
