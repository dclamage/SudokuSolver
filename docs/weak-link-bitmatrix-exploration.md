# A dense weak-link bitmatrix: measured, and mostly a no

Date: 2026-08-29. Exploration, not an implementation. Prototype parked on `explore/wl-bitmatrix`
(not merged); this document is the deliverable.

The idea: add a 729 x 729 bit view of the weak-link relation (~70 KB at 9x9, 12 `ulong`s per
candidate row) alongside the existing three, on the observation that `wlGrouped*` and `cf*` are the
same relation folded on opposite endpoints. Three hoped-for payoffs — `IsWeakLink` as load/shift/test,
cell forcing as an AND of rows, set-level queries as row ops — plus a possible fourth quadrant,
"group forcing".

## Verdict

| claim | verdict |
| --- | --- |
| cell forcing as row-AND makes it cheap enough to switch on | **No.** 1.8-6.8% *slower* than the pruned CSR where cell forcing is hot, and the premise is wrong twice over |
| `IsWeakLink` as load/shift/test is worth having | **Not where the brief expected**, and 99.8% of the traffic is one constraint |
| that one constraint | **Yes, and it is large**: `skyscraper-search` 131.5 -> 50.3 ms, **0.382x** as shipped |
| group forcing falls out for free | The *primitive* does; the wake condition and the instrument to value it do not |

Net recommendation: **do not add the bitmatrix as a general derived view.** Add it as a view the
board owns and builds only when some constraint declares it needs it — today only
`SkyscraperConstraint`. See § "What was built" for why the data must not live on the constraint.

## Order parity: the flagged risk is not a risk

Checked before interpreting any timing, as instructed. Cell-forcing pop censuses
(`SUDOKU_CF_STATS=1`) over all 398 ISS puzzles, CSR path against row-AND path:

```
pops 28,998,000 | value set 7,500,284 | <2 cands 533,026 | nothing fired 0
              | fired-target-gone 16,722,302 | eliminated 4,242,388
```

**Bit-identical, every bucket, both arms.** The two orders coincide, and structurally rather than by
luck: `CompileCellForcingTable` does `touched.Sort()` on candidate indices and the default
`SUDOKU_CF_ORDER=target` preserves that, while `TrailingZeroCount` walks bits in ascending candidate
index — the same sequence. Both then group clears by target cell, so the same `ClearMaskFromCell`
calls happen in the same order.

Worth knowing for any future row-AND work: parity is free *as long as* the CSR stays in candidate
order. `SUDOKU_CF_ORDER=popcount` would break it.

(On `corpus.json` the censuses differ by ~0.006%. That is not the paths disagreeing — the same arm
run twice differs too, because the `est-*` cases sample randomly. Control before concluding.)

## Cell forcing: the bet was wrong, twice

§ "The open empirical question" of the brief: the bitmatrix gets no `popcount(S) < 2` pruning, and
the bet is that AND-until-zero wins anyway "because the no-op path dominates and the accumulator
zeroes after two or three rows."

Measured, with the § "Step 4" protocol (long cases, interleaved, 9 iterations):

| case | CSR | row-AND | |
| --- | ---: | ---: | --- |
| `nc-4given` count | 34.7 ms | 37.1 ms | **+6.8%** |
| `tc-blank-nonconsecutive` true candidates | 5159 ms | 5354 ms | **+3.8%** |
| `tc-nc-no-r3c4` true candidates | 2046 ms | 2083 ms | **+1.8%** |
| `iss--Uj9xZPyzM4` count | 2019 ms | 2005 ms | -0.7% (noise; few CF pops) |

Allocation rises with it (0.97/0.63/0.66 -> 1.11/0.77/0.80 MB).

**Why the accumulator does not zero early:** the pop census says "scanned, nothing fired" is **0.0%**
of pops. The enqueue filter is exact and only queues a cell when a row genuinely fires, so by the
time the scan runs there is essentially always a surviving bit. The no-op path the bet relies on was
removed by `cfCanFire` two steps ago. The row-AND therefore pays its full `|cand| x 12` word loads
every time — rows 96 bytes apart — where the CSR walks a *pruned*, contiguous run of 4-byte masks
that prefetches.

**And the ceiling was low regardless.** `docs/cell-forcing-worklist.md` § "Step 5" bounds the entire
table scan at single-digit percent of runtime: removing 223.4M of 875.7M row scans measured *slower*.
The brief's break-even needs a ~2x total cost reduction. Even an infinitely fast scan does not
deliver it, so no scan representation can. The premise "does the row-AND make cell forcing cheap
enough to switch on" was answerable before the prototype, and the prototype agrees.

## `IsWeakLink`: 99.8% of the traffic is one constraint's inner loop

Counted per call site (throwaway probe, on `explore/wl-bitmatrix`):

| site | calls, `corpus.json` |
| --- | ---: |
| `SkyscraperConstraint` support search | **92,689,284** |
| `FastFindTriples` | 95,809 |
| `FastFindPairs` | 14,046 |
| `SolverLogic` triples, `SlowThermometer` | 0 |
| **`FindBestBilocal`** | **0** |
| whole ISS corpus, all sites | 110,190 |

Two things to record:

- **`FindBestBilocal` contributes nothing because the bilocal tier is off by default.**
  `BILOCAL_SEARCH_WEIGHT_DEFAULT = 0`, so `GetLeastCandidateCell`'s `bilocalWeightPercent > 0` guard
  is never taken. The brief's "runs per node" reading of it is correct about the code and wrong about
  the configuration.
- **Weak-link adjacency is not a hot query in the search.** 110,190 calls across 398 puzzles is
  nothing; ~5.1 binary-search steps each. The representation of `IsWeakLink` cannot matter there,
  under any AOT. (The WASM `call_indirect` concern in `SolverAnalysis.cs`'s remarks is about
  `Comparer<int>.Default`, which the hand-rolled search already removed.)

## Skyscraper: a real 1.87x, and the structure is what wins

`skyscraper-search` does 18.9M `IsWeakLink` calls per iteration in a 158 ms case — at ~8.4 ns per
call that is essentially the whole case. The support search asks, per candidate: *is this candidate
weakly linked to any of the (up to 8) already-assigned candidates?*

Three implementations of that one question, 9 iterations:

| arm | `skyscraper-search` | |
| --- | ---: | --- |
| list, `pos` binary searches per candidate (today) | 129.6 ms | baseline |
| **bitmatrix row AND assigned-bitset** | **69.3 ms** | **0.535x** |
| per-candidate block counts, incremented over the DFS | 133.4 ms | 1.03x |

`variant-renban-sky` 52.8 -> 53.4 ms and `renban-sky-logical` 473.6 -> 471.2 ms (flat; the logical
path has no matrix). Results unchanged in every arm.

The counts arm is the interesting control. It answers the same query in O(1) per test using only the
existing lists, and it is **no better than the baseline** — because this DFS prunes hard and does
few tests per assignment, so per-assign maintenance (~22 increments and ~22 decrements) swamps the
per-test saving. The bitmatrix wins because it is the only one of the three with *zero* maintenance
cost: one bit set per assign, one 12-word pass per test.

So the win here is genuinely a representation win, and it is specific to the query shape "is X
adjacent to any of this small, evolving set?". Nothing else in the solver currently asks that.

## Cost of carrying it

~70 KB per compiled search, shared by reference like the other views, and **~0.1 ms per compile**
(`iss-blPgSzctUMg` 4.08 -> 4.19 ms, +0.07 MB). That is an order of magnitude cheaper than
`cfNewlyFires`' ~0.9 ms — the matrix is a linear fill, not a zeta transform — but it is not free, and
paying it on every `truecandidates` edit for puzzles with no Skyscraper constraint is pure loss.
Gate on a consumer existing.

Memory is quadratic: 16x16 gives 4096^2 bits = 2 MB. Same ceiling `cfCanFire` hits; the prototype
caps at `MAX_VALUE <= 12` and falls back to the list path.

## Group forcing, the missing quadrant

The primitive does fall out: for group G and value v, AND the rows of every `(cell, v)` still holding
v; surviving bits are eliminations. Same row-AND, different index set — and it is a *new deduction*,
not a faster old one, so `FastFindPointing` being a restricted case of it is a real gap.

Two things stop it being a cheap experiment, neither about the primitive:

1. **No cheap wake condition.** A cell's forcing set changes when that cell loses a candidate — one
   dirty item per board write. A group's changes when *any* of its cells loses a candidate, which is
   most writes, against a 9x-larger unit. `docs/cell-forcing-worklist.md` § "Where the waste is"
   already shows cell forcing losing on trigger economics; group forcing starts worse.
2. **No instrument to value it.** Its payoff is pruning, measured in nodes, and this repo has no
   committed node counter (`NodeBudget.nodesVisited` only counts under a budget and is never
   surfaced). Every node-count figure in these docs came from throwaway instrumentation.

If group forcing is worth pursuing, the prerequisite is a node counter in the harness, not a
bitmatrix.

## What was built, and why not what this document first recommended

This section originally recommended a ~1 KB line-local bitset stored on `SkyscraperConstraint`, on
the grounds that the support search only ever asks about candidates on its own line. **That was the
wrong home for it,** as the repo owner pointed out: the adjacency is derived from *one board's* weak
links, and a constraint instance is not board-scoped by contract — it is handed a solver, it does not
remember one. A constraint carrying it would answer for the wrong board the moment the same instance
were added to a second one (a campaign reusing constraints across puzzles), and the failure mode is
silent: under-reported adjacency makes candidates look supported when they are not, so propagation
quietly weakens instead of breaking. `needsLogic` is not a precedent for it — that is derived from
`clue` and `MAX_VALUE`, which are fixed when the constraint is constructed.

What shipped instead keeps the data on the board and leaves the constraint stateless:

- **`Solver` owns the matrix** (`SolverWeakLinkMatrix.cs`), built, shared by reference and invalidated
  in exactly the places its three sibling views are. Nothing new to reason about: `AddWeakLink` nulls
  it, `DiscoverWeakLinks` nulls it, `CompileGroupedWeakLinks` rebuilds it, and null means "use the
  lists".
- **The constraint only declares a need**: `Constraint.WantsWeakLinkMatrix`, default false, overridden
  to true by `SkyscraperConstraint`. No puzzle without a declaring constraint pays the ~70 KB or the
  ~0.1 ms build.
- **It is also compiled at the setup fixpoint**, not only from `CompileGroupedWeakLinks`, because the
  logical solver never reaches the latter. That is why `variant-renban-sky` gains here and did not in
  the prototype.
- `SUDOKU_WL_MATRIX=0` forces the list path, so the two arms are checkable from one build.

Measured in one build, paired, 9 iterations:

| case | matrix off | matrix on | |
| --- | ---: | ---: | --- |
| `skyscraper-search` count | 131.5 ms | **50.3 ms** | **0.382x** |
| `variant-renban-sky` solve | 52.1 ms | **22.2 ms** | **0.426x** |
| `renban-sky-logical` | 463.1 ms | 439.4 ms | 0.949x |
| `iss-blPgSzctUMg` (no skyscraper) | 4.16 ms | 4.08 ms | unchanged, matrix not built |

Both corpora validate under both arms, and `SkyscraperConstraint` now has the tests it did not have
when its inner loop was rewritten. One known and accepted gap: on the logical path, a mid-solve
`AddWeakLink` nulls the matrix and nothing rebuilds it, so that solve finishes on the list path.
Correct, just not accelerated.

The cost of the 12-word rows over the 1 KB variant's 2-word rows is real but was not worth the
architecture: the measured 0.382x already beats the prototype's 0.535x, because reaching the setup
fixpoint mattered more than row width did.

Superseded, kept for the reasoning:

1. ~~**Put a bitset in `SkyscraperConstraint`.**~~ It needs one row per *candidate it might test*, not
   the full matrix — the support search only ever asks about candidates on its own line. That is 9
   cells x 9 values = 81 rows, ~1 KB, built when the constraint initializes. Same 0.535x, none of
   the 70 KB or the per-compile cost, and no new solver-wide view. **Rejected: wrong owner, see
   above.**
2. **Leave the three existing views alone** for their own sakes. A fourth now exists, but only
   because one constraint asks a question none of the three can answer — "is this candidate adjacent
   to any member of this set" — and only for boards where something asks it. The naming observation
   stands on its own: `wlGrouped*` and `cf*` are transposes of one relation.
3. **A node counter in the benchmark harness**, if group forcing or any other pruning change is next.
   It is the missing instrument behind three of these documents.
