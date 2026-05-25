# C# Arrow Benchmark

This is the C# native port of the simplified arrow benchmark in `../js-chrome`. It is intentionally standalone and does not call the production SudokuSolver code.

The solver mirrors the hardened JavaScript benchmark contract:

- input is the ISS `.Arrow~R1C1~...` text from `../../iss-arrow-data.txt`
- candidates are 9-bit masks
- setup and solve timing are separate
- timed solve uses a preallocated grid pool, explicit frame stack, fixed constraint queue, support masks, and branch-selection scratch fields
- tracing is opt-in via `--trace-limit`; the default benchmark path does not allocate trace entries

## Run

```powershell
dotnet run -c Release --project benchmarks/csharp/SudokuArrowCSharpBench.csproj -- --warmup=10 --samples=30
```

From this folder, the equivalent command is:

```powershell
dotnet run -c Release -- --warmup=10 --samples=30
```

Expected check values for `../../iss-arrow-data.txt`:

- `solutions`: `1`
- `guesses`: `8213`
- `valuesTried`: `20216`
- `traceHash`: `cf840d07`

Use `--puzzle <path>` to run another ISS arrow-text puzzle.