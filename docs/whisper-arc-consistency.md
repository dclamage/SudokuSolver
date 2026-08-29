# The Whisper propagation gap: pairwise arc consistency

Date: 2026-08-19. Closes the Whisper half of the top `HANDOFF.md` item, scoped in
[`renban-required-values.md`](renban-required-values.md) §6. Read that first: it establishes *why*
these puzzles were slow, and this is the second of the two fixes it predicted.

**Result: `OqyXKDOhfDA` goes from 19,566 ms to 116 ms — 169× — and `1HuNjcLWlPE` finishes for the
first time.** It had never completed a count; it had been left running for 28 minutes. It now takes
18.9 s and returns the right answer. The 398-puzzle ISS corpus moves to **geomean 0.771** with
**0 mismatches**, and the holdout gains more than the tune split.

---

## 1. What was missing

`WhispersConstraint.StepLogic` returned `LogicResult.None` unconditionally, with a comment saying the
weak links from `InitLinks` enforce the constraint. They do enforce it — but **weak links only fire
on `SetValue`**, and the brute-force propagation loop runs nothing but singles and each constraint's
`StepLogic`. So a whisper contributed no propagation whatsoever at candidate level.

The deduction it was missing is ISS's `BinaryConstraint.enforceConsistency`
(`js/solver/handlers.js:840`), which is two table lookups:

```js
grid[cell0] = v0 & tables[1][v1];
grid[cell1] = v1 & tables[0][v0];
```

`tables[i][mask]` is the union of values compatible with *any* value in `mask`. A value survives in
one cell only if some remaining value in its neighbour supports it — plain arc consistency, and it
needs neither cell to be solved.

The comment was half right, and the tests say which half: where a cell *is* solved, the weak links
already do exactly this at `SetValue` time. The gap was only ever the unsolved case — which is every
node of a search.

## 2. What shipped

`WhispersConstraint` now precomputes `compatibleMask[v - 1]` — the values a neighbour may take when
this cell is `v` — in its constructors, and `StepLogic` runs one forward and one backward sweep over
the adjacent pairs, applying eliminations as it goes. Two deliberate choices:

- **A per-value table, not ISS's per-mask table.** ISS indexes `tables[i]` by a candidate *bitmask*,
  which is 2^maxValue entries. `SupportedByAnyOf` ORs at most `maxValue` per-value masks instead,
  which is cheaper for 9 values and needs no 2^16 allocation for large grids.
- **Forward then backward in one call**, which is what ISS's prefix/suffix pass buys: the whole line
  reaches a fixpoint in a single `StepLogic` rather than one elimination per invocation.

`compatibleMask` and `cellIndices` are readonly and built in the constructor, which matters because
a constraint instance is shared by reference across every search-tree clone *and* every thread.
`CellIndicesForPropagationQueue` is overridden to the line's cells: `Group` is null for a whisper —
it does not make its cells distinct — so the base implementation would have put it in the always-run
bucket rather than the queue.

## 3. Measurements

Paired A/B against `11a1d5a`, one build per arm.

### The outliers

| case | before | after | ISS | |
| --- | ---: | ---: | ---: | --- |
| `OqyXKDOhfDA` | 19,566 ms | **115.6** | 410.9 | **169×**, and 3.6× faster than ISS |
| `1HuNjcLWlPE` | **did not finish in 28 min** | **18,914** | 71.8 | ≥89×; still 263× off ISS |
| `h-ymyScJa2s` | 26.6 | 26.6 | 105.3 | no whispers |
| `blPgSzctUMg` | 4.1 | 4.1 | 14.0 | no whispers |

`OqyXKDOhfDA` was 42,672,862 nodes at ~1 µs each. The measured prize in
[`renban-required-values.md`](renban-required-values.md) §6 was 10×, from a generic full-grid
`List<int>` arc-consistency probe; the targeted, queue-driven version is 169×. The difference is not
extra deductions — it is the same deduction without the full-grid rescan.

**`1HuNjcLWlPE` is the headline and also the remaining problem.** It is whispers, renban and dots
with no givens at all, and it is the last of the five pathological outliers still far off ISS. It is
now a *measurable* puzzle rather than an unbounded one, which is the precondition for fixing it (§5).

### ISS corpus, all 398, uncapped counts, **0 mismatches**

