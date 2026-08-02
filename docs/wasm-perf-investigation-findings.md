# WASM performance investigation — follow-up findings

Date: 2026-08-02

This follows [`wasm-perf-investigation-prompt.md`](wasm-perf-investigation-prompt.md) and the
prototype baseline in [`wasm-prototype-findings.md`](wasm-prototype-findings.md).

## Verdict

The `variant-renban-sky` 23× outlier is explained, and a fix is validated. The cause was **not** recursive
`Span<T>` parameters or `stackalloc`. It was `List<int>.BinarySearch` in `Solver.IsWeakLink`,
called repeatedly from `SkyscraperSearch`. Mono AOT retained an indirect generic-comparer call in
that path. An explicit integer binary search removes the indirect dispatch.

The fix variant improves both targets: the focused case is about **1.5× faster natively** and **23× faster
in WASM**. Its WASM/native ratio falls from 22.5× to 1.47×. All 16 corpus cases still match and all
94 NUnit tests pass.

This does not explain the general WASM tax. On a fresh full-corpus run with the fix, the geomean is
**5.38×** (versus the documented 6.06× baseline). Most of the remaining slowdown is still
unattributed Mono-AOT-versus-RyuJIT codegen cost.

## Confirmed findings

### 1. Recursive spans are not the skyscraper pathology

The first probe replaced all three `stackalloc` buffers with preallocated `uint[]`/`int[]` fields
and removed all span parameters from the recursive call. It was an investigation-only 1T build;
solver clones share constraint instances, so that scratch layout would be unsafe as a production
multi-threaded implementation.

Focused measurements, 15 iterations, minimum/median milliseconds:

| implementation | native | WASM | WASM/native (min) |
| --- | ---: | ---: | ---: |
| stackalloc + recursive spans | 154.38 / 157.84 | 3475.33 / 3550.39 | 22.51× |
| preallocated arrays | 145.44 / 147.76 | 3454.14 / 3555.42 | 23.75× |

The arrays made native 5.8–6.4% faster, while the WASM difference was 0.6% by minimum and −0.1%
by median: noise. Because native improved and WASM did not, the cross-host ratio became slightly
worse. The leading hypothesis is therefore rejected, and the original allocation-free span code
is retained.

### 2. `List<int>.BinarySearch` caused the 23× outlier

`SkyscraperSearch` checks every tentative assignment against previously assigned candidates by
calling `Solver.IsWeakLink`. The old implementation was:

```csharp
weakLinks[candIndex0].BinarySearch(candIndex1) >= 0
```

The replacement performs the same binary search directly over the already-sorted `List<int>`.
Focused final-source measurements, 15 iterations:

| host | before min / median | after min / median | speedup by min |
| --- | ---: | ---: | ---: |
| native | 154.38 / 157.84 ms | 101.94 / 106.94 ms | 1.51× |
| WASM | 3475.33 / 3550.39 ms | 149.67 / 150.63 ms | 23.22× |

The result and allocation are unchanged. The final ratio is 1.47× by minima and 1.41× by medians.

A differential disassembly compared builds where this was the only source change. Only five WASM
function bodies changed size. The baseline weak-link body contains a `call_indirect` on the generic
comparison path; the replacement contains the integer comparison loop directly. This is consistent
with RyuJIT specializing the BCL path better and Mono AOT paying indirect comparer dispatch at every
binary-search step. The native 1.5× win shows the BCL call was not free there either, but Mono's
penalty was pathological.

### 3. TZC lowers correctly, but intrinsic recognition is caller-dependent

The original measurements showed direct `TrailingZeroCount` at 1.56–1.64 ns in WASM versus
`LeadingZeroCount` at 0.35 ns. No-inlining controls sharpened the diagnosis:

| operation | native direct | native no-inline wrapper | WASM direct | WASM no-inline wrapper |
| --- | ---: | ---: | ---: | ---: |
| TrailingZeroCount | 0.335 ns | 0.924 ns | 1.643 ns | 1.195 ns |
| LeadingZeroCount | 0.295 ns | 0.913 ns | 0.353 ns | 0.552 ns |

Native behaves normally: both direct operations inline, and both wrappers pay similar call cost.
In WASM, the LZC wrapper adds call cost, but the TZC wrapper is 27% faster than the direct caller.
That proves the two direct call sites are optimized differently.

