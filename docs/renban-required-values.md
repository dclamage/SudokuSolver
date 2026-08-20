# The Renban propagation gap: required-value exclusion

Date: 2026-08-19. Closes the Renban half of `HANDOFF.md` §2's top item ("The Renban/Whisper
propagation gap"). The Whisper half was closed the same day —
[`whisper-arc-consistency.md`](whisper-arc-consistency.md) — from the scoping in §6 here.

**Result: `h-ymyScJa2s` goes from 8,293 ms to 26 ms — 321× — and from 14.0 M search nodes to
essentially none.** ISS takes 105 ms on it, so we now beat ISS by 4×. The whole 398-puzzle ISS
corpus is unchanged at the median (p50 1.00, geomean 0.967) with **0 result mismatches**, and the
32-case corpus is 2.8% faster.

---

## 1. ISS records its own node counts, and they framed the problem

`data/mappings.json` from sigh's index carries a `guesses` and a `solve_ms` field per puzzle. That
is ISS's node count, available for all 1,671 puzzles without running ISS. Against the five
pathological outliers:

| id | ISS guesses | ISS ms | our nodes (before) | our ms (before) |
| --- | ---: | ---: | ---: | ---: |
| `1HuNjcLWlPE` | 2,436 | 71.8 | >25 M | did not finish |
| `OqyXKDOhfDA` | 3,936 | 410.9 | 42,672,862 | 19,054 |
| `h-ymyScJa2s` | 5,091 | 105.3 | 14,018,146 | 8,293 |
| `blPgSzctUMg` | 101 | 14.0 | ~3,800 | 26 |
| `Wb5YT1b-U9Q` | 26 | 7.4 | ~180 | 3.7 |

This sharpens the group-A diagnosis in [`pathological-outliers.md`](pathological-outliers.md) into
an exchange rate: **ISS spends ~29 µs per node against our ~1 µs, and explores 2,700× fewer nodes.**
It is not that ISS is a faster solver; it is 30× *slower* per node and buys four orders of magnitude
of tree with that. We were at the wrong point on that curve for line puzzles.

## 2. What our brute-force loop actually propagates

`StepBruteForceLogic` runs **naked singles, hidden singles, and each constraint's `StepLogic`** —
and nothing else. Everything after that in `Solver.StepLogic` (tuples, pointing, fishes, wings,
AIC, contradictions) is behind `if (isBruteForcing) return LogicResult.None;`.

That is a defensible design, but it interacts badly with how the line constraints are written.
`WhispersConstraint.StepLogic` returns `None` unconditionally and `OrthogonalValueConstraint`
short-circuits on `isBruteForcing` — both are "enforced entirely by weak links". But **weak links
only fire on `SetValue`**: they give no candidate-level (arc-consistency) propagation at all. So on
a puzzle made of whispers, renban and dots, the brute-force arm was running on singles alone.

`ThermometerConstraint`, by contrast, has a real min/max `StepLogic` that runs at every node — which
is exactly why the corpus statistic in [`pathological-outliers.md`](pathological-outliers.md) has
`Thermo` at **0.20×** while `Renban` is 4.50× and `Whisper` 2.89×. The constraint statistic was
measuring which constraints have brute-force propagation.

## 3. Three candidate fixes, measured on `h-ymyScJa2s`

All three were probed with a temporary `ProbeCounters.cs` plus a node counter, per the
`HANDOFF.md` §4 technique. Baseline: 8,426 ms, 14,018,146 nodes.

| arm | ms | nodes | verdict |
| --- | ---: | ---: | --- |
| baseline | 8,426 | 14,018,146 | |
| generic weak-link arc consistency (`FindDirectCellForcing` in the BF loop) | 16,741 | 3,765,626 | 3.7× fewer nodes, 2× slower |
| `FastAdvancedStrategies` in the BF loop | 226 | 4,000 | 37× faster |
| — pointing only | 175 | | |
| — triples only | 103 | | 89.8 MB allocated |
| — plus `WEAK_LINK_DISCOVERY=always` | 69 | **0** | solved by propagation alone |
| **renban required-value exclusion** | **26** | ~0 | shipped |

### 3a. Generic arc consistency is real but not the lever

`FindDirectCellForcing` already implements exactly ISS's binary arc consistency, generically over
weak links: for each cell, intersect the weak-link sets of all its remaining candidates and
eliminate the intersection. Turning it on inside the brute-force loop cut nodes 3.7× — so the
deduction is genuinely missing — but the implementation is a full-grid scan with `List<int>`
intersections, so it cost more than it bought. Worth remembering for whispers (§6).

### 3b. `FastAdvancedStrategies` wins enormously and must not be enabled

Enabling the existing `doAdvancedStrategies` arm during brute force takes `h-ymyScJa2s` from 8,426
to 226 ms, and with discovery forced on it solves **without searching at all**. It is also a clear
loss everywhere else:

| corpus | total | p50 | p90 | geomean |
| --- | ---: | ---: | ---: | ---: |
| `iss-tune`, all four strategies | 3.44× | 2.24× | 6.32× | 2.185 |
| `iss-tune`, pointing only | 14.9× | 2.67× | 5.99× | 2.585 |
| 32-case corpus, all four | 6.1× | | | |

