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

## The remaining opportunity, and why it was not taken

The `List<T>` per combination is the real cost, and the arithmetic says it is most of the
attribution. On a 9×9, `FindFishes` enumerates combinations of unset rows/columns for tuple sizes
2–4: C(9,2)+C(9,3)+C(9,4) = 246 per (value, orientation), ×9 values ×2 orientations ≈ 4,400
combinations per call. A `List<int>` holding 2–4 ints costs roughly 72 bytes, so ≈ 320 KB per
`FindFishes` call — and at ~1,500 calls that is ≈ 480 MB, which matches the 570 MB the bisect
attributes to fishes.

So a buffer-reusing enumeration would plausibly remove several hundred MB. It was **not**
implemented because `Combinations` has **19 call sites** across `SolverLogic`, `SumGroup`,
`SolverAnalysis` and others, and yielding a reused buffer is only safe if no caller retains or
defers the yielded list. Auditing all of them is the prerequisite, not an afterthought — a single
caller that stores the reference would produce silent, data-dependent wrong answers.

The safe shape:

1. Add a separate buffer-reusing method rather than changing `Combinations`, so existing callers
   keep fresh-list semantics by default.
2. Adopt it only in call sites individually verified not to retain — `FindFishes` and `FindWings`
   first, since they carry the cost.
3. Document the borrowed-buffer contract on the new method the way
   `CountSolutions`'s `solutionEvent` is documented.

## Also unexamined

`renban-sky-logical` allocates 531 MB while making only **4** `ConsolidateBoard` passes and 220
`StepLogic` calls — 2.4 MB per `StepLogic` call, four times `killer-innie`'s rate. Whatever it is
doing is qualitatively different from the other cases and is not explained by the combination
arithmetic above. That is the next thing to instrument.

## Reproducing

The bisect is a throwaway probe: build a `Solver`, set one `Disable*` property, call
`ConsolidateBoard()`, and diff `GC.GetTotalAllocatedBytes(precise: true)`. Corpus `logical` cases
give the baseline:

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3 --filter logical
```
