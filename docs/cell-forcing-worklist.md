# Cell forcing in the search: what is measured, and the design that follows

Date: 2026-08-28, status refreshed 2026-08-29. Handoff for an unfinished piece of work. Everything
below is measured on the 398-puzzle ISS corpus unless stated. Branch `wasm-prototype`.

## Status in one paragraph

Cell forcing — eliminate candidate `X` when *every* remaining candidate of cell `A` is weakly linked
to `X` — currently runs **only during root setup**, and enabling it inside the search prunes well but
costs about twice what it saves. The design agreed for closing that gap is a **priority worklist**
that drains to fixpoint inside `BruteForcePropagate`, cheapest deductions first, plus a
**precomputed filter** that refuses to enqueue work that provably cannot fire. The filter's value is
now measured and it is large: **97.1% of cell-forcing work items do nothing** — but see § "Step 3",
where that figure turned out to be a poor guide to where the time was.

**Status correction (2026-08-29).** This paragraph used to say the groundwork was "committed on
`perf/propagation-worklist` … all gated off", which is no longer true and contradicted § "Step 4"
below. `cf/scan-prunes` (`d88a36c`) is **merged into `wasm-prototype`** and the table prunes are
**on by default** (`SUDOKU_CF_PRUNE`, default 1). What remains off is the *trigger*:
`SUDOKU_CF_TRIGGER=off` means root setup only.

**And the gap is not closed.** With the prunes active, `SUDOKU_CF_TRIGGER=every` against `off` on
`corpus.json` measures **geomean 1.364× — 36% slower** — for 9 cases with fewer nodes and 2 with
more. So the prunes did not move the economics, consistent with § "Step 5" concluding the remaining
candidate is the enqueue path rather than the table.

**Steps 1-3 have landed. § "Step 4" adds exact board-relative table prunes. § "Step 5" measures the
per-row usefulness question the prunes were groundwork for and closes it: a perfect oracle filter
recovers 12.9% of the scan cost in the regime where cell forcing wins, the scan is single-digit
percent of runtime, and cost and value sit in the same (size, distance) bucket. The remaining
candidate is the enqueue path, not the table.**

**Step 1 of the recommended order has since landed on its own** — see § "Step 1 is done" — worth
**−14.3%** on the static-order ISS tune corpus with node counts untouched. It did not land as the
tiered bucket structure described below, and the reason it paid is not the reason predicted here.
Read that section before building steps 2 or 3.

## Addendum (2026-08-29): the verdict is about the scan, not the deduction

`OrthogonalValueConstraint`'s new brute-force arm
([`whisper-arc-consistency.md`](whisper-arc-consistency.md) §5) **is** cell forcing — its
`InitLinks` adds a weak link for exactly the bits of `clearValues[v0-1]`, so its sweep is the rule
below applied to that constraint's own weak links. It measures **0.10× nodes on
`kropki-search-cap50k` for roughly 0.02 µs paid per node removed**, against the 6.6 µs that makes
the general version 2× underwater.

Nothing about the deduction changed; the cost of *finding* it did. The general scan searches the
weak-link lists to discover which targets are forced. There, the forcing cell and the target are
four orthogonal neighbours known at compile time, the intersection is an AND of precomputed 9-bit
masks, and there is no worklist or enqueue path because it rides the constraint queue that already
exists — which is also why the 97.1% do-nothing rate below does not apply to it.

**So read the table below as a verdict on the general scan.** A deduction too expensive to find in
general can be cheap inside a constraint that already knows where to look. That is a reusable shape
rather than a one-off, and it is worth trying against any other constraint whose weak links carry
this much structure.

### The worklist was being fed for nothing, and that was worth −17%

Independent of everything else here, and found while A/B-ing the hook below. `EnqueueCellForcing`'s
filter (`cfCanFire`) is built only when `CellForcingRunsInSearch`. At the default trigger it is
null, so the method falls through to `pendingCellForcing.Add(cellIndex)` on **every board write** —
into a list `FastFindCellForcing` drains only under the Queue trigger. At the default it is never
emptied; it grows, and the copy constructor and `CopyBruteForceRuntimeStateFrom` deep-copy it into
every branch clone.

Feeding it only when something reads it: **ISS corpus 17,571 → 12,412 ms (−29.4% total, −17.0%
geomean, 193 better / 1 worse)** and **`corpus.json` 19,918 → 15,633 ms (−21.5%, −12.6% geomean)**,
with node counts **identical on all 398 ISS puzzles and all 33 corpus cases**. Cell forcing behaves
exactly as before when switched on.

