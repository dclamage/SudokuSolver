# Branch-solver pooling: coverage audit and the multi-threading question

Date: 2026-08-02

Follow-up to [`truecandidates-allocation.md`](truecandidates-allocation.md), answering two
questions: do all brute-force entry points pool their branch solvers, and what should
multi-threading do?

## Coverage: 2 of 4

| entry point | pooled (1T) | pooled (MT) | per-node clone site |
| --- | --- | --- | --- |
| `CountSolutions` | yes | **yes** (`isThreadSafe: true`) | — |
| `TrueCandidates` | yes | no | `SolverBruteForce.cs:1031` uses the pool |
| `FindSolution` | **no** | no | `SolverBruteForce.cs:172` |
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
