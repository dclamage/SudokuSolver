# Branch-solver pooling: coverage audit and the multi-threading question

> **Updated 2026-08-02** after a design review by GPT-5.6-sol, which corrected three claims below,
> and after implementing lazy pool growth and `FindSolution` pooling. Corrections are marked
> inline.

Date: 2026-08-02

Follow-up to [`truecandidates-allocation.md`](truecandidates-allocation.md), answering two
questions: do all brute-force entry points pool their branch solvers, and what should
multi-threading do?

## Coverage (originally 2 of 5 — the heading said "4" while the table listed five entry points)

| entry point | pooled (1T) | pooled (MT) | per-node clone site |
| --- | --- | --- | --- |
| `CountSolutions` | yes | **yes** (`isThreadSafe: true`) | — |
| `TrueCandidates` | yes | no | `SolverBruteForce.cs:1031` uses the pool |
| `FindSolution` | **yes** (added) | no | — |
| `EstimateSolutions` | **no** | no | `SolverBruteForce.cs:1236` |
| `EstimateTrueCandidates` | **no** | no | `SolverBruteForce.cs:1555` |

`FindSolution` is the notable gap: it backs the `solve` op, and `variant-cloneways` allocates
**57 MB** solving a single puzzle.

### FindSolution has a correctness hazard that must be handled first

`FindSolutionState.ReportSolution` stores a **reference**, not a copy:

```csharp
Interlocked.CompareExchange(ref result, solver.board, null);
```

If the reporting solver were released to the pool and later rented out, the returned solution would
be silently overwritten. `CountSolutions` has no such issue because it only counts. So pooling
`FindSolution` requires either never releasing the winning solver, or copying its board at report
time. This is a real trap, not a theoretical one, and is why the change was not made blind.

The two `Estimate` paths clone one child per candidate value inside their sampling loop and have
the same shape, but no equivalent aliasing hazard.

## Multi-threading: the existing pool is the wrong shape

`CountSolutions` does pool under MT, so the pattern exists. The ownership rule is clean: whoever
pops a solver owns it, `PushSolver` transfers ownership to the new task, and each task releases
every solver it drops plus whatever remains on its stack at exit.

Applying exactly that to `TrueCandidates` **works and validates**, but is a net regression for the
workload that matters:

| case | MT unpooled | MT pooled | |
| --- | ---: | ---: | --- |
| tc-escargot | 0.12 MB | 15.39 MB | **128x worse** |
| tc-blank9 | 6.20 MB | 15.61 MB | worse |
| tc-blank-nonconsecutive | 5,353 MB | 120 MB | 44x better, but 824 ms -> 1,256 ms |

Two distinct problems:

1. **Fixed preallocation cost.** The MT pool is sized `(NUM_CANDIDATES + 1) x maxRunningTasks` —
   roughly 7,300 solvers, ~15 MB — and it is built on *every call*, however little searching
   follows. A setting UI runs true candidates on every grid edit, so most calls are small. Paying
   15 MB each time is worse than the allocation it avoids.
2. **Lock contention.** `BruteForceSolverPool` is one shared pool behind a single `syncLock`, taken
   on every rent and release. At ~18.8M nodes across threads that is heavy contention, and is the
   likely cause of the wall-time regression on the one case pooling should have helped most.

The change was therefore reverted; `TrueCandidates` pools single-threaded only, which is committed
and validated.

### What a real MT solution looks like

Neither problem is inherent, and both fixes are in the pool rather than its callers:

- **Grow lazily.** Rent allocates on miss and keeps the solver on release, instead of building the
  full array up front. Small calls then cost roughly what they cost unpooled, and long searches
  still converge to zero steady-state allocation. This also removes the ~1.5 MB the single-threaded
  path currently charges trivial calls.
- **Per-thread pools.** Give each worker its own pool and drop the lock entirely. Ownership is
  already per-task, so this fits the existing model; the only wrinkle is a solver rented on one
  thread and released on another, which the current ownership rule already avoids.

Both changes would benefit `CountSolutions` MT as well, which pays the same contention today.

## Priority

1. **Lazy pool growth** — removes the fixed cost that makes MT pooling a regression, and helps 1T
   trivial calls. Smallest change, broadest benefit.
2. **`FindSolution` pooling (1T)** — clear win (57 MB on one solve), but fix the `ReportSolution`
   aliasing first.
3. **Per-thread pools** — unlocks MT pooling properly.
4. **The two `Estimate` paths** — least used, no hazards, mechanical once the above lands.


## Implemented

### Lazy pool growth

The pool now starts empty and clones on a miss, retaining only the search's high-water mark of
simultaneously live branches. This was the fix for the eager-preallocation problem and it removed
the trivial-call regression entirely:

