# Weak-link representation: grouped targets for the brute-force hot path

Date: 2026-08-04. Idea from a conversation with the repo owner: weak links are stored candidate →
candidate, which is what AIC needs, but the brute-force search only ever *applies* them when a
candidate becomes true — and for that, `candidate → (cell, mask)` is a better shape.

That is now what `SetValue` uses. **ISS corpus (398 puzzles): 13,618 → 12,862 ms, −5.6%.**

## Why the old shape cost something

`SetValue` applied weak links by walking `weakLinks[setCandidateIndex]` and calling `ClearValue`
per target candidate (`SolverModification.cs`). Two measurements say that loop is worth attacking:

1. It is the mechanism by which weak-link *count* becomes per-node *cost*. `killer-cage` pays 64%
   more time for only 16% more nodes when discovery adds 4,132 links — see
   [`branch-ordering.md`](branch-ordering.md).
2. It is the dominant cost of the largest case in the corpus: `tc-blank-nonconsecutive` runs
   978M iterations of it.

## What the probe found before writing any code

Instrumented the loop to count iterations, distinct target cells, and how many targets were actually
still present:

| case | iters/cell | hits/hitCell | **hit rate** |
| --- | ---: | ---: | ---: |
| variant-cloneways | 1.48× | 1.21 | 26.9% |
| est-killer-cage | 1.46× | 1.34 | 43.2% |
| **tc-blank-nonconsecutive** | **1.31×** | 1.10 | 23.4% |
| variant-orbit | 1.23× | 1.09 | 29.0% |
| kropki-search-cap50k | 1.20× | 1.07 | 16.8% |
| killer-cage | 1.19× | 1.12 | 26.2% |
| blank6-cap5M, escargot, vanilla-u17, arrow-search, tc-blank9 | **1.00×** | 1.00 | 5–14% |

Two things worth keeping:

- **Vanilla clusters not at all, and the reason is structural.** Same-cell exclusivity is *not*
  stored as weak links — `SetValue` writes `valueSetMask | valMask` and wipes the cell's other
  candidates directly. So a vanilla candidate's targets are all "same digit, a peer cell", which is
  inherently one target per cell. Clustering only ever comes from constraints that link *different*
  digits across cells (nonconsecutive, kropki, killer, clones) and from discovered links. Those sit
  on top of ~20 unclustered vanilla links, which is what caps the ratio: nonconsecutive adds ~12
  links across 4 cells, giving ≈32 links / 24 cells ≈ 1.33, matching the measured 1.31.
- **The hit rate, not the clustering, is the main lever.** 70–95% of iterations find the target
  candidate already gone. Grouping lets a whole cell be dismissed with one `board[cell] & mask` test
  instead of k early-outs, and *skipping is the common path*.

So the win is three things, only one of which is the clustering the idea was aimed at:

1. one board read + one mask test per cell rather than per candidate (the 70–95% no-op path),
2. no per-iteration `candidateToCellAndValueLookup` read — the cell index is stored inline,
3. per-cell bookkeeping once instead of once per cleared candidate. This is the sleeper: today
   `TrackHiddenSingles` walks `CellToGroupsLookup[cell]` (3+ groups) *per cleared candidate*.

## The design

`weakLinks` stays authoritative. AIC, the fish and wing searches and `IsWeakLink` all need
candidate-to-candidate adjacency with binary search and set intersection, which the grouped form
cannot answer. The grouped form is a **derived, compiled** view in CSR layout:

```
wlGroupedOffsets[c] .. wlGroupedOffsets[c+1]   →   spans of wlGroupedCells / wlGroupedMasks
```

one entry per distinct target *cell*, with every targeted value in that cell OR-ed into one mask.
Building it is two linear passes; the lists are already sorted by candidate index and
`candIndex = cell * MAX_VALUE + (v-1)`, so entries sharing a cell are already adjacent.