Two things are worth carrying from this:

- **Pointing alone is worse than pointing plus everything** (geomean 2.59 vs 2.19), and one puzzle
  regressed **345×** under pointing-only. The strategies are not additive: each changes candidate
  counts, which changes the `score/count` branch ranking. Do not tune this by picking a subset.
- **Deferral does not rescue it.** Simulating the `WeakLinkDiscoveryMode.Deferred` shape — cheap
  budgeted attempt, restart with advanced strategies if the budget blows — gives a **3.1× loss at
  every threshold from 5 ms to 800 ms**, because the slow puzzles do not uniformly benefit: of the
  14 `iss-tune` puzzles over 100 ms, 8 improve (best 0.03×) and 6 get worse (worst 20.9×). p50 stays
  at exactly 1.00, so the shape is right and the payload is wrong.

  Note the measurement caveat, though: **`corpus-iss.json` membership is wall-clock gated, so the
  puzzles escalation exists for are excluded from it by construction.** `h-ymyScJa2s` (8.3 s) and
  `1HuNjcLWlPE` (>28 min) are not in the corpus. The 3.1× is honest about the corpus and silent
  about the tail.

### 3c. The shipped fix: what ISS actually does for renban

ISS builds a renban as a single `BinaryPairwise` handler with `enableHiddenSingles()`, keyed on
`|a−b| < numCells && a !== b` (`sudoku_builder.js:657`, `handlers.js:927`). Beyond pairwise
consistency it precomputes a `_validCombinationInfoTable`: for the union of the line's candidates it
gives both the **valid** values (union over all legal n-subsets) and the **required** values
(intersection over them). The required values then drive two deductions — `exposeHiddenSingles` and
`enforceRequiredValueExclusions`.

We already had the valid-values half: `RenbanConstraint.StepLogic` computes the union of the
still-possible range masks and intersects each cell with it, which for a renban is the same set and
in fact slightly stronger, since `IsRangeMaskValid` also requires every cell to intersect the range.

**We had no required-values half.** For a renban the required values are the *intersection* of the
still-valid range masks — one extra `&=` in a loop we were already running — and once you have them:

- a required value with no cell left on the line is a contradiction;
- with exactly one, that cell takes it (a hidden single argued from the sequence, not from a house);
- with two or more, every cell that sees all of them can be eliminated.

The third case is the one that mattered. `Solver.FindHiddenSingle` already reached the second case
via `group.FromConstraint.CellsMustContain`, but only when the group's candidate count was already 1.

`RenbanConstraint.ApplyRequiredValues` / `EliminateSeenByAll` implement this. It runs in both arms,
is allocation-free unless it finds something, and rides the existing propagation queue (renban
declares its cells via `Group`, so `CellIndicesForPropagationQueue` only re-runs it when a line cell
changes).

## 4. Measurements

Paired A/B via `git stash push -- SudokuSolver/`, one build per arm.

### The outliers (3 iterations)

| case | before | after | ISS | |
| --- | ---: | ---: | ---: | --- |
| `h-ymyScJa2s` | 8,292.8 ms / 79.4 MB | **25.8 / 2.7** | 105.3 | **321×**, and 4× faster than ISS |
| `blPgSzctUMg` | 26.1 / 47.2 | **4.1 / 3.8** | 14.0 | 6.4×, and 3.4× faster than ISS |
| `Wb5YT1b-U9Q` | 3.7 / 1.06 | **3.3 / 0.73** | 7.4 | |
| `OqyXKDOhfDA` | 19,054 | 19,054 | 410.9 | unchanged — no renban (§6) |
| `1HuNjcLWlPE` | did not finish | did not finish | 71.8 | whisper-dominated (§6) |

### ISS corpus, all 398, uncapped counts, **0 mismatches**

| split | n | total | p10 | p50 | p90 | max | geomean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| all | 398 | 11,749 → 12,603 (1.073) | 0.80 | **1.00** | 1.21 | 5.7 | **0.967** |
| `iss-tune` | 280 | 7,444 → 7,727 (1.038) | 0.79 | 0.99 | 1.20 | 5.35 | 0.956 |
| `iss-holdout` | 118 | 4,305 → 4,876 (1.133) | 0.82 | 1.00 | 1.24 | 5.66 | 0.995 |

Allocation is flat: 465 → 489 MB across the corpus.

**The total is worse while the distribution is better, and that is down to one puzzle per split.**
Excluding them, `iss-tune` is **0.936×** and `iss-holdout` is **0.995×**. Score the distribution, as
`HANDOFF.md` §4 requires; but the two regressions are real and are recorded in §5.

Biggest wins: `iss-ftx4AbNRU84` 0.06×, `iss-blPgSzctUMg` 0.10×, `iss-sWzize3UkqE` 0.12×,
`iss-Ks9cvaX91W4` 0.23×, `iss-vFE7SlSNnGE` 0.38×.

### 32-case corpus (3 iterations), 0 FAIL