| case | unpooled (original) | eager pool | **lazy pool** |
| --- | ---: | ---: | ---: |
| tc-escargot | 0.11 MB | 1.59 MB | **0.07 MB** |
| tc-blank9 | 19.1 MB | 1.56 MB | **0.13 MB** |
| tc-blank-nonconsecutive | 18,756 MB | 1.58 MB | **0.09 MB** |

~208,000x on the worst case, and now *better* than unpooled even on calls that barely search.

Per Sol's cautions, the implementation clones the caller-owned `source` on a miss rather than
retaining the mutable `root` as a template (which under MT is actively being searched), skips the
redundant `CopyBruteForceRuntimeStateFrom` on a freshly cloned solver, carries an owner *reference*
rather than a bare flag so a release into the wrong invocation's pool is detectable, and honours a
retention cap so a pathological search's high-water set is not held alive.

### FindSolution pooling (single-threaded)

`ReportSolution` now **copies** the board:

```csharp
uint[] solution = (uint[])solver.board.Clone();
Interlocked.CompareExchange(ref result, solution, null);
```

Copying (81 uints) rather than never-releasing the winner was Sol's recommendation and is the right
call: "never release the winner" would be a permanent special case in every completion and cleanup
path, and would rot the moment cancellation or MT pooling changed. With the copy, result ownership
is independent of pool lifecycle.

The loop also gained the cleanup it never had: releases on invalid, complete, failed `ClearValue`,
failed `SetValue`, and a `finally` that drains the stack, since a found result or a cancellation can
leave entries behind.

Allocation, paired at 10 iterations:

| case | before | after |
| --- | ---: | ---: |
| killer-cage | 41.51 ms / 3.24 MB | 39.69 ms / **1.25 MB** |
| arrow-search | 19.64 ms / 1.69 MB | 19.47 ms / **0.23 MB** |
| littlekiller-10 | 13.92 MB | **0.24 MB** |

## Corrections to the original analysis

Three claims above were wrong or overstated:

1. **"The current ownership rule already avoids cross-thread release" is false.** `PushSolver`
   explicitly hands a solver to another task, so a solver rented on thread A can be released on
   thread B. The rule avoids *concurrent* ownership, not cross-thread release. Naive thread-local
   pools would therefore be unsound; the pool must stay synchronised, or the design must be the
   two-level one below.
2. **Lock contention is plausible, not established.** The 824 ms -> 1,256 ms figure cannot prove it,
   because true candidates randomises tied-cell selection and this case's timing variance is large
   (documented in `truecandidates-allocation.md`). Proving it needs node-equal or seeded comparison
   plus contention/GC counters.
3. **"Converges to zero steady-state allocation" holds only within one invocation.** Pools are
   call-scoped, so the next grid edit re-pays its high-water set. Persisting pools across edits
   would remove that but risks retaining a pathological call's memory.

Also: "false sharing on the recycled Solver objects" is the wrong description — a pooled solver has
exclusive ownership while active. What remains is cold-cache transfer when thread B rents an object
last touched by A, plus genuinely shared traffic on `conflictScores`/`conflictDecayState`, which
clones share by design regardless of pooling.

## Remaining work

### Multi-threading

Not enabled, deliberately: Sol's ordering is to benchmark on deterministic `CountSolutions` MT
before trusting any MT measurement on the randomised true-candidates path. The recommended design
is two-level rather than one shared lock or a CAS stack:

1. a small cache local to each `CountSolutionsInternal` / `TrueCandidatesInternal` invocation,
2. a lazy shared fallback (`ConcurrentBag<Solver>`-shaped),
3. flush the local free cache back to the shared pool when the task finishes.

Most node-level rents and releases then never touch shared synchronisation, while the handoff
through `PushSolver` still works: the receiving task adopts the solver into its own local cache when
it drops it. A shared Treiber/CAS stack is simpler than today's lock but keeps one hot cache line,
so it is unlikely to be sufficient on its own.

### Two latent aliasing hazards of the same class as FindSolution's

- **`CountSolutions` hands a pooled solver to `solutionEvent` and releases it immediately**
  (`SolverBruteForce.cs:337`). Current callers consume it synchronously, but the public callback may
  retain the reference and then observe mutation. Either document it as borrowed-only or give the
  callback stable data.
- The estimate paths are **not mechanical**, contrary to the original claim. Each estimator builds
  every open candidate child, picks one and abandons the rest, so pooling must release contradictory
  / solved / exactly-counted children, every unchosen open child, each consumed frame solver, and
  whatever remains on cancellation.

### An apparent pre-existing bug

Single-threaded `EstimateSolutions` clones `solver` and then calls
`EstimateSolutionsInternal(root, state)` — passing `root`, not the clone
(`SolverBruteForce.cs:1104`). At minimum the clone is wasted. Pooling that path should not paper
over it.
