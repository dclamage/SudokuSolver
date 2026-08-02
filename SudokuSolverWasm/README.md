# SudokuSolver on WebAssembly — prototype

A throwaway experiment: compile the C# solver to WebAssembly, drive it from a web page using the
**same JSON protocol the f-puzzles userscript already sends over websockets**, and measure what
running in the browser costs versus native.

This is a viability testbed, not a product. There is deliberately **no Sudoku UI** — you paste an
f-puzzles URL and press a button. The question it answers is "does the solver work in the browser,
and how much slower is it?", which is the thing that decides whether a solver-backed setting site
is worth building on this codebase instead of a TypeScript rewrite.

Not in `SudokuSolver.sln`, so it can't break existing builds or CI.

## Layout

| Path | What it is |
| --- | --- |
| `SolverInterop.cs` | `[JSExport]` surface — takes protocol messages, emits protocol responses |
| `SolverCommandProcessor.cs` | Port of `SudokuSolverConsole/WebsocketListener.cs` with the websocket swapped for a callback |
| `Responses.cs` | Protocol DTOs, byte-for-byte the same shapes the websocket server uses |
| `BenchInterop.cs` | Runs the native harness's benchmark cases inside WASM |
| `wwwroot/` | The static testbed site (`index.html`) and benchmark page (`bench.html`) |
| `serve.js` | Dependency-free static server that sets COOP/COEP |
| `run-node.mjs` | Headless driver — same runtime, scriptable, used for the measurements below |
| `compare.js` | Diffs a native benchmark run against a WASM one |

The benchmark deliberately compiles `benchmarks/SudokuSolverBenchmark/BenchCore.cs` rather than
reimplementing it, so native and WASM time cases with identical code. That file was extracted out
of the native harness's `Program.cs` for this purpose; the native harness behaves as before.

## Protocol

Identical to the websocket server:

```json
{ "nonce": 1, "command": "solve", "dataType": "fpuzzles", "data": "<f-puzzles URL or ?load= value>" }
```

Commands: `truecandidates`, `solve`, `check`, `count`, `estimate`, `solvepath`, `step`, `cancel`.
Responses: `truecandidates`, `solved`, `count`, `logical`, `estimate`, `invalid`, `canceled`.

Because the wire format matches, the existing userscript could be pointed at this with only its
transport swapped — that's the point of porting the processor instead of inventing a new API.

## Building

Requires the `wasm-tools` workload (`sudo dotnet workload install wasm-tools`).

```bash
# Fast dev loop (interpreted — do not use for timings)
dotnet build SudokuSolverWasm -c Debug

# Single-threaded, AOT
caffeinate -i dotnet publish SudokuSolverWasm -c Release -o /tmp/wasm-1t

# Multi-threaded, AOT (needs a cross-origin-isolated page)
caffeinate -i dotnet publish SudokuSolverWasm -c Release -p:WasmEnableThreads=true -o /tmp/wasm-mt
```

## Running

```bash
node SudokuSolverWasm/serve.js /tmp/wasm-1t/wwwroot 8080
# http://localhost:8080/        testbed
# http://localhost:8080/bench.html  benchmark
```

`serve.js` sets `Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp`. Threads-enabled builds need that isolation for
`SharedArrayBuffer`; a plain static server will fail to boot them.

Headless equivalents:

```bash
node SudokuSolverWasm/run-node.mjs /tmp/wasm-1t/wwwroot solve '<f-puzzles URL>'
node SudokuSolverWasm/run-node.mjs /tmp/wasm-1t/wwwroot bench --iterations 5 --save /tmp/wasm-1t.json
```

Node and Chrome share V8's WebAssembly engine, so headless numbers stand in for browser numbers
and are far easier to diff.

## Comparing against native

Run both sides with the same iteration count and the same threading mode:

```bash
# native
caffeinate -i dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- \
    --iterations 5 --save /tmp/native-1t.json
# wasm
caffeinate -i node SudokuSolverWasm/run-node.mjs /tmp/wasm-1t/wwwroot bench \
    --iterations 5 --save /tmp/wasm-1t.json
# diff
node SudokuSolverWasm/compare.js /tmp/native-1t.json /tmp/wasm-1t.json
```

`compare.js` flags any case where the two hosts disagree on the *result*, not just the timing —
a correctness difference would invalidate the comparison.

## Results

See [`../docs/wasm-prototype-findings.md`](../docs/wasm-prototype-findings.md).

## Threading constraints (learned the hard way)

Multi-threaded .NET WASM is meaningfully different from single-threaded, not just faster:

1. **It will not run outside a browser.** Node/V8 shells are rejected outright:
   *"This build of dotnet is multi-threaded, it doesn't support shell environments like V8 or
   NodeJS."* That is why `drive-chrome.mjs` exists — `run-node.mjs` can only measure 1T bundles.
2. **It cannot boot inside a Web Worker.** The runtime spawns its own workers, so it has to be
   created on the page's main thread.
3. **Synchronous `[JSExport]` calls from the main thread are illegal** — *"Cannot call synchronous
   C# methods."* Managed code lives on a deputy thread, so every export the page calls must return
   `Task`. Hence the `…Async` variants in `SolverInterop` / `BenchInterop`.

Consequence for the two pages:

| page | host | works with |
| --- | --- | --- |
| `index.html` (testbed) | Web Worker, sync exports | single-threaded bundles only |
| `bench.html` (benchmark) | main thread, async exports | both |

Making the interactive testbed work with MT means moving it to the main-thread + async-export
model too, and re-solving response streaming: `SendResponse` is a `[JSImport]`, and JS interop is
per-thread, so progress callbacks raised on a pool thread have to be marshalled back (there is a
`SynchronizationContext` hop in `SolverInterop.DispatchResponse` for this, **untested under MT**).
The benchmark path sidesteps all of it by returning results by value.

## Known prototype limitations

- **Cancellation in single-threaded builds.** The runtime blocks its worker for the whole solve, so
  a queued `cancel` is never read; the page tears the worker down and reboots instead. That also
  drops the solver's true-candidates cache. Threads-enabled builds cancel cooperatively.
- **The command processor is a copy**, not a refactor, of `WebsocketListener`. If this graduates,
  merge the two into one shared processor so they cannot drift.
- **`additionalConstraints` is not wired up** — the console's `-c` equivalent is ignored here.
- **Memory ceiling.** WASM is 32-bit; large `count` runs that are fine natively can exhaust the
  browser heap.
