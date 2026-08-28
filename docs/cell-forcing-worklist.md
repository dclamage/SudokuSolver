# Cell forcing in the search: what is measured, and the design that follows

Date: 2026-08-28. Handoff for an unfinished piece of work. Everything below is measured on the
398-puzzle ISS corpus unless stated. Branch `wasm-prototype`, last pushed commit `706e8cf`.

## Status in one paragraph

Cell forcing — eliminate candidate `X` when *every* remaining candidate of cell `A` is weakly linked
to `X` — currently runs **only during root setup**, and enabling it inside the search prunes well but
costs about twice what it saves. The design agreed for closing that gap is a **priority worklist**
that drains to fixpoint inside `BruteForcePropagate`, cheapest deductions first, plus a
**precomputed filter** that refuses to enqueue work that provably cannot fire. The filter's value is
now measured and it is large: **97.1% of cell-forcing work items do nothing**. Cell forcing itself
is still uncommitted; five files carry the groundwork, in the `wasm-prototype` working tree.

**Step 1 of the recommended order has since landed on its own** — see § "Step 1 is done" — worth
**−14.3%** on the static-order ISS tune corpus with node counts untouched. It did not land as the
tiered bucket structure described below, and the reason it paid is not the reason predicted here.
Read that section before building steps 2 or 3.

## Why in-search cell forcing does not pay yet

Measured with `SUDOKU_BRANCH_ORDER=static SUDOKU_NODE_CAP=200000`, on the puzzles that complete in
every arm. Baseline explores one node in **3.35 µs**; that is the price a removed node must beat.

| trigger | nodes | vs off | time | µs paid per node removed | |
| --- | ---: | ---: | ---: | ---: | --- |
| off | 1,589,026 | 1.000 | ~5,320 ms | — | |
| **worklist** | 925,005 | **0.582** | ~9,724 ms | **6.6** | 2.0× too costly |
| every propagation step | 925,005 | 0.582 | ~13,571 ms | 12.4 | 3.7× too costly |

The worklist and the every-step scan produce **bit-identical node counts on all 339 puzzles**, which
confirms the wake-up condition is exact: a cell can only *newly* force after it loses a candidate,
because dropping a term from an intersection can only enlarge it. So the worklist misses nothing and
halves the scan cost — and is still 2× short.

## Where the waste is

Counting every call to `CellForcingForCell` in production order, worklist trigger:

```
cell-forcing pops: 155,266,680
  cell already solved         21,899,440   14.1%
  scanned rows, nothing fired 86,518,368   55.7%
  row fired, target dead      24,259,552   15.6%
  ELIMINATED SOMETHING         4,463,548    2.9%
```

**97.1% of pops accomplish nothing**, ~35 pops per useful elimination, ~32 pops per search node. The
55.7% bucket is worse than its share suggests: it is the only one that pays the *full row scan*
before finding nothing. The others exit on a single mask test.

## The design

### 1. A priority worklist, drained to fixpoint

Replace the `StepBruteForceLogic` pipeline — which returns on the first stage that changes anything
and re-enters from the top — with one worklist per solver, drained completely inside
`BruteForcePropagate`, items pushed during the drain picked up in the same drain, popped in cost
order.

The pipeline *already* implements this priority; restart-on-change is what enforces "cheapest work
first". What the worklist removes is the cost of re-entering naked singles' loop, hidden singles'
zero-check and the constraint queue's always-run re-marking every time something deeper fires.

The new property, and the reason it matters here: a cell-forcing item is popped only when nothing
cheaper is pending, so every single and constraint elimination has already landed and the cell's
candidate set is as small as it will get. That directly attacks the 14.1% "already solved" bucket —
those cells get resolved by a naked single first and their stale item is rejected instantly.

**Tiered buckets, not a heap.** Each tier is homogeneous — tier 0 cell indices, tier 1 group
indices, tier 2 constraint indices, tier 3 cell indices — so every item is an `int` and the bucket
*is* the type tag. No boxing, no tagged struct, no comparator. Every tier has a natural capacity
bound (`NUM_CELLS`, `Groups.Count`, `constraints.Count`, `NUM_CELLS`), so the arrays are allocated
once and never grow. Pop is a scan of four counts, or a 4-bit non-empty mask plus
`TrailingZeroCount`.

Note this path is **already allocation-free** — `pendingNakedSingles` has its capacity pre-sized at
pool creation (`SolverBruteForce.cs:772`) and `2d9e87e` made `FindHiddenSingle` allocation-free. This
is re-entry-overhead recovery, not allocation recovery, and must be measured not to regress.