`ClearMaskFromCell` applies one entry. It is equivalent to per-candidate `ClearValue` because
`TrackHiddenSingles` is composable: it iterates the bits of `oldMask & ~newMask` and decrements each
value's per-group count independently, so one call with a multi-bit diff lands in the same state as
one call per bit.

### Why compile-once is safe

Weak links are frozen for the duration of a brute-force search. The only mutator reachable from a
search is `DiscoverWeakLinks`, which runs at the root before the search loop starts — and
`StepLogic`'s "re-evaluate weak links" block sits *after* its `isBruteForcing` early return, so
constraint `InitLinks` (where every constraint's `AddWeakLink` call lives, all 21 of them) never runs
mid-search.

Rather than rely on that, the invariant is enforced locally: **non-null implies it matches these
lists.** `AddWeakLink` nulls the table, `DiscoverWeakLinks` nulls it before cloning its scratch
solver, and `CompileGroupedWeakLinks` early-outs when it is already non-null. Clones inherit it by
reference — including clones that *copied* the lists, since `CloneWeakLinks` copies contents so the
table still describes them, and a later mutation on the copy nulls only the copy's field.

That early-out matters more than it looks: without it the estimators rebuilt the table once per
sample, because `EstimateSolutions` runs a nested `CountSolutions` per descent (2 attempts × 200
samples = 400 rebuilds). That doubled `est-escargot-6clue`'s allocation, 45 → 91 MB, and its time,
17 → 30 ms. With the early-out both are back to baseline.

## Results

Paired A/B via `git stash push -- SudokuSolver/`, **15 iterations** — `killer-innie` and
`littlekiller-10` both read as large regressions at 3 iterations (128 ms and 93 ms) and are fine at
15, which is the documented variance trap in `handoff.md`, hit again:

| case | baseline | grouped | ratio |
| --- | ---: | ---: | ---: |
| kropki-search-cap50k | 2,280.8 ms | 2,014.1 | **0.88×** |
| killer-innie | 57.33 | 52.02 | 0.91× |
| arrow-search | 18.96 | 17.35 | 0.92× |
| littlekiller-10 | 63.78 | 61.89 | 0.97× |

At 3 iterations over the whole corpus, the cases that move most:

| case | baseline | grouped | ratio |
| --- | ---: | ---: | ---: |
| **ISS corpus, 398 puzzles** | **13,618 ms** | **12,862** | **0.944×** |
| tc-blank-nonconsecutive | 10,100.2 | 9,209.1 | 0.912× |
| blank6-cap5M | 2,374.6 | 2,278.4 | 0.959× |
| 28-case corpus total | 16,894.1 | 15,922.5 | 0.942× |

All 121 tests, all 28 corpus cases and all 398 ISS puzzles agree exactly.

## The cost, and what is left

The table is **additional** memory, not a replacement: ~116 KB for a 9×9 (`total` entries × 8 bytes),
shared by reference across every clone in a search. That shows up as roughly +0.12 MB per compiled
search — `tc-escargot` goes 0.03 → 0.15 MB, `kropki` 0.47 → 0.73 MB (two compiles, because deferral
recompiles after discovery). Modest next to the 729-`List` `CloneWeakLinks` that the same path
already pays, but it is a real per-call cost for a browser doing a true-candidates scan on every
edit, and pooling the two arrays would remove most of it.

Not done, and deliberately:

- **`EvaluateSetValue` still uses the list form.** It is the non-brute-force path and not hot.
- **The dense whole-board variant was not tried.** 729 × 81 × 4 = 236 KB is affordable and the ANDs
  would vectorise, but the AND is not the cost — the *bookkeeping* is, and a dense form pays the
  "did this cell become empty / become a single / need re-enqueueing" check on all 81 cells instead
  of only the ones that changed. The sparse grouped form gets the same clustering win with
  bookkeeping proportional to cells actually touched.
- **Vanilla gains only items 2 and 3** above, since it has no clustering at all. It still measures
  as a win (`blank6-cap5M` 0.959×, `arrow-search` 0.92×), which is what says the no-op path and the
  bookkeeping — not the clustering — are carrying this.
