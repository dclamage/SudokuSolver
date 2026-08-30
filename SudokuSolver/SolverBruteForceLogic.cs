namespace SudokuSolver;

public partial class Solver
{
    /// <summary>
    /// Picks the cell to branch on next, and optionally the value.
    /// </summary>
    /// <param name="bilocalWeightPercent">
    /// How strongly a bilocal may override the conflict-score choice, as a percentage. 0 disables
    /// the bilocal tier entirely; <see cref="BILOCAL_WEIGHT_ISS"/> (50) reproduces ISS's weighting.
    /// Higher values pick bilocals more often. See docs/branch-ordering.md.
    /// </param>
    private (int, int) GetLeastCandidateCell(long bilocalWeightPercent = BILOCAL_WEIGHT_ISS)
    {
        if (StaticBranchOrder)
        {
            for (int staticCell = 0; staticCell < NUM_CELLS; staticCell++)
            {
                if (!IsValueSet(board[staticCell]))
                {
                    return (staticCell, 0);
                }
            }
            return (-1, 0);
        }

        // Conflict-score path: rank cells by (score / candidateCount), highest first.
        // Only considers cells with score > 0 so a cold-start (all zeros) falls through to MRV.
        int csBestCell = -1;
        int csBestScore = -1;
        int csBestCount = 1;

        if (conflictScores != null)
        {
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint cellMask = board[cellIndex];
                if (IsValueSet(cellMask)) continue;

                int score = conflictScores[cellIndex];
                if (score == 0) continue;

                int count = ValueCount(cellMask);
                // Maximize score/count using cross-multiply to avoid floating point:
                // score/count > csBestScore/csBestCount  ↔  score*csBestCount > csBestScore*count
                if (csBestCell < 0 || score * csBestCount > csBestScore * count)
                {
                    csBestCell = cellIndex;
                    csBestScore = score;
                    csBestCount = count;
                }
            }
        }

        // Bilocal path — mirrors ISS CandidateFinders.House.
        // ISS scores bilocals as maxConflictScore(c0,c1) * w (w = 0.5) and compares against
        // the regular score/count metric.  A bilocal wins when:
        //   maxCS * w > csBestScore / csBestCount
        //   ↔  maxCS * csBestCount * weightPercent > csBestScore * 100
        //
        // Run this alongside the conflict-score path (not only as a fallback) so that
        // a well-scoring bilocal can override even a strong conflict-score candidate.
        if (bilocalWeightPercent > 0)
        {
            var (bCell, bOtherCell, bVal) = FindBestBilocal();
            if (bVal > 0)
            {
                if (csBestCell < 0)
                {
                    // No conflict-score winner: bilocal is our best structured hint.
                    return (bCell, bVal);
                }

                long bMaxCS = Math.Max(conflictScores[bCell],
                                       bOtherCell >= 0 ? conflictScores[bOtherCell] : 0);
                if (bMaxCS * csBestCount * bilocalWeightPercent > (long)csBestScore * 100)
                {
                    return (bCell, bVal);
                }
            }
        }

        if (csBestCell >= 0)
        {
            return (csBestCell, 0);
        }