Two existing structures to follow rather than reinvent:

| | unit | structure | wake condition |
| --- | --- | --- | --- |
| naked singles | cell | LIFO `List<int>`, duplicates allowed | cell reached 1 candidate |
| hidden singles | group | flag array + count, scanned to consume | a group's count for some value fell to ≤1 |

Hidden singles' condition is *predictive* — the flag means a deduction may now exist. Cell forcing's
is merely *permissive*. That difference is the 97.1%.

### 2. The enqueue filter

A row `(X, S)` fires for cell `A` iff `cand(A) ⊆ S`, where `S` is the static set of `A`'s values
weakly linked to `X`. For 9 values there are only 512 masks, so precompute exactly which masks can
fire anything:

- `cfCanFire[cell]` — a 512-bit bitmap, bit `m` set iff some row has `m ⊆ S`. Eight `ulong`s per
  cell, ~5 KB per puzzle, shared by reference across clones like `cfOffsets`.
- Build by a downward zeta transform (mark each `S`, then propagate over the 9 bits), **not** by
  enumerating subsets per row. `512 × 9` per cell, independent of row count.
- Enqueue becomes `(cfCanFire[cell][m >> 6] >> (m & 63)) & 1`. Clear ⇒ skip the push entirely.

This catches the 55.7% bucket exactly.

**Exact refinement.** The bitmap answers "can anything fire", but if the cell already had firing rows
applied you want to know whether the firing set *grew*. Since the elimination removes value `v`, a
row newly fires iff `S ⊇ cand'(A)` **and** `v ∉ S`. Index by the removed value —
`newlyFires[cell][v][m]`, ~46 KB per puzzle, still shared — and OR across values for a masked clear.
This bites into the 15.6% bucket.

**The risk to weigh:** the lookup lands on the board-write path, the hottest code in the solver, and
is paid on *every* elimination to save work on the ones it filters.

### 3. Constraint costs

Constraints currently drain in **declaration order**. Their costs span orders of magnitude — the
allocation attribution had Quadruple at 215.7 MB and Sandwich at 170.3 MB against ~0 for everything
else, and Sandwich's exact arm is 12–29× its heuristic arm. Sorting cheap-first lets cheap
eliminations make expensive constraints unnecessary — by resolving the cell that dirtied them, or by
finding the contradiction that backtracks the node before Sandwich is ever paid for.

Prefer a **static per-instance** cost declared at construction, not a measured one: a runtime-adaptive
cost makes propagation order depend on timing and destroys reproducibility, which this repo has
already paid for once (`Make true-candidates search reproducible, exposing a 64x branch-order
cliff`). Per-instance rather than per-type, because a 3-cell sandwich and a 9-cell sandwich are not
the same animal, and the constraint knows its geometry at build time. It fits beside
`WantsBruteForcePropagation` and `CellIndicesForPropagationQueue`.

**Test it before building sub-tiers.** Constraints are already deduped by `_constraintQueued` and the
median puzzle has ~13–16 of them, so the cheapest experiment is iterating that flag array in a
precomputed cost-sorted index order instead of `0..constraints.Count`. One changed loop, no new
structures. Only if that pays is internal sub-bucketing worth it.

## Step 1 is done: packed worklists, −14% on static-order ISS

Date: 2026-08-28. Branch `perf/propagation-worklist`, off `wasm-prototype` @ `706e8cf`.
This is step 1 of "Recommended order" below — the worklist, decoupled from cell forcing.
Cell forcing itself is untouched and still off.

### What shipped

Not the four-tier bucket structure sketched in § "A priority worklist". The pipeline's
restart-on-change *is* the priority mechanism and it costs almost nothing on its own; what it costs
is the two flag-array scans it re-enters. So both became `ulong[]` bitsets walked with
`TrailingZeroCount`:

- `_checkGroupForHiddens` — a bitset over group indices. `Groups[i].Index == i` at every
  construction site, so an ascending word-then-bit walk visits exactly the groups the old
  `foreach (var group in Groups)` visited, in the same order.
- `_constraintQueued` — a bitset over constraint indices, drained in the same declaration order.
  `_alwaysRunConstraintIndices` became `_alwaysRunConstraintBits`, OR'd in as a mask instead of
  walked as an index list.

