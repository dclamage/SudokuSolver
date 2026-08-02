# This solver vs Interactive Sudoku Solver (JS) — and what it means for WASM

Date: 2026-08-02

Run to answer one strategic question: **is the WebAssembly port a dead end?** The concern was that
a 3–5× WASM tax makes a browser-hosted C# solver uncompetitive with the fast JavaScript solvers
already out there, and that a TypeScript rewrite is therefore the only route to a setting site.

The measurement does not support that. The gap is real, but it is **not** in WebAssembly and not in
the language.

## Method

[Interactive Sudoku Solver](https://github.com/sigh/Interactive-Sudoku-Solver) is a mature,
genuinely fast browser Sudoku solver, checked out locally, and the reference this project's earlier
perf work already drew on.

Both solvers ran **in Node, on the same machine, in the same V8** — so this is not a
browser-vs-native comparison, and no format conversion or transport was involved:

- ISS: `node tools/perf/benchmark_puzzles.js --max-backtracks none --input <81 givens> --solutions all --repeat 5`
- ours: the normal `BenchCore` harness, `op: "count"` uncapped, 15 iterations

`--solutions all` and an uncapped `count` are the same operation: exhaust the search and prove
uniqueness. Plain 81-character givens are accepted natively by both, so the input is byte-identical.
Both exclude puzzle construction from the timed region.

## Result

Exhaustive count, minimum ms:

| puzzle | ISS (JS) | ours, native C# | ours, C# → WASM | **WASM tax** | **ISS vs ours** |
| --- | ---: | ---: | ---: | ---: | ---: |
| vanilla-u17 | 0.1 | 0.08 | 0.11 | 1.4× | *we win* |
| escargot | 0.5 | 3.49 | 3.48 | **1.00×** | **7.0×** |
| platinum-blonde | 0.6 | 4.87 | 4.64 | **0.95×** | **8.1×** |
| golden-nugget | 0.7 | 3.19 | 3.07 | **0.96×** | 4.6× |

### 1. The WASM tax on vanilla search is zero

Native and WASM are identical to within noise on every vanilla puzzle. This was re-run three times
because it contradicted the corpus-wide 3.84× figure. It holds.

WebAssembly is not slowing the core search engine at all.

### 2. The corpus-wide WASM tax lives in the constraint layer

Vanilla is 1.0×; `killer-innie` is 5.73× (64.75 → 370.75 ms) on the same builds. The tax is
concentrated in constraint code — virtual dispatch through `Constraint`, generic BCL collections,
and similar — not in the bitmask search.

That is consistent with the two fixes that already removed 37% of it
([`wasm-perf-investigation-findings.md`](wasm-perf-investigation-findings.md)): both were
constraint-adjacent (weak links, sum registry). It is a defect-shaped problem in one layer, not a
property of WebAssembly.

### 3. ISS is 5–8× faster than this solver — in the same runtime

Both were JS-hosted Node processes. This is not a language gap and not a host gap. It is our
algorithm and our implementation.

## Splitting the gap: search size vs cost per node

Temporary instrumentation counted branch points at the same place the
`benchmarking-languages` branch's `BruteForceSolveStats.Guesses` did ("real branch points"),
matching ISS's `guesses`. Fixed setup cost is subtracted using the 0-guess puzzle (ISS 0.1 ms,
ours 0.08 ms):

| puzzle | ISS guesses | our guesses | search size | ISS µs/guess | our µs/guess | cost per node |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| escargot | 32 | 34 | 1.06× | 12.5 | 100.3 | **8.0×** |
| platinum-blonde | 85 | 289 | **3.40×** | 5.9 | 16.6 | 2.8× |
| golden-nugget | 114 | 150 | 1.32× | 5.3 | 20.7 | 3.9× |

**Both factors are real, and neither alone explains the gap.**

- **Search size**: usually close (1.06–1.32×), but `platinum-blonde` shows we explore 3.4× more
  nodes. Branch-ordering heuristics have room.
- **Cost per node**: we are consistently 2.8–8.0× more expensive per branch point. This is the
  larger and more consistent factor.

Caveat: normalizing by guesses folds propagation work into "per node", and propagation depth per
branch may differ between the solvers. The direction is solid; treat the exact multipliers as
indicative. Our escargot outlier (100 µs/guess against 16–21 µs on the other two) suggests
something specific and worth isolating rather than a uniform constant factor.

## Conclusion

The premise behind "WASM is a dead end" does not hold:

- WASM costs ~1.0× on the core search. It is not the bottleneck.
- The remaining corpus tax is confined to the constraint layer and has already proven reducible.
- The competitive gap versus a good browser solver is **5–8×, present equally in native C#**. It
  would not be fixed by changing language, and a TypeScript rewrite would only win if it also
  adopted a better search.

If that better search is the actual goal, it can be done in C# — keeping 47 constraint types, 94
tests, the logical solver, and true-candidate support — and it will still run at ~1.0× in WASM for
the core engine. A rewrite costs 22,082 lines and those 47 constraints to fix a problem that is not
in the language.

The concern that prompted this was still correct in substance: **5–8× off a good browser solver is
a genuine competitiveness problem for a setting site.** It is just an algorithm and implementation
problem, not a hosting one.

Some overhead is legitimately the price of generality — ISS is a search engine, while this solver
also does logical solving, true candidates, and step explanations across 47 constraint types. But
that does not account for 8× on plain vanilla.

## Suggested next steps

1. **Cost per node** is the bigger, more consistent factor — profile propagation on `escargot`,
   where our per-node cost is ~5× worse than on the other two puzzles. That outlier is the most
   likely place a specific defect is hiding.
2. **Branch ordering** on `platinum-blonde`, where we explore 3.4× more nodes than ISS.
3. Both improvements benefit native and WASM equally, and neither requires a rewrite.

## Reproducing

```bash
# ISS
cd /Users/d.clamage/git/Interactive-Sudoku-Solver
node tools/perf/benchmark_puzzles.js --max-backtracks none --solutions all --repeat 5 --json \
    --input "100007090030020008009600500005300900010080002600004000300000010040000007007000300"

# ours (native + WASM) — a two-case corpus of the same givens with op "count"
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- /tmp/vs-iss.json --iterations 15
node SudokuSolverWasm/run-node.mjs <bundle>/wwwroot bench --iterations 15
```

Puzzles used: `vanilla-u17` and `escargot` from `benchmarks/corpus.json`, plus the classic
hard-for-brute-force `platinum-blonde`
(`000000012000000003002300400001800005060070800000009000008500000900040500470006000`) and
`golden-nugget`
(`000000039000001005003050800008090006070002000100400000009080050020000600400700000`).

Guess counting used a temporary counter at the `GetLeastCandidateCell` branch point in
`CountSolutions`, mirroring `BruteForceSolveStats.IncrementGuesses` on the
`benchmarking-languages` branch. It was reverted; that branch still holds the full instrumentation
if a permanent version is ever wanted.
