# The pathological ISS outliers: two different diseases

Date: 2026-08-04. The four outliers from the ISS import, re-measured under the current default
(deferred discovery + grouped weak links) as `handoff.md` required, then split by cause.

**The tail is not fixed.** Deferred discovery rescued `Wb5YT1b-U9Q`; it did nothing for the rest.

| id | ISS | previously | now | vs ISS | constraints |
| --- | ---: | ---: | ---: | ---: | --- |
| `Wb5YT1b-U9Q` | 7.4 ms | 308 → 21 ms | **20.8 ms** | 2.8× | AntiKnight, Renban, Sandwich |
| `blPgSzctUMg` | 14 ms | 887 → 709 ms | **679.9 ms** | 49× | Renban, Sandwich |
| `h-ymyScJa2s` | 105 ms | 9.7 s | **8.5 s** | 81× | Renban |
| `OqyXKDOhfDA` | 411 ms | >10 s | **20.2 s** | 49× | Thermo, Whisper |
| `1HuNjcLWlPE` | **72 ms** | >240 s | **did not finish in 28 min** | **>23,000×** | Whisper, Renban, dots |

Timings at 15 iterations where the case is fast enough; the multi-second ones are single-run.

## The split: search size vs cost per node

A node-rate probe (20 s budget, count nodes) separates the two failure modes cleanly, and they are
opposites:

| id | nodes | nodes/sec | diagnosis |
| --- | ---: | ---: | --- |
| `1HuNjcLWlPE` | 25,333,813 (unfinished) | **1,266,515** | tree is astronomically large |
| `OqyXKDOhfDA` | 21,336,431 | 1,071,583 | tree is large |
| `h-ymyScJa2s` | 7,009,073 | 827,781 | tree is large |
| `blPgSzctUMg` | **2,870** | **3,470** | **280 µs/node** |
| `Wb5YT1b-U9Q` | 178 | 8,227 | high per-node cost |

- **Group A — `1HuNjcLWlPE`, `OqyXKDOhfDA`, `h-ymyScJa2s`: a propagation-strength problem.** The
  node rate is *healthy* (~1M nodes/s); there are simply far too many nodes. ISS solves
  `1HuNjcLWlPE` in 72 ms, so its search must be some four orders of magnitude smaller. This is not
  something a faster inner loop can fix — we are missing deductions ISS makes.
- **Group B — `blPgSzctUMg`, `Wb5YT1b-U9Q`: a cost-per-node problem.** `blPgSzctUMg` explores only
  **2,870 nodes** and still takes 680 ms, at 280 µs/node against ~1 µs/node in group A. It also
  allocated **1,529 MB**, i.e. 533 KB *per node*.

Do not treat these as one list. Group B is an implementation problem in one constraint; group A is an
algorithmic gap.

## Group B is Sandwich, and it is mostly one line

`Combinations` attribution by `[CallerFilePath]`/`[CallerLineNumber]`, one count of `blPgSzctUMg`:

```
SandwichConstraint.cs:544:  4,215,029 combinations     <- 1,469 per node
SandwichConstraint.cs:419:     91,184
SandwichConstraint.cs:315:     17,405
```

All three sites share one shape: enumerate every `C(n,k)` combination of the candidate values, reject
by sum, then permute the survivors. Two costs were removed:

1. **A `List<int>` per combination** → `CombinationsBuffered`. All four Sandwich sites were verified
   non-retaining: `Sum` is eager, `Permutations` copies its input via `ToList()`, and the fourth site
   only reads the combination.
2. **`combination.Sum()`** → a manual loop. `Enumerable.Sum` on a `List<int>` boxes the list's struct
   enumerator, and this ran on all 4.2 million combinations.

Paired A/B at 15 iterations:

| case | before | after | |
| --- | ---: | ---: | --- |
| blPgSzctUMg | 710.99 ms / 1,528.90 MB | 679.92 / **1,228.74** | time −4.4%, **alloc −19.6%** |
| Wb5YT1b-U9Q | 21.13 ms / 35.58 MB | 20.80 / 31.32 | flat, alloc −12.0% |

**The remaining 1,229 MB is `Permutations()`, and that is the next target here.**
`Extensions.Permutations` is a recursive iterator: for a k-element combination it yields k!
permutations and allocates nested iterator state at every level, and `SandwichConstraint` calls it
for every combination that passes the sum filter. The loop's actual goal is only "which values can
appear at which position", which is a bipartite-matching question — enumerating k! placements to
answer it is the wrong algorithm, not merely a slow one.

## Group A: what the constraint statistics say

Renban appears in all five outliers, which looks damning but needed checking — it is in 97 of 398
corpus puzzles, so it is simply common. Median solve time with and without each constraint over the
whole ISS corpus, with a constraint-count control to rule out "these puzzles just have more stuff":

| constraint | n | median with | median without | ratio | #constraints w/ vs w/o |
| --- | ---: | ---: | ---: | ---: | ---: |
| **Renban** | 97 | 5.27 ms | 1.17 | **4.50×** | 16 vs 13 |
| **Whisper** | 134 | 3.46 | 1.20 | **2.89×** | 14 vs 13 |
| Entropic | 13 | 4.28 | 1.64 | 2.61× | 14 vs 13 |
| RegionSumLine | 80 | 2.06 | 1.54 | 1.33× | 11 vs 14 |
| Cage | 129 | 1.62 | 1.77 | 0.92× | 15 vs 13 |
| AntiKnight | 35 | 0.77 | 1.90 | 0.41× | 11 vs 14 |
| **Thermo** | 67 | 0.46 | 2.27 | **0.20×** | 13 vs 14 |

**`Renban` and `Whisper` are the two constraints most associated with slow solves**, at 4.5× and 2.9×
the median, on large samples, and not explained by constraint count. `Thermo` is the mirror image at
0.20× — thermos prune hard, so puzzles containing them are *five times faster* than those without.
Group A's profile (huge trees at healthy node rates) is exactly what weak propagation looks like, and
`1HuNjcLWlPE` is the extreme case: whispers, renban and dots, **no givens at all**.

A methodological note, because it nearly sent this the wrong way: ranking by *slowest 40 absolute*
instead of by median made `RegionSumLine` look like the top suspect at 2.36× enrichment. It is
tail-driven, and those puzzles have *fewer* constraints than average. Median with a count control is
the honest statistic here.

## Suggested next steps

1. **Sandwich's `Permutations()`** — group B's remaining 1.2 GB, and a self-contained fix.
2. **Compare Renban propagation against ISS's** on `1HuNjcLWlPE`. `RenbanConstraint` has
   `NeedsEnforceConstraint => false`, so it contributes nothing during `SetValue` and does all its
   work in a ~155-line `StepLogic`. Whether that is the gap is unmeasured — but the node counts say
   the deductions, not the speed, are what is missing.
3. Leave `Wb5YT1b-U9Q` alone; at 2.8× off ISS it is no longer an outlier.

## Reproducing

The raw `.iss` text is not in the repo and these puzzles exceed the import's ceilings, so fetch by id:

```bash
for id in 1HuNjcLWlPE blPgSzctUMg h-ymyScJa2s OqyXKDOhfDA Wb5YT1b-U9Q; do
  curl -sS -f -o "$id.iss" "https://sigh.github.io/iss-sudoku-index/data/puzzles/$id/puzzle.iss"
done
```

Then build a benchmark corpus of `{"name":…, "iss":<text>, "op":"count"}` cases. `1HuNjcLWlPE` will
not terminate — give it a node budget or a cancellation token rather than waiting.