Both are **order-preserving by construction**, which is what makes the result unambiguous: node
counts cannot move, so any time delta is pure overhead and there is no pruning/cost trade to argue
about. Confirmed empirically — every result value identical to baseline on all 398 uncapped ISS
puzzles, all 34 `corpus.json` cases (including the five `logical` cases, which are sensitive to
*which* hidden single is found first), and all 281 static-order cases. 137 tests green.

### The scan was not the win — the write path was

Measured first, to size the prize. Instrumented counts over one `--filter iss-tune` run under
`SUDOKU_BRANCH_ORDER=static SUDOKU_NODE_CAP=200000`:

```
StepBruteForceLogic entries    47,624,856
hidden-single group iterations 510,310,568   (19.7 per scan, nearly all finding nothing)
constraint scan iterations     151,081,414   of which only 46,689,666 called a constraint
always-run re-marks             15,033,694
```

~614M scan iterations, ~567M of them finding no work. That number is real. **It is also not where
the time was.** Packing the flags — which removes almost all of those iterations — measured:

| arm | run 1 | run 2 |
| --- | ---: | ---: |
| baseline | 17,955.2 ms | 17,858.0 ms |
| bitsets only | 18,783.3 ms | 18,709.1 ms |

**+4.7% slower**, each arm reproducible to <0.6%. Packing 27–40 group flags into one `ulong` turns
every flag update into a read-modify-write serializing on the same 8 bytes, where `bool[]` had
independent byte stores. The scan is cheap *per iteration*; the marking it feeds sits on the
board-write path, the hottest code in the solver. `corpus.json` said so per-case before the
static arm did — `blank6-cap5M` +11.2% and the `logical` cases +3–5% (marking-dominated) against
`killer-cage` −26.9% and `tc-blank9` −8.4% (scan-dominated).

Two write-path changes turned it around:

- **`cellToConstraintIndices` → `cellToConstraintMask`.** The set of constraints watching a cell is
  static, so precompute it at build time as a bitmask. An enqueue is now one `|=` for the common
  ≤64-constraint case, instead of a test-and-set per watching constraint.
- **`_numGroupsNeedingHiddenCheck` deleted.** It existed only to serve `FindHiddenSingle`'s `== 0`
  early-out, which the bitset answers itself by testing its words — so it was pure bookkeeping
  maintained on the board-write path. Removing it also collapses the marking loop from up to nine
  bit-sets per group to one, and drops a guard load from the common path.

The second one is the lesson worth carrying forward: **the packing's real value was making an
existing piece of bookkeeping redundant, not making the scan faster.** Look for that shape again in
steps 2 and 3.

### Result

Five alternating base→new→base→new pairs, `--filter iss-tune`, static order, node cap 200k:

| pair | baseline | packed | delta |
| ---: | ---: | ---: | ---: |
| 1 | 18,842.6 | 15,546.1 | −17.5% |
| 2 | 17,940.1 | 14,970.5 | −16.6% |
| 3 | 17,953.0 | 16,437.4 | −8.4% |
| 4 | 18,851.8 | 16,557.7 | −12.2% |
| 5 | 17,960.0 | 15,397.3 | −14.3% |

**Median −14.3%**, every pair between −8% and −18%. Alternate the arms and compare pairwise: a
single arm drifts 5%+ across a session on this machine, so an unpaired number against a mean
measured an hour earlier is not a result.

### A case that isolates the write path

Non-consecutive is the sharpest available probe of what step 1 actually fixed.
`OrthogonalValueConstraint.WantsBruteForcePropagation` is `false`, so NC is excluded from the
brute-force constraint queue entirely and is enforced purely by weak links — the constraint-queue
half of step 1 contributes *nothing* on it. Every weak-link `ClearValue` fires the hidden-single
group marking, so what remains is almost exactly the board-write path.

It measured **−19.6%** (median of three alternating pairs), against −14.3% on the ISS corpus. The
larger win on the case where only the write path is in play is direct evidence for the diagnosis
above.

The puzzle — four givens, unique, no positive constraints — is now `nc-4given` in `corpus.json`;
neither corpus previously had *any* non-consecutive `count` case. Do not reach for a blank NC grid
instead: its exact count (5,287,048) takes a very long time, and it is slow even capped — a
200,000-solution cap did not finish one arm in ten minutes.

This is the case to re-measure when cell forcing goes in (step 3). NC's weak-link density is
exactly the regime where cell forcing should pay, and it now has a committed, validated baseline.

### A methodology correction

