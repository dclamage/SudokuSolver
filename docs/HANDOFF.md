# Handoff: solver performance & the browser port

Last updated 2026-08-03. Branch `wasm-prototype`, working tree clean, pushed. Read this first, then
the linked docs as needed.

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

The WASM tax on core search is **~1.0×**; the corpus-wide 3.84× is confined to the *constraint layer*.
And on these two puzzles ISS beats us 5–8× **in native C# too**, so whatever gap exists is algorithmic
rather than a hosting or language problem. That is the part not to re-litigate — full argument in
[`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md).

**Don't read the 5–8× as a general verdict, though.** Those are two hard *vanilla* classics. Across
402 real CTC puzzles imported from the ISS index, **we are faster than ISS on 84% of them and ~2.6×
faster at the median** (p50 0.38×, p90 1.39×). The deficit is confined to hard vanilla and to a thin
pathological tail (12 puzzles >5× slower, worst >3,300×). Since the product runs variant puzzles, the
relevant distribution is the favourable one. Data and caveats:
[`iss-corpus-import.md`](iss-corpus-import.md) §2.

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

### What landed in the last session

**Deferred weak-link discovery** — the Priority 1 item — is built, tuned and **on by default**.
`WeakLinkDiscoveryMode` (`Always`/`Never`/`Deferred`) is now a real solver option with
`Solver.WeakLinkDiscoveryNodeThreshold` beside it, default N=2000, chosen by sweeping `iss-tune` and
confirmed on `iss-holdout` (0 result mismatches on either). Full write-up:
[`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) § "Deferred discovery, as built".

**It is a latency/throughput dial, not the free win the plan assumed.** Total corpus time and median
puzzle latency want opposite thresholds: raising N makes the typical puzzle much faster and a
handful of puzzles much slower. At the shipped N=2000 the median ISS puzzle is **~1.5× faster** while
total corpus time is **unchanged** (0.994× tune, 1.003× holdout), and the 28-case corpus is flat
(0.99–1.00×) once the untimeable randomised case is excluded. N≈5000, the value this file previously
suggested, is past the point where the total turns negative.

Three things it turned up that affect work elsewhere:

1. **Search-tree clones share `conflictScores` by reference**, so the abandoned attempt's branch
   ordering leaked into the retry — a 431% regression on `variant-orbit` until it was rolled back.
   Anything else that reruns a search on the same `Solver` needs `SnapshotConflictState`.
2. **`Never` cannot finish the ISS tune split at all** — killed after 8 minutes on one puzzle. Real
   CTC puzzles are effectively unsolvable without discovery, which is why the *bounded prefix* shape
   is right and a predictive on/off classifier is not: a misclassification there hangs.
3. **It fixed `Wb5YT1b-U9Q`**, one of the five pathological outliers in Priority 5 below: 308 ms →
   21 ms against ISS's 7.4 ms. Worth re-measuring the others before diagnosing them.

**True-candidates timing was also made reproducible**, which was a prerequisite for measuring
anything on that path and immediately exposed a branch-order latency cliff of up to 64× — now
Priority 2. Its branch choice drew from a time-seeded thread-static `Random`; it now uses a stream
scoped to the invocation. `tc-blank-nonconsecutive` went from a ~40% run-to-run swing to ~1%.
**Treat every pre-2026-08-03 `tc-*` timing as optimistic**: each benchmark iteration used to be a
fresh draw from a wide distribution and the harness reports `min ms`, so old figures fall as
iteration count rises and are not comparable to current ones. The 28-case corpus total rose
15,887 → 17,243 ms purely from removing that bias.

### What landed the session before

`benchmarks/corpus-iss.json`, 398 cases, 402 agree / 0 disagree, tune/holdout split in place.
Write-up: [`iss-corpus-import.md`](iss-corpus-import.md). Two things it turned up:

