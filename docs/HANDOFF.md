# Handoff: solver performance & the browser port

Last updated 2026-08-03. Branch `wasm-prototype`, pushed, clean. Read this first, then the linked
docs as needed.

---

## 1. Where things stand

The goal behind all of this is a **browser-hosted Sudoku setting site** backed by this C# solver,
exporting to SudokuPad — a SudokuMaker rival with a much stronger solver. See
[`wasm-prototype-findings.md`](wasm-prototype-findings.md).

**The strategic question is settled: the WASM port is viable and a TypeScript rewrite is not
indicated.** The evidence, in one table — exhaustive count, same machine, same Node/V8:

| puzzle | ISS (JS) | ours native C# | ours WASM | WASM tax | ISS vs ours |
| --- | ---: | ---: | ---: | ---: | ---: |
| escargot | 0.5 ms | 3.49 | 3.48 | **1.00×** | 7.0× |
| platinum-blonde | 0.6 ms | 4.87 | 4.64 | **0.95×** | 8.1× |

The WASM tax on core search is **~1.0×**. The corpus-wide 3.84× is confined to the *constraint
layer*. And ISS beats us 5–8× **in native C# too**, so the competitive gap is algorithmic, not a
hosting or language problem. Full argument in
[`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md). Don't re-litigate this; start there.

**The 5–8× does not generalise, though** — it was measured on two hard vanilla classics. Across 402
real CTC puzzles imported from the ISS index, **we are faster than ISS on 84% of them and ~2.6×
faster at the median** (p50 0.38×, p90 1.39×). The gap is confined to hard vanilla and to a thin
pathological tail (12 puzzles >5×, worst >3,300×). For a setting site, which runs variants, the
relevant distribution is favourable. Data and caveats:
[`iss-corpus-import.md`](iss-corpus-import.md).

### Infrastructure you now have

- **28-case corpus**, 5 ops: `count`, `solve`, `logical`, `truecandidates`, `estimate`.
  `benchmarks/README.md` documents each. `truecandidates` is the operation a setting UI actually
  runs on every edit; `logical` is the half the product leans on hardest.
- **398-case ISS corpus** in `benchmarks/corpus-iss.json`, generated from sigh's CTC index with
  `--import-iss` and validated against the solution counts ISS recorded — **402 agree, 0 disagree**.
  Split `iss-tune` (280) / `iss-holdout` (118) by a hash of the puzzle id, so re-importing never
  reassigns a puzzle. **Never tune against the holdout.**
- **`SolverFactory.CreateFromIss`** — parses the ISS text format, now covering 30% of the index.
- **WASM prototype** in `SudokuSolverWasm/` (not in the .sln). Same JSON protocol as the websocket
  server. `run-node.mjs` for headless 1T, `drive-chrome.mjs` for MT (MT WASM refuses to run outside
  a browser).

---

## 2. Recommended order of work

Priority 1 is **done**, which is what unblocks Priority 2.

### ~~Priority 1~~ — Corpus expansion: DONE

398 cases in `benchmarks/corpus-iss.json`, 402 agree / 0 disagree, tune/holdout split in place. The
self-validation earned its keep immediately: it found **four silent-wrong-answer defects**, including
a digit set the parser was ignoring (`.Shape~9x9~0-8` solved a different puzzle) and a cancelled
`CountSolutions` being indistinguishable from a completed one. All fixed, all covered by tests (111,
up from 102). Full write-up: [`iss-corpus-import.md`](iss-corpus-import.md).

**The one thing to know before using it:** the holdout split is a pure function of the puzzle id
precisely so that re-importing can't move a puzzle out of the holdout. Don't replace it with anything
random, and don't tune against `iss-holdout`.

Remaining upside, in value order — details in the doc's §5:

- **NFA/Pair is worth 237 more puzzles**, roughly doubling the corpus. `NFAConstraint` already
  deserializes ISS's own serializer format; the blocker is structural (it can't be built from a
  constraint string, and `IssParser` only produces strings), and the f-puzzles path at
  `SolverFactory.cs:1017` already shows the shape needed.
- Cheap: `GreaterThan` with 3–5 arguments (6 puzzles) — implement the chain reading and let the
  import adjudicate it.
- Don't chase: ISS's `Var`/`Or`/`And`/`Replicate` DSL (note it has **block structure** with `.End`
  terminators), weighted `Sum`, and `LittleKiller` (ISS records no direction, so it can't be inferred).

### Priority 2 — Deferred (heuristically triggered) weak-link discovery

**This is the single largest performance lever found so far**, and it cuts both ways:

| case | discovery ON | OFF | |
| --- | ---: | ---: | --- |
| variant-cloneways | 20.42 ms | 0.90 ms | **22.6× faster off** |
| variant-equalsums | 14.33 ms | 0.69 ms | **20.7× faster off** |
| escargot | 0.99 ms | 0.12 ms | 8.5× faster off |
| kropki-search | 2225 ms | 10630 ms | 4.8× *slower* off |
| variant-orbit | 17.37 ms | 303.97 ms | **17.5× slower off** |

Corpus total still favours ON, so **do not flip the default**. Full data in
[`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md).