**The static arm is much sharper than ±3–4%.** Back-to-back runs of one arm reproduce to <0.6%,
which is how a 4.7% regression was legible instead of debatable. The ±3–4% figure in
§ "Methodology" is for production-order full-corpus runs and should not be applied here. The drift
that *does* bite is across a session (base ranged 17,832–18,852 over an afternoon) — which paired
alternation removes and a saved baseline file does not.

**Two harness traps.** The node-capped arm exits non-zero by design (truncated counts fail
validation), so `set -e` in a measurement script kills the run mid-sequence. And running the two
arms concurrently to save wall-clock silently contaminates both — an early ISS pair here read
−1.2% that way and was discarded.

## Methodology — read before measuring anything

**Node counts are reproducible. The harness allocation column is not.** Same binary, same config,
three runs:

```
run a: alloc=362 MB   nodes=3,884,298
run b: alloc=515 MB   nodes=3,884,298
run c: alloc=449 MB   nodes=3,884,298
```

`GC.GetTotalAllocatedBytes` is process-wide, so background tiered-JIT work lands on whichever case is
being timed; cases with near-zero real allocation absorb 100–266 MB spikes at random. Use
`GC.GetAllocatedBytesForCurrentThread()` around the specific call instead — that is thread-local and
clean. `docs/HANDOFF.md`'s "allocation is the reliable signal" holds only for the thread-local
counter.

**Wall clock on this machine carries ±3–4% noise** on a full corpus run at 10 iterations. Anything
smaller than that is not a result.

**Use `SUDOKU_BRANCH_ORDER=static` for propagation changes.** Under a fixed cell sequence and fixed
value order, propagation can only *remove* cells from the sequence, never reorder it, so a stronger
propagator's tree is a subtree of a weaker one's and its node count cannot rise. Any reduction is
real pruning; any increase means the change is unsound. Pair it with `SUDOKU_NODE_CAP` or the harder
puzzles will not finish. Because arms cap different puzzle counts, build a corpus subset of the
puzzles that complete under *every* arm before comparing times.

**Node counts are not the metric.** You can always reduce nodes by doing more work per node. The
measurement that answers "does the pruning pay for itself" is **time under static order**, which
removes the branch-order lottery without pretending node count is free. Express it as µs paid per
node removed against the baseline's µs per node explored.

**Set the acceptance bar before building.** Proposed: static-order paired time ≤ baseline as the
primary gate; production time and the tune/holdout node-ratio distribution as secondary; allocation
via the thread-local counter only. This is written down because three results in this investigation
had to be retracted, each because the bar was defined after the measurement.

## Traps that already cost time

- **`searchDepth` counts committed assignments, not tree depth.** The eliminate-a-candidate branch
  (`RentBranchSolver` + `ClearValue`) inherits the parent's depth and never increments; only
  `SetValue` does. So `depth:1` is every node reachable without an assignment — a large slice of the
  tree, not "the root". Depth gating is a dead end here and was measured so.
- **The cell-forcing table is dead code in the shipped configuration.** `CountSolutionsAttempt` runs
  `DiscoverWeakLinks(...)` *then* `CompileGroupedWeakLinks()`, and the only `BruteForcePropagate(true,
  ...)` call site is inside `DiscoverWeakLinks`. So root cell forcing always runs with `cfOffsets ==
  null` and uses the list-intersection fallback. The table only ever pays if cell forcing runs
  in-search. **Do not delete that fallback** — doing so silently disables root cell forcing and looks
  like a cheap win (it is not; it is less pruning).
- **Sweeps are redundant against an exact trigger.** `step:N` and `depth:N` remain in the enum but are
  known dead: with a correct dirty condition, a sweep either re-scans cells that provably cannot have
  changed or delays deductions the worklist already identified. `step:N`'s counter is also
  per-solver-instance and never reset on pool reuse, so its cadence is arbitrary.
- **A latent bug sits in the hidden-check seeding, preserved verbatim by step 1.** In
  `FinalizeConstraints`, `_checkGroupForHiddens[groupIdx] = count <= 1` sits *inside* the per-value
  loop, so the initial flag reflects only value `MAX_VALUE`'s count, not "any value's count is ≤1".
  It looks like it wants `|=`. Step 1 reproduced the behavior exactly rather than fix it, because a
  performance change whose results must be bit-identical cannot also change a deduction. Fix it as
  its own change, with its own node-count measurement — it will legitimately move results, and it
  may well be a small pruning *win* that has been sitting there unclaimed.
