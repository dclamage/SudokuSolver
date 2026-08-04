# Logical-solver allocation: where it goes

Date: 2026-08-02

The logical solver allocates 100–1000× more than brute force on the same puzzle — `killer-innie`
takes 1.76 MB to count all solutions and **902 MB** to logic-solve. That is the largest demonstrated
memory problem for the browser target, which has a 2 GiB heap and a weaker collector. This is the
attribution; only part of it is fixed.

## Not AIC, and not per-pass overhead

Counter instrumentation first ruled out the obvious suspects:

| case | ConsolidateBoard passes | StepLogic calls | **FindAIC calls** | alloc |
| --- | ---: | ---: | ---: | ---: |
| killer-innie-logical | 1,368 | 1,532 | **2** | 902 MB |
| arrow-search-logical | 1,432 | 1,546 | **2** | 965 MB |
| escargot-logical | 426 | 1,038 | **2** | 284 MB |
| renban-sky-logical | 4 | 220 | **2** | 570 MB |

AIC runs twice. It is not the allocator, despite being the most complex stage. The cost is spread
across `StepLogic` calls — ~589 KB per call on `killer-innie`, and ~2.6 MB per call on `renban-sky`.

## Bisecting by logic stage

`Solver` exposes `Disable*` properties per technique, which makes this directly measurable. Turning
one stage off at a time, `ConsolidateBoard`:

| config | killer-innie | renban-sky |
| --- | ---: | ---: |
| baseline (all on) | 903.8 MB / 710 ms | 567.7 MB / 870 ms |
| −Contradictions | 902.1 MB | 552.9 MB |
| −AIC | 896.1 MB | 565.8 MB |
| **−Fishes** | **335.4 MB / 189 ms** | 3542.4 MB / 2106 ms |
| **−Wings** | **582.1 MB / 379 ms** | **290.4 MB / 511 ms** |
| −Tuples | 912.4 MB | 569.0 MB |
| −Pointing | 901.1 MB | **18,237.8 MB / 255 s** |
| −ShortestContradiction | 902.1 MB | 569.7 MB |

**`FindFishes` and `FindWings` account for the bulk.** Disabling fishes removes 63% of
`killer-innie`'s allocation; disabling wings removes 49% of `renban-sky`'s.

Two incidental findings from the same table, both about what happens when a technique is *removed*:

- **Pointing is load-bearing.** Without it `renban-sky` takes **255 seconds and 18 GB** instead of
  0.87 s and 568 MB — a 290× time blowup, because the solver falls back on far more expensive
  techniques to make the same deductions.
- Fishes is similarly load-bearing on `renban-sky` (568 MB → 3.5 GB without). So these stages are
  not simply overhead to trim; they pay for themselves in avoided work. The goal is to make them
  allocate less, not to run them less.

## What was fixed

`Combinations` — called per candidate per tuple size inside exactly these searches — yielded
`indexes.Select(i => collection[i]).ToList()`, which allocates a `Select` iterator and a capturing
closure on top of the list. Replaced with a hand-built list, identical semantics:

| case | before | after |
| --- | ---: | ---: |
| vanilla-u17-logical | 10.71 MB | 7.31 MB |
| killer-innie-logical | 902.0 MB | 830.8 MB |
| renban-sky-logical | 569.7 MB | 531.2 MB |

Only 2–8%. The iterator and closure were the cheap part.

## Counting the call sites, which corrected the arithmetic above

Date: 2026-08-04. The section that used to sit here predicted the `List<T>` per combination was
"most of the attribution" and worth "several hundred MB", from this arithmetic: `FindFishes`
enumerates C(9,2)+C(9,3)+C(9,4) = 246 combinations per (value, orientation), ×9 values ×2
orientations ≈ 4,400 per call, ×~1,500 calls ≈ 480 MB.

**Two of those three factors were wrong.** A `[CallerFilePath]`/`[CallerLineNumber]` counter on
`Combinations` — which attributes yields per call site without touching any call site — gives the
real distribution:

| call site | what it is | killer-innie | arrow-search | renban-sky | escargot | u17 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `SolverLogic:1127` | **`FindFinnedFishes` inner** (positions) | **429,358** | **435,948** | 232,622 | 39,138 | 0 |
| `SolverLogic:294` | **`IsBoardValid`** contradiction check | 40,662 | 13,527 | **496,661** | 7,400 | **60,898** |
| `AICSolver:540` | ALS strong links | 13,552 | 13,527 | 5,072 | 2,399 | 0 |
| `SolverLogic:1099` | `FindFinnedFishes` outer | 4,428 | 4,428 | 3,016 | 1,750 | 0 |
| `SolverLogic:585` | naked tuples | 94 | 0 | 4,201 | 1,522 | 0 |
| `SolverLogic:960` | **`FindFishes`** | 0 | 0 | 636 | 274 | 0 |
| `SumGroup` / `SandwichConstraint` / `SolverAnalysis` (12 sites) | — | 0 | 0 | 0 | 0 | 0 |

