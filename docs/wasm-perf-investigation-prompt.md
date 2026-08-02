# Investigate: where the WASM slowdown actually comes from

> A self-contained brief for picking this up in a fresh session or handing to another agent.
> Measurements and background: [`wasm-prototype-findings.md`](wasm-prototype-findings.md).

## Context

`SudokuSolver` (C#, .NET 10) has been compiled to WebAssembly as a prototype, in
`SudokuSolverWasm/` (not in `SudokuSolver.sln`).

The port works — Mono AOT to `browser-wasm`, no solver source changes, and all 16 benchmark cases
return results identical to native. The open question is purely performance.

## The question

WASM runs **6.06× slower than native** single-threaded (geomean over the corpus; median 6.37×).
Find out what that 6× is actually made of, and whether any of it is recoverable.

A secondary, possibly related question: one case, `variant-renban-sky`, is **23.10×** slower —
nearly 4× worse than the corpus median.

## Already ruled out — do not redo these

**Bit manipulation is not the dominant cause.** The solver is a bitmask solver, so `BitOperations`
was the first suspect. It was measured (`BitOpsBench.cs`, run with `--bitops`, timing each
intrinsic against a hand-written software equivalent in the same host):

- `PopCount` and `TrailingZeroCount` genuinely *are* mis-lowered in WASM — both lose to
  hand-written software (ratios 0.79× and 0.70×), which cannot happen if a one-instruction
  lowering were inlined. Likely out-of-line calls.
- But `LeadingZeroCount` is lowered correctly and runs at **1.1× of native**, proving near-native
  is achievable and validating the harness.
- Absolute WASM/native on the intrinsic path: LZC 1.1×, Log2 1.5×, ValueCount 1.8×, PopCount 2.1×,
  TrailingZeroCount 5.0×.

**All of those are below the 6.06× whole-program geomean.** If the hottest primitives run at ~2×
and the program runs at 6×, the remainder must be worse than 6×. So the gap lives somewhere else.

**AOT was verified active** — "you measured an interpreted build" is the obvious objection and it
does not hold. `RunAOTCompilation=true`, measurements taken from `dotnet publish -c Release` only,
publish log shows `SudokuSolver.dll.bc -> .o` (LLVM), and `WasmStripILAfterAOT=true` removes the IL
outright. Empirically the AOT bundle runs the micro-benchmark in ~15 s while the interpreted Debug
bundle was killed after 25 minutes unfinished.

Note that .NET 10's `BitOperations.TrailingZeroCount(uint)` is documented to check
`WasmBase.IsSupported` and call `WasmBase.TrailingZeroCount`, which Mono lowers to `OP_CTTZ32` →
`i32.ctz`. So the *expected* result is a single instruction, and the measurement disagrees with
that expectation. Resolving that disagreement is the concrete task.

**Allocation volume is not the cause either.** Spearman rank correlation between per-case
allocation and WASM slowdown is **−0.04**. The highest-allocating case (`variant-cloneways`,
41 MB) is middling at 7.97×, while the 23× outlier allocates almost nothing (0.23 MB). (Allocation
*does* matter for multi-threaded viability and the 2 GiB heap ceiling — but not for
single-threaded throughput.)

**Reflection is a non-issue.** No assembly scanning, no `Activator.CreateInstance`, one
`Attribute.GetCustomAttribute`.

## Leading hypothesis — start here

`variant-renban-sky` (the 23× case) exercises `SkyscraperConstraint`, whose hot path was rewritten
in commit `01adeac` for ~6× native speedup and "near-zero allocation". That rewrite uses exactly
the constructs Mono AOT is weakest at:

- `SudokuSolver/Constraints/SkyscraperConstraint.cs:180-182` — three `stackalloc` buffers per
  `StepLogic` call.
- `SkyscraperConstraint.cs:264` — `SkyscraperSearch(...)` is **recursive** and passes
  `ReadOnlySpan<uint>` / `Span<uint>` / `Span<int>` as parameters on every frame.

Suspicion: `Span<T>` byref-struct params across deep recursion may not enregister under Mono AOT,
and span bounds checks that RyuJIT elides may survive. If so, the optimization that made this
constraint fast natively is what makes it pathological in WASM — which would be a genuinely
important result for the whole port, and would mean other "near-zero allocation" work on the perf
roadmap needs re-validating against WASM rather than assumed to carry over.

**This is an inference from code shape, not a measurement.** Test it directly: write a variant of
the same algorithm using plain `uint[]` fields (or preallocated per-instance scratch arrays)
instead of `stackalloc` + `Span`, and measure both hosts. Confirm or kill it before moving on.

## Attributing instructions to methods, given stripped symbols

Counting `i32.ctz` occurrences in the module (there are 197) proves nothing about *your* method,
because the runtime and BCL contribute their own. The reliable technique is a differential build:

1. Wrap the operation in a `[MethodImpl(MethodImplOptions.NoInlining)]` method, called with a
   non-constant runtime value so it cannot be constant-folded.
2. Publish twice — once using `BitOperations.X`, once using a deliberate software fallback.
3. Diff the two disassemblies and compare the corresponding function bodies.

The sharpest single question to answer first: `LeadingZeroCount` (0.35 ns) and
`TrailingZeroCount` (1.67 ns) are equally trivial, have equally direct WASM instructions, sit
behind structurally identical `BitOperations` code, and were compiled in the same AOT pass — yet
differ by **4.8×**. Any explanation must account for that asymmetry.

## Secondary hypotheses

- **Array bounds checks.** `SolverBruteForce.cs` / `SolverLogic.cs` index hot arrays constantly.
  RyuJIT elides many of these; Mono AOT may not. Probe with a targeted `Unsafe`/unchecked variant
  of one hot loop.
- **Virtual dispatch through `Constraint`.** `EnforceConstraint` is called on every `SetValue`.
  Interface/virtual call cost in Mono AOT vs devirtualized RyuJIT.
- **General Mono AOT vs RyuJIT codegen quality** — the residual, if the above don't account for it.

## Reproducing

Requires the `wasm-tools` workload. Always wrap long commands in `caffeinate -i` (this machine
idle-sleeps, which silently stalls builds and corrupts timings).

```bash
# native baseline
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- \
    --iterations 5 --save /tmp/native-1t.json

# wasm build (AOT; takes ~7-10 min — budget for this, it dominates iteration time)
dotnet publish SudokuSolverWasm -c Release -o /tmp/wasm-1t

# wasm benchmark, headless
node SudokuSolverWasm/run-node.mjs /tmp/wasm-1t/wwwroot bench \
    --iterations 5 --save /tmp/wasm-1t.json

# diff (also flags any case where the two hosts disagree on the result)
node SudokuSolverWasm/compare.js /tmp/native-1t.json /tmp/wasm-1t.json

# bit-ops micro-benchmark
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --bitops
node SudokuSolverWasm/drive-chrome.mjs /tmp/wasm-1t/wwwroot --bitops
```

Both hosts compile the *same* `BenchCore.cs` and `BitOpsBench.cs`, so the timing procedure is
identical by construction. Keep it that way — divergence there invalidates everything.

## Practical constraints

- **Symbols are stripped** from `dotnet.native.*.wasm` (no name section), so you cannot locate
  methods by name in the disassembly. `wasm-dis` lives at
  `/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.*/tools/bin/wasm-dis`. If you
  need per-function attribution, first find a publish setting that preserves names, or use Chrome
  DevTools' sampling profiler over CDP.
- **Multi-threaded WASM cannot run under Node** — it refuses shell environments. Use
  `drive-chrome.mjs` (headless Chrome over CDP, no npm dependencies) for MT. Single-threaded works
  fine under Node.
- Timings are noisy on a laptop; use `--iterations 5`+ and treat only repeatable deltas as real.
  Reference machine: Apple M3 Pro, 11 logical CPUs.

## Deliverable

An attribution of the 6×: which mechanisms account for how much, with measurements backing each
claim. Explicitly separate confirmed findings from hypotheses — the existing writeup does this and
it matters more than being conclusive.

If a fix is identified, note whether it helps or hurts **native**, since the same source compiles
for both. A WASM win that regresses native needs conditional compilation, which is a real cost.

Do not change solver behaviour: `compare.js` must still report `match` on every case.