Nothing here was a slow algorithm and no profile pointed at it. **A feature that ships disabled left
its bookkeeping enabled on the hottest path in the solver** — the cost of the drift `ideas.md`
describes, rather than only the confusion of it.

### Naming cells for cell forcing: measured, and parked

The repo owner's alternative to a constraint reimplementing this rule: let it *name* the cells where
the rule is worth applying (`Constraint.CellIndicesForCellForcing`, `SUDOKU_OVC_MODE=cellforcing`).
Cell forcing at a named cell reads that cell's whole weak-link set, so it is strictly stronger than
any one constraint's view:

| case | own sweep | hook |
| --- | ---: | ---: |
| `1HuNjcLWlPE` | 92.9 ms / 30,288 | **74.9 ms / 15,611** |
| `kropki-search-cap50k` | 264.7 ms / 305,138 | **232.9 ms / 172,844** |
| `iss-ite7WigjGGI` | **53.7 ms / 2,935** | 119.1 ms / 2,755 |

ISS corpus geomean **1.049×** (83 better, 98 worse) — and swapping which arm is the baseline
inverts it to 0.955×, so that is a real cost and not drift. It wins where declared cells are many
and dot-dense, and loses where a couple of dots get a full weak-link intersection every step for
nothing. **This is the same cost curve as the table below**, which is the point: the hook narrows
*which* cells are scanned but does nothing about the per-cell price, so it inherits the problem this
document exists to solve. Solve that and the hook is how constraints buy in for free.

A dirty-worklist variant was built and deleted: `pendingCellForcing` has no dedup guard, so a cell
is rescanned once per write touching it — best of the three on `kropki-search-cap50k` (182.9 ms),
worst on `iss-ite7WigjGGI` (175.3 ms).

### It overlaps cell forcing exactly, and it has eaten some of the remaining upside

**Re-baseline before continuing this work.** Measured under `SUDOKU_BRANCH_ORDER=static`, so these
are propagator effects with no ordering noise:

| nodes | pre-OVC, CF off | pre-OVC, CF on | post-OVC, CF off | post-OVC, CF on |
| --- | ---: | ---: | ---: | ---: |
| `kropki-search-cap50k` | 2,580,190 | 643,663 | 1,004,219 | **643,663** |
| `nc-4given` | 65,633 | 31,465 | 32,493 | **31,465** |
| `variant-cloneways` | 2,001 | 54 | 625 | **54** |

With cell forcing on, the last column is **byte-identical** to the pre-change CF-on column. That is
proof rather than inference: the constraint's deduction is an exact *subset* of cell forcing, and
with cell forcing enabled it contributes nothing new.

Two consequences:

- **In-search there was no overlap, which is exactly why it paid.** `SUDOKU_CF_TRIGGER` defaults to
  `off`, which means *root setup only* (`FastFindCellForcing` is called unguarded from
  `FastAdvancedStrategies`, and guarded by `ShouldRunCellForcing()` from `StepBruteForceLogic`).
  Nothing re-derived these eliminations as the board changed. **At root the two do duplicate** —
  one redundant pass, not worth gating out.
- **The remaining prize on this puzzle class is now smaller.** The constraint already captures
  **81% / 97% / 71%** of the available node reduction on the three cases above, so the marginal gain
  from switching cell forcing on drops from 4.0x to 1.56x on `kropki-search-cap50k`. Cell forcing
  was already 2x underwater on cost; on dots and nonconsecutive puzzles most of what it had left to
  win is now free. The corpus is mostly puzzles with no such constraint, where nothing moved — but
  any figure in this document measured before 2026-08-29 overstates cell forcing's remaining value
  on the affected class.

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

## Step 2 is done: cost-ordered propagation, and what it is worth

Date: 2026-08-28, same branch. Constraints are **~70% of brute-force runtime** (12.6 s of a ~17.9 s
`iss-tune` run), so how they are scheduled is worth measuring properly.

### Sizing it first — the number that nearly killed it

The aggregate says 46.7M constraint calls against 47.6M `StepBruteForceLogic` entries: **0.98 calls
per step**. At a queue depth of 1 the order is irrelevant no matter how far the costs spread, and
the right answer would have been to report that and stop.

That average is wrong, because two-thirds of steps never reach the constraint stage. Per *stage
entry*:

