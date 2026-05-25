# JavaScript Chrome Arrow Benchmark

This is the first equal-code benchmark contender. It is intentionally anchored on ISS, not the C# solver:

- input is the ISS `.Arrow~R1C1~...` text from `../../iss-arrow-data.txt`
- candidates are 9-bit masks
- hot data uses typed arrays
- search uses a preallocated grid pool and explicit frame stack
- the default hot solve loop does not allocate per branch or per propagation step
- conflict scores are seeded from Sudoku houses and arrow/sum constraints using the ISS priority shape
- setup and solve timing are separate

The Chrome runner measures inside the browser page with `performance.now()`. Browser startup, file I/O, page load, and Puppeteer IPC are outside the reported setup/runtime timings.

## Benchmark Contract

The simplified solver is meant to become the shared algorithm for the language comparison. It is not an ISS rewrite, but it should avoid obvious runtime-specific artifacts:

- setup may allocate parser output, arrow tuple tables, lookup tables, result objects, and sample arrays
- timed solve uses preallocated grid states, frame arrays, queue storage, support masks, and branch-selection scratch fields
- tracing is opt-in; `traceLimit: 0` keeps trace object creation outside the normal benchmark path
- arrow propagation uses precomputed valid tuple tables and support-mask filtering rather than calling back into parser or UI code
- the output counters and `traceHash` are expected to stay deterministic for a given algorithm revision

Real ISS remains the reference baseline for what a mature browser solver can do on the same puzzle. The simplified solver is the fair-port candidate for C#, Rust native, and Rust wasm.

## Quick Smoke Check

```powershell
node benchmarks/js-chrome/node-smoke.mjs
```

The smoke runner uses Node only to validate the algorithm while developing. It is not the ranked JavaScript-in-Chrome result.

## Chrome Run

```powershell
cd benchmarks/js-chrome
npm install
node chrome-runner.mjs --warmup=10 --samples=30
```

After the preallocated search rewrite, the expected check values for `../../iss-arrow-data.txt` are:

- `solutions`: `1`
- `guesses`: `8213`
- `valuesTried`: `20216`
- `traceHash`: `cf840d07`

## Real ISS Baseline

This runs the actual ISS parser, `solver_worker.js`, `SudokuBuilder`, and solver engine in Chrome against the same `../../iss-arrow-data.txt` puzzle. It reports the same sample/summary shape as the simplified benchmark, with ISS-native counters such as `constraintsProcessed`.

```powershell
cd benchmarks/js-chrome
node iss-baseline-runner.mjs --warmup=10 --samples=30
```

Add `--no-trace-hash` if you want to skip retrieving and hashing ISS's branch trace after each timed solve. Trace retrieval is outside the reported setup/runtime timings, but it adds post-sample overhead.

If Chrome is not discoverable by Puppeteer, set `CHROME_PATH` to the Chrome executable before running.