# The pathological ISS outliers: two different diseases

Date: 2026-08-04. The four outliers from the ISS import, re-measured under the current default
(deferred discovery + grouped weak links) as `handoff.md` required, then split by cause.

**The tail is not fixed.** Deferred discovery rescued `Wb5YT1b-U9Q`; it did nothing for the rest.

> **Superseded in part, 2026-08-19.** `h-ymyScJa2s` and `blPgSzctUMg` are fixed — 26 ms and 4 ms —
> by renban required-value exclusion. The two whisper-dominated puzzles are not. See the update
> before "Suggested next steps", and [`renban-required-values.md`](renban-required-values.md).

| id | ISS | previously | now | vs ISS | constraints |
| --- | ---: | ---: | ---: | ---: | --- |
| `Wb5YT1b-U9Q` | 7.4 ms | 308 → 21 ms | **20.8 ms** | 2.8× | AntiKnight, Renban, Sandwich |
| `blPgSzctUMg` | 14 ms | 887 → 709 ms | **679.9 ms** | 49× | Renban, Sandwich |
| `h-ymyScJa2s` | 105 ms | 9.7 s | **8.5 s** | 81× | Renban |
| `OqyXKDOhfDA` | 411 ms | >10 s | **20.2 s** | 49× | Thermo, Whisper |
| `1HuNjcLWlPE` | **72 ms** | >240 s | **did not finish in 28 min** | **>23,000×** | Whisper, Renban, dots |

Timings at 15 iterations where the case is fast enough; the multi-second ones are single-run.

## The split: search size vs cost per node

A node-rate probe (20 s budget, count nodes) separates the two failure modes cleanly, and they are
opposites:

| id | nodes | nodes/sec | diagnosis |
| --- | ---: | ---: | --- |
| `1HuNjcLWlPE` | 25,333,813 (unfinished) | **1,266,515** | tree is astronomically large |
| `OqyXKDOhfDA` | 21,336,431 | 1,071,583 | tree is large |
| `h-ymyScJa2s` | 7,009,073 | 827,781 | tree is large |
| `blPgSzctUMg` | **2,870** | **3,470** | **280 µs/node** |
| `Wb5YT1b-U9Q` | 178 | 8,227 | high per-node cost |

- **Group A — `1HuNjcLWlPE`, `OqyXKDOhfDA`, `h-ymyScJa2s`: a propagation-strength problem.** The
  node rate is *healthy* (~1M nodes/s); there are simply far too many nodes. ISS solves
  `1HuNjcLWlPE` in 72 ms, so its search must be some four orders of magnitude smaller. This is not
  something a faster inner loop can fix — we are missing deductions ISS makes.
- **Group B — `blPgSzctUMg`, `Wb5YT1b-U9Q`: a cost-per-node problem.** `blPgSzctUMg` explores only
  **2,870 nodes** and still takes 680 ms, at 280 µs/node against ~1 µs/node in group A. It also
  allocated **1,529 MB**, i.e. 533 KB *per node*.

Do not treat these as one list. Group B is an implementation problem in one constraint; group A is an
algorithmic gap.

## Group B is Sandwich, and it is mostly one line

`Combinations` attribution by `[CallerFilePath]`/`[CallerLineNumber]`, one count of `blPgSzctUMg`:

```
SandwichConstraint.cs:544:  4,215,029 combinations     <- 1,469 per node
SandwichConstraint.cs:419:     91,184
SandwichConstraint.cs:315:     17,405
```

All three sites share one shape: enumerate every `C(n,k)` combination of the candidate values, reject
by sum, then permute the survivors. Two costs were removed:

1. **A `List<int>` per combination** → `CombinationsBuffered`. All four Sandwich sites were verified
   non-retaining: `Sum` is eager, `Permutations` copies its input via `ToList()`, and the fourth site
   only reads the combination.
2. **`combination.Sum()`** → a manual loop. `Enumerable.Sum` on a `List<int>` boxes the list's struct
   enumerator, and this ran on all 4.2 million combinations.

Paired A/B at 15 iterations:

| case | before | after | |
| --- | ---: | ---: | --- |
| blPgSzctUMg | 710.99 ms / 1,528.90 MB | 679.92 / **1,228.74** | time −4.4%, **alloc −19.6%** |
| Wb5YT1b-U9Q | 21.13 ms / 35.58 MB | 20.80 / 31.32 | flat, alloc −12.0% |

### Then the framing changed: exactness was the problem, not its cost

The remaining cost was `Extensions.Permutations` — a recursive iterator yielding k! permutations per
surviving combination — and the obvious move was to compute the same answer faster with bipartite
matching. **That was the wrong question.** `StepLogic` is propagation only; `EnforceConstraint` plus
the weak links are what make an answer correct. So the real question is not "how do we compute the
exact placement set cheaply" but "do we want the exact placement set here at all", and brute force
and logical solving get to answer it differently.

`blPgSzctUMg` was already the evidence. **2,870 nodes** is an extraordinarily small tree; a solver
spending 680 ms to keep it that small is *over*-propagating — paying eagerly at every node to avoid
branches the search dispatches lazily and only where needed.

Three arms, best of 5 runs, `SUDOKU_SANDWICH_BF_ARM`:

| case | arm | nodes | time | alloc |
| --- | --- | ---: | ---: | ---: |
| blPgSzctUMg | exact | 2,870 | 630.1 ms | 1,195 MB |
| | **heuristic** | 3,790 | **26.6 ms** | **47 MB** |
| | none | 55,802,068+ | **>60,000 ms** | — |
| Wb5YT1b-U9Q | exact | 178 | 20.1 ms | 31 MB |
| | **heuristic** | 174 | **0.7 ms** | **1 MB** |
| | none | 2,000 | 5.7 ms | — |
| blank-sw2 (cap 5k) | exact | 13,672 | 157.5 ms | 275 MB |
| | **heuristic** | 13,721 | **12.5 ms** | **8 MB** |
| | none | 82,142 | 59.6 ms | — |

**The exact placement test bought almost no search reduction** — 32% more nodes at worst, and
slightly *fewer* on one case — for 12–29× the time and 25–34× the allocation. `heuristic` (union whole
value sets, skip the placement enumeration) is now the brute-force default; logical solving keeps
`exact`.

`none` is the opposite mistake and worth recording: it beats `exact` on easy boards but explodes to
55M nodes on `blPgSzctUMg`. The shape `IndexerConstraint` uses ("just let weak links handle it when
brute forcing") does **not** transfer here. The sandwich needs propagation; it did not need exactness.

Effect on the real distribution: **ISS corpus 12,772 → 11,872 ms, −7.0%**, from this one constraint.
`blPgSzctUMg` goes from 49× off ISS to about **1.9×**, which closes it as an outlier.

There is a second reason to prefer the heuristic that has nothing to do with speed. An elimination
justified by "I enumerated 40,320 permutations and 6 never landed in the second cell" is not a weak
explanation, it is a *non*-explanation — and for a UI whose value is showing solve paths, that is a
defect. The range/Hall argument a human actually makes is both cheaper and the thing worth
displaying, so the logical arm may want to move too. That is a product call about step quality rather
than a perf one, and it is left open.

## The regime trap: the same experiment on Skyscraper reached the opposite answer

Date: 2026-08-05. `SkyscraperConstraint` looked like the next sandwich: ~130× X-Sum's cost per
solution, and an `isBruteForcing` flag it accepts and ignores. It is **not** a defect, and the way
that nearly went wrong is the most transferable thing on this page.

Its `StepLogic` runs `SkyscraperSearch`, an allocation-free DFS over full-line assignments that
already replaced an older permutation filter. `EnforceConstraint` checks the visible count and
`InitCandidates` applies the static "restrict high digits" bounds, so the DFS is pure propagation and
skipping it is answer-preserving. Measured on blank grids with a few clues and a cap of 100, skipping
it looked like a triumph:

| case | arm | nodes | time |
| --- | --- | ---: | ---: |
| sky2-c4 | exact | 240 | 150.3 ms |
| | none | 1,335 | **5.9 ms** |
| sky4-mixed | exact | 248 | 300.4 ms |
| | none | 463 | **2.2 ms** |
| sky2-c2 | exact | 242 | 236.1 ms |
| | none | 7,161 | **17.2 ms** |

14–137× faster. Then the same two arms on workloads that look like real puzzles — 36 clues derived
from a solved grid, and the 8-clue board counted *exhaustively* instead of to a cap:

| case | arm | nodes | time |
| --- | --- | ---: | ---: |
| sky36-real (36 clues, uncapped) | **exact** | 555 | **46.3 ms** |
| | none | 7,068,913 | **9,702.6 ms** |
| sky8-blank (uncapped) | **exact** | 226 | **309.2 ms** |
| | none | 227,358,502 | **>120,000 ms** |

**Exact is 209× faster, and skipping propagation explodes to 227M nodes.** The DFS earns its keep;
the 130×-per-solution figure is the price of propagation that repays 209×. No change was made.

### Why the first measurement lied

A blank grid with 2–4 clues counted to a cap of 100 asks for a hundred solutions out of an
astronomically large set. Almost nothing needs pruning, so propagation is close to pure overhead and
*any* arm that does less looks better. Requiring an exhaustive count, or adding enough clues to make
the board nearly unique, inverts it.

**Do not tune propagation strength on blank-grid capped counts.** That includes the
`xsum-search` / `skyscraper-search` / `sandwich-search` cases added to `corpus.json` on 2026-08-04 —
they are good *regression detectors* (they notice when behaviour changes) and bad *tuning targets*
(they systematically favour propagating less). Tune on real puzzles, uncapped.

The sandwich result above survives this test, which is why it shipped: it was measured on two real
uncapped unique-solution puzzles and the whole 398-puzzle ISS corpus, and a follow-up 18-clue
constructed puzzle confirms it — heuristic 12.7 ms against exact's 103.9 ms, both returning 364.
Had sandwich only been measured the way skyscraper first was, the same 8× win would have been
indistinguishable from skyscraper's 137× illusion.

## Group A: what the constraint statistics say

Renban appears in all five outliers, which looks damning but needed checking — it is in 97 of 398
corpus puzzles, so it is simply common. Median solve time with and without each constraint over the
whole ISS corpus, with a constraint-count control to rule out "these puzzles just have more stuff":

| constraint | n | median with | median without | ratio | #constraints w/ vs w/o |
| --- | ---: | ---: | ---: | ---: | ---: |
| **Renban** | 97 | 5.27 ms | 1.17 | **4.50×** | 16 vs 13 |
| **Whisper** | 134 | 3.46 | 1.20 | **2.89×** | 14 vs 13 |
| Entropic | 13 | 4.28 | 1.64 | 2.61× | 14 vs 13 |
| RegionSumLine | 80 | 2.06 | 1.54 | 1.33× | 11 vs 14 |
| Cage | 129 | 1.62 | 1.77 | 0.92× | 15 vs 13 |
| AntiKnight | 35 | 0.77 | 1.90 | 0.41× | 11 vs 14 |
| **Thermo** | 67 | 0.46 | 2.27 | **0.20×** | 13 vs 14 |

**`Renban` and `Whisper` are the two constraints most associated with slow solves**, at 4.5× and 2.9×
the median, on large samples, and not explained by constraint count. `Thermo` is the mirror image at
0.20× — thermos prune hard, so puzzles containing them are *five times faster* than those without.
Group A's profile (huge trees at healthy node rates) is exactly what weak propagation looks like, and
`1HuNjcLWlPE` is the extreme case: whispers, renban and dots, **no givens at all**.

A methodological note, because it nearly sent this the wrong way: ranking by *slowest 40 absolute*
instead of by median made `RegionSumLine` look like the top suspect at 2.36× enrichment. It is
tail-driven, and those puzzles have *fewer* constraints than average. Median with a count control is
the honest statistic here.

## Update 2026-08-19: step 2 was done, and it closed the renban half

**Renban propagation was the gap, and the missing deduction was required-value exclusion.**
`h-ymyScJa2s` 8,293 → **26 ms** (321×, and 4× faster than ISS), `blPgSzctUMg` 26 → **4 ms**. The
group-A table above is superseded for those two. Full write-up, including why
`FastAdvancedStrategies` during brute force is a trap and why deferral does not rescue it:
[`renban-required-values.md`](renban-required-values.md).

The group-A/group-B split held up, and the node-rate probe is now cheaper than that: **ISS's own
`guesses` and `solve_ms` are in `data/mappings.json`** for every puzzle in the index, so the
"nodes or cost per node?" question can be answered before writing any probe.

One correction to the framing above: the `Thermo` 0.20× / `Renban` 4.50× / `Whisper` 2.89× spread is
not really about those constraints. It is about **which constraints have brute-force propagation at
all** — `ThermometerConstraint` has a real `StepLogic`, while whispers and dots declare that weak
links enforce them, and weak links only fire on `SetValue`.

## Suggested next steps

1. **The whisper half.** `OqyXKDOhfDA` is 19.0 s / 42.7 M nodes against ISS's 411 ms / 3,936
   guesses, and `1HuNjcLWlPE` still does not finish. A generic weak-link arc-consistency probe
   already measures **19,054 → 1,913 ms** on the former, so the prize is known and the fix is
   contained: [`renban-required-values.md`](renban-required-values.md) §6.
2. **Sandwich's `Permutations()`** — group B's remaining 1.2 GB, and a self-contained fix.
3. Leave `Wb5YT1b-U9Q` alone; at 2.8× off ISS it is no longer an outlier.

## Reproducing

The raw `.iss` text is not in the repo and these puzzles exceed the import's ceilings, so fetch by id:

```bash
for id in 1HuNjcLWlPE blPgSzctUMg h-ymyScJa2s OqyXKDOhfDA Wb5YT1b-U9Q; do
  curl -sS -f -o "$id.iss" "https://sigh.github.io/iss-sudoku-index/data/puzzles/$id/puzzle.iss"
done
```

Then build a benchmark corpus of `{"name":…, "iss":<text>, "op":"count"}` cases. `1HuNjcLWlPE` will
not terminate — give it a node budget or a cancellation token rather than waiting.