1. **The 4,400 figure was the wrong loop.** It matches `FindFinnedFishes`'s *outer* loop exactly
   (4,428 measured — the arithmetic was right about that one), but the volume is an order of
   magnitude higher in the *inner* loop over fin positions, which the arithmetic never accounted
   for. And `FindFishes` itself, the function the arithmetic named, yields **essentially nothing**.
2. **The ~1,500 was `StepLogic` calls, not `FindFishes` calls.** The fish search only runs when
   cheaper techniques fail, and bails out early. Actual total yields are ~0.5M, not ~6.6M.
3. **`FindWings` never calls `Combinations` at all.** `FindNWing` hand-rolls its combination walk
   over a reused `wingInfoArray`, so it was already buffer-reusing. The bisect's 49% wings
   attribution on `renban-sky` is therefore *not* combination lists.

At ~72 bytes per `List<int>`, ~0.5M yields is ~35 MB on `killer-innie` — **4%** of its 831 MB, not
"most of the attribution".

## What buffer reuse actually bought

Both changes below were made 2026-08-04. `CombinationsBuffered` is the Priority-3 item as scoped;
the `HashSet` reuse is the thing the counter turned up next to it, and is ~10× larger.

`Extensions.CombinationsBuffered` yields one borrowed list that is overwritten per step, adopted at
the six sites above with nonzero volume after each was individually verified not to retain. The
buffer is allocated per *enumerator*, which is what makes the nested pair at
`SolverLogic:1099`/`:1127` safe. The 12 zero-volume sites were left on fresh-list `Combinations`:
no measured benefit, so no reason to take the retention risk.

**`FindFinnedFishes` allocated a fresh `HashSet<int> elims` per inner combination** — 429,358 of
them per `killer-innie` logical solve, one for every position combination examined. Hoisted to one
per call, reused via `Clear()`. The `elims != null` sentinel became an explicit `hasElims` flag.
This is safe because `elims` escapes only in the success block, which copies it (the
`LogicalStepDesc` constructor `.ToList()`s) or consumes it eagerly (`DescribeElims`,
`ClearCandidates`), and then returns immediately.

| case | baseline | + CombinationsBuffered | + HashSet reuse | total |
| --- | ---: | ---: | ---: | ---: |
| vanilla-u17-logical | 7.31 MB | 3.12 | 3.12 | **−57%** |
| escargot-logical | 281.57 | 278.03 | 262.03 | −6.9% |
| killer-innie-logical | 830.80 | 795.99 | **316.53** | **−62%** |
| arrow-search-logical | 942.19 | 908.96 | **376.16** | **−60%** |
| renban-sky-logical | 531.15 | 478.11 | **330.88** | **−38%** |

Total across the five: 2,593 → 1,289 MB, **−50%**. Time also improved, which allocation work usually
does not: `killer-innie` 484 → 414 ms (−14%), `arrow-search` 478 → 419 ms (−12%), the five together
1,895 → 1,741 ms (−8%).

Note the split: the `HashSet` averaged ~1.1 KB per allocation against the `List`'s 72 bytes, because
it grew through the resize sequence 3→7→17→37→89 on every combination. **The biggest allocation in
a hot loop was the container that grows, not the one that is merely numerous.**

## Enumeration order, which had to be checked

`elims` reaches `DescribeElims` and `LogicalStepDesc.elimCandidates`, both order-visible, so reuse
would be a behaviour change if it altered iteration order. It does not: `HashSet<T>.Clear()` resets
the bucket and entry arrays and the free list, so a cleared set enumerates in insertion order
exactly as a fresh one does. (`IntersectWith`'s removals within an iteration leave free slots, but
those affect fresh and reused sets identically.) Confirmed empirically — all 28 corpus cases, all
116 tests and all 398 ISS puzzles return identical results.

## Still unexamined

`renban-sky-logical`'s allocation is still the odd one out, though less so: it is now 331 MB in only
**4** `ConsolidateBoard` passes and 220 `StepLogic` calls. Its dominant `Combinations` site is
`IsBoardValid` (496,661 yields, more than twice any other case), which is a *contradiction check*
rather than a deduction step — worth asking why that check is so much more expensive here. The
bisect's 49% wings attribution is also still unexplained now that `FindWings` is known not to use
`Combinations`.

## Reproducing

The bisect is a throwaway probe: build a `Solver`, set one `Disable*` property, call
`ConsolidateBoard()`, and diff `GC.GetTotalAllocatedBytes(precise: true)`. Corpus `logical` cases
give the baseline:

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3 --filter logical
```