```
stage entries        16,823,148      ended in a fire 7,838,958   exhausted 8,984,190
calls per entry            2.77
entries with >=2 calls    62.8%
calls before a firing constraint   12,927,446   (27.8% of all calls, ~3.5 s, ~19% of the run)
```

Those 12.9M pre-fire calls are the reorderable budget. **Always take the depth distribution, never
the mean** — the mean here understated the opportunity by ~3x.

### Order by cost / P(fire), not by cost

The stage is a scan that stops at the first success, and the expected cost of such a scan is
minimized by ordering on cost / P(success) — not on cost. Raw cost mis-schedules both extremes, and
measurably so:

| | ns/call | fires | rank by cost | rank by cost/P |
| --- | ---: | ---: | ---: | ---: |
| Quadruple | 41 | 1.6% | 3 | **9** |
| RegionSumLines | 531 | 27.3% | 11 | **8** |
| InnieCage | 486 | 6.9% | 9 | **12** |

Both variants were measured. They save the same modeled cost (-4.4% raw, -4.5% cost/P) but raw cost
grew total calls **+8.6%** against **+1.4%** — it promotes constraints that are cheap and never
deduce. Nine types recorded **zero deductions across 9.5M calls** (ExtraRegion, ModularLine,
Knight/King, EntropicLine, the diagonal groups); at P->0 their weight goes to the top of the range
and they now run last. The modeled cost cannot see that improvement — those types price at 0 ns —
so **cost/P is better than its own headline number**.

Ordering can never affect correctness: propagation runs to fixpoint regardless. A wrong weight costs
time, not answers. That is what makes this safe to tune.

### Result

| corpus | modeled constraint cost | total calls |
| --- | ---: | ---: |
| `iss-tune` (fitted) | **-4.5%** | +1.4% |
| `iss-holdout` (never tuned on) | **-1.9%** | -2.4% |
| `corpus.json` | ~0 | +0.05% |

Holdout generalizes with attenuation, which is the honest shape for weights fitted on tune.

**Call this ~1-3% of total runtime, and call it modeled.** Wall clock could not resolve it: the
machine developed 26% within-arm noise (base ranged 15,427-19,444 ms) against ~5% earlier the same
day. Three alternating pairs disagreed in sign. The modeled figure stands; a measured one still
needs a quiet machine.

### The negative result: single-constraint puzzles gain nothing

`corpus.json` did not move, and Skyscraper stayed at exactly 194 calls — despite being the most
expensive constraint measured anywhere, at **1.20 ms per call**, 26% of that corpus's constraint time
from 194 calls. The expectation that the pathological outlier was where ordering would pay was
**wrong**. Those cases have effectively one constraint type each, so there is nothing to reorder
against.

Cost ordering pays only where constraints of *differing* cost compete in one queue — real mixed
variant puzzles. Do not reach for `skyscraper-search` or the other `*-search` cases to evaluate a
scheduling change; they cannot show one.

### How it is built

Slot renumbering, not a sorted scan. Each queue-participating constraint gets a slot ordered by
`(BruteForcePropagationCost, declaration index)` at `FinalizeConstraints` time, and
`_constraintQueued`, `cellToConstraintMask` and `_alwaysRunConstraintBits` are all built in slot
space. Ascending bit order *is* cost order, so the drain stays the same `TrailingZeroCount` walk with
one indirection through `_propagationSlotToConstraint`. No comparator, no heap, no per-step sort.
Ties keep declaration order, so equal weights reproduce the old behavior exactly. Slots cover only
participating constraints, so `WantsBruteForcePropagation == false` no longer occupies a bit.

**Trap:** `Constraint.InitLinksByRunningLogic` clones a solver *during* `FinalizeConstraints`, before
the slot map exists. Sizing the clone's queue from `other._constraintQueued.Length` NREs on 22 tests;
the old code read `constraints.Count`, which is always valid. Such a clone runs logic, not brute
force, so an empty queue is correct for it.

### Known gap: the weights are per-type, the cost is per-instance

The same type measured **2-4x apart** across the two corpora (InnieCage 486 vs 889 ns, Renban 636 vs
202 ns) while the rank order stayed stable. That spread is instance geometry, which per-type
constants cannot capture. `BruteForcePropagationCost` is a virtual property precisely so an override
can scale with its own cell count — but doing that now would be a guess, since the measurement is
per-type. Attribute per-instance before adding a geometry term.

## Step 3: the enqueue filter is built and exact; cell forcing is regime-dependent

Date: 2026-08-28, same branch. Both filters ship, gated off with cell forcing.

**Read § "Where it does pay" before concluding anything from the corpus average.** In-search cell
forcing costs +21.5% on the static-order `iss-tune` corpus and is worth **-46%** on non-consecutive
true candidates. The ISS corpus contains **zero** non-consecutive puzzles, so its average cannot
see that at all.

### Where the gap went

| arm | CF on vs off |
| --- | ---: |
| this doc's original measurement | ~+100% (the 2x it records) |
| steps 1 + 2 + can-fire filter | +27.6% |
| + newly-fires filter | **+21.5%** |

Roughly 2x became 1.22x. It did not become 1.0x. Cell forcing stays off.

### The filter's prediction was wrong, and the reason generalizes

§ "The enqueue filter" says `cfCanFire` "catches the 55.7% bucket exactly" — the pops that scan
every row and find nothing, the only bucket paying a full row scan. Measured, it does not:

```
enqueues offered 527,270,558   rejected 204,164,144  (38.7%)
pops 127,454,936               fired 8,579,190       (6.7%)
bitmap admits 19.3% of masks
cells with no rows at all      44.2%
cells admitting every mask      4.1%
```

**44% of cells have no cell-forcing rows at all**, and the bitmap rejects every mask for those. That
is where most of the 38.7% rejection comes from — and a no-row cell's pop was already free
(`cfOffsets[c] == cfOffsets[c+1]`, the loop body never runs). So the filter removed the *cheap* half
of a correctly-counted no-op population while paying a load and bit test on all 527M board writes.
Net **-1.3%**.

This is the same shape as step 1, where a real 510M-iteration scan turned out not to be where the
time was. **Count the no-ops by cost, not by frequency.** A no-op census is a starting point, not a
target list; bucket it by what each bucket actually costs before building anything.

### The sharper filter, and why it was the right next move

The can-fire filter is *exact*: `canFire[m]` true means a row genuinely does fire. Yet only 6.7% of
pops reported a change. Those two facts together pin the residue precisely — for 93.3% of pops a row
fires but its target candidate was **already eliminated**. No "can anything fire" predicate can see
that.

`cfNewlyFires[cell][v][m]` is this doc's own § "Exact refinement", and the measurement above is the
argument for it: a row whose S contains the removed value v already covered the wider pre-write
mask, so it has been applied and its target is gone. Excluding those rows per removed value is
exact, built by the same zeta transform. ~47 KB per 9x9 puzzle, shared by reference.

It bought another **3.0%** off the CF-on arm (21,381 -> 20,742 ms). Real, and an order of magnitude
short of closing the gap.

### Both filters are exact — verified, not argued

`SUDOKU_CF_FILTER` is a level: `0` none, `1` can-fire, `2` (default) newly-fires. All three produce
**bit-identical results on all 281 static-order cases**. This matters more than it looks: an
over-aggressive filter does not produce wrong answers, it silently drops deductions and shows up as
*slower*. Validating against `expected` would have proved nothing. One build, three levels, so the
comparison never crosses two separately-JITted binaries.

### Two costs worth not repeating

- **Do not build the filters when cell forcing is off.** They were being built during every solver
  setup regardless — ~2^MAX_VALUE * MAX_VALUE per cell per level, ~3.4M operations per puzzle, paid
  by the default configuration for a feature that is disabled. Now gated on
  `CellForcingRunsInSearch`. Same family as this doc's note that the table itself is dead code in
  the shipped configuration.
- **Writes that set a value need not enqueue at all.** `CellForcingForCell` skips value-set cells
  outright, so queuing them could only ever cost a pop.

### Where it does pay

The +21.5% is `count` over the ISS corpus, which is constraint-sparse relative to what cell forcing
needs — it is a weak-link deduction, and its table deliberately drops one-bit masks, so what remains
is exactly the cross-constraint links. Non-consecutive is the opposite regime: enforced purely by
weak links, with an adjacency link for every neighbouring pair.

| workload | cell forcing |
| --- | ---: |
| `iss-tune` count, static order (280 puzzles, **no NC at all**) | +21.5% |
| `nc-4given` count, static order | +8.5% |
| `nc-4given` count, **production order** | **-33.1%** |
| `tc-nc-no-*` true candidates (4 boards) | **-46.3%** |

Two different things are happening, and they should not be conflated:

- **The `count` production win is a branch-order effect, not a propagation win.** Under static order
  cell forcing still *loses* on `nc-4given` (+8.5%), so it is not paying for itself as a propagator
  there; the -33% comes from its eliminations changing candidate counts and reshuffling
  `GetLeastCandidateCell` favourably. That is the lottery this repo has been burned by before. One
  puzzle. Do not generalise it.
- **The true-candidates win is large, consistent and on the UI path.** All four leave-one-out boards
  move -42% to -50%, allocation drops with it (0.94 -> 0.72 MB), and every score is unchanged.
  `truecandidates` runs on every grid edit in a setting UI, against exactly this shape of
  under-constrained board.

**So "cell forcing does not pay" was too broad a conclusion.** The measured claim is that it loses
on constraint-sparse counting and wins substantially on weak-link-dense setting work. The obvious
follow-up — unmeasured — is a targeted enable rather than a global one: `truecandidates` is a
different entry point from `CountSolutions`, and weak-link density per cell is known at setup from
the cell-forcing table itself (44% of cells having no rows at all is exactly the signal that a
puzzle is a poor fit). Either would be a decision made from structure, not from a timing lottery.

### What break-even would take

CF-on carries ~3.7 s of overhead on a ~17.0 s baseline. Every remaining pop now has a row that
fires *and* newly fires, so the cheap structural rejections are exhausted — what is left is the
table scan itself plus the eliminations. Closing 21.5% from here means attacking the scan, not the
trigger. Candidates, unmeasured:

- Order rows within a cell by target cell so a run of eliminations shares one board read.
- Stop at the first row whose target is still alive rather than scanning all rows every pop.
- Accept that the worklist's no-dedup design (§ "A priority worklist") re-pops a cell written many
  times in one step, and measure a per-step dedup guard — carefully, given this doc's own warning
  about mixing a dedup guard with a scan.

## Step 4: board-relative table prunes, and two negative results

Date: 2026-08-29, branch `cf/scan-prunes` off `perf/propagation-worklist`. This is the first piece of
the "attack the scan, not the trigger" work § "What break-even would take" calls for. Filtering rows
out of the LUT is that attack: rows are scanned on every pop of their source cell, so a row that
cannot contribute is pure recurring cost.

### What shipped

Two prunes in `CompileCellForcingTable`, both **exact**, both on by default (`SUDOKU_CF_PRUNE=0` to
disable):

1. **Narrow each row's mask to the source cell's live candidates**, then apply the existing two-bit
   test. `cand(A)` only shrinks below what it is at compile time, and `cand(A) ⊆ S` is the same
   question as `cand(A) ⊆ S ∩ cand(A)`.
2. **Drop rows whose target candidate is already eliminated.** A candidate gone at compile time is
   gone on every board below, so such a row can only ever fire as a no-op — and it is scanned until
   it does. A target in a cell whose value is *set* is live, not dead: that row firing is a
   contradiction worth finding.

Both are sound because of one invariant, worth stating since the prunes are the first thing to make
the table board-dependent: **every solver in a search descends from the solver the table was compiled
on.** All five `CompileGroupedWeakLinks` call sites compile on the search root immediately before
`InitializeSolverPool`, the copy constructor does *not* inherit the table, and only
`CopyBruteForceRuntimeStateFrom` shares it — parent to child.

`CellForcingForCell` also now returns early for a cell with fewer than two candidates. The table has
always assumed that (it is the justification for dropping one-bit masks), but saying it explicitly is
what makes prune 1 exactly equivalent rather than nearly so.

### The prunes are verified, not argued

`SUDOKU_CF_VERIFY=1` checks every compiled table against the definition of cell forcing: for each
unset cell and **every** candidate subset it could ever hold, the eliminations the production scan
would apply must equal the live targets that every value of the subset links to. It throws on the
first disagreement. Clean over all 434 boards of `corpus.json` and `corpus-iss.json`, in both row
orders.

This is the right shape of check for this family of change, and cheaper than the alternative. An
over-aggressive prune does not produce a wrong answer — it silently drops deductions and shows up
only as *slower* — so validating counts against `expected` proves nothing, and node-count equality
across two arms proves it only on the boards sampled. Exhausting the masks proves it on the board.

### Result: -8.4% where the board is given-dense, flat where it is not

| case | prunes off | prunes on | |
| --- | ---: | ---: | --- |
| `nc-4given` count | 39.5 ms | **36.1 ms** | -8.4%, allocation 1.18 -> 1.06 MB |
| `iss--Uj9xZPyzM4` count (heaviest ISS case) | 2055 ms | 2037 ms | -0.9% |
| `iss-0cvA-XDiQNQ` count | 1634 ms | 1632 ms | flat |
| `tc-blank-nonconsecutive` true candidates | 5201 ms | 5196 ms | flat |
| `tc-nc-no-r3c4` true candidates | 2060 ms | 2061 ms | flat |