1. **The self-validation found four silent-wrong-answer defects** in `IssParser`, plus one trap in
   `CountSolutions` — cancellation returns a partial count that looks completed, which can bite any
   caller with a timeout. All fixed and covered by tests;
   [`iss-corpus-import.md`](iss-corpus-import.md) §3 lists them.
2. **It produced the corrected ISS speed picture above**, and with it four new pathological outliers
   that are now the most promising perf targets in the file (Priority 5).

Note that the raw ISS data is **not in the repo**: the importer needs `mappings.json` and a directory
of `.iss` files fetched from the index site. `benchmarks/README.md` has the fetch commands; a fresh
machine has to run them before `--import-iss` will work.

---

## 2. Recommended order of work

### Priority 1 — The two remaining weak-link-discovery levers

Deferral is done (see above). Two follow-ups from
[`weak-link-discovery-tradeoff.md`](weak-link-discovery-tradeoff.md) remain, and they are
*complementary* to it rather than superseded — both attack the **cost** side, which is exactly what
deferral cannot: a puzzle that exceeds the budget still pays full price for discovery.

1. **Discovery *increases* node count on `littlekiller-10`, `killer-cage` and `escargot`** (7,550 vs
   2,554 on the first). It degrades the conflict-score branch ordering. That looks like a defect
   rather than an inherent trade-off, and fixing it would remove the downside for a whole group of
   puzzles without giving up `kropki`/`orbit`.
2. **Abort a discovery pass early when it is unproductive.** Discovery loops
   `do { … } while (innerResult == Changed)`. The pure-cost cases (`killer-innie`, `blank6`,
   `arrow-search` — identical guess counts with and without) might be detectable after a partial
   pass. Needs data on how many links each puzzle class actually yields; nothing has counted this.

If you revisit the deferral threshold itself, **tune against `--filter iss-tune` and confirm on
`--filter iss-holdout`**, and score the *ratio distribution*, not `total min ms` — the total alone
picks N=250, which makes the median puzzle slower. The 28-case corpus is too small to separate a
threshold honestly and is flat across the whole range.

### Priority 2 — The true-candidates branch-order cliff (up to 64×)

**A latency cliff on the operation the setting UI runs on every edit.**
`TrueCandidatesInternal` picks among equally-good cells at random, weighted by uncovered candidates.
Now that the stream is seeded deterministically (see above), sweeping the seed isolates what branch
order costs — ten seeds, `--iterations 12`:

| case | seeds 0–9 | verdict |
| --- | --- | --- |
| `tc-escargot` | 0.73–1.02 ms | insensitive |
| `tc-blank9` | 13.3–14.4 ms | insensitive |
| `tc-escargot-partial` | eight at 5.99–6.82 ms; then **76.8** and **381.3** | **heavy tail: 12× and 64×** |
| `tc-blank-nonconsecutive` | 9.1 … 28.7 s | broad, 2.9× |

The picker is **not** uniformly weak — it is fine on most puzzles. The problem is a **pathological
branch-order mode** that a minority of searches fall into, at 12–64× normal cost. So the fix is to
detect and escape that mode (the search already knows when coverage progress stalls), not to replace
the heuristic wholesale. Options in
[`truecandidates-allocation.md`](truecandidates-allocation.md) § "Branch order is a tail risk".

**Fixing the seed made this worse in the worst case, deliberately:** a puzzle in the bad mode is now
stuck there on every call rather than one call in five. That is the trade for being able to measure
it at all — and it is why this is a cliff to remove rather than an optimisation to schedule.

**Do not tune the seed** — the shipped value is the natural counter origin, chosen before any of this
was measured. A seed that dodges the bad mode on these four cases says nothing about the puzzles a
user opens.

**Keep the randomisation itself.** It is not a hack: true candidates is a coverage problem, and a
deterministic DFS yields consecutive solutions differing only in their last few assignments, so each
covers almost no new candidates. Any replacement has to preserve that decorrelation.

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