| split | n | total | p10 | p50 | p90 | max | geomean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| all | 398 | 12,548 → 12,401 (0.988) | 0.26 | 1.00 | 1.16 | 2.94 | **0.771** |
| `iss-tune` | 280 | 7,707 → 7,694 (0.998) | 0.33 | 1.00 | 1.22 | 2.94 | 0.799 |
| `iss-holdout` | 118 | 4,842 → 4,707 (0.972) | 0.19 | 1.00 | 1.11 | 1.69 | **0.708** |

Allocation falls 489 → 454 MB.

**The holdout gains more than the tune split** (0.708 vs 0.799), and the worst regression anywhere is
2.94× against renban's 5.7×. That is the cleanest generalisation signal any change in this file has
produced, and it is what you would expect from a deduction that is a *property of the constraint*
rather than a tuned threshold.

Top wins: `iss-7xiLo-oAha8` **0.006×** (108 ms → 0.60), `iss-WbDuklPfeSQ` 0.027×,
`iss-1hECaKWWVX0` 0.027×, `iss-enPVjJSFTIU` 0.039×, `iss-mG5gaW1lrgE` 0.042×.

Worst: `iss-g7YE5xR5-G4` 1.45 → 4.26 ms (2.94×) and, by absolute time, `iss-NsiGrBiISOs`
689 → 1,234 ms (1.79×). Same second-order mechanism as always — stronger propagation changes
candidate counts, which changes the `score/count` branch ranking; see
[`branch-ordering.md`](branch-ordering.md).

Worth noting: **`iss-NjpUueqFEHY`, the puzzle that regressed 4.08× under the renban change, improves
978 → 569 ms here.** Two independent propagation additions partially cancelling on the same puzzle is
a reminder not to chase an individual branch-ordering casualty.

### `corpus.json` (3 iterations), 0 FAIL single- and multi-threaded

Flat: 15,641 → 15,613 on the shared 32 cases. Every case moving by more than 5% is either
`killer-innie` (the known high-variance case) or sub-millisecond.

New case **`whisper-zoomout`** (`OqyXKDOhfDA`, 113 ms, expected 1). The corpus had **no whisper case
of any kind**, so a 169× win would have been completely invisible in the default run — the same gap
that hid the sandwich and renban results.

## 4. Tests

`SudokuTests/WhispersTests.cs`. The interesting one is negative: the "cell fixed to a value with no
support" branch in `ApplyKeep` turns out to be *unreachable* through normal play, because `SetValue`
applies the pair's weak links first. Trying to build that case makes the ISS parser reject the puzzle
at the given. The branch stays as a safety net, and the test that replaced it asserts the real
behaviour — a given of 4 on a difference-5 whisper leaves only 9 next door, before any `StepLogic`
runs. That is what the old "weak links enforce it" comment got right.

## 5. The dots: done

`1HuNjcLWlPE` also carries three white and three black kropki dots, and
`OrthogonalValueConstraint` — the base class for kropki, difference, ratio and XV — had the same
defect whispers had, deliberately: `WantsBruteForcePropagation => false` plus an `isBruteForcing`
short-circuit, with a comment explaining that weak links enforce it.

Fixed, and it was worth more than the 3x the probe suggested.

| | before | after | |
| --- | ---: | ---: | --- |
| `1HuNjcLWlPE` time | 25,143 ms | **91.8 ms** | **274x** |
| `1HuNjcLWlPE` nodes | 15,206,082 | **30,288** | 502x |
| `1HuNjcLWlPE` allocation | 1,022 MB | **4.6 MB** | 223x |
| ISS corpus, geomean of per-case ratios | — | **0.773x (-22.7%)** | 189 better, 14 worse |
| ISS corpus, total | 13,969 ms | 12,974 ms | -7.1% |
| `corpus.json` nodes | 26,830,146 | 17,186,712 | -35.9% |

`1HuNjcLWlPE` is now **1.28x off ISS** (71.8 ms), down from 263x. It is no longer an outlier.

### Why the probe under-predicted by 90x

The earlier probe just deleted the two guards and measured 3x for 7.8 GB of garbage. Three things
separate that from the shipped version, and the allocation was the smallest of them:

1. **The arms are different code.** The logical arm must stop at the first deduction and describe
   it. The brute-force arm has nobody to explain itself to, so it sweeps the whole grid and applies
   everything it finds in one call. The probe inherited the first-match-and-return shape, which is
   the wrong shape for a propagator.
2. **A compiled adjacency table.** `InitLinks` already walks every constrained ordered pair, so it
   now also emits a CSR of (neighbor, clear-values) alongside the weak links. The hot loop never
   touches the `markers` dictionary, never allocates an `AdjacentCells` enumerator, and never
   re-derives which pairs are live. The `overrideMarkers` `HashSet` the probe rebuilt on *every*
   `StepLogic` call is now built once, there.