That distribution is the mechanism, not noise: pruning needs candidates to be gone, and a
`truecandidates` board is nearly blank, so there is almost nothing to remove. It cuts most where the
root board is already narrow.

### Negative result 1: the popcount row bound does not pay

Firing needs `popcount(S) >= popcount(cand(A))`, so sorting a cell's rows by mask size descending
makes the firable rows a prefix and turns the bound into the loop limit — `cfPopEnd[cell][p]`, no
per-row test. It is exact and it does remove iterations. It also loses:

| case | vs target order |
| --- | ---: |
| `nc-4given` | +2.0% |
| `tc-blank-nonconsecutive` | +0.8% |
| `tc-nc-no-r3c4` | +0.8% |
| ISS corpus geomean | +1% to +2% |

Identical with the enqueue filter on and off (0.593x vs 0.598x against a common baseline), so the
filter is not what hides it. Giving up target grouping — which lets a run of eliminations share one
masked clear — costs more than the trimmed rows were worth. Kept behind `SUDOKU_CF_ORDER=popcount`,
default `target`, because it is the obvious next idea and one build should answer it.

The prediction that this would be the *large* lever was wrong for the third time in this document's
history, and for the third time the same reason: **it counted iterations rather than what they
cost.** § "The filter's prediction was wrong" already says this. Predicted lever, measured wash.

### Negative result 2 turned into the real finding: the filter's build cost

Turning the enqueue filter off (`SUDOKU_CF_FILTER=0`) was supposed to isolate the bound. It produced
a **0.60x per-case geomean on the ISS corpus with a 3.5% worse total** — the divergence the
benchmark README warns about, and here it is diagnostic rather than a trap:

| case | filter on | filter off | |
| --- | ---: | ---: | --- |
| `iss-blPgSzctUMg` count (5 ms puzzle) | 4.55 ms | **3.67 ms** | filter build is ~0.9 ms, ~19% of the solve |
| `tc-nc-no-r3c4` true candidates | 2061 ms | 2360 ms | +14.5% without it |
| `tc-blank-nonconsecutive` true candidates | 5196 ms | 5486 ms | +5.6% without it |
| `nc-4given` count | 36.1 ms | 36.8 ms | +1.7% without it |

`cfNewlyFires` is built by a zeta transform over `2^MAX_VALUE * MAX_VALUE` per cell — ~3.4M
operations and ~47 KB per puzzle — and that is **fixed setup cost paid before the first node**. On a
long search it earns itself back several times over. On the median ISS puzzle, which solves in 1.1
ms, it is most of the solve.

So the filter is not primarily a write-path-lookup trade, which is how § "The enqueue filter" framed
its risk and how the -1.3% was read. It is a **setup-cost versus search-savings** trade, and nothing
currently decides which side a given puzzle is on.

**The follow-up this implies, unmeasured:** build the filter *lazily*, triggered by node count. Before
the trigger, enqueue unconditionally; at node N, build and start consulting. Both filters are exact,
so consulting them or not cannot change a deduction — node counts stay bit-identical and there is no
branch-order lottery to fear. Easy puzzles would never pay the build; long searches would pay it once,
N nodes late. `truecandidates` recompiles per grid edit, so it is the entry point that gains most.

### Methodology: this machine's noise floor is +/-5% at 5 iterations

The first version of the result above claimed the prunes were worth -8.0% on the ISS corpus geomean.
They are not. Running the **same arm** against its own saved baseline gives 0.947x, and a second
control gives 1.047x — a bidirectional +/-5% band that swamps anything this change does corpus-wide.
The first run of a session is also systematically slow, so a baseline saved from it inflates every
later arm.

What works instead, and what every figure in this section uses: **a few long single cases, arms run
interleaved, `--iterations 9`.** `tc-blank-nonconsecutive` repeated within 0.04% across runs
(5200.6 / 5198.6 ms) and `iss--Uj9xZPyzM4` within 0.9%. A 2-second case measured twice beats 398
one-millisecond cases measured five times, for this kind of change.

Recorded because § "Methodology" already says three results had to be retracted for defining the bar
after the measurement. This one was caught before it was written down, by the cheapest possible
control: run the baseline arm twice.

### Where this leaves the plan

