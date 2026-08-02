# WASM prototype — findings

Experiment: compile the C# solver to WebAssembly, drive it from the browser with the **same JSON
protocol the f-puzzles userscript sends over websockets**, and measure the cost versus native. The
goal was to decide whether a solver-backed setting site can be built on this codebase, or whether
it needs a TypeScript rewrite.

Prototype lives in [`SudokuSolverWasm/`](../SudokuSolverWasm/README.md). Not in the solution.

**Verdict: viable.** The solver compiles to WASM unmodified, produces identical results on every
benchmark case, and runs roughly **6× slower than native single-threaded** and **4.7× slower
multi-threaded**. Nothing structural blocks the plan. There is one pathological case and one
23× outlier that need explaining before this becomes a product.

## Environment

- Apple M3 Pro, 11 logical CPUs (5 P + 6 E), macOS
- .NET SDK 10.0.301, `wasm-tools` workload, runtime 10.0.10, Mono AOT to WASM
- Native: `dotnet run -c Release`, ServerGC
- WASM: `dotnet publish -c Release` with `RunAOTCompilation=true`
- WASM 1T measured under Node 24; WASM MT measured in headless Chrome (MT cannot run in Node — see
  below). Both use V8's WebAssembly engine.
- 5 iterations per case, minimum time reported

## Did it work at all?

Yes, with less friction than expected:

- **No reflection problems.** The solver has no assembly scanning, no `Activator.CreateInstance`,
  and exactly one `Attribute.GetCustomAttribute`. It trims and AOT-compiles cleanly.
- **No source changes to the solver library.** `SudokuSolver.csproj` was not touched.
- **Every case returns an identical result** native vs WASM, in both threading modes. `compare.js`
  checks this explicitly — a timing comparison between hosts that disagree would be meaningless.
- The full protocol surface works: `solve`, `check`, `count`, `truecandidates`, `solvepath`,
  `step`, `estimate`.

Bundle size, AOT, single-threaded: **15 MB raw / 3.9 MB gzipped**, of which `dotnet.native.wasm`
is 9.8 MB. The solver's own assembly is only 343 KB — nearly all the weight is runtime.

## Results

`n1T`/`nMT` = native single- and multi-threaded; `w1T`/`wMT` = WASM equivalents. Min ms.

| case | n1T | nMT | w1T | wMT | w1T/n1T | wMT/nMT | wasm MT gain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| blank4 | 1.21 | 0.75 | 0.83 | 8.41 | 0.68× | 11.15× | 0.10× |
| blank6-cap5M | 2420.78 | 3010.37 | 17274.82 | — | 7.14× | — | — |
| vanilla-u17 | 0.02 | 0.02 | 0.11 | 0.09 | 4.94× | 3.77× | 1.19× |
| vanilla-u17-s | 0.02 | 0.04 | 0.10 | 0.10 | 5.19× | 2.54× | 0.94× |
| escargot | 1.08 | 1.24 | 7.98 | 6.47 | 7.37× | 5.24× | 1.23× |
| killer-innie | 72.69 | 38.89 | 938.94 | 292.98 | 12.92× | 7.53× | 3.20× |
| killer-cage | 69.76 | 47.30 | 326.64 | 112.55 | 4.68× | 2.38× | 2.90× |
| littlekiller-8 | 3.79 | 5.32 | 31.98 | 29.09 | 8.45× | 5.47× | 1.10× |
| littlekiller-10 | 60.42 | 35.58 | 292.37 | 41.87 | 4.84× | 1.18× | 6.98× |
| kropki-search-cap50k | 2332.12 | 303.83 | 10494.71 | 1187.02 | 4.50× | 3.91× | 8.84× |
| arrow-search | 23.31 | 11.35 | 123.97 | 26.69 | 5.32× | 2.35× | 4.64× |
| variant-renban-sky | 154.55 | 154.65 | 3569.36 | 3315.64 | **23.10×** | **21.44×** | 1.08× |
| variant-cloneways | 21.09 | 31.97 | 168.01 | 116.49 | 7.97× | 3.64× | 1.44× |
| variant-orbit | 19.32 | 5.81 | 97.29 | 25.36 | 5.04× | 4.37× | 3.84× |
| variant-equalsums | 15.74 | 16.40 | 100.25 | 92.50 | 6.37× | 5.64× | 1.08× |
| variant-killerblister | 5.55 | 5.73 | 57.32 | 55.63 | 10.34× | 9.70× | 1.03× |

- **Single-threaded: geomean 6.06×, median 6.37×**
- **Multi-threaded: geomean 4.67×, median 4.37×** (excludes `blank6-cap5M`, see below)

