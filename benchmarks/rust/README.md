# Rust Arrow Benchmark

This crate is the Rust port of the simplified arrow benchmark used by `../js-chrome` and `../csharp`. The native CLI and wasm exports share the same solver core.

The benchmark contract mirrors the other fair-port implementations:

- input is the ISS `.Arrow~R1C1~...` text from `../../iss-arrow-data.txt`
- candidates are 9-bit masks
- setup and solve timing are separate
- timed solve uses a preallocated grid pool, explicit frame stack, fixed constraint queue, support masks, and branch-selection scratch fields
- tracing is opt-in via `--trace-limit`; the default benchmark path does not allocate trace entries

## Native Run

```powershell
cargo run --release --manifest-path benchmarks/rust/Cargo.toml --bin sudoku-arrow-rust-native -- --warmup=10 --samples=30
```

From this folder, the equivalent command is:

```powershell
cargo run --release -- --warmup=10 --samples=30
```

Expected check values for `../../iss-arrow-data.txt`:

- `solutions`: `1`
- `guesses`: `8213`
- `valuesTried`: `20216`
- `traceHash`: `cf840d07`

## Wasm Build

The crate includes `wasm32-unknown-unknown` exports for host-timed setup and solve calls. The wasm target must be installed before building:

```powershell
rustup target add wasm32-unknown-unknown
cargo build --release --manifest-path benchmarks/rust/Cargo.toml --target wasm32-unknown-unknown --lib
```

Then run the Chrome harness:

```powershell
node benchmarks/rust/wasm-chrome-runner.mjs --warmup=10 --samples=30
```

The runner uses the existing `puppeteer-core` dependency installed under `../js-chrome`; run `npm install` there first if needed.

The exported functions are `bench_alloc`, `bench_dealloc`, `bench_setup`, `bench_solve`, `bench_free_state`, and counter getters such as `bench_get_guesses` and `bench_get_trace_hash`. The intended timing model is that JavaScript brackets the whole `bench_setup` and `bench_solve` exports with `performance.now()`; the solver does not call back into JavaScript during setup or search.