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

**Tier 2 is disabled everywhere the `solve`/`count` search runs**, and as of 2026-08-04 that is a
measured decision rather than an unexplained one. The `solve`/`count` sites pass
`state.bilocalWeightPercent`, which defaults to 0; the true-candidates search
(`SolverBruteForce.cs:1442`) passes nothing and so gets ISS's weight of 50, which is what it has
always used. The section below is why the search sites are 0.

## Enabling bilocals is worse on real puzzles — and the small corpus says the opposite

**Conclusion first: leave `BilocalSearchWeightPercent` at 0.** It is 0 because it was measured, not
because it was never tried.

The tier is now controlled by a weight rather than a bool (`bilocalWeightPercent`, env
`SUDOKU_BILOCAL_WEIGHT`). A bilocal wins when
`maxCS * csBestCount * weightPercent > csBestScore * 100`, so weight 50 reproduces ISS exactly and 0
disables the tier.

### First, on the 28-case corpus — which is misleading

| case | w=0 | w=25 | w=50 | w=100 | w=200 |
| --- | ---: | ---: | ---: | ---: | ---: |
| kropki-search-cap50k | 3,045,565 | 0.96× | **0.27×** | 0.27× | 0.29× |
| variant-cloneways | 71 | 0.70× | 0.20× | 0.25× | 0.25× |
| variant-renban-sky | 13 | 1.00× | 0.46× | 0.46× | 0.46× |
| killer-cage | 8,303 | 1.02× | 0.52× | 0.90× | 0.72× |
| littlekiller-10 | 17,090 | 1.06× | 0.69× | **7.71×** | 6.48× |
| escargot | 16 | 1.00× | 1.00× | 6.81× | 6.81× |
| littlekiller-8 | 9 | 1.00× | 1.00× | 5.44× | 5.44× |
| variant-orbit | 5,255 | 1.00× | 2.12× | 2.51× | 3.04× |
| variant-killerblister | 150 | 1.00× | 3.65× | 8.79× | 9.01× |
| variant-equalsums | 204 | 1.00× | **6.13×** | 9.81× | 9.81× |
| **total nodes** | 13,227,164 | 0.992× | **0.832×** | 0.842× | 0.845× |

Read alone, that says weight 50 is a 17% win. **It is not.**

### Then, on `iss-tune` — 221 non-trivial puzzles, which is the authority

| | total nodes | p10 | p50 | p90 | worst | better / same / worse |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| w=50 | **7.71× worse** | 0.62× | 1.000× | 2.56× | **180×** | 63 / 64 / 94 |
| w=75 | 13.74× worse | 0.70× | 1.335× | 6.10× | 108× | 30 / 62 / 129 |

The median puzzle is untouched and the tail is savage: `L4BKOaUr1GE` 180×, `NjpUueqFEHY` 77×,
`AIV47AgKIO0` 32×. A handful of puzzles do improve enormously (`qIqlRzG5x5E` 0.03×,
`1t-ASgvUrEw` 0.04×), which is what the small corpus was picking up on.

**This is the clearest example yet of why the ISS corpus exists.** The 28-case corpus and the
398-puzzle corpus disagree about the *sign* of the effect, by an order of magnitude in each
direction. `handoff.md` already warned that the small corpus "is too small to separate a threshold
honestly"; this is what that failure looks like in practice, and it caught out the first pass of this
very investigation.

### Why the dial is coarse

Conflict scores are seeded uniformly at `MAX_VALUE * 3`, so early in a search
`maxCS ≈ csBestScore` and the condition collapses to `csBestCount * weightPercent > 100`. The weight
is therefore really encoding *"how many candidates must the conflict-score cell have before a 2-way
bilocal beats it"*:

- w ≤ 33 → needs `csBestCount ≥ 4`, which almost never wins. w=10 and w=25 are near no-ops.
- w = 34..99 → beats cells with **3+** candidates. This is the sane band, and ISS's 50 sits in it.
- w ≥ 100 → beats even **bivalue** cells, i.e. it overrides an already-minimal 2-way branch. That is
  why `escargot`, `littlekiller-8` and `littlekiller-10` fall off a cliff between 50 and 100.

So there is no intermediate weight that keeps the wins and drops the losses; the transition happens
all at once, and both sides of it are present at every setting in the sane band.

### Vanilla is untouched either way

`escargot` (16 nodes), `blank6-cap5M`, `vanilla-u17`, `killer-innie` and `arrow-search` are
bit-identical at w=50. This does **not** address the hard-vanilla gap versus ISS, which was the
original reason for looking here. Every effect is on variant puzzles.

### If anyone returns to this

Don't re-run the static sweep — that question is answered. The only shape that could still work is a
**deferred** one, mirroring `WeakLinkDiscoveryMode.Deferred`: search without bilocals, and if the
node budget blows, restart with them. The asymmetry that makes it plausible is that a restart only
triggers on searches that are *already* expensive, which is exactly where the 0.03× puzzles live, and
`SnapshotConflictState` already exists to make the retry clean. The risk is that the bad tail is just
as heavy as the good one, so a wrong-arm restart could make an expensive search far worse — it would
need scoring on the ratio distribution over `iss-tune`, confirmed on `iss-holdout`, before it could
ship.

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