Caveats on the extremes: `blank4` at 0.68× (1T) is sub-millisecond and inside the noise floor —
not a WASM win. The sub-0.1 ms vanilla cases are likewise too small to mean much.

## Multi-threading works, but scales worse than native

Threads are a real win in WASM where the solver parallelises well — `kropki-search-cap50k` goes
10.5 s → 1.19 s (8.8×) and `littlekiller-10` 292 ms → 42 ms (7×). That is why the MT ratio (4.67×)
beats the 1T ratio (6.06×) on some cases.

But WASM's parallel overhead is much higher at small sizes. `blank4` goes from 0.83 ms
single-threaded to **8.41 ms** multi-threaded — 10× *worse*. Native shows the same effect far more
mildly (1.21 → 0.75 ms, still a win). Any production use should pick the threading mode per
workload, not globally.

### One pathological case

`blank6-cap5M` (count 5 M solutions in a blank 6×6) **did not complete in over 10 minutes** under
WASM MT, against 17 s WASM 1T and 3 s native MT. It was excluded from the MT figures rather than
left to run.

The likely cause is allocation: this case's allocation jumps from 0.29 MB single-threaded to
192 MB under native MT. WASM is 32-bit with a much smaller heap and a weaker GC, so the same
allocation explosion appears to push it into thrashing. This is worth understanding — it is the
one result that says "don't just flip threads on and ship it".

## The `variant-renban-sky` outlier

At 23× (1T) and 21× (MT) it is nearly 4× worse than the corpus median and barely benefits from
threads. It is not noise — 154 ms → 3.6 s is far outside iteration variance. Something in that
constraint mix hits a path Mono AOT handles badly. Unexplained.

## BitOperations: tested, partly broken, but not the main cause

The solver is a bitmask solver and its hottest primitives are in `SolverUtility.cs` —
`ValueCount` (PopCount), `GetValue` (Log2), `MinValue` (TrailingZeroCount), `MaxValue`
(LeadingZeroCount). If Mono's WASM AOT failed to lower these to `i32.popcnt` / `i32.clz` /
`i32.ctz`, the solver would be paying software cost in its innermost loop.

Two tests were run.