A second differential AOT build changed only the no-inline TZC wrapper from the intrinsic to the
software fallback. Exactly one WASM function body changed. The intrinsic body is a short type-init
guard followed by:

```wat
(i32.ctz
  (local.get $0))
```

The software body is much larger and contains no `i32.ctz`. Therefore Mono does know how to lower
TZC to the WebAssembly instruction. The defect is caller-specific intrinsic recognition/inlining,
not absence of a TZC lowering. A no-inline wrapper is a possible WASM-only optimization, but it
would make native TZC about 2.8× slower and is not implemented in production source.

## Full-corpus effect

The five-iteration full corpus with the weak-link fix produced identical results in all 16 cases.
The paired fixed-source run had a 5.38× geomean and 6.54× median:

| notable case | native ms | WASM ms | ratio |
| --- | ---: | ---: | ---: |
| variant-renban-sky | 112.91 | 148.88 | 1.32× |
| killer-innie | 73.88 | 917.69 | 12.42× |
| variant-killerblister | 5.39 | 54.85 | 10.18× |
| littlekiller-8 | 3.52 | 30.58 | 8.69× |
| variant-cloneways | 21.05 | 178.24 | 8.47× |
| blank6-cap5M | 2387.64 | 17565.19 | 7.36× |

The geomean falls 11% from the documented 6.06× baseline, but the median does not improve because
one outlier cannot materially move the middle of a 16-case corpus. This mechanism explains almost
all of `variant-renban-sky` and only a small part of the program-wide tax.

## Still hypotheses

- **Array bounds checks / general array codegen:** still a leading explanation for the remaining
  5.38× geomean, but not directly measured in this investigation.
- **Virtual dispatch through `Constraint`:** not isolated here. The weak-link result shows that
  indirect dispatch inside an inner loop can be catastrophic, so this remains worth measuring.
- **Other generic BCL helpers:** the `List<int>.BinarySearch` result makes comparer-based and
  generic collection helpers higher-priority audit targets than spans.
- **Bit operations:** confirmed secondary. TZC has a recoverable 27% micro-benchmark improvement
  with a wrapper, but all measured bit-operation ratios remain below the remaining whole-program
  slowdown.

## Production change — applied

The recommended fix landed, and was **extended beyond `IsWeakLink`**. Eight further call sites
performed the identical `weakLinks[x].BinarySearch(y)` and bypassed `IsWeakLink` entirely, so they
kept paying the comparer dispatch:

| site | path | shape |
| --- | --- | --- |
| `AICSolver.cs:294` | AIC chain search | membership |
| `SolverLogic.cs:1283-1285` (×3) | logical solver | membership |
| `SolverAnalysis.cs:49,133` | `IsGroup`, `CanPlaceDigits` | membership, list hoisted out of a loop |
| `SolverInitialization.cs:312,321` | `AddWeakLink` | needs the insertion index |

All now route through a shared `Solver.WeakLinkSearch(List<int>, int)` that keeps
`List<T>.BinarySearch`'s exact contract (index if found, `~insertionPoint` otherwise), so the
index-returning callers work unchanged and the two hoisted-list loops keep their hoist.

Verification of the applied change:

| measurement | before | after | delta |
| --- | ---: | ---: | ---: |
| `variant-renban-sky`, native, 15 iters | 154.45 ms | 103.20 ms | **1.50×** |
| `variant-renban-sky`, WASM AOT, 15 iters | 3569.36 ms | 164.22 ms | **21.7×** |
| `variant-renban-sky`, WASM/native ratio | 23.10× | 1.55× | — |
| corpus geomean, WASM/native | 6.06× | **~5.3×** | −13% |
| corpus worst case | 23.10× | 10.22× | — |

All 16 corpus cases report `match`, and `dotnet test` passes 94/94.

Two caveats on the numbers. `killer-innie` is a high-variance case — 106 ms at 5 iterations versus
56 ms at 15 — so 5-iteration corpus figures for it should not be trusted; the geomean above uses
the 15-iteration value, and the raw 5-iteration pairing would have read an over-optimistic 5.05×.
The extension's eight sites were initially reported as unmeasured. **That was wrong** — they are
measured below, and the benefit is large in WASM.