        // Fallback: existing MRV logic (no conflict scores, no useful bilocal found above)
        int bestCellIndex = -1;
        int numCandidates = MAX_VALUE + 1;
        if (smallGroupsBySize != null)
        {
            int lastValidGroupSize = MAX_VALUE + 1;
            foreach (SudokuGroup group in smallGroupsBySize)
            {
                int groupSize = group.Cells.Count;
                if (lastValidGroupSize < groupSize)
                {
                    break;
                }

                foreach (int cellIndex in group.Cells)
                {
                    uint cellMask = board[cellIndex];
                    if (!IsValueSet(cellMask))
                    {
                        int curNumCandidates = ValueCount(cellMask);
                        if (curNumCandidates == 2)
                        {
                            return (cellIndex, 0);
                        }
                        if (curNumCandidates < numCandidates)
                        {
                            lastValidGroupSize = groupSize;
                            numCandidates = curNumCandidates;
                            bestCellIndex = cellIndex;
                        }
                    }
                }
            }
            if (bestCellIndex != -1)
            {
                return (bestCellIndex, 0);
            }
        }

        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint cellMask = board[cellIndex];
            if (!IsValueSet(cellMask))
            {
                int curNumCandidates = ValueCount(cellMask);
                if (curNumCandidates == 2)
                {
                    return (cellIndex, 0);
                }
                if (curNumCandidates < numCandidates)
                {
                    numCandidates = curNumCandidates;
                    bestCellIndex = cellIndex;
                }
            }
        }

        return (bestCellIndex, 0);
    }

    // Returns (primaryCell, otherCell, val) for the bilocal with the highest
    // max(conflictScores[c0], conflictScores[c1]).  primaryCell has the higher score.
    // Returns (-1, -1, 0) when no bilocal exists.
    private (int, int, int) FindBestBilocal()
    {
        if (maxValueGroups == null || maxValueGroups.Count == 0)
            return (-1, -1, 0);

        int bestC0 = -1, bestC1 = -1, bestVal = 0, bestScore = -1;

        foreach (SudokuGroup group in maxValueGroups)
        {
            List<int> groupCells = group.Cells;
            int numCells = groupCells.Count;

            uint atLeastOnce = 0, atLeastTwice = 0, moreThanTwice = 0;
            for (int gi = 0; gi < numCells; gi++)
            {
                uint mask = board[groupCells[gi]];
                moreThanTwice |= atLeastTwice & mask;
                atLeastTwice |= atLeastOnce & mask;
                atLeastOnce |= mask;
            }

            uint exactlyTwice = atLeastTwice & ~moreThanTwice & ~valueSetMask;
            while (exactlyTwice != 0)
            {
                int val = MinValue(exactlyTwice);
                exactlyTwice &= ~ValueMask(val);
                uint valMask = ValueMask(val);

                int c0 = -1, c1 = -1;
                foreach (int ci in groupCells)
                {
                    if ((board[ci] & valMask) != 0)
                    {
                        if (c0 < 0) c0 = ci;
                        else { c1 = ci; break; }
                    }
                }
                if (c0 < 0 || c1 < 0) continue;
                if (!IsWeakLink(CandidateIndex(c0, val), CandidateIndex(c1, val))) continue;

                // Score by max conflict score of the two cells (ISS CandidateFinders.House)
                int s0 = conflictScores != null ? conflictScores[c0] : 0;
                int s1 = conflictScores != null ? conflictScores[c1] : 0;
                int score = s0 >= s1 ? s0 : s1;

                if (score > bestScore)
                {
                    bestScore = score;
                    // Return the higher-scored cell as primary
                    if (s0 >= s1) { bestC0 = c0; bestC1 = c1; }
                    else           { bestC0 = c1; bestC1 = c0; }
                    bestVal = val;
                }
            }
        }
        return (bestC0, bestC1, bestVal);
    }

    /// <summary>
    /// Run some hand-selected logic until nothing changes
    /// </summary>
    /// <returns></returns>
    private LogicResult StepBruteForceLogic(bool doAdvancedStrategies, CancellationToken cancellationToken)
    {
        LogicResult curResult = FindNakedSingles(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        curResult = FindHiddenSingle(null);
        if (curResult != LogicResult.None)
        {
            return curResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (doAdvancedStrategies)
        {
            curResult = FastAdvancedStrategies(cancellationToken);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }
        else if (ShouldRunCellForcing())
        {
            curResult = FastFindCellForcing(cancellationToken);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }
        else if (constraintCellForcingCells != null)
        {
            // The general scan is off, but some constraint named cells worth scanning anyway.
            curResult = CellForcingForCells(constraintCellForcingCells, cancellationToken);
            if (curResult != LogicResult.None)
            {
                return curResult;
            }
        }

        if (isBruteForcing && _constraintQueued != null && _constraintQueued.Length > 0)
        {
            // Re-mark always-run constraints (those with no declared cells) on every step.
            if (_alwaysRunConstraintBits != null)
                for (int word = 0; word < _constraintQueued.Length; word++)
                    _constraintQueued[word] |= _alwaysRunConstraintBits[word];

            // Drain queued constraints cheapest-first; stop on first change. Slots are assigned in
            // cost order at FinalizeConstraints time, so the ascending bit walk *is* the cost
            // order — no comparator here. The word is cached across the inner loop: a StepLogic
            // that returns None wrote nothing, so it cannot have queued anything, and any other
            // result returns out of the loop.
            for (int word = 0; word < _constraintQueued.Length; word++)
            {
                ulong remainingSlots = _constraintQueued[word];
                while (remainingSlots != 0)
                {
                    int slot = (word << 6) + BitOperations.TrailingZeroCount(remainingSlots);
                    remainingSlots &= remainingSlots - 1;
                    _constraintQueued[word] &= ~(1UL << (slot & 63));

                    cancellationToken.ThrowIfCancellationRequested();
                    curResult = constraints[_propagationSlotToConstraint[slot]].StepLogic(this, (List<LogicalStepDesc>)null, true);
                    if (curResult != LogicResult.None) return curResult;
                }
            }
        }
        else
        {
            foreach (Constraint constraint in constraints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                curResult = constraint.StepLogic(this, (List<LogicalStepDesc>)null, true);
                if (curResult != LogicResult.None) return curResult;
            }
        }

        return LogicResult.None;
    }

    private LogicResult BruteForcePropagate(bool doAdvancedStrategies, CancellationToken cancellationToken)
    {
        bool changed = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogicResult curResult = StepBruteForceLogic(doAdvancedStrategies, cancellationToken);
            if (curResult is LogicResult.Invalid or LogicResult.PuzzleComplete)
            {
                return curResult;
            }
            if (curResult == LogicResult.Changed)
            {
                changed = true;
            }
            else
            {
                break;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Prepares a cloned grid for brute force: propagates once, then — if
    /// <paramref name="probeWeakLinks"/> — discovers non-trivial weak links by setting candidates
    /// and seeing what happens. Only call for brute force methods on deeply cloned grids, otherwise
    /// there will be a lot of "magical" eliminations during logical stepping.
    /// </summary>
    /// <param name="probeWeakLinks">
    /// Whether to run the probing pass. The propagation happens either way, so a caller that skips
    /// probing still gets the setup work and the early-out on an already-solved grid.
    /// </param>
    private LogicResult DiscoverWeakLinks(CancellationToken cancellationToken, bool probeWeakLinks)
    {
        // Compile the cell-forcing table before the setup pass rather than after it. Constraints
        // have already contributed their weak links by now -- that happens in FinalizeConstraints,
        // and those are the links cell forcing actually runs on -- so the table is fully determined
        // at this point for everything except the probing below.
        //
        // Two things follow. The root-setup pass now takes the table path instead of the pre-table
        // fallback, which intersects candidate lists and clears them one at a time. And it inherits
        // the emptiness gate, so a puzzle whose table has no rows skips the pass entirely instead of
        // discovering that the slow way.
        //
        // Probing can still add links, and AddWeakLink invalidates the table when it does, so the
        // search gets a rebuild exactly when the link set actually changed.
        CompileCellForcingTable();

        // Run logic on the base solver first
        LogicResult result = BruteForcePropagate(true, cancellationToken);
        if (result == LogicResult.PuzzleComplete || result == LogicResult.Invalid)
        {
            return result;
        }

        if (!probeWeakLinks)
        {
            return result;
        }

        // Drop the grouped table before cloning the scratch solver below: probing mutates the weak
        // link lists, and a clone that inherited the table by reference would not see AddWeakLink's
        // invalidation. The caller recompiles once discovery is done.
        wlGroupedOffsets = null;
        cfOffsets = null;
        cfCanFire = null;
        cfNewlyFires = null;
        cfPopEnd = null;
        cfStatsBlock = null;
        wlMatrix = null;
        wlGroupedCells = null;
        wlGroupedMasks = null;

        LogicResult innerResult;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            innerResult = LogicResult.None;
            // Reuse a single scratch solver across all probes in this pass, copying the
            // host runtime state into it before each probe instead of cloning per probe.
            Solver scratchSolver = Clone(willRunNonSinglesLogic: false);
            scratchSolver.isBruteForcing = true;
            scratchSolver.pendingNakedSingles.Capacity = Math.Max(scratchSolver.pendingNakedSingles.Capacity, NUM_CANDIDATES);

            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint cellMask = board[cellIndex];
                if (IsValueSet(cellMask))
                {
                    continue;
                }

                while (cellMask != 0)
                {
                    int value = MinValue(cellMask);
                    cellMask &= ~ValueMask(value);

                    scratchSolver.CopyBruteForceRuntimeStateFrom(this);
                    if (!scratchSolver.SetValue(cellIndex, value))
                    {
                        // Trivially invalid, we can eliminate it from the host solver
                        if (!ClearValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                    }

                    // Run constraint + singles propagation to find eliminations for weak links.
                    // Advanced strategies (pairs/triples/pointing) are skipped here — they are
                    // expensive per-clone and rarely contribute additional links in practice.
                    LogicResult curResult = scratchSolver.BruteForcePropagate(false, cancellationToken);
                    if (curResult == LogicResult.None)
                    {
                        continue;
                    }

                    if (curResult == LogicResult.Invalid)
                    {
                        // Non-trivially invalid, we can eliminate it from the host solver
                        if (!ClearValue(cellIndex, value))
                        {
                            return LogicResult.Invalid;
                        }
                        result = LogicResult.Changed;
                        innerResult = LogicResult.Changed;
                    }
                    else
                    {
                        int setCandidate = CandidateIndex(cellIndex, value);

                        // Find new eliminations and form the proper weak links
                        for (int curCellIndex = 0; curCellIndex < NUM_CELLS; curCellIndex++)
                        {
                            if (curCellIndex == cellIndex)
                            {
                                continue;
                            }

                            uint oldMask = board[curCellIndex];
                            if (IsValueSet(oldMask))
                            {
                                continue;
                            }

                            uint newMask = scratchSolver.board[curCellIndex] & ~valueSetMask;
                            uint elimMask = oldMask & ~newMask;
                            while (elimMask != 0)
                            {
                                int elimValue = MinValue(elimMask);
                                elimMask &= ~ValueMask(elimValue);

                                int elimCandidate = CandidateIndex(curCellIndex, elimValue);
                                _ = AddWeakLink(setCandidate, elimCandidate);
                            }
                        }
                    }
                }
            }
        } while (innerResult == LogicResult.Changed);

        return result;
    }

    /// <summary>
    /// Process-wide default for <see cref="WeakLinkDiscovery"/>, read once from the environment.
    /// </summary>
    /// <remarks>
    /// The environment variables exist so that a benchmark run can A/B the modes without a rebuild
    /// — which matters because the two arms differ by up to 22x in both directions and stale
    /// baselines have caused false regressions here before. Ordinary callers set the property.
    /// <list type="bullet">
    /// <item><c>SUDOKU_WEAK_LINK_DISCOVERY=always|never|deferred</c></item>
    /// <item><c>SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY=1</c> — the older gate, still honoured
    /// because the published measurements were taken with it; equivalent to <c>never</c>.</item>
    /// </list>
    /// </remarks>
    internal static readonly WeakLinkDiscoveryMode DefaultWeakLinkDiscovery = ReadDefaultWeakLinkDiscovery();

    /// <summary>
    /// Process-wide default for <see cref="WeakLinkDiscoveryNodeThreshold"/>, overridable with
    /// <c>SUDOKU_WEAK_LINK_DEFER_NODES</c> so the threshold can be swept from the benchmark harness.
    /// </summary>
    internal static readonly long DefaultWeakLinkDiscoveryNodeThreshold = ReadDefaultWeakLinkDiscoveryNodeThreshold();

    // Chosen by sweeping 250..100000 over the iss-tune split and confirmed on iss-holdout; see
    // docs/weak-link-discovery-tradeoff.md. The threshold is a latency-vs-throughput dial: raising
    // it makes the median puzzle faster and the slowest ones slower. 2000 is where the median
    // puzzle is ~1.5x faster while total corpus time is still unchanged.
    private const long WEAK_LINK_DEFER_NODES_DEFAULT = 2000;

    private static WeakLinkDiscoveryMode ReadDefaultWeakLinkDiscovery()
    {
        string mode = Environment.GetEnvironmentVariable("SUDOKU_WEAK_LINK_DISCOVERY");
        if (mode != null)
        {
            if (mode.Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                return WeakLinkDiscoveryMode.Always;
            }
            if (mode.Equals("never", StringComparison.OrdinalIgnoreCase))
            {
                return WeakLinkDiscoveryMode.Never;
            }
            if (mode.Equals("deferred", StringComparison.OrdinalIgnoreCase))
            {
                return WeakLinkDiscoveryMode.Deferred;
            }
        }

        string disabled = Environment.GetEnvironmentVariable("SUDOKU_DISABLE_DYNAMIC_WEAK_LINK_DISCOVERY");
        if (disabled != null &&
            (disabled == "1" ||
            disabled.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            disabled.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            return WeakLinkDiscoveryMode.Never;
        }

        return WeakLinkDiscoveryMode.Deferred;
    }

    /// <summary>
    /// Process-wide default for <see cref="TrueCandidatesStallLimit"/>, overridable with
    /// <c>SUDOKU_TC_STALL_LIMIT</c> for sweeps.
    /// </summary>
    internal static readonly long DefaultTrueCandidatesStallLimit = ReadDefaultTrueCandidatesStallLimit();

    // Well clear of a healthy search: the good seeds on tc-escargot-partial peak at 5-10 consecutive
    // barren solutions, the pathological ones at 1,279 and 6,551. See
    // docs/truecandidates-allocation.md.
    private const long TC_STALL_LIMIT_DEFAULT = 100;

    private static long ReadDefaultTrueCandidatesStallLimit()
    {
        string value = Environment.GetEnvironmentVariable("SUDOKU_TC_STALL_LIMIT");
        return value != null && long.TryParse(value, out long limit) && limit >= 0
            ? limit
            : TC_STALL_LIMIT_DEFAULT;
    }

    private static long ReadDefaultWeakLinkDiscoveryNodeThreshold()
    {
        string value = Environment.GetEnvironmentVariable("SUDOKU_WEAK_LINK_DEFER_NODES");
        return value != null && long.TryParse(value, out long nodes) && nodes > 0
            ? nodes
            : WEAK_LINK_DEFER_NODES_DEFAULT;
    }

    /// <summary>
    /// The bilocal override weight ISS itself uses (it scores a bilocal at half the cell's conflict
    /// score). This is the weight the true-candidates search has always run with.
    /// </summary>
    internal const long BILOCAL_WEIGHT_ISS = 50;

    /// <summary>
    /// Process-wide default for <see cref="BilocalSearchWeightPercent"/>, overridable with
    /// <c>SUDOKU_BILOCAL_WEIGHT</c> so it can be swept from the benchmark harness without a rebuild.
    /// </summary>
    internal static readonly long DefaultBilocalSearchWeightPercent = ReadDefaultBilocalSearchWeightPercent();

    // Zero because it is measured to be worse, not because it is untried. The bilocal tier was
    // unexplained dead code in the solve/count searches until 2026-08-04; enabling it at ISS's own
    // weight of 50 costs 7.7x total nodes over the 221 non-trivial iss-tune puzzles (p90 2.56x,
    // worst 180x), even though it looks like a 0.83x win on the 28-case corpus. Don't re-derive
    // this from the small corpus; see docs/branch-ordering.md.
    private const long BILOCAL_SEARCH_WEIGHT_DEFAULT = 0;

    /// <summary>
    /// Maximum candidate count for a cell to be considered by cell forcing; 0 means no cap.
    /// </summary>
    /// <remarks>
    /// This was 3 because the list-intersection path's cost grew with the candidate count. The
    /// table path costs one mask test per row regardless, so the cap only suppressed deductions.
    /// Removing it is 0.996x nodes on iss-tune (42 better, 3 worse) and 0.998x on iss-holdout
    /// (16 better, 0 worse), with several puzzles dropping to zero search nodes entirely.
    /// </remarks>
    internal static readonly long CellForcingMaxCandidates = ReadLongEnv("SUDOKU_CF_MAX", 0);

    /// <summary>
    /// Which cell-forcing enqueue filter to build. <c>SUDOKU_CF_FILTER</c>: <c>0</c> none (every
    /// board write queues its cell), <c>1</c> (default) the can-fire bitmap, <c>2</c> the
    /// newly-fires bitmap indexed by the removed value.
    /// </summary>
    /// <remarks>
    /// The filter is meant to be <em>exact</em> — it must skip only cells that provably cannot fire
    /// any row — so the arms should produce bit-identical results and node counts, with only the
    /// time differing. This switch exists so that equivalence can be checked from one build rather
    /// than two, which also sidesteps comparing across separately-JITted binaries.
    ///
    /// Level 2 was the default until its 3.0% credit was re-measured case by case: it is 4.7%
    /// <em>slower</em> on nc-4given, 1.3% slower on iss-0cvA-XDiQNQ, and within 0.7% on the two
    /// long NC true-candidates boards and the heaviest ISS count. It is nine times the memory
    /// (~47 KB against ~5 KB) and roughly nine times the build — the zeta transform is
    /// 2^MAX_VALUE * MAX_VALUE per cell per level, and that build measured at ~0.9 ms per puzzle,
    /// against a median ISS solve of 1.1 ms. Being sharper about which pops to skip does not pay
    /// when the pops it skips are cheap; see docs/cell-forcing-worklist.md § "Step 5".
    /// </remarks>
    internal static readonly long CellForcingFilterLevel = ReadLongEnv("SUDOKU_CF_FILTER", 1);

    /// <summary>Whether any enqueue filter is built at all.</summary>
    internal static bool CellForcingFilterEnabled => CellForcingFilterLevel > 0;

    /// <summary>
    /// Whether the cell-forcing table's board-relative prunes are applied. <c>SUDOKU_CF_PRUNE</c>:
    /// <c>1</c> (default) on, <c>0</c> off.
    /// </summary>
    /// <remarks>
    /// Both prunes are exact, for the same reason: every board reachable from the search descends
    /// from the board the table is compiled on, so a candidate absent then is absent everywhere
    /// below. Narrowing a row's mask to the source cell's live candidates cannot change whether it
    /// fires, and a row whose target candidate is already gone can only ever fire as a no-op. The
    /// two arms must therefore produce bit-identical results and node counts, with only the time
    /// differing -- an inexact prune shows up as fewer deductions, i.e. as <em>slower</em>, which
    /// is why the equivalence is checked rather than argued. <c>SUDOKU_CF_VERIFY</c> does the
    /// checking; both prunes are clean over all 434 boards of corpus.json and corpus-iss.json.
    ///
    /// Worth -8.4% on nc-4given (39.5 -> 36.1 ms, allocation 1.18 -> 1.06 MB) and about -1% on the
    /// heaviest ISS counts, and flat on the NC true-candidates boards -- as the mechanism predicts,
    /// since a near-blank board has little to prune. The corpus-wide figure is not resolvable on
    /// this machine; see docs/cell-forcing-worklist.md on the noise floor.
    /// </remarks>
    internal static readonly bool CellForcingPruneEnabled = ReadLongEnv("SUDOKU_CF_PRUNE", 1) != 0;

    /// <summary>
    /// Row order within a cell's block of the cell-forcing table. <c>SUDOKU_CF_ORDER</c>:
    /// <c>target</c> (default) groups rows by target cell so a run of eliminations shares one masked
    /// clear; <c>popcount</c> sorts by mask size descending so the scan can stop at the first row
    /// too small to fire.
    /// </summary>
    /// <remarks>
    /// The two cannot both hold, and <c>popcount</c> lost: +2.0% on nc-4given, +0.8% on both NC
    /// true-candidates boards and +1% on the heaviest ISS counts, with the same result whether the
    /// enqueue filter was on or off. The bound is exact and removes real iterations, so the reading
    /// is that giving up target grouping costs more than the trimmed rows were worth -- the
    /// argument for it (target grouping pays only on the 6.7% of pops that fire, the bound pays on
    /// every pop) counted iterations rather than what they cost, which is the mistake
    /// docs/cell-forcing-worklist.md records twice already. Kept switchable because it is the
    /// obvious thing to try next and one build should be able to answer it.
    /// </remarks>
    internal static readonly bool CellForcingPopcountOrder =
        (Environment.GetEnvironmentVariable("SUDOKU_CF_ORDER") ?? "target") == "popcount";

    /// <summary>
    /// Checks every compiled cell-forcing table against the definition of cell forcing.
    /// <c>SUDOKU_CF_VERIFY=1</c>. Off by default; it is exponential in the candidate count.
    /// </summary>
    /// <remarks>
    /// The prunes and the popcount bound claim to be exact, and an inexact one does not produce a
    /// wrong answer: it silently drops deductions and shows up only as a slower search, which
    /// validating counts against expected values cannot see. This switch turns the claim into
    /// something a corpus run can falsify.
    /// </remarks>
    internal static readonly bool CellForcingVerify = ReadLongEnv("SUDOKU_CF_VERIFY", 0) != 0;

    /// <summary>
    /// Collects the cell-forcing pop census and per-row scan/fire/elimination histogram.
    /// <c>SUDOKU_CF_STATS=1</c>. See <see cref="CellForcingStats"/>; run single-threaded.
    /// </summary>
    internal static readonly bool CellForcingStatsEnabled = ReadLongEnv("SUDOKU_CF_STATS", 0) != 0;

    /// <summary>
    /// Runs cell forcing at every propagation step of the search, not just during root setup.
    /// </summary>
    /// <remarks>
    /// Off because it was measured, not because it was untried. Under the order-frozen control arm
    /// it is a real propagator -- 0.475x nodes over 330 ISS puzzles, 77 better and <b>zero</b>
    /// worse -- but in production it is about 1.9x slower (13.1-13.8 s against 25.7-26.0 s on the
    /// ISS corpus). The loss is not its cost: gating it to the root solver alone reproduces almost
    /// the whole regression, so it comes from its eliminations changing candidate counts, which
    /// changes the score/candidateCount ranking in GetLeastCandidateCell and reshuffles branching.
    /// That is the same mechanism docs/branch-ordering.md records for weak-link discovery.
    /// </remarks>
    /// <summary>
    /// How cell forcing is triggered inside the search. <c>SUDOKU_CF_TRIGGER</c>:
    /// <c>off</c> (default, root setup only), <c>every</c> (every propagation step),
    /// <c>dirty</c> (every step, scanning only cells that lost a candidate),
    /// <c>depth:N</c> (only while searchDepth &lt; N), <c>step:N</c> (every Nth propagation step).
    /// </summary>
    /// <remarks>
    /// Under <see cref="StaticBranchOrder"/> these are ordered by construction: a trigger that
    /// fires whenever another does, plus more, can only lower the node count. The useful reading is
    /// therefore how much of the available pruning each one captures for how often it runs.
    /// </remarks>
    private enum CfTrigger { Off, Every, Queue, Depth, Step, Nodes }

    private static readonly (CfTrigger mode, int param) CellForcingTrigger = ParseCellForcingTrigger();

    /// <summary>
    /// Whether cell forcing runs inside the search at all. When it does not, the cell-forcing
    /// enqueue filters are never consulted, so building them would be pure setup cost.
    /// </summary>
    internal static bool CellForcingRunsInSearch => CellForcingTrigger.mode != CfTrigger.Off;

    /// <summary>
    /// Whether any trigger needs a live node count. Static readonly so the JIT folds the guard away
    /// entirely when it is false, which is the default -- the alternative is a store per node on the
    /// hottest path in the solver for a feature that ships disabled, which is the exact shape this
    /// branch has spent today deleting.
    /// </summary>
    internal static readonly bool CellForcingNeedsNodeCount = CellForcingTrigger.mode == CfTrigger.Nodes;

    private static (CfTrigger, int) ParseCellForcingTrigger()
    {
        string raw = Environment.GetEnvironmentVariable("SUDOKU_CF_TRIGGER");
        if (string.IsNullOrEmpty(raw) || raw == "off") return (CfTrigger.Off, 0);
        if (raw == "every") return (CfTrigger.Every, 0);
        if (raw == "dirty" || raw == "queue") return (CfTrigger.Queue, 0);
        int colon = raw.IndexOf(':');
        if (colon > 0 && int.TryParse(raw[(colon + 1)..], out int n) && n > 0)
        {
            string head = raw[..colon];
            if (head == "depth") return (CfTrigger.Depth, n);
            if (head == "step") return (CfTrigger.Step, n);
            // Deferral: drain the worklist only once the search has proven expensive. The queue is
            // still fed from the first node, so this isolates the cost of *scanning* rather than of
            // the whole machinery.
            if (head == "nodes") return (CfTrigger.Nodes, n);
        }
        return (CfTrigger.Off, 0);
    }

    private long _cfStepCounter;

    private bool ShouldRunCellForcing()
    {
        switch (CellForcingTrigger.mode)
        {
            case CfTrigger.Every:
            case CfTrigger.Queue: return true;
            case CfTrigger.Nodes: return searchNodesSoFar >= CellForcingTrigger.param;
            case CfTrigger.Depth: return searchDepth < CellForcingTrigger.param;
            case CfTrigger.Step: return (++_cfStepCounter % CellForcingTrigger.param) == 0;
            default: return false;
        }
    }

    internal static readonly bool CellForcingInSearch = ReadLongEnv("SUDOKU_CF_SEARCH", 0) != 0;

    /// <summary>
    /// Branch on the lowest-index unset cell and its lowest value, ignoring conflict scores,
    /// bilocals and MRV entirely. Off unless <c>SUDOKU_BRANCH_ORDER=static</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a measurement arm, not a solving mode -- it explores far more nodes than the real
    /// heuristic. Its purpose is to make a propagation change measurable on its own terms.
    /// </para>
    /// <para>
    /// The normal heuristic ranks cells by <c>score / candidateCount</c>, so any change in
    /// propagation strength changes candidate counts, changes the ranking, and reshuffles
    /// branching. A node-count A/B against it therefore mixes "does this prune more" with a
    /// branch-order draw that <c>docs/branch-ordering.md</c> shows can swing a puzzle by orders of
    /// magnitude in either direction.
    /// </para>
    /// <para>
    /// Under a fixed cell sequence and fixed value order, propagation can only <em>remove</em>
    /// cells from the sequence, never reorder it, so a stronger propagator's search tree is a
    /// subtree of a weaker one's and its node count can never rise. That makes the comparison
    /// clean: any reduction is real pruning, and any increase means the change is unsound. Both
    /// propagators measured this way so far came back with zero increases across the ISS corpus.
    /// </para>
    /// <para>
    /// Pair it with <see cref="DefaultCountNodeCap"/>; without a cap the harder puzzles do not
    /// finish under this ordering.
    /// </para>
    /// </remarks>
    internal static readonly bool StaticBranchOrder =
        Environment.GetEnvironmentVariable("SUDOKU_BRANCH_ORDER") == "static";

    /// <summary>
    /// Node cap for the final counting attempt; 0 (the default) is unlimited. A capped search
    /// returns -1 rather than a partial count, so a truncated run cannot be mistaken for an answer.
    /// </summary>
    internal static readonly long DefaultCountNodeCap = ReadLongEnv("SUDOKU_NODE_CAP", 0);

    private static long ReadLongEnv(string name, long fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return value != null && long.TryParse(value, out long parsed) && parsed >= 0 ? parsed : fallback;
    }

    private static long ReadDefaultBilocalSearchWeightPercent()
    {
        string value = Environment.GetEnvironmentVariable("SUDOKU_BILOCAL_WEIGHT");
        return value != null && long.TryParse(value, out long weight) && weight >= 0
            ? weight
            : BILOCAL_SEARCH_WEIGHT_DEFAULT;
    }

    private LogicResult FastFindPairs(CancellationToken cancellationToken)
    {
        // Gather a list of all bivalue cells
        List<(int cellIndex, uint mask)> bivalueCells = [];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint mask = board[cellIndex];
            if (ValueCount(mask) == 2)
            {
                bivalueCells.Add((cellIndex, mask));
            }
        }
        bivalueCells.Sort((a, b) =>
        {
            int compareMask = a.mask.CompareTo(b.mask);
            return compareMask != 0 ? compareMask : a.cellIndex.CompareTo(b.cellIndex);
        });

        for (int i0 = 0; i0 < bivalueCells.Count; i0++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int cellIndex0, uint mask0) = bivalueCells[i0];
            List<int> weakLinks0 = weakLinks[cellIndex0];
            int valueA = MinValue(mask0);
            int valueB = MaxValue(mask0);
            int candidate0a = CandidateIndex(cellIndex0, valueA);
            int candidate0b = CandidateIndex(cellIndex0, valueB);
            for (int i1 = i0 + 1; i1 < bivalueCells.Count; i1++)
            {
                (int cellIndex1, uint mask1) = bivalueCells[i1];
                if (mask0 != mask1)
                {
                    break;
                }

                int candidate1a = CandidateIndex(cellIndex1, valueA);
                int candidate1b = CandidateIndex(cellIndex1, valueB);
                if (!IsWeakLink(candidate0a, candidate1a) || !IsWeakLink(candidate0b, candidate1b))
                {
                    continue;
                }

                List<int> elims = CalcElims(mask0, [cellIndex0, cellIndex1]);
                if (elims.Count > 0)
                {
                    return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                }
            }
        }

        return LogicResult.None;
    }

    // Helper struct for FastFindTriples
    private readonly record struct PotentialTripleParticipant(int CellIndex, uint ActualCellMask, uint TargetTripleMask);

    private LogicResult FastFindTriples(CancellationToken cancellationToken)
    {
        List<PotentialTripleParticipant> participants = [];

        // Populate the list of potential triple participants
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint actualMask = board[cellIndex];
            if (IsValueSet(actualMask))
            {
                continue;
            }

            int numActualCandidates = ValueCount(actualMask);

            if (numActualCandidates == 2) // Cell has 2 candidates, e.g., {A,B}
            {
                for (int valX = 1; valX <= MAX_VALUE; valX++)
                {
                    uint maskValX = ValueMask(valX);
                    if ((actualMask & maskValX) != 0)
                    {
                        continue;
                    }
                    uint targetTripleMask = actualMask | maskValX;
                    participants.Add(new PotentialTripleParticipant(cellIndex, actualMask, targetTripleMask));
                }
            }
            else if (numActualCandidates == 3) // Cell has 3 candidates, e.g., {A,B,C}
            {
                participants.Add(new PotentialTripleParticipant(cellIndex, actualMask, actualMask));
            }
        }

        if (participants.Count < 3)
        {
            return LogicResult.None;
        }

        participants.Sort((a, b) =>
        {
            int compareMask = a.TargetTripleMask.CompareTo(b.TargetTripleMask);
            return compareMask != 0 ? compareMask : a.CellIndex.CompareTo(b.CellIndex);
        });

        for (int i = 0; i < participants.Count - 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PotentialTripleParticipant p_i = participants[i];
            int c0 = p_i.CellIndex;

            for (int j = i + 1; j < participants.Count - 1; j++)
            {
                PotentialTripleParticipant p_j = participants[j];
                int c1 = p_j.CellIndex;

                if (p_j.TargetTripleMask != p_i.TargetTripleMask)
                {
                    break;
                }

                if (c1 == c0)
                {
                    continue;
                }

                // Extract values from the target triple mask
                uint tempMask = p_i.TargetTripleMask;
                int vA = MinValue(tempMask);
                tempMask &= ~ValueMask(vA);
                int vB = MinValue(tempMask);
                tempMask &= ~ValueMask(vB);
                int vC = MinValue(tempMask);

                // Check non-repeat property between p_i and p_j for all three values
                bool p_i_j_nonrepeat =
                    (!HasValue(p_i.ActualCellMask, vA) || !HasValue(p_j.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c0, vA), CandidateIndex(c1, vA))) &&
                    (!HasValue(p_i.ActualCellMask, vB) || !HasValue(p_j.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c0, vB), CandidateIndex(c1, vB))) &&
                    (!HasValue(p_i.ActualCellMask, vC) || !HasValue(p_j.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c0, vC), CandidateIndex(c1, vC)));

                if (!p_i_j_nonrepeat)
                {
                    continue; // p_i and p_j can repeat digits, so this cannot form a triple
                }

                for (int k = j + 1; k < participants.Count; k++)
                {
                    PotentialTripleParticipant p_k = participants[k];
                    int c2 = p_k.CellIndex;

                    if (p_k.TargetTripleMask != p_i.TargetTripleMask)
                    {
                        break;
                    }

                    if (c2 == c0 || c2 == c1)
                    {
                        continue;
                    }

                    uint combinedActualMask = p_i.ActualCellMask | p_j.ActualCellMask | p_k.ActualCellMask;

                    if (combinedActualMask == p_i.TargetTripleMask)
                    {
                        // p_i and p_j already confirmed to not repeat digits.
                        // Now check p_k's non-repeat with p_i and p_j.
                        bool p_k_links_nonrepeat =
                            (!HasValue(p_i.ActualCellMask, vA) || !HasValue(p_k.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c0, vA), CandidateIndex(c2, vA))) &&
                            (!HasValue(p_j.ActualCellMask, vA) || !HasValue(p_k.ActualCellMask, vA) || IsWeakLink(CandidateIndex(c1, vA), CandidateIndex(c2, vA))) &&
                            (!HasValue(p_i.ActualCellMask, vB) || !HasValue(p_k.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c0, vB), CandidateIndex(c2, vB))) &&
                            (!HasValue(p_j.ActualCellMask, vB) || !HasValue(p_k.ActualCellMask, vB) || IsWeakLink(CandidateIndex(c1, vB), CandidateIndex(c2, vB))) &&
                            (!HasValue(p_i.ActualCellMask, vC) || !HasValue(p_k.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c0, vC), CandidateIndex(c2, vC))) &&
                            (!HasValue(p_j.ActualCellMask, vC) || !HasValue(p_k.ActualCellMask, vC) || IsWeakLink(CandidateIndex(c1, vC), CandidateIndex(c2, vC)));

                        if (!p_k_links_nonrepeat)
                        {
                            continue;
                        }

                        List<int> tripleCellIndices = [c0, c1, c2];
                        List<int> elims = CalcElims(p_i.TargetTripleMask, tripleCellIndices);
                        if (elims.Count > 0)
                        {
                            return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                        }
                    }
                }
            }
        }
        return LogicResult.None;
    }

    private LogicResult FastFindPointing(CancellationToken cancellationToken)
    {
        List<int> pointingCandidates = new(4);
        List<int> elims = [];
        foreach (SudokuGroup group in maxValueGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint groupSpecific_SetValuesMask = 0;    // Mask of values already SET within this group
            uint groupSpecific_CandidatePoolMask = 0; // Mask of all candidates in UNSET cells of this group

            foreach (int cellIndex in group.Cells)
            {
                uint currentCellMask = board[cellIndex];
                if (IsValueSet(currentCellMask))
                {
                    groupSpecific_SetValuesMask |= currentCellMask;
                }
                else
                {
                    groupSpecific_CandidatePoolMask |= currentCellMask;
                }
            }

            // Candidates that are in the pool AND NOT already set in the group.
            // These are the candidates we might find pointing logic for.
            uint actualCandidatesForPointing = groupSpecific_CandidatePoolMask & ~groupSpecific_SetValuesMask;

            if (actualCandidatesForPointing == 0) // No candidates left for pointing in this group
            {
                continue;
            }

            while (actualCandidatesForPointing != 0)
            {
                int value = MinValue(actualCandidatesForPointing);
                uint valueMask = ValueMask(value);
                actualCandidatesForPointing &= ~valueMask;

                // Find all cells in 'group' where 'v' is a candidate.
                pointingCandidates.Clear();
                foreach (int cellIndex in group.Cells)
                {
                    uint currentCellMask = board[cellIndex];
                    if ((currentCellMask & valueMask) != 0)
                    {
                        pointingCandidates.Add(CandidateIndex(cellIndex, value));
                        if (pointingCandidates.Count > 3)
                        {
                            break;
                        }
                    }
                }

                if (pointingCandidates.Count is 2 or 3)
                {
                    CalcElims(elims, pointingCandidates);
                    if (elims.Count > 0)
                    {
                        return !ClearCandidates(elims) ? LogicResult.Invalid : LogicResult.Changed;
                    }
                }
            }
        }

        return LogicResult.None;
    }

    // Reused across calls: cell forcing runs once per propagation step when enabled in-search, and
    // a fresh list per call was allocating on every node.
    private List<int> _cellForcingElims;

    /// <summary>
    /// Eliminates every candidate that all of a cell's remaining candidates are weakly linked to.
    /// </summary>
    /// <remarks>
    /// The table path is allocation-free, which is what lets this run per node: rows are stored
    /// grouped by target cell, so a run of firing rows is applied with one <c>ClearMaskFromCell</c>
    /// rather than one <c>ClearCandidate</c> each. That matters because the per-cell bookkeeping —
    /// the constraint enqueue, the naked-single check and <c>TrackHiddenSingles</c> — is paid once
    /// per cell instead of once per candidate, and the masked clear early-outs on candidates that
    /// are already gone, which is most of them.
    /// </remarks>
    private LogicResult FastFindCellForcing(CancellationToken cancellationToken)
    {
        // No rows anywhere means no cell can ever force, for this puzzle, for the whole search.
        // Vanilla sudoku is always in this state: cell forcing needs two of a cell's values to rule
        // out one target, and house links give exactly one, so nothing survives the table build.
        // cfOffsets is null during root setup, which runs before the table is compiled and has its
        // own fallback path -- so this only short-circuits the in-search scans.
        if (cfOffsets != null && cfOffsets[NUM_CELLS] == 0)
        {
            return LogicResult.None;
        }

        if (CellForcingTrigger.mode == CfTrigger.Nodes)
        {
            return FastFindCellForcingQueue(cancellationToken);
        }

        if (CellForcingTrigger.mode == CfTrigger.Queue)
        {
            return FastFindCellForcingQueue(cancellationToken);
        }

        bool anyChanged = false;
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogicResult one = CellForcingForCell(cellIndex);
            if (one == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            anyChanged |= one == LogicResult.Changed;
        }

        return anyChanged ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Drains the dirty worklist, scanning only cells that lost a candidate since the last pass.
    /// </summary>
    /// <remarks>
    /// Exact rather than a heuristic: a cell can only <i>newly</i> force after it loses a candidate,
    /// because dropping a term from an intersection can only enlarge it.
    /// </remarks>
    private LogicResult FastFindCellForcingQueue(CancellationToken cancellationToken)
    {
        bool queueChanged = false;
        while (pendingCellForcing.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cellIndex = pendingCellForcing[^1];
            pendingCellForcing.RemoveAt(pendingCellForcing.Count - 1);

            LogicResult one = CellForcingForCell(cellIndex);
            if (one == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            queueChanged |= one == LogicResult.Changed;
        }

        return queueChanged ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Cell forcing over a named set of cells, for constraints that asked for it via
    /// <see cref="Constraint.CellIndicesForCellForcing"/>.
    /// </summary>
    private LogicResult CellForcingForCells(int[] cellIndices, CancellationToken cancellationToken)
    {
        bool anyChanged = false;
        for (int i = 0; i < cellIndices.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogicResult one = CellForcingForCell(cellIndices[i]);
            if (one == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            anyChanged |= one == LogicResult.Changed;
        }

        return anyChanged ? LogicResult.Changed : LogicResult.None;
    }

    /// <summary>
    /// Cell forcing for one cell: eliminates every candidate that all of this cell's remaining
    /// candidates are weakly linked to. Returns Changed only when something was actually removed.
    /// </summary>
    private LogicResult CellForcingForCell(int cellIndex)
    {
        bool stats = CellForcingStatsEnabled;
        uint mask = board[cellIndex];
        if (IsValueSet(mask))
        {
            if (stats)
            {
                CellForcingStats.PopsValueSet++;
            }
            return LogicResult.None;
        }

        if (CellForcingMaxCandidates > 0 && ValueCount(mask) > CellForcingMaxCandidates)
        {
            if (stats)
            {
                CellForcingStats.PopsOverCap++;
            }
            return LogicResult.None;
        }

        if (cfOffsets == null)
        {
            // The table is compiled after DiscoverWeakLinks, because probing mutates the link
            // lists. Root-setup cell forcing therefore runs before it exists and must fall back to
            // intersecting the sorted candidate lists directly.
            List<int> elims = _cellForcingElims ??= [];
            elims.Clear();
            int candBase = cellIndex * MAX_VALUE - 1;
            bool isFirst = true;
            uint remaining = mask;
            while (remaining != 0)
            {
                int v = MinValue(remaining);
                remaining &= ~ValueMask(v);
                if (isFirst)
                {
                    InitIntersectWeakLinks(elims, candBase + v);
                    isFirst = false;
                }
                else
                {
                    IntersectWeakLinks(elims, candBase + v);
                }
                if (elims.Count == 0)
                {
                    break;
                }
            }
            if (elims.Count == 0)
            {
                return LogicResult.None;
            }
            return ClearCandidates(elims) ? LogicResult.Changed : LogicResult.Invalid;
        }

        // cand(cell) is a subset of a row's mask exactly when every remaining candidate of this
        // cell links to that target, which is the cell-forcing condition.
        uint candMask = mask & ~valueSetMask;
        int candCount = ValueCount(candMask);
        if (candCount < 2)
        {
            // The table has always assumed at least two candidates -- that is the justification for
            // dropping rows whose mask holds fewer than two values, which a one-candidate cell
            // could otherwise still fire. Such a cell is a naked single, and the worklist resolves
            // those before cell forcing pops, so no deduction is lost. Saying so here is what makes
            // narrowing a row's mask to the cell's live candidates exactly equivalent.
            if (stats)
            {
                CellForcingStats.PopsTooFewCandidates++;
            }
            return LogicResult.None;
        }

        // Exact O(1) dismissal before touching a single row. cfCanFire marks every candidate mask
        // m for which some row (X, S) has m subset of S -- the scan's firing condition exactly, by
        // construction (BuildCellForcingFilter takes the downward zeta transform of the row masks).
        // So an unmarked cell provably fires nothing and the row walk is pure loss.
        //
        // The bitmap already existed and was consulted only on the enqueue path, which meant the
        // every-step scan -- where nothing is enqueued at all -- built it and never used it. The
        // field's own comment names the symptom it was meant to prevent: "pays a full row scan
        // before finding nothing".
        ulong[] canFire = cfCanFire;
        if (canFire != null
            && (canFire[cellIndex * cfCanFireWords + (int)(candMask >> 6)] & (1UL << (int)(candMask & 63))) == 0)
        {
            if (stats)
            {
                CellForcingStats.PopsOverCap++;
            }
            return LogicResult.None;
        }

        // A row fires only if cand(cell) is a subset of its mask, which needs the mask to hold at
        // least as many values. In popcount order those rows are a prefix, so the bound is a table
        // lookup rather than a per-row test.
        int[] popEnd = cfPopEnd;
        int rowEnd = popEnd != null
            ? popEnd[cellIndex * (MAX_VALUE + 1) + candCount]
            : cfOffsets[cellIndex + 1];
        int pendingCell = -1;
        uint pendingMask = 0;
        bool changed = false;
        CellForcingStats.Block statsBlock = stats ? cfStatsBlock : null;
        bool anyFired = false;

        for (int row = cfOffsets[cellIndex]; row < rowEnd; row++)
        {
            if (statsBlock != null)
            {
                statsBlock.Scans[row]++;
            }

            if ((candMask & ~cfMasks[row]) != 0)
            {
                continue;
            }

            int target = cfTargets[row];
            int targetCell = target / MAX_VALUE;
            if (stats)
            {
                anyFired = true;
                if (statsBlock != null)
                {
                    statsBlock.Fires[row]++;
                    // Novel exactly when the target is still live at this instant: clears from
                    // earlier rows of this same pop have already been applied, later ones have not.
                    if ((board[targetCell] & ValueMask(target - targetCell * MAX_VALUE + 1)) != 0)
                    {
                        statsBlock.Elims[row]++;
                    }
                }
            }

            if (targetCell != pendingCell)
            {
                if (pendingMask != 0 && (board[pendingCell] & pendingMask) != 0)
                {
                    changed = true;
                    if (!ClearMaskFromCell(pendingCell, pendingMask))
                    {
                        if (stats)
                        {
                            CellForcingStats.PopsChanged++;
                        }
                        return LogicResult.Invalid;
                    }
                }
                pendingCell = targetCell;
                pendingMask = 0;
            }
            pendingMask |= ValueMask(target - targetCell * MAX_VALUE + 1);
        }

        if (pendingMask != 0 && (board[pendingCell] & pendingMask) != 0)
        {
            changed = true;
            if (!ClearMaskFromCell(pendingCell, pendingMask))
            {
                if (stats)
                {
                    CellForcingStats.PopsChanged++;
                }
                return LogicResult.Invalid;
            }
        }

        if (stats)
        {
            if (changed)
            {
                CellForcingStats.PopsChanged++;
            }
            else if (anyFired)
            {
                CellForcingStats.PopsFiredNoChange++;
            }
            else
            {
                CellForcingStats.PopsNothingFired++;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private LogicResult FastAdvancedStrategies(CancellationToken cancellationToken)
    {
        LogicResult result;

        result = FastFindPairs(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindPointing(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindCellForcing(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        result = FastFindTriples(cancellationToken);
        if (result != LogicResult.None)
        {
            return result;
        }

        return LogicResult.None;
    }
}