**Disassembly.** `wasm-dis` on the AOT module finds the instructions present: 85 `i32.popcnt`,
244 `i32.clz`, 197 `i32.ctz`. So the compiler does know how to emit them. (Symbols are stripped,
so individual methods can't be located by name.)

**AOT was verified active**, since "you measured an interpreted build" is the obvious objection and
would invalidate everything:

- `RunAOTCompilation=true` is set for Release, and every measurement came from
  `dotnet publish -c Release` — never `build` or `run`, which leave the app as interpreted IL.
- The publish log shows the solver going through LLVM bitcode: `SudokuSolver.dll.bc -> .o`.
- `WasmStripILAfterAOT=true` **removes the IL**, so interpretation is not merely unlikely, it is
  impossible.
- Empirically: the AOT bundle finishes this micro-benchmark in ~15 s. The interpreted Debug bundle
  was killed after **25 minutes** without finishing — a >100× gap that leaves no doubt which one
  was measured.

**Micro-benchmark** (`BitOpsBench.cs`, shared by both hosts, `--bitops`). Each intrinsic is timed
against a hand-written software equivalent *in the same host*, so the comparison needs no
cross-host calibration. Median of 3 runs, nanoseconds per call:

| operation | native intrinsic | native software | native ratio | wasm intrinsic | wasm software | wasm ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| PopCount | 0.40 | 0.68 | 1.69× | 0.85 | 0.65 | **0.79×** |
| TrailingZeroCount | 0.34 | 0.65 | 1.93× | 1.67 | 1.14 | **0.70×** |
| Log2 | 0.53 | 5.10 | 9.7× | 0.78 | 0.53 | 0.68×* |
| LeadingZeroCount | 0.31 | 10.64 | 34.3× | 0.35 | 9.49 | 27.3× |
| `SolverUtility.ValueCount` | 0.42 | 0.73 | 1.74× | 0.75 | 0.60 | **0.79×** |

A ratio below 1 means the "intrinsic" **loses to hand-written software** — which cannot happen if a
single-instruction lowering were being inlined.

- **`PopCount` and `TrailingZeroCount` are not properly lowered in WASM.** Both lose to software.
  `PopCount` is beaten by a ~12-operation SWAR sequence; `TrailingZeroCount` by a de Bruijn table
  lookup. The most likely explanation is that the intrinsic exists as an out-of-line function (the
  85 `popcnt` sites), so each call pays call overhead that swamps the one-instruction body, while
  the software version gets inlined.
- **`LeadingZeroCount` is the control, and it works.** 27× faster than software, and **1.1× of
  native** in absolute terms. This both validates the harness and proves WASM can reach near-native
  on these primitives when lowering succeeds.

The sharpest form of the anomaly: `LeadingZeroCount` (0.35 ns) and `TrailingZeroCount` (1.67 ns)
are equally trivial operations with equally direct WASM instructions (`i32.clz`, `i32.ctz`), sit
behind structurally identical `BitOperations` code, and were compiled in the same AOT pass — yet
differ by **4.8×**. Whatever explains the gap has to explain that asymmetry.

Absolute WASM/native cost on the intrinsic path: `LeadingZeroCount` 1.1×, `Log2` 1.5×,
`ValueCount` 1.8×, `PopCount` 2.1×, **`TrailingZeroCount` 5.0×**.

\* The WASM software-`Log2` figure is not trustworthy: 0.53 ns for a 9-iteration shift loop is
physically impossible, so LLVM's loop-idiom recognizer almost certainly rewrote it into a
`clz` form. Only the absolute intrinsic comparison (1.5×) is meaningful for that row.

### Why this is *not* the explanation for the 6× gap

Every one of these penalties (1.1×–5.0×) is **smaller than the solver's overall 6.06× geomean**.
If the hottest primitives run at 2× and the whole program runs at 6×, the remainder must be worse
than 6× — so the bulk of the gap lives somewhere other than bit manipulation. This narrows the
search rather than ending it: likely suspects are array bounds checks, virtual dispatch through
`Constraint`, and general Mono-AOT-vs-RyuJIT code quality.

### Still worth fixing

`TrailingZeroCount` at 5.0× is the standout, and it backs `MinValue` — lowest-candidate
extraction, with 54 call sites and about as hot as anything in a bitmask solver. A hand-written
de Bruijn `MinValue` would be roughly 1.5× faster in WASM.

The catch: it would be ~2× *slower* natively, where the intrinsic wins 1.93×. Any change here has
to be conditional on target, which is a real cost in a codebase that currently has one
implementation per helper.

## Threading constraints discovered

Multi-threaded .NET WASM is meaningfully different from single-threaded, and these shaped the
prototype's architecture:

1. **It will not run outside a browser** — Node/V8 shells are rejected outright. Hence
   `drive-chrome.mjs`, which drives headless Chrome over the DevTools protocol.
2. **It cannot boot inside a Web Worker** — the runtime spawns its own workers, so it must be
   created on the page's main thread.
3. **Synchronous `[JSExport]` calls from the main thread are illegal** (*"Cannot call synchronous
   C# methods"*). Managed code lives on a deputy thread, so exports must return `Task`.

Consequence: the interactive testbed (worker + sync exports) currently supports 1T bundles only;
the benchmark page (main thread + async exports) supports both. Making the testbed MT-capable also
means solving response streaming across threads — JS interop is per-thread, so `SendResponse`
callbacks raised on a pool thread must be marshalled back. There is a `SynchronizationContext` hop
in `SolverInterop.DispatchResponse` for this that is **untested under MT**.

## Allocation

WASM consistently reported *lower* allocation than native — e.g. `variant-cloneways` 41.19 MB vs
57.12 MB. That is a difference in what the two runtimes count, not a real improvement; allocation
should not be compared across hosts.

## What this means for the setting-site plan

- Compiling the existing C# solver is a real option. The TypeScript rewrite is not forced.
- A 5–6× slowdown is fine for interactive setting work — true-candidates on a typical puzzle is
  tens to low hundreds of ms — but not for heavy search: a 2.4 s native count becomes 10.5 s.
- Protocol compatibility means the existing userscript is already a working client with only its
  transport swapped, so migration can be incremental.
- Threading is worth having but must be applied selectively, and the 32-bit heap is a real ceiling.

## Open items

A self-contained brief for picking these up is in
[`wasm-perf-investigation-prompt.md`](wasm-perf-investigation-prompt.md).

- **Where the other 6× actually lives** — bit ops are ruled out as the dominant cause (above).
  Next suspects: bounds checks, virtual dispatch through `Constraint`, Mono AOT code quality.
- Explain the `variant-renban-sky` 23× outlier.
- Diagnose `blank6-cap5M` under MT (allocation/GC thrashing suspected).
- **Startup cost was not measured** — runtime boot plus AOT module instantiation. It matters for a
  real site and should be quantified before committing.
- Memory ceiling untested beyond the case above.
- MT response streaming (`DispatchResponse`) is written but unexercised.