Also unchanged from the investigation: `BitOpsBench` keeps its no-inline TZC/LZC controls, the Node
driver exposes the `bitops` export, and the array-backed skyscraper probe and differential-build
switches were removed.

## Round 2: the same defect in `SumConstraintRegistry`

Counter instrumentation (temporary, reverted) settled which call sites are actually hot, after a
first guess went wrong:

| method | calls per 2 solver runs | verdict |
| --- | ---: | --- |
| `SumConstraintRegistry.ContainsCell` | 5,848,440 (killer-innie) | **very hot** |
| | 2,041,554 (killer-cage) | |
| | 809,028 (littlekiller-10) | |
| `Solver.WeakLinkSearch` | 43k–174k | hot |
| `FastFindPairs` / `FastFindTriples` | **2–6** | cold — setup only |

`FastFindPairs`/`FastFindTriples` sort with lambda comparators, which looked like an obvious
delegate-dispatch target. They are not: `doAdvancedStrategies` is `false` at every brute-force call
site, and the only `true` caller is `DiscoverWeakLinks`, which runs once per solve immediately
after the root `Clone` — never inside the search. **Guessing at hot paths from call-graph shape
produced a wrong target twice; counters settled it in one run.**

`ContainsCell` used `Array.BinarySearch`, the same `Comparer<int>.Default` defect. Replaced with
`SolverUtility.SortedContains`, which scans linearly for small arrays (bailing out on sort order)
and falls back to binary search above 16 elements. All four affected arrays are provably sorted.

Paired measurements, same iteration count:

| case | native before | native after | WASM before | WASM after | WASM gain |
| --- | ---: | ---: | ---: | ---: | ---: |
| killer-innie | 56.35 | 53.62 | 889.69 | 358.86 | **2.48×** |
| killer-cage | 39.01 | 36.80 | 311.85 | 138.34 | **2.25×** |
| littlekiller-8 | — | — | 31.24 | 11.17 | **2.80×** |
| littlekiller-10 | 54.26 | 53.94 | 297.15 | 217.22 | 1.37× |
| variant-killerblister | — | — | 54.75 | 16.64 | **3.29×** |

Native gains ~5%; WASM gains up to 3.3×. That asymmetry is the whole thesis of this
investigation: comparer dispatch is a mild cost natively and a severe one under Mono AOT.

### The weak-link extension was not unmeasured after all

A paired run isolated it. `escargot` is a plain vanilla puzzle with no constraints, so
`ContainsCell` cannot execute there — its entire gain comes from the eight extended weak-link
sites:

| case (WASM, 10 iters) | before | after | gain |
| --- | ---: | ---: | ---: |
| escargot | 7.44 | 3.21 | **2.32×** |
| variant-orbit | 102.68 | 61.35 | 1.67× |
| variant-equalsums | 102.24 | 47.78 | 2.14× |
| variant-cloneways | 187.77 | 166.88 | 1.13× |

## Cumulative result

| stage | geomean | median | worst case |
| --- | ---: | ---: | ---: |
| original prototype | 6.06× | 6.37× | 23.10× |
| + `IsWeakLink` fix | ~5.3× | — | 10.22× |
| + extension + `ContainsCell` | **3.84×** | **3.92×** | **7.62×** |

A **37% reduction in the WASM tax**, with every one of the 16 cases still reporting `match` and
94/94 tests passing. Native is unchanged-to-slightly-better throughout.

## Benchmark coverage gap

The corpus is entirely brute-force (`count`/`solve`). It has no `solvepath`/`step`/`truecandidates`
case, so the logical solver — `AICSolver`, `SolverLogic`, `IsGroup`, `CanPlaceDigits` — is
effectively unmeasured. That is the half of the solver a setting site leans on hardest, and it is
also where five of the eight newly-fixed call sites live. Adding a logical-solve op to `BenchCore`
is the prerequisite for measuring any further work in that half.

## Validation

- `dotnet test -c Release SudokuTests/SudokuTests.csproj --no-restore`: **94/94 passed**.
- The fixed-variant AOT publish completed with `WasmStripILAfterAOT=true`.
- Fixed-variant focused result: native 101.94 ms, WASM 149.67 ms (15 iterations, minima).
- Fixed-variant AOT bundle, full corpus: all 16 `compare.js` results report `match`.