- **Do not put a dedup guard and a scan in the same mechanism.** An earlier version used
  `_cellForcingDirty` as both a scan filter and a queue membership guard; the scan cleared flags
  without dequeuing, the two desynced, and a fixed-capacity queue overran.

## Tree state

Step 1 lives on `perf/propagation-worklist`, branched off `wasm-prototype` @ `706e8cf`. It touches
`Solver.cs`, `SolverBruteForce.cs`, `SolverBruteForceLogic.cs`, `SolverInitialization.cs`,
`SolverLogic.cs`, `SolverModification.cs` and `SolverUtility.cs`, and is independent of the
cell-forcing groundwork below — the two have not been merged and do not conflict in intent, though
both touch `SolverModification.cs`'s board writes and `SolverBruteForce.cs`'s clone path.

The cell-forcing groundwork described here is still uncommitted in the `wasm-prototype` working
tree, pushed through `706e8cf` (`Give thermometers bounds propagation: 0.306x nodes on thermo
puzzles`).

Uncommitted, five files, 137 tests green and the ISS corpus clean single- and multi-threaded:

- `Solver.cs`, `SolverInitialization.cs` — the cell-forcing table (`cfOffsets`/`cfTargets`/`cfMasks`,
  `CompileCellForcingTable`) and `pendingCellForcing`.
- `SolverBruteForceLogic.cs` — `CellForcingForCell` with the table path, masked clears and the
  list-intersection fallback; the `SUDOKU_CF_TRIGGER` enum (`off`/`every`/`queue`/`depth:N`/`step:N`);
  `SUDOKU_CF_MAX`.
- `SolverModification.cs` — enqueue at all 7 board writes; `ClearCandidates` documented as never for
  brute force.
- `SolverBruteForce.cs` — worklist copy in `CopyBruteForceRuntimeStateFrom`.

All of it is gated off by default. The table is 4.94% the size of the link entries the merge-join
walked, and its `popcount(S) < 2` prune is exact — an unset cell always has ≥2 candidates, so such a
row can never fire. That prune happens to remove every ordinary house link (`A=v` links only to
`peer=v`, a one-bit mask), which is why what remains is precisely the cross-constraint deductions.

## Recommended order

1. ~~**Decouple.** Build the priority worklist on its own~~ — **done**, branch
   `perf/propagation-worklist`, −14.3%. Decoupling was the right call for the reason given: the
   bitset scan alone was a **4.7% regression**, and only separate measurement made that visible
   instead of being absorbed into a cell-forcing bundle.
2. **Cost-sorted constraint iteration.** Now cheaper to build than when this was written, but no
   longer "one changed loop" — `_constraintQueued` is a bitset, so ascending bit order *is* the
   iteration order. The clean form is to renumber: give each queue-participating constraint a slot
   ordered by `(cost, declaration index)`, build `cellToConstraintMask` and
   `_alwaysRunConstraintBits` in slot space, and keep `slotToConstraint[]` for the `StepLogic` call.
   The scan stays a plain ascending walk and cost order falls out for free. Bonus: slots are dense
   over *participating* constraints only, so the bitset shrinks by whatever `WantsBruteForcePropagation`
   excludes.
   **The blocker is cost data, not plumbing.** With every cost equal the reordering is a no-op, so
   there is nothing to measure until per-constraint brute-force `StepLogic` time is attributed.
   Do that measurement first; it is its own task.
3. **The enqueue filter**, then cell forcing as a tier on top, measured separately against the bar
   above. Note the filter's stated risk — "the lookup lands on the board-write path" — is exactly
   what step 1 got wrong in its first attempt. Budget for it: on that path a redundant load or a
   read-modify-write costs more than a 500M-iteration scan does.

Blast radius for step 1: `BruteForcePropagate` serves solve, count, truecandidates and estimate,
single- and multi-threaded. It is the hottest loop in the solver.

## Open questions

- Does removing ~70% of pops, weighted toward the expensive ones, actually close a 2× gap? The ratio
  is favourable but unproven.
- The worklist drains to fixpoint within one call, so cascading eliminations reprocess cells; the
  scan does one pass and lets `BruteForcePropagate` interleave cheaper logic between passes. Draining
  one batch per call instead would test whether that accounts for part of the remaining cost.
- The no-op breakdown above is production order. The static-order breakdown is unmeasured, though the
  hit rate should be a property of the table and candidate masks rather than the branch heuristic.
