# Branch ordering in the brute-force search

Date: 2026-08-04. Everything here is measured on **node counts**, which are deterministic — immune
to every timing trap in `handoff.md` § "Benchmarking traps". Timings quoted alongside are from
`--iterations 1` unless stated and are indicative only.

A "node" is one iteration of the search loop in `FindSolutionInternal` /
`CountSolutionsInternal` — one pop, propagate, and branch.

## The branch-selection code, and what is actually live

`GetLeastCandidateCell` (`SolverBruteForceLogic.cs:5`) has three tiers:

1. **Conflict score** — maximise `conflictScores[cell] / candidateCount`. Seeded at
   `MAX_VALUE * 3` plus per-constraint priorities (`SolverInitialization.cs:590-616`), then
   incremented on every failed branch (VSIDS-style, with decay every 16,384 increments).
2. **Bilocal** — `FindBestBilocal`, explicitly "mirrors ISS `CandidateFinders.House`": a value with
   exactly two positions in a max-value group, scored by the higher of the two cells' conflict
   scores, allowed to *override* tier 1.
3. **MRV fallback** — fewest candidates, preferring small groups.

**Tier 2 is disabled everywhere the search actually runs.** All four search call sites pass
`allowBilocals: false`; only `SolverBruteForce.cs:1446` uses the default `true`. So the ISS-derived
bilocal finder is, in practice, dead code for `solve` and `count`.

## What enabling bilocals does

Flipping the two main search sites (`SolverBruteForce.cs:300` and `:679`) to
`allowBilocals: true`, default `Deferred` discovery, node counts on the 28-case corpus:

| case | bilocal OFF | bilocal ON | ratio |
| --- | ---: | ---: | ---: |
| kropki-search-cap50k | 3,045,565 | **822,348** | **0.27×** |
| variant-cloneways | 71 | 14 | 0.20× |
| variant-renban-sky | 13 | 6 | 0.46× |
| killer-cage | 8,303 | 4,289 | 0.52× |
| littlekiller-10 | 17,090 | 11,743 | 0.69× |
| est-escargot-6clue | 1,530 | 1,335 | 0.87× |
| variant-orbit | 5,255 | 11,142 | 2.12× |
| variant-killerblister | 150 | 547 | 3.65× |
| variant-equalsums | 204 | **1,250** | **6.13×** |
| blank4, blank6-cap5M, vanilla-u17-s, escargot, killer-innie, arrow-search, littlekiller-8, est-blank6 | — | — | 1.00× |

**6 better, 3 worse, 8 unchanged.** This is a genuine two-sided ordering lever, not a free win — the
same shape as the weak-link-discovery trade-off, and it should be treated the same way.

Two things worth noting:

- **`kropki-search-cap50k` at 3.7× fewer nodes is the largest single branch-ordering effect measured
  in this repo**, and it is large enough that its wall time moved with it (2,209 → 1,089 ms) despite
  the tier-0 JIT caveat.
- **Vanilla is completely untouched** — `escargot` (16 nodes), `blank6-cap5M`, `vanilla-u17`,
  `killer-innie` and `arrow-search` are all bit-identical. So this does **not** address the hard-vanilla
  gap versus ISS, which was the original hypothesis for looking here. The wins and losses are all on
  *variant* puzzles, which is the distribution the product actually runs.

Anyone tuning this should score the **ratio distribution across `iss-tune`**, not the 28-case corpus
and not a total — exactly the lesson from the discovery threshold, where scoring `total min ms`
picked a value that made the median puzzle slower.

## Why weak-link discovery increases node count (Priority 1.1)

Confirmed, on `Always` vs `Never`:

| case | never | always | nodes | never ms | always ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| littlekiller-10 | 5,104 | 15,090 | **2.96×** | 63.4 | 85.2 |
| killer-cage | 5,450 | 6,303 | 1.16× | 105.3 | 172.6 |
| escargot | 16 | 33 | 2.06× | 0.23 | 7.47 |

**`escargot` should come off this list**: at the shipped `Deferred` default it never triggers
discovery at all (`calls=0`, 16 nodes, 0.23 ms), because it finishes far inside the 2,000-node
threshold. Only `Always` hurts it, and by 32× in time.

`Deferred` measures as exactly `Always` **plus the 2,000-node budget** — 17,090 = 15,090 + 2,000 and
8,303 = 6,303 + 2,000 — which is direct confirmation that `SnapshotConflictState` makes the retry
bit-identical to an undeferred search.

### Two plausible mechanisms, both ruled out

- **Not conflict-score pollution.** The discovery scratch solver is a `Clone`, and clones share
  `conflictScores` by reference (`SolverInitialization.cs:138`), so probing *looks* like it should
  pollute the branch-ordering heuristic. It does not: `IncrementConflictScore` is only ever called
  from the search loops, guarded by `branchCellIndex >= 0`, and the scratch solver never enters them.
- **Not bilocals.** Discovery adds weak links, and the bilocal gate at
  `SolverBruteForceLogic.cs:170` tests `IsWeakLink` — but bilocals are disabled at every search call
  site, so that gate is never consulted during a search.

### The actual mechanism

`SetValue` propagates along weak links (`SolverModification.cs:123` and `:190`): every assignment
eliminates each candidate weakly linked to it. That is how discovery pays off — and also how it
changes ordering. More weak links means different candidate counts after each assignment, which
changes the `score / candidateCount` ranking in tier 1, which changes which cell gets branched on.

So the node-count increase is a **second-order consequence of stronger propagation**, not a defect.
It is the same class of thing as the bilocal trade-off above, and there is no bug to fix here. That
also means the per-node cost rises with link count, which is why `killer-cage` pays 64% more time for
only 16% more nodes.

## How productive is a discovery pass? (Priority 1.2)

`handoff.md` recorded that "nothing has counted this". Counted now, with `Always` forcing discovery
everywhere. `DiscoverWeakLinks` loops `do { probe every candidate } while (a candidate was
eliminated)`, so the continue-condition is *eliminations* while the cost is a full re-probe.

Per-pass, single-discovery-call cases on the 28-case corpus:

```
littlekiller-10:  1522 links/721 probes | 104/719 | 0 links/714 probes
killer-cage:      4132 links/654 probes |   0/650 | 0 links/649 probes
variant-equalsums: 7392 links/622 probes | 29,488 links/1,576 probes
```

Aggregated over the 258 `iss-tune` puzzles that trigger discovery (251 of them multi-pass):

| | links | probes |
| --- | ---: | ---: |
| pass 1 | 2,742,126 | 134,048 |
| passes ≥2 | 1,784,598 | 174,131 |
| **passes ≥2 share** | **39.4%** | **56.5%** |

- **Probes spent in passes that yielded zero links: 8.3%** on `iss-tune`, 27.9% on the 28-case corpus
  (where `killer-cage` alone wastes 1,299 of 1,953).
- **93 of 251 multi-pass puzzles get nothing at all from their extra passes.**

**Capping the pass count is therefore the wrong fix** — later passes produce 39% of all discovered
links, and `variant-equalsums` gets *four times more* from passes ≥2 than from pass 1. The waste is
not "later passes are useless", it is "the loop re-probes all ~650 candidates to discover that
nothing changed".

The fix that preserves the discovered set exactly is a **targeted re-probe**: a candidate probed
after the last board change in a pass was evaluated against the final board, so only candidates
probed *before* the last change are stale. Tracking the probe ordinal of the last mutation bounds the
next pass to that prefix instead of a full sweep. Note that the board can be mutated from two places
inside a pass — `ClearValue` on an invalid probe, and `AddWeakLink` itself
(`SolverInitialization.cs:293-310`, when one side is already a singleton) — so a version counter is
safer than tracking eliminations alone.

Expected value is bounded by the waste above: **~8% of discovery probes corpus-wide, ~28% on the
small corpus, up to 66% on `killer-cage`.** Discovery is a root-only cost, so this is a few percent
of total runtime on discovery-heavy puzzles — real, but much smaller than the bilocal lever.

## Reproducing

Node counts need a temporary counter; there is no persistent one (`NodeBudget.ChargeNode` returns
early and does not count when the budget is unlimited, which is the normal case). Drop a static
`BfCounters` class in `SudokuSolver/`, `Interlocked.Increment` it just after the `ChargeNode` call in
both search loops, print from `BenchCore.Run`, and `git checkout --` to revert. Force discovery with
`SUDOKU_WEAK_LINK_DISCOVERY=always|never|deferred` rather than rebuilding.