Total 16,067.7 → 15,610.5 on the same 31 cases, **−2.8%**. `variant-renban-sky` 113.9 → 71.1
(0.62×), `renban-sky-logical` 695.6 → 513.9 (0.74×) with its `logical` score unchanged at 81, so no
expectation re-baseline was needed. Multi-threaded run also 0 FAIL.

New case **`renban-vivian`** (`h-ymyScJa2s`, 26 ms, expected 1) — the corpus had no real renban
count case, so this whole change would have been invisible in the run people make by habit. Same
lesson as the sandwich work.

## 5. The two regressions, and what they are

| id | before | after | nodes before → after | cause |
| --- | ---: | ---: | --- | --- |
| `iss-NjpUueqFEHY` | 241 ms | 985 ms (4.08×) | 398,182 → **1,784,794** | branch ordering |
| `iss--Uj9xZPyzM4` | 1,314 ms | 1,898 ms (1.45×) | 1,374,236 → **1,136,766** | cost per node |

They are opposite failures and neither is a bug in the deduction:

- **`NjpUueqFEHY` explores 4.5× *more* nodes with strictly stronger propagation.** This is the
  second-order effect already documented in [`branch-ordering.md`](branch-ordering.md): `SetValue`
  propagates along weak links, so changing candidate counts changes the `score/count` ranking that
  picks the branch cell. Stronger propagation is not monotone in tree size. Do not hunt for a bug.
- **`Uj9xZPyzM4` gets 17% fewer nodes and spends 45% more time.** The extra cost is *not* the
  81-cell scan in `EliminateSeenByAll`: capping the number of holders it will attempt (the
  `FastFindPointing` trick of only handling 2–3) changed the time by under 2% at cap 3, and at cap 2
  cost `NjpUueqFEHY` **13.6 s**. The cost is that returning `Changed` more often means more
  propagation rounds per node, which re-runs every constraint. If this is ever worth chasing, that
  is where to look — not at the scan.

## 6. The Whisper half — **done the same day**, see [`whisper-arc-consistency.md`](whisper-arc-consistency.md)

**`OqyXKDOhfDA` 19,566 → 116 ms (169×) and `1HuNjcLWlPE` finishes for the first time** (18.9 s,
correct answer, having previously not completed in 28 minutes). ISS corpus geomean 0.771, holdout
0.708, 0 mismatches. The prediction below was right about the shape and low by 17× on the size: the
10× came from a generic full-grid probe, and the targeted queue-driven version is 169×. What remains
open is the *dots* (`OrthogonalValueConstraint`), which has the identical defect — a probe gives 3×
on `1HuNjcLWlPE` — and required-value exclusion for binary pairs.

The original scoping is kept below because it is what the measurement said before the work.

### The scoping, as written

`OqyXKDOhfDA` (Thermo + twelve 2-cell whispers, 3 givens) is **untouched** by this change: 19,054 ms
and 42,672,862 nodes against ISS's 410.9 ms and 3,936 guesses. `1HuNjcLWlPE` (whispers, renban and
dots, **no givens**) still does not finish.

The measurement that says what to build is already in §3a: the generic weak-link arc-consistency
probe takes `OqyXKDOhfDA` from **19,054 ms / 42.7 M nodes to 1,913 ms / 448 k nodes** — 95× fewer
nodes, 10× faster — even in its slow full-grid `List<int>` form. That is the whole prize, and it is
the same deduction ISS gets from `BinaryConstraint.enforceConsistency`
(`handlers.js:840`): `grid[cell0] &= tables[1][grid[cell1]]`, a two-table lookup per pair.

So the shape of the fix is clear and contained:

1. Give `WhispersConstraint` a real `StepLogic` doing pairwise arc consistency on adjacent cells via
   a memoized `mask → allowed mask` table per difference, the way ISS keys its binary tables. Drop
   `OrthogonalValueConstraint`'s `isBruteForcing` short-circuit at the same time — its existing
   "remove candidates which would empty an orthogonal cell" loop *is* this deduction, deliberately
   disabled during brute force on the (wrong) grounds that weak links cover it.
2. Then add ISS's required-value exclusion for a binary pair — the `_exclusionsCellsForRequiredValues`
   branch of the same handler — which is the pairwise analogue of what §3c shipped for renban.

Expect this to be worth roughly 10× on the whisper outliers, and note that `Whisper` appears in 134
of 398 corpus puzzles at a 2.89× median, so the corpus-wide effect should be much more visible than
renban's was.

## 7. Reproducing

```bash
for id in h-ymyScJa2s OqyXKDOhfDA blPgSzctUMg Wb5YT1b-U9Q 1HuNjcLWlPE; do
  curl -sS -f -o "$id.iss" "https://sigh.github.io/iss-sudoku-index/data/puzzles/$id/puzzle.iss"
done
curl -sS -o iss-index.json https://sigh.github.io/iss-sudoku-index/data/mappings.json  # guesses/solve_ms
```

Build a corpus of `{"name":…, "iss":<text>, "op":"count", "expected":1}` cases. `1HuNjcLWlPE` will
not terminate. `h-ymyScJa2s` is now in `benchmarks/corpus.json` as `renban-vivian`.
