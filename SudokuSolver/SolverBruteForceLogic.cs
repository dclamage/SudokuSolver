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

        if (isBruteForcing && _constraintQueued != null && _constraintQueued.Length > 0)
        {
            // Re-mark always-run constraints (those with no declared cells) on every step.
            if (_alwaysRunConstraintIndices != null)
                foreach (int ai in _alwaysRunConstraintIndices)
                    if (!_constraintQueued[ai]) { _constraintQueued[ai] = true; _numConstraintsQueued++; }

            if (_numConstraintsQueued == 0)
                goto skipConstraints;

            // Drain queued constraints in list order; stop on first change.
            for (int ci = 0; ci < constraints.Count; ci++)
            {
                if (!_constraintQueued[ci]) continue;
                _constraintQueued[ci] = false;
                _numConstraintsQueued--;
                cancellationToken.ThrowIfCancellationRequested();
                curResult = constraints[ci].StepLogic(this, (List<LogicalStepDesc>)null, true);
                if (curResult != LogicResult.None) return curResult;
            }
            skipConstraints:;
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

    private LogicResult FastFindCellForcing(CancellationToken cancellationToken)
    {
        List<int> elims = [];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint mask = board[cellIndex];
            if (IsValueSet(mask) || ValueCount(mask) > 3)
            {
                continue;
            }

            elims.Clear();
            int candBase = cellIndex * MAX_VALUE - 1;
            bool isFirst = true;

            uint remainingMask = mask;
            while (remainingMask != 0)
            {
                int v = MinValue(remainingMask);
                remainingMask &= ~ValueMask(v);

                int candIndex = candBase + v;

                if (isFirst)
                {
                    InitIntersectWeakLinks(elims, candIndex);
                    isFirst = false;
                }
                else
                {
                    // Subsequent candidates: keep only common elements
                    IntersectWeakLinks(elims, candIndex);
                }

                if (elims.Count == 0)
                {
                    break;
                }
            }

            if (elims.Count > 0)
            {
                if (!ClearCandidates(elims))
                {
                    return LogicResult.Invalid;
                }
                return LogicResult.Changed;
            }
        }
        return LogicResult.None;
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