**Tune the trigger against `--filter iss-tune` and confirm on `--filter iss-holdout`** — that is what
the new corpus is for. The 28-case corpus is too small to separate a threshold honestly.

**The design to implement** (self-limiting, so it cannot over-fit — a *predictive* trigger that
classifies puzzles is exactly the trap to avoid): don't run discovery up front. Start brute force;
if the search exceeds N nodes, stop, run discovery, restart. Every corpus case separates cleanly at
N ≈ 5,000, and the mechanism generalises because "the search is actually expensive" is precisely the
condition under which discovery pays.

Costs to be honest about: pure-cost cases (`killer-innie`, `blank6`) still trigger and gain nothing,
plus a wasted prefix. Restarting means discarding partial work — fine for count/solve/true-candidates.

**Also worth fixing:** on `littlekiller-10`, `killer-cage` and `escargot`, discovery *increases* node
count (7,550 vs 2,554 on the first). It degrades the conflict-score branch ordering. That looks like
a defect rather than an inherent trade-off, and fixing it would remove the downside for a whole group
of puzzles without giving up `kropki`/`orbit`.

### Priority 3 — Buffer-reusing `Combinations`

Worth **several hundred MB** on logical solves, which is the largest demonstrated browser memory
problem (2 GiB heap, weaker GC). Attribution is done:
[`logical-solver-allocation.md`](logical-solver-allocation.md).

Allocation is `FindFishes` + `FindWings`, **not AIC** (AIC runs *twice* per logical solve). The
`List<T>` allocated per combination is the cost: ~4,400 combinations per `FindFishes` call × ~72
bytes × ~1,500 calls ≈ 480 MB, matching the 570 MB the bisect attributes to fishes.

**Do:** add a *separate* buffer-reusing method — don't change `Combinations`, whose 19 call sites all
rely on fresh-list semantics. Adopt it only in call sites individually verified not to retain or
defer the yielded list, `FindFishes`/`FindWings` first. Document the borrowed-buffer contract the way
`CountSolutions`'s `solutionEvent` is documented. **A single retaining caller produces silent,
data-dependent wrong answers** — the audit is the work, not an afterthought.

### Priority 4 — Smaller, well-defined items

- **`renban-sky-logical` allocates 2.4 MB per `StepLogic` call**, 4× `killer-innie`'s rate, in only 4
  `ConsolidateBoard` passes. Unexplained by the combination arithmetic. Nothing has instrumented it.
- **`platinum-blonde` is still 8.9× off ISS** even with discovery disabled (the other hard classics
  drop to 1.6–2.2×). It's the cleanest remaining outlier, and it explores 3.4× more nodes than ISS.
- **Four new pathological cases from the ISS import**, all far worse than `platinum-blonde` and none
  previously known. The best one to start on is `1HuNjcLWlPE` "N is for Naomi": ISS solves it in
  **72 ms**, we did not finish in **240 s**, and it is only whispers, renban and dots — four
  constraint types, so the diagnosis surface is small. Then `h-ymyScJa2s` (93×), `OqyXKDOhfDA` (53×),
  `blPgSzctUMg` (63×). All are in `iss-tune`, except the timeouts which are excluded from the corpus;
  the .iss text is in the report the importer writes. See
  [`iss-corpus-import.md`](iss-corpus-import.md) §2.
- **The constraint-layer WASM tax**: vanilla is 1.0×, `killer-innie` is 5.73×. Two dispatch fixes
  already took 37% off corpus-wide. Same defect class is worth hunting: comparer/delegate dispatch in
  inner loops is mildly costly natively and severe under Mono AOT.
- **WASM startup cost is unmeasured** (runtime boot + module instantiation). ~270 ms and 2.8 MB
  brotli measured informally in Node; never measured properly in a browser. Matters for the product.
- **`EstimateTrueCandidates` is unpooled** — same shape as `EstimateSolutions`, which is now pooled,
  but it has no benchmark case, so changing it would be unmeasured. Add a case first.
- **MT pooling** stays off for `TrueCandidates`/`FindSolution`. Per-invocation local caches are **not
  worth building**: the pool already hits 99.99% (9.68M rents, 272 misses), so they'd only address
  lock contention, which remains unproven. See [`solver-pooling-audit.md`](solver-pooling-audit.md).

---

## 3. Working with Sol (GPT-5.6-sol via Codex)

Sol earned its keep this session: it found a bug I'd missed entirely, and corrected three claims I'd
made too strongly. Use it for **design review and hard diagnosis**, not bulk work — the Codex token
window is shared and capped.

### Mechanics

```bash
# Prompt MUST go via stdin. Passing it as an argument makes codex block on stdin and time out.
codex exec -s read-only -c model=gpt-5.6-sol -c model_reasoning_effort=high < /tmp/prompt.md
```

