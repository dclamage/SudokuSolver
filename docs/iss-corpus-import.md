# Importing the CTC puzzle index

What the bulk import from [sigh's ISS index](https://sigh.github.io/iss-sudoku-index/) produced, what
it costs to go further, and the four defects it found. Written 2026-08-03.

The mechanics — how to run it, what the flags mean, and the tune/holdout discipline — are in
[`benchmarks/README.md`](../benchmarks/README.md). This file is the findings.

---

## 1. What we got

**398 real CTC puzzles** in `benchmarks/corpus-iss.json`, up from a 28-case hand-maintained corpus,
each validated against the solution count ISS itself recorded.

| outcome | rows | |
| --- | ---: | --- |
| **Agree** | **402** | our count equals ISS's; 398 fast enough for the corpus |
| Unsupported | 956 | a constraint with no equivalent here |
| NoExactCount | 307 | ISS never finished it either, so there is nothing to validate against |
| Timeout | 4 | we exceeded the 10 s budget |
| ParseError | 1 | see §4 |
| **Disagree** | **0** | |

Zero disagreements is the number that matters: it is what licenses trusting the corpus. The import
fails the run on any disagreement, because one means either the parser mistranslated a constraint or
the solver is wrong.

Of 1,364 index rows with an exact count, 1,363 had a puzzle file (18 of 1,671 ids 404 on the index
site). So the ceiling for this approach is ~1,363, and we reach 402 of it (30%).

## 2. Where we stand against ISS — this changes the earlier picture

[`solver-vs-iss-comparison.md`](solver-vs-iss-comparison.md) concluded ISS beats us **5–8×**. That
was measured on two hard vanilla classics, and it does not generalise. Across the 402 imported
puzzles, using ISS's own recorded `solve_ms`:

| percentile | ours / ISS |
| --- | ---: |
| p05 | 0.04× |
| p25 | 0.21× |
| **p50** | **0.38×** |
| p75 | 0.74× |
| p90 | 1.39× |
| p95 | 2.49× |
| p99 | 11.5× |
| max | 93.3× |

**We are faster than ISS on 338 of 402 (84%), and ~2.6× faster at the median.** The competitive gap
is not general: it is concentrated in hard *vanilla* puzzles (where ISS's specialised machinery wins)
and in a thin pathological tail. For a setting site — which runs variant puzzles — this is the
relevant distribution, and it is favourable.

Treat the ratios as indicative, not precise: ISS's times come from the index (different machine,
JS/V8), and ours are single-run wall clock from the import rather than the harness's min-of-N. The
*shape* is robust to that; individual ratios are not.

### The tail is a list of things to fix

12 puzzles are >5× slower than ISS and 3 are >20×. These are new outliers, none previously known:

| puzzle | ours | ISS | ratio |
| --- | ---: | ---: | ---: |
| `1HuNjcLWlPE` "N is for Naomi" | >240,000 ms | 71.8 ms | **>3,300×** |
| `h-ymyScJa2s` "Vivian" | 9,825 ms | 105.3 ms | 93× |
| `blPgSzctUMg` "How to slice a sandwich" | 875 ms | 14.0 ms | 63× |
| `Wb5YT1b-U9Q` "That Boy Ain't Right" | 314 ms | 7.4 ms | 42× |
| `OqyXKDOhfDA` "Zoom Out" | 21,709 ms | 410.9 ms | 53× |
| `7212m1XUn8Q` "New York City" | >10,000 ms | 644.8 ms | >16× |

`1HuNjcLWlPE` is the standout: whispers, renban and dots only, ISS solves it in 72 ms, and we did not
finish in **240 seconds**. Its translation is not the problem — the whisper loops it uses were
verified equivalent to their adjacent pairs (§3), and nothing else in it is exotic. It is the single
best pathological case now known, and being 4 constraint types wide it should be tractable to
diagnose.

## 3. Four defects the self-validation caught

Each of these was found *because* the import checks itself, and each was a silent wrong answer.

**Non-default digit sets were ignored.** `.Shape~9x9~0-8` is a 9x9 played with digits 0–8;
`.Shape~6x6~9` a 6x6 played with 1–9. The parser read the grid size and dropped the digit set, which
does not fail — it quietly solves a *different puzzle*. Caught as `CanKleiBRkg` "Manifold" losing its
only solution. 30 puzzles carry a digit set that is merely a restatement of the default and are
unaffected; the rest are now refused. There are 103 such puzzles in the index, so this was not an
edge case.

**Auxiliary variable references parsed as malformed cells.** ISS's DSL declares variables with `.Var`
and references them as `V` plus the name (`VD1`, `VR`), anywhere a cell may appear. These raised
`ArgumentException` — indistinguishable from a real format bug — for 22 puzzles. They are now
reported as unsupported, so bulk import skips them cleanly and the error channel stays meaningful.

**Repeated cells silently collapse some line constraints.** ISS writes a closed loop by repeating the
starting cell at the end. Whether that survives translation depends on the constraint, so it was
settled by exhaustive count on a 6x6 rather than by reasoning:

| line | closed loop | open line | |
| --- | ---: | ---: | --- |
| whisper | 626,688 | 1,410,048 | loop **equals** its four adjacent pairs ✔ |
| renban | **0** | 5,640,192 | collapses ✘ |
| between line | **0** | 4,700,160 | collapses ✘ |
| thermo / palindrome / double arrow | construction throws | — | loud, not silent |

Sliding-window constraints (whisper, entropic, modular) are correct as-is — independently confirmed
by three index puzzles that use repeated cells and import with the right count. Set-like and
positional ones (renban, nabner, between, region-sum line, thermo, palindrome, cage, all-different)
now refuse a repeat, the same way the arrow already refused a repeated shaft cell. No puzzle in the
index currently trips this, so it is insurance — but a renban loop would have silently counted 0.

**A cancelled `CountSolutions` looks exactly like a completed one.** It swallows
`OperationCanceledException` and returns however many solutions it had found so far. The first import
recorded two of these as *disagreements* (count 0 vs ISS's 1) when both were merely slow — and much
worse, a timeout that had already found `solutions_found` solutions would have been recorded as an
**agreement**, putting an unverified puzzle into the corpus. The importer now checks the token
itself; `CountSolutions`'s doc comment now says so.

## 4. The one unresolved case

`YYC5GdOkZKk` throws "the constraints are invalid (no solutions)" at construction. It is 18 killer
cages and one given; no single cage is at fault (removing any one still fails, removing all of them
builds), and ISS reports a unique solution.

What was ruled out: that ISS's `.Cage` is sum-only rather than a killer cage. Five ISS-validated
puzzles do contain cages impossible under distinctness — but every one is either a non-default digit
set (a *single-cell* cage of 12 needs digits past 9) or sits inside an `Or`/`Replicate` block, which
ISS applies conditionally and which we refuse wholesale. Against 402 agreements the killer reading
stands.

It fails loudly and can never enter the corpus, so it is contained. Left open deliberately.

## 5. The coverage ceiling, and what it would cost to raise it

`IssParser` gained seven mappings this round, each exercised by agreeing puzzles — which is the only
evidence that matters, since a wrong direction would have shown up as a disagreement:

| added | agreeing puzzles using it |
| --- | ---: |
| `AllDifferent` → extra region | 43 |
| `Quad` → quadruple (ISS names only the 2x2's top-left cell) | 27 |
| `Diagonal~1` / `~-1` → `dpos` / `dneg` | 25 |
| `Sandwich` → sandwich (row/col clue, out-of-range coordinate picks the axis) | 8 |
| `GreaterThan` → two-cell thermometer (ISS names the larger cell first) | 8 |
| `Indexing~R` / `~C` → row/col indexer | 7 |
| `AntiConsecutive` → `difference:neg1` | 4 |

The diagonal slope and the `GreaterThan` direction were the two coin-flips; both are confirmed, by 3
puzzles using only `.Diagonal~1`, 2 using only `.Diagonal~-1`, and 8 `GreaterThan` puzzles that would
have had every inequality inverted. Non-9x9 grids import too (5 6x6s, 1 8x8).

### Ranked by what remains blocked

| blocker | puzzles | assessment |
| --- | ---: | --- |
| `Var` / `Or` / `And` / `Replicate` / `End` | ~170+ | ISS's general constraint DSL, including **block structure** with `.End` terminators. A different modelling approach; not worth chasing. |
| **`NFA`** | **135** | **The big one — see below.** |
| **`Pair` / `PairX`** | **102** | Same machinery: a compiled automaton plus a named relation (`_non-consecutive`, `_canonical-pair`). `PairX` is a chain of cells over one binary relation, which maps onto `BinaryLookupConstraint` per adjacent pair. |
| `EqualSum` | 54 | Every instance seen references variables, so mostly blocked by `Var` regardless. |
| `Sum` | 39 | **Weighted** sums (`.Sum~54_=_2_2_2_1_1_1_1~cells`). Not expressible; skip. |
| `Shape` digit set | 103 | Correctly refused; would need a value range decoupled from grid size. |
| `NoBoxes` / `Jigsaw` / `ChaosConstruction` | ~70 | Region-structure changes rather than constraints; mostly co-occur with the DSL. |
| `LittleKiller` | 26 | ISS gives only a start cell (`.LittleKiller~17~R8C6`) with **no direction**, and the cell is not always on an edge, so the diagonal cannot be inferred. Skip unless the direction turns out to be encoded elsewhere. |
| `Between` | 20 | Distinct from the already-mapped `BetweenLine`; semantics unclear. Needs investigation before it can be trusted. |
| `CountingCircles`, `SumLine`, `PillArrow`, `DutchFlatmates`, `StrictXV`, … | ~60 | No equivalent constraint here. |

### NFA is the highest-value next step

`NFAConstraint` **already deserializes ISS's own `NFASerializer` base64url bitstream** — see its
header comment and `scripts/constraint-nfa-builder.mjs`. So `.NFA~<serialized>~<name>~<cells>` is
plausibly a near-direct translation, and it is worth **135 + 102 = 237 puzzles**, which would roughly
double the corpus.

The blocker is structural, not semantic: `NFAConstraint` has `ConsoleName = null` and a constructor
taking `(solver, int[] cellIndices, string serializedNFA, name)`, so it cannot be built from a
constraint string. `IssParser` produces strings and hands them to `SolverFactory.CreateFromGivens`,
which calls `FinalizeConstraints()` internally — and constraints must be added before that. The
f-puzzles path already does the programmatic version (`SolverFactory.cs:1017`), so the work is to
give `IssParser` the same shape rather than to invent anything.

Two things to check first: that ISS's serialized strings round-trip through `NFADeserializer` (the
builder is described as local to this repo, so compatibility is by construction but untested against
ISS's own output), and how `.Pair`'s `~~`-separated groups and `_named-relation` segments are meant
to be read. Many `.NFA` instances address variables rather than cells and will stay out of reach
regardless.

### Cheap leftovers

- `GreaterThan` with 3–5 arguments (157 occurrences, 6 puzzles blocked solely by it) is probably a
  descending chain, which is one reversed thermometer. Ambiguous against "greater than each of", so
  implement it and let the import adjudicate — a wrong reading over-constrains and will disagree.
- The 4 agreeing-but-slow puzzles excluded by `--corpus-max-ms 2000` can be promoted deliberately.