Still open, in the order they now look worth doing:

1. **Lazy, node-triggered filter build.** The measured cost is real and the exactness argument makes
   it free of behavioral risk. Biggest number on the table.
2. **A usefulness filter over rows**, per the discussion these prunes came out of. The exact prunes
   are now exhausted; what remains needs a value judgment. Before choosing any threshold, instrument
   a histogram of rows scanned / fired / newly eliminated bucketed by `k = |cand(A)| - popcount(S)`
   and by `popcount(S)` — **weighted by cost, not frequency**, which is the lesson this document has
   now learned three times.
3. **The entry-point gate stays orthogonal.** Prunes cut cost; they cannot predict the `nc-4given`
   -33.1% production win, which § "Where it does pay" is explicit is a branch-order effect.

## Step 5: the usefulness histogram, and why per-row filtering cannot close the gap

Date: 2026-08-29, branch `cf/scan-prunes`. `SUDOKU_CF_STATS=1` collects a pop census, an enqueue
census, and per-row scan/fire/novel-elimination counts folded at process exit into a histogram over
the two static properties a build-time filter could threshold on:

- **size** = `popcount(S)`. A row cannot fire for a candidate set larger than its mask.
- **distance** = `popcount(cand(A) & ~S)` on the board the table was compiled on: how many
  candidates the source cell must still lose before the row can fire at all.

Both are computed after any row reordering, so they index the way the scan sees them. Counters are
plain adds behind a `static readonly bool`; a stats run must be single-threaded (both corpora are).

This was built before any filter, on purpose. It says do not build the filter.

### The ceiling for a per-row filter is small, and smaller where cell forcing wins

The number that matters is not how many rows are useless but **what share of the scan cost they
carry**. A perfect oracle — drop every row that never once produced a novel elimination in the whole
run — would remove:

| corpus | useless rows | their share of scan cost |
| --- | ---: | ---: |
| `corpus-iss.json` (398 puzzles, CF loses here) | 73.8% of rows | **38.3% of scan cost** |
| the three NC cases (CF wins here) | 52.3% of rows | **12.9% of scan cost** |

Rows that never even *fired* are 47.4% / 27.0% of rows and carry 5.7% / 3.6% of the cost. So the
useless rows are overwhelmingly the cheap ones — the same shape as `cfCanFire`, one level down. In
the regime the whole exercise is for, an oracle gets 12.9%.

### And there is no separating signal, because cost and value live in the same bucket

The NC scan-cost and novel-elimination distributions over (size, distance) are nearly the same
distribution:

```
                scan share    novel elims
size 3          66.3%         85.8%
distance 6      72.2%         82.6%
size 3, dist 6  59.6%         78.5%
```

One bucket is 60% of the cost and 79% of the value. On the ISS corpus it is less extreme but points
the same way: size 3 / distance 6 is 34.4% of scan cost and 14.3% of eliminations, and the cheapest
buckets by `scans/elim` (distance 2 at 28.6) are also small in absolute cost. There is no threshold
in either property that keeps the eliminations and drops the cost, because the productive rows *are*
the expensive rows — they are expensive **because** they keep firing.

A filter is still constructible, and the histogram prices it in advance: dropping distance >= 6 on
the ISS corpus removes 55.3% of scan cost and 21.6% of novel eliminations. On the NC cases it removes
91.2% of the cost and 91.3% of the eliminations. That is not a filter, it is a switch to turn cell
forcing off, spelled at greater length.

### The scan is not where the money goes anyway

The popcount-order arm from § "Step 4" turns out to be a measuring instrument. It removes **223.4M of
875.7M row scans (-25.5%)** on the ISS corpus and still measures 1-2% *slower*, because it gives up
target grouping. So 223M row scans are worth less than that ordering loss — under ~0.25 s on a ~15.6 s
run — which puts the **entire** scan at single-digit percent of runtime, and a perfect per-row filter
at ~2% of it.

Where the pops actually go, and it is not into scanning:

| | ISS corpus | NC cases |
| --- | ---: | ---: |
| pops | 22.3M | 33.9M |
| value already set | **30.8%** | **41.5%** |
| under two candidates | 2.1% | 9.9% |
| scanned, nothing fired | **0.0%** | 0.0% |
| fired, target already gone | 48.1% | 16.7% |
| eliminated something | 19.0% | 31.9% |

Two things to read here. First, **the 55.7% "scanned, nothing fired" bucket this document was written
around is gone** — 0.0%, both corpora. The enqueue filter did exactly what § "The enqueue filter"
promised; it just did not make cell forcing pay, because that bucket was not the cost.

