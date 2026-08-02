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

## Recommended production change

- Replace `Solver.IsWeakLink`'s `List<int>.BinarySearch` call with the measured explicit integer
  binary search. It benefits native and WASM, so no target-specific compilation is needed. The
  investigation leaves the production solver unchanged pending acceptance of that fix.
- `BitOpsBench` now includes no-inline TZC/LZC controls, and the Node driver exposes the existing
  `bitops` export, preserving the diagnostic that resolved the lowering question.
- The array-backed skyscraper probe and differential-build switches were removed.

## Validation

- `dotnet test -c Release SudokuTests/SudokuTests.csproj --no-restore`: **94/94 passed**.
- The fixed-variant AOT publish completed with `WasmStripILAfterAOT=true`.
- Fixed-variant focused result: native 101.94 ms, WASM 149.67 ms (15 iterations, minima).
- Fixed-variant AOT bundle, full corpus: all 16 `compare.js` results report `match`.
