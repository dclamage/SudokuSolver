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

## 5. Still open: the dots

`1HuNjcLWlPE` also carries three white and three black kropki dots, and
`OrthogonalValueConstraint` — the base class for kropki, difference, ratio and XV — has the same
defect whispers had, deliberately:

```csharp
public override bool WantsBruteForcePropagation => false;
...
if (isBruteForcing) { return LogicResult.None; }
```

with a comment explaining that weak links enforce it. Its `StepLogic` already contains the arc
consistency ("remove candidates which would remove all values from any orthogonal cells") plus a
multi-candidate cell-forcing pass.

A probe that simply deletes both guards takes **`1HuNjcLWlPE` from 18,914 to 6,350 ms — 3× — and
allocation from 1.0 GB to 7.8 GB.** So the deduction is worth having and the current implementation
cannot be the one that ships. What it needs, in order:

1. Hoist the `GetRelatedConstraints(solver).SelectMany(x => x.Markers.Keys).ToHashSet()` at the top
   of `StepLogic` into a readonly field. It is constant after construction and is currently rebuilt
   on every call.
2. Give it a queue-driven, allocation-free arc-consistency path for brute force, keeping the
   full-grid group scan (the third phase, over `sudokuSolver.Groups`) for the logical arm only.
3. `CellIndicesForPropagationQueue` needs care here in a way it did not for whispers: with a negative
   constraint every orthogonal adjacency is constrained, so the constraint watches the whole grid.

Expect roughly 3× on `1HuNjcLWlPE` and, since kropki dots are common, a visible corpus effect.

After that, the remaining lever on `1HuNjcLWlPE` is ISS's **required-value exclusion for a binary
pair** — the `_exclusionsCellsForRequiredValues` branch of the same `BinaryConstraint`, which is the
pairwise analogue of what the renban change shipped: if every legal assignment of a pair uses value
`v`, then no cell seeing both can be `v`. That was deliberately *not* built here, because the
measured prize was arc consistency alone and mixing the two would have made neither attributable.