Second, **a third to a half of all pops are stale**: the cell was solved or reduced to one candidate
between enqueue and pop. § "A priority worklist" predicted cost-ordered draining would fix this
("those cells get resolved by a naked single first and their stale item is rejected instantly"). It
did not — 41.5% on the NC cases. The no-dedup design is the likely reason (§ "Open questions"), and a
stale pop is cheap, so this is a large count attached to a small cost. Do not build against it
without pricing it first. That is the mistake this document has now recorded four times.

The remaining candidate, by elimination, is the **enqueue path itself**: 330M offers on ISS and 433M
on the NC cases, each a table lookup on the hottest path in the solver, of which 67.5% / 58.1% are
rejected. That is one lookup per board write to prevent a pop that measurement says is cheap.

### What did come out of it: level 2 is not worth its size

Re-measuring the two filter levels case by case, with the § "Step 4" protocol rather than a corpus
total:

| case | level 1 (can-fire, ~5 KB) | level 2 (newly-fires, ~47 KB) | |
| --- | ---: | ---: | --- |
| `nc-4given` | **34.4 ms** | 36.1 ms | level 2 is 4.7% slower |
| `iss-0cvA-XDiQNQ` | **1611 ms** | 1632 ms | 1.3% slower |
| `iss--Uj9xZPyzM4` | 2036 ms | 2037 ms | identical |
| `tc-blank-nonconsecutive` | 5177 ms | 5196 ms | within 0.4% |
| `tc-nc-no-r3c4` | 2076 ms | 2061 ms | within 0.7% |

Level 2 also allocates more on every case (0.97 / 0.63 / 0.66 MB against 1.06 / 0.72 / 0.77 MB). Its
credited 3.0% in § "The sharper filter" came from a corpus total under the protocol § "Step 4" shows
is worth +/-5%. **Default is now level 1.** Being sharper about which pops to skip does not pay when
the skipped pops are cheap — and the sharper table costs nine times the memory and nine times the
zeta transform, ~0.9 ms per puzzle against a median ISS solve of 1.1 ms.

Level 2 stays available, because it is exact and the switch is what made this comparison possible
from one build.

### Where this leaves cell forcing

The per-row filtering line of attack is **closed**, with numbers rather than opinion: a perfect
oracle recovers 12.9% of the scan cost in the winning regime, the scan is single-digit percent of
runtime, and no static property separates productive rows from expensive ones because they are the
same rows.

What the census leaves standing, in order:

1. **The enqueue path.** It is the only remaining line item proportional to something huge (board
   writes), and it is now the least-measured. Worth trying: replace the bitmap with a per-cell byte
   holding `max(popcount(S))` over that cell's rows and reject when `candCount` exceeds it — 81 bytes
   permanently in L1, no zeta transform, no per-puzzle build. It is a weaker filter than can-fire, so
   the question is whether the pops it lets through cost less than the lookups it saves. The census
   says pops are cheap, which is the argument for trying it.
2. **The entry-point gate**, still orthogonal and still unbuilt: `truecandidates` is -46% with cell
   forcing on, `count` on the ISS corpus is not. That is a decision about *when to run it at all*,
   which is where the leverage actually was all along — see § "Where it does pay".
3. **Not** an easy-puzzle cutoff on the filter build. It would recover most of that ~0.9 ms and it
   would also change the measurement surface under everything above, which is why it is last: a
   cutoff makes the cheap cases cheap for a reason unrelated to cell forcing, and every later
   comparison then has to be read through it.

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
2. ~~**Cost-sorted constraint iteration.**~~ — **done**, see § "Step 2 is done". Built as slot
   renumbering; weights are cost/P(fire), not cost. Worth ~1-3% of runtime on mixed-constraint
   puzzles and nothing at all on single-constraint ones. The prediction that the blocker was cost
   data held: the plumbing was an afternoon, the measurement was the work.
3. ~~**The enqueue filter**, then cell forcing as a tier on top~~ — **built and measured**, see
   § "Step 3". Both filters are exact and ship gated off. Cell forcing went from ~2x too costly to
   1.22x on ISS counting, but is **-46% on non-consecutive true candidates**, so the remaining work
   is choosing *when* to enable it rather than making it universally cheaper. The filter's stated
   risk was real but not decisive: the write-path lookup roughly cancelled its own savings because
   it caught the cheap no-ops, not the expensive ones.

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