### Priority 4 — Extend ISS coverage to NFA/Pair

Optional, and not blocking anything — but it is the one remaining lever on *corpus size*, which is
what protects every heuristic above from over-fitting. Worth **237 more puzzles**, roughly doubling
the corpus. Coverage is currently 30% of the index and this is most of the reachable remainder.

`NFAConstraint` already deserializes ISS's own `NFASerializer` base64url format, so this is plumbing
rather than invention. The blocker is structural: it has `ConsoleName = null` and a constructor taking
`(solver, int[] cellIndices, string serializedNFA, name)`, so it cannot be built from a constraint
string — and `IssParser` produces strings for `SolverFactory.CreateFromGivens`, which calls
`FinalizeConstraints()` internally, after which constraints can no longer be added. The f-puzzles path
at `SolverFactory.cs:1017` already does the programmatic version; give `IssParser` that shape.

Check two things first: that ISS's own serialized strings round-trip through `NFADeserializer`
(compatibility is by construction but untested against ISS's output), and how `.Pair`'s
`~~`-separated groups and `_named-relation` segments are meant to be read. Many `.NFA` instances
address ISS variables rather than cells and stay out of reach regardless.

Also cheap while you are in there: `GreaterThan` with 3–5 arguments (6 puzzles) is probably a
descending chain, i.e. one reversed thermometer. It is ambiguous against "greater than each of", so
implement the chain reading and **let the import adjudicate** — a wrong reading over-constrains and
will show up as a disagreement. Don't chase ISS's `Var`/`Or`/`And`/`Replicate` DSL (it has block
structure with `.End` terminators), weighted `Sum`, or `LittleKiller` (ISS records no direction, so it
cannot be inferred). Details in [`iss-corpus-import.md`](iss-corpus-import.md) §5.

### Priority 5 — Smaller, well-defined items

- **`renban-sky-logical` allocates 2.4 MB per `StepLogic` call**, 4× `killer-innie`'s rate, in only 4
  `ConsolidateBoard` passes. Unexplained by the combination arithmetic. Nothing has instrumented it.
- **`platinum-blonde` is still 8.9× off ISS** even with discovery disabled (the other hard classics
  drop to 1.6–2.2×). It's the cleanest remaining outlier, and it explores 3.4× more nodes than ISS.
- **New pathological cases from the ISS import**, far worse than `platinum-blonde` and none previously
  known. Start with `1HuNjcLWlPE` "N is for Naomi": ISS solves it in **72 ms**, we did not finish in
  **240 s**, and it uses only whispers, renban and dots — four constraint types, so the diagnosis
  surface is small. Then `h-ymyScJa2s` (9.7 s vs 105 ms), `OqyXKDOhfDA` (>10 s vs 411 ms),
  `blPgSzctUMg` (887 ms vs 14 ms). `Wb5YT1b-U9Q` was on this list at 308 ms and **deferred discovery
  fixed it** (21 ms, against ISS's 7.4 ms) — so **re-measure the rest under the new default before
  diagnosing anything**, since every timing here predates it. `blPgSzctUMg` did *not* benefit; it is
  one of the few cases deferral costs (603 → 709 ms on the tune split), which makes it the better
  target of the two.
  Only `blPgSzctUMg` is in the corpus (`iss-tune`); the rest exceed the import's ceilings, so **fetch
  their `.iss` text by id from the index site** — the import report records outcomes and timings, not
  puzzle text. See [`iss-corpus-import.md`](iss-corpus-import.md) §2.
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

Sol has earned its keep here: it found a bug that had been missed entirely, and corrected three claims
that had been made too strongly. Use it for **design review and hard diagnosis**, not bulk work — the
Codex token window is shared and capped.

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
  builds the display class on method entry, allocating on every *declined* offer. This was the biggest
  MT win to date, and it had been misdiagnosed as accepted-task churn.
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

**Measure before optimising.** Confident guesses have been wrong here at least four times, and each
time a five-minute counter settled it:

- `FastFindPairs`/`FastFindTriples` lambda sorts looked like a perfect delegate target. They run
  **2–6 times per solve** — `doAdvancedStrategies` is false at every brute-force call site.
- Recursive `Span<T>`/`stackalloc` was the confident explanation for the 23× skyscraper outlier. It
  was `List<int>.BinarySearch`'s comparer dispatch.
- MT allocation "must be" accepted-task churn. It was declined-offer closures.
- Two ISS puzzles "disagreed" with their known solution counts, which looked like translation bugs.
  Both were just slow, and the real bug was in the harness reading a cancelled count as a completed
  one.

Technique that works: drop a temporary `XxxCounters.cs` static class in `SudokuSolver/`, increment at
the sites in question, print from the harness, `git checkout --` to revert. Cheap and decisive.

The same applies to *semantics*, not just performance: when it mattered whether a constraint means
the same thing after a translation, the decisive move was an exhaustive count on a 6x6 — small enough
to enumerate fully, big enough to be a real puzzle — comparing the two forms. That settled four line
types in minutes and contradicted the intuition on two of them.

### Benchmarking traps in this repo

- **`killer-innie` is high-variance**: 106 ms at 5 iterations vs 56 ms at 15. Use ≥15 iterations or
  its numbers mislead. It produced a fake "+46% REGRESSION" once.
- **`--iterations 1` measures tier-0 JIT code**, not steady state. The harness's single warm-up call
  does not escape tiered compilation, so anything under ~100 ms reads several times too slow. This
  produced *two fabricated findings* in one sitting — a "6.4× spread" on `tc-escargot` and a "3.4×"
  on `tc-blank9`, both of which are actually flat within 1.1×. Beware the specific trap of reasoning
  that the workload is deterministic so one iteration is enough: determinism removes *path* variance,
  not warm-up. Use ≥10 iterations for anything sub-100 ms regardless.
- **`truecandidates` timing is now trustworthy** (fixed 2026-08-03) — it used to be the loudest trap
  here. Its branch choice is still randomised, but from a stream scoped to the invocation instead of
  a time-seeded global, so runs repeat. Beware old numbers: each benchmark iteration used to be a
  fresh draw from a very wide distribution, so `min ms` was an order statistic that got
  systematically *lower* the more iterations you ran. Any pre-2026-08-03 `tc-*` timing is optimistic
  and not comparable to a current one.
- **MT timing is noisy**; allocation is the reliable MT signal too.
- **Always A/B paired** via `git stash push -- SudokuSolver/`, measure, `git stash pop`. Stale
  `--baseline` files caused three false regressions in one run.
- **`truecandidates` returns unclamped counts** and callers clamp them, so score clamped. Raw counts
  now reproduce run to run, but they still depend on how many solutions the search happened to
  enumerate, which is a search-order property rather than an answer.
- **A cancelled `CountSolutions` is indistinguishable from a completed one.** It swallows
  `OperationCanceledException` and returns the partial count. Any timeout-bounded caller must check the
  token itself, or a timed-out count silently reads as a real answer — which is exactly how two fake
  "disagreements" got reported during the ISS import.
- **`corpus-iss.json` membership is wall-clock gated**, so it is not bit-reproducible: regenerating on
  a slower machine quietly drops the slowest cases. Treat the committed file as the artefact.
- Wrap anything long in `caffeinate -i`; this laptop idle-sleeps and silently suspends builds.

### Validation checklist before committing and pushing

```bash
dotnet test -c Release SudokuTests/SudokuTests.csproj                                    # 111 tests
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

**Then push.** `git push` once the checklist is green, rather than stacking commits locally to the end
of a session — sessions get interrupted, and this branch is the only record of a lot of measurement
that is expensive to reproduce. Keeping the header line of this file accurate about the branch state
is part of the same habit.

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