Run it backgrounded and poll — a `high`-effort review takes 10–20 minutes. It reads the repo itself
(read-only sandbox), so give it file paths and line numbers rather than pasting code.

### What made the consultations work

1. **Measure first, then ask.** Both useful reviews came from bringing real numbers. Sol's best
   catch came directly from a ratio I'd reported without understanding (17–19 bytes per pool rent →
   "that's a display-class allocation").
2. **Ask it explicitly to say where you're wrong.** It did, three times, and was right each time.
3. **Give it the product context.** "Target is a browser, 2 GiB heap, most calls are trivial" changed
   its recommendation materially.
4. **Structure as numbered questions** and it answers them in order.

### Its track record here, for calibration

- **Found** the `PushSolver` closure bug: the `Task.Run` lambda captures `solver`, so the compiler
  builds the display class on method entry, allocating on every *declined* offer. This was the
  session's biggest MT win and I had misdiagnosed it as accepted-task churn.
- **Corrected** "cross-thread release doesn't happen" (it does, via `PushSolver`), so thread-local
  pools would have been unsound.
- **Corrected** "lock contention causes the MT slowdown" — plausible but unproven, since the path is
  randomised.
- **Corrected** "converges to zero steady-state allocation" — only within one invocation; pools are
  call-scoped.
- **Right call** on copying the board in `ReportSolution` rather than special-casing "never release
  the winner", which would rot in every cleanup path.
- Also spotted the `EstimateSolutions` wasted clone and the `solutionEvent` aliasing hazard.

---

## 4. Methodology, hard-won

**Measure before optimising. I guessed wrong three times this session**, and each time a
five-minute counter settled it:

- `FastFindPairs`/`FastFindTriples` lambda sorts looked like a perfect delegate target. They run
  **2–6 times per solve** — `doAdvancedStrategies` is false at every brute-force call site.
- Recursive `Span<T>`/`stackalloc` was my confident explanation for the 23× skyscraper outlier. It
  was `List<int>.BinarySearch`'s comparer dispatch.
- MT allocation "must be" accepted-task churn. It was declined-offer closures.

Technique that works: drop a temporary `XxxCounters.cs` static class in `SudokuSolver/`, increment at
the sites in question, print from the harness, `git checkout --` to revert. Cheap and decisive.

### Benchmarking traps in this repo

- **`killer-innie` is high-variance**: 106 ms at 5 iterations vs 56 ms at 15. Use ≥15 iterations or
  its numbers mislead. It produced a fake "+46% REGRESSION" once.
- **`truecandidates` search is randomised** (`RandomNext` in cell selection), so wall-time
  comparisons on `tc-blank-nonconsecutive` are unreliable — allocation is the trustworthy signal.
  `count` ops are deterministic and safe to time.
- **MT timing is noisy**; allocation is the reliable MT signal too.
- **Always A/B paired** via `git stash push -- SudokuSolver/`, measure, `git stash pop`. Stale
  `--baseline` files caused three false regressions in one run.
- **`truecandidates` raw counts are non-deterministic** — the solver returns unclamped counts and
  callers clamp them. Score clamped or results won't reproduce.
- Wrap anything long in `caffeinate -i`; this laptop idle-sleeps and silently suspends builds.

### Validation checklist before committing

```bash
dotnet test -c Release SudokuTests/SudokuTests.csproj                                    # 102 tests
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3        # 0 FAIL
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- --iterations 3 --multithread
dotnet build -c Release SudokuSolver.sln                                                 # sln excludes the WASM project
```

Add the ISS corpus to that list for anything touching the solver or `IssParser` — 398 puzzles with
known answers is the strongest correctness signal available, and it takes about 15 s per iteration:

```bash
dotnet run -c Release --project benchmarks/SudokuSolverBenchmark -- benchmarks/corpus-iss.json --iterations 1
```

For WASM changes, republish and re-diff — `dotnet publish SudokuSolverWasm -c Release -o /tmp/wasm`
takes 7–10 minutes, so budget for it. `compare.js` flags result mismatches, not just timing.

---

## 5. Reading order for a cold start

1. This file.
2. [`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md) — why WASM isn't the problem.
3. [`iss-corpus-import.md`](iss-corpus-import.md) — the 398-puzzle corpus, where we actually stand
   against ISS, and the coverage ceiling.
4. [`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) — the biggest lever.
5. [`logical-solver-allocation.md`](logical-solver-allocation.md) — the browser memory problem.
6. [`solver-pooling-audit.md`](solver-pooling-audit.md) + [`truecandidates-allocation.md`](truecandidates-allocation.md) — what's already pooled and why MT is deliberately off.
7. [`wasm-prototype-findings.md`](wasm-prototype-findings.md) — the .NET-WASM constraints that dictate host architecture.
8. `SudokuSolverWasm/README.md` — how to build and run the browser prototype.

`docs/optimization-roadmap.md` predates this work and covers the earlier per-session solver-perf
plan; it is still accurate but narrower in scope.