3. **An exact popcount guard**, below.

### The guard, and why it is exact rather than a heuristic

The deduction fires only when every candidate of the neighbor falls inside one `clearValues` mask.
So a neighbor holding more candidates than the widest such mask cannot yield one, and the pair can
be dismissed on a `popcount` instead of a loop over the cell's candidates. For a negative
constraint — the case that watches the whole grid and therefore dominates cost — those masks are
three values wide, so nearly every pair is dismissed in one instruction.

This took `tc-nc-no-r3c4` from **+21.4% to +2.6%** with **every node count byte-identical**, which is
what makes it a guard rather than a weakening. Same for the other two cost cuts (iterating only
watched cells, hoisting a popcount out of the inner loop): node counts unchanged throughout.

### The pass that was deleted: this is cell forcing, and half of it was redundant

Worth stating plainly, because it reframes an earlier result. `InitLinks` adds a weak link
`(cell0,v0) -> (cell1,v1)` for exactly the bits of `clearValues[v0-1]`, so the table the sweep reads
**is** this constraint's weak-link set — and the deduction is precisely cell forcing over it:
*every candidate of cell1 is weakly linked to `(cell0,v)`, so `(cell0,v)` dies.*

The first implementation also carried the mirror-image pass (fold a mask across cell0's candidates,
clear it from cell1), capped at three candidates. **It was deleted as provably redundant.** Every
marker relation here is symmetric in its two values — difference `v0+k==v1 || v1+k==v0`, ratio
likewise, XV `v0+v1==k` — so `clearValues` is a symmetric relation and the two passes test the same
condition, one from each end. The sweep visits every cell, so it already sees both ends.

Removing it left **all 33 `corpus.json` cases and all 398 ISS puzzles bit-identical on node count**.

**The reframing:** [`cell-forcing-worklist.md`](cell-forcing-worklist.md) measured cell forcing at
0.582x nodes but **2x too costly** — 6.6 us paid per node removed against a 3.35 us node. That
verdict is about the **general scan**, not about the deduction. Here the same rule pays about
**0.02 us per node removed** on `kropki-search-cap50k`, roughly two orders of magnitude cheaper,
because the forcing cell and the target are not searched for: they are four orthogonal neighbors
known at compile time, the intersection is an AND of precomputed 9-bit masks, and there is no
worklist or enqueue bookkeeping because it rides the constraint queue that already exists.

Worth carrying forward: **a deduction that is too expensive to find in general may be cheap inside a
constraint that already knows where to look.**

### What it cost, honestly

Stronger propagation changes candidate counts, which feed `GetLeastCandidateCell`, which changes
branch order — the same two-sided trade-off [`branch-ordering.md`](branch-ordering.md) records for
weak-link discovery. On the ISS corpus **93 puzzles explore fewer nodes and 13 explore more**. The
worst is **`iss-g2PUwXrKogU`: 47,252 -> 124,426 nodes and 2.6x the time.** That is a real regression
and it is not a bug; it is the known cost of reordering. 189 cases better against 14 worse is the
trade being accepted.

Two things that look like regressions and are not. Roughly a dozen `corpus.json` cases showed
10-40% worse timings with **identical node counts** — and a check of the corpus shows only **5 of 36
cases contain an `OrthogonalValueConstraint` at all** (`kropki-search-cap50k`, `nc-4given`,
`tc-blank-nonconsecutive`, `tc-nc-no-r3c4`, `variant-cloneways`). With no such constraint the class
is never instantiated and `StepLogic` is never called, so those numbers are machine noise; two runs
of the *same* baseline build differed by 2.6% on the corpus total. Gating the propagator on
"kropki/difference/ratio/XV present" would be a no-op for the same reason.

The corpus-wide timings above were measured with the redundant second pass still present, so they
are a floor rather than the final figure.

### Still open

`CellIndicesForPropagationQueue` returns every watched cell, which under a negative constraint is
the whole grid. That is honest — the constraint really does watch everything — and it costs nothing
extra per write, since the enqueue is one OR against a per-cell mask. But it does mean the sweep
runs on nearly every propagation step. A dirty-cell hint would let it re-check only pairs touching a
changed cell; `StepLogic` currently receives no such information.

`BruteForcePropagationCost` is still the unmeasured default of 2000. Changing it reorders the
constraint stage and therefore changes node counts, so it wants its own measured commit rather than
a ride-along here.

