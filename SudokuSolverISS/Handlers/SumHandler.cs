namespace SudokuSolverISS.Handlers;

/// <summary>
/// Arrow constraint: grid[_circle] = sum of grid[_arrow[i]].
/// Dispatches based on ISS numUnfixed = n_unfixed_arrows + (circle_unfixed ? 1 : 0):
///   numUnfixed 0 → verify fixed sum matches fixed circle
///   numUnfixed 1 → _enforceOneRemainingCell (direct assignment)
///   numUnfixed 2 → _enforceTwoRemainingCells (reverse[] bit formula)
///   numUnfixed 3 → _enforceThreeRemainingCells (pairwiseSums[] + doubles[])
///                  circle unfixed: reversed to positive coeff, doubles always for circle+arrow pairs
///   numUnfixed 4+ → _restrictCellsWithCoefficients (per-cell range restriction)
/// _arrowExclusive[i,j] = true if arrow[i] and arrow[j] share a house (must be distinct).
/// </summary>
sealed class SumHandler : IHandler
{
    private readonly int     _circle;
    private readonly int[]   _arrow;
    private readonly bool[,] _arrowExclusive;  // [i,j]: arrow[i] and arrow[j] share a house
    private readonly int[][] _arrowGroups;     // greedy maximal-clique partition of arrow indices

    public SumHandler(int circleCell, int[] arrowCells)
    {
        _circle = circleCell;
        _arrow  = arrowCells;
        int n = arrowCells.Length;
        _arrowExclusive = new bool[n, n];
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                bool excl = G.Row(arrowCells[i]) == G.Row(arrowCells[j])
                         || G.Col(arrowCells[i]) == G.Col(arrowCells[j])
                         || G.Box(arrowCells[i]) == G.Box(arrowCells[j]);
                _arrowExclusive[i, j] = _arrowExclusive[j, i] = excl;
            }
        _arrowGroups = BuildArrowGroups(n);
    }

    // Greedy FIRST-strategy maximal-clique partition, mirroring ISS findExclusionGroupsGreedy
    // with GREEDY_STRATEGY_FIRST and cells sorted by cell index (matching ISS g.cells.sort()).
    private int[][] BuildArrowGroups(int n)
    {
        int[] sortedOrder = new int[n];
        for (int i = 0; i < n; i++) sortedOrder[i] = i;
        Array.Sort(sortedOrder, (a, b) => _arrow[a].CompareTo(_arrow[b]));

        var groups  = new List<int[]>();
        bool[] done = new bool[n];
        int remaining = n;
        var cands = new bool[n];
        var group = new List<int>(n);

        while (remaining > 0)
        {
            // candidates = unassigned cells
            for (int i = 0; i < n; i++) cands[i] = !done[i];
            group.Clear();

            while (true)
            {
                // Pick first candidate in sorted (by cell index) order
                int best = -1;
                foreach (int k in sortedOrder)
                    if (cands[k]) { best = k; break; }
                if (best < 0) break;

                group.Add(best);
                cands[best] = false;
                // Intersect candidates with exclusions of best
                for (int i = 0; i < n; i++)
                    if (cands[i] && !_arrowExclusive[best, i])
                        cands[i] = false;
            }

            foreach (int i in group) { done[i] = true; remaining--; }
            groups.Add(group.ToArray());
        }
        return groups.ToArray();
    }

    public bool EnforceConsistency(uint[] grid, HandlerAccumulator acc)
    {
        uint circle = grid[_circle];
        if (circle == 0) return false;

        // Split arrow cells into fixed and unfixed.
        Span<int>  uIdx  = stackalloc int[_arrow.Length];
        Span<uint> uMask = stackalloc uint[_arrow.Length];
        int n = 0, fixedSum = 0;
        foreach (int ac in _arrow)
        {
            uint v = grid[ac];
            if (v == 0) return false;
            if (G.IsSingleton(v)) fixedSum += G.SingletonValue(v);
            else { uIdx[n] = ac; uMask[n] = v; n++; }
        }

        // Restrict circle to [minArrow, maxArrow].
        int minSum = fixedSum, maxSum = fixedSum;
        for (int i = 0; i < n; i++)
        {
            minSum += BitOperations.TrailingZeroCount(uMask[i]) + 1;
            maxSum += BitOperations.Log2(uMask[i]) + 1;
        }
        circle = RestrictRange(circle, minSum, maxSum);
        if (circle == 0) return false;
        if (circle != grid[_circle])
            grid[_circle] = circle;

        // Handle zero-unfixed case.
        if (n == 0)
        {
            if (fixedSum < 1 || fixedSum > G.SIZE) return false;
            uint need = G.ValueBit(fixedSum);
            if ((circle & need) == 0) return false;
            if (!G.IsSingleton(circle)) grid[_circle] = need;
            return true;
        }

        return n switch
        {
            1 => N1(grid, acc, circle, fixedSum, uIdx, uMask),
            2 => G.IsSingleton(circle)
                 ? N2Fixed(grid, acc, circle, fixedSum, uIdx, uMask)
                 : N2Unfixed(grid, acc, circle, fixedSum, uIdx, uMask),
            3 => G.IsSingleton(circle)
                 ? N3(grid, acc, circle, fixedSum, uIdx, uMask)
                 : NMany(grid, acc, circle, fixedSum, uIdx, uMask, n),
            _ => NMany(grid, acc, circle, fixedSum, uIdx, uMask, n),
        };
    }

    // n=1: cell must equal (circleValue - fixedSum).
    private bool N1(uint[] grid, HandlerAccumulator acc,
                    uint circle, int fixedSum, Span<int> uIdx, Span<uint> uMask)
    {
        uint keptArrow = 0, keptCircle = 0;
        uint c = circle;
        while (c != 0) {
            uint b = G.LowestBit(c); c &= ~b;
            int need = G.SingletonValue(b) - fixedSum;
            if (need >= 1 && need <= G.SIZE) {
                uint vb = G.ValueBit(need);
                if ((uMask[0] & vb) != 0) { keptArrow |= vb; keptCircle |= b; }
            }
        }
        if (keptArrow == 0) return false;
        return Narrow(grid, acc, uIdx[0], uMask[0], keptArrow)
            && Narrow(grid, acc, _circle, circle, keptCircle);
    }

    // n=2, circle fixed: ISS numUnfixed=2 → _enforceTwoRemainingCells([arrow0,arrow1], T).
    // Includes ISS exclusive-pair check: if T even and both arrows share a house, remove T/2.
    private bool N2Fixed(uint[] grid, HandlerAccumulator acc,
                         uint circle, int fixedSum, Span<int> uIdx, Span<uint> uMask)
    {
        int T = G.SingletonValue(circle) - fixedSum;
        if (T < 2 || T > 2 * G.SIZE) return false;
        uint v0 = uMask[0], v1 = uMask[1];
        uint nv1 = (uint)((SumData.Reverse[v0] << (T - 1)) >> G.SIZE) & v1 & G.ALL_VALUES;
        uint nv0 = (uint)((SumData.Reverse[nv1] << (T - 1)) >> G.SIZE) & v0 & G.ALL_VALUES;
        if ((T & 1) == 0) {
            int ai0 = Array.IndexOf(_arrow, uIdx[0]);
            int ai1 = Array.IndexOf(_arrow, uIdx[1]);
            if (_arrowExclusive[ai0, ai1]) {
                uint halfMask = G.ValueBit(T >> 1);
                nv0 &= ~halfMask;
                nv1 &= ~halfMask;
            }
        }
        if (nv0 == 0 || nv1 == 0) return false;
        return Narrow(grid, acc, uIdx[0], uMask[0], nv0)
            && Narrow(grid, acc, uIdx[1], uMask[1], nv1);
    }

    // n=2, circle unfixed: ISS numUnfixed=3 → _enforceThreeRemainingCells with circle reversed.
    // Circle coeff=-1: reversing it (value k→10-k) and adding N+1 to target converts the
    // equation to all-positive coefficients for the standard three-remaining formula.
    // Circle always has a different exclusion group from arrows (opposite coefficient signs in ISS),
    // so doubles are unconditionally added for any (rcircle, arrow) pair.
    private bool N2Unfixed(uint[] grid, HandlerAccumulator acc,
                           uint circle, int fixedSum, Span<int> uIdx, Span<uint> uMask)
    {
        int T = G.SIZE + 1 - fixedSum;   // = 10 - fixedSum_arrows
        if (T < 3 || T > 3 * G.SIZE) return false;
        uint v0 = SumData.Reverse[circle & G.ALL_VALUES];  // reversed circle mask
        uint v1 = uMask[0];
        uint v2 = uMask[1];
        int ai0 = Array.IndexOf(_arrow, uIdx[0]);
        int ai1 = Array.IndexOf(_arrow, uIdx[1]);
        bool e12 = _arrowExclusive[ai0, ai1];
        uint psForV2 = (SumData.PairwiseSums[(v0 << G.SIZE) | v1] << 2) | SumData.Doubles[v0 & v1];
        uint psForV1 = (SumData.PairwiseSums[(v0 << G.SIZE) | v2] << 2) | SumData.Doubles[v0 & v2];
        uint psForV0 = (SumData.PairwiseSums[(v1 << G.SIZE) | v2] << 2) | (e12 ? 0u : SumData.Doubles[v1 & v2]);
        int shift = T - 1;
        uint nv0 = SumData.Reverse[((psForV0 << G.SIZE) >> shift) & G.ALL_VALUES] & v0;
        uint nv1 = SumData.Reverse[((psForV1 << G.SIZE) >> shift) & G.ALL_VALUES] & v1;
        uint nv2 = SumData.Reverse[((psForV2 << G.SIZE) >> shift) & G.ALL_VALUES] & v2;
        if (nv0 == 0 || nv1 == 0 || nv2 == 0) return false;
        uint nv_circle = (uint)SumData.Reverse[nv0] & circle;
        if (nv_circle == 0) return false;
        return Narrow(grid, acc, uIdx[0], uMask[0], nv1)
            && Narrow(grid, acc, uIdx[1], uMask[1], nv2)
            && Narrow(grid, acc, _circle, circle, nv_circle);
    }

    // n=3, circle fixed: ISS numUnfixed=3 → _enforceThreeRemainingCells([arrow0,arrow1,arrow2], T).
    // For non-exclusive pairs (cells not sharing a house), doubles[] is OR'd in.
    private bool N3(uint[] grid, HandlerAccumulator acc,
                    uint circle, int fixedSum, Span<int> uIdx, Span<uint> uMask)
    {
        int ai0 = Array.IndexOf(_arrow, uIdx[0]);
        int ai1 = Array.IndexOf(_arrow, uIdx[1]);
        int ai2 = Array.IndexOf(_arrow, uIdx[2]);
        bool e01 = _arrowExclusive[ai0, ai1];
        bool e02 = _arrowExclusive[ai0, ai2];
        bool e12 = _arrowExclusive[ai1, ai2];
        // circle is singleton here (always — unfixed circle routes to NMany).
        int T = G.SingletonValue(circle) - fixedSum;
        if (T < 3 || T > 3 * G.SIZE) return false;
        uint v0 = uMask[0], v1 = uMask[1], v2 = uMask[2];
        uint ps01 = (SumData.PairwiseSums[(v0 << G.SIZE) | v1] << 2) | (!e01 ? SumData.Doubles[v0 & v1] : 0u);
        uint ps02 = (SumData.PairwiseSums[(v0 << G.SIZE) | v2] << 2) | (!e02 ? SumData.Doubles[v0 & v2] : 0u);
        uint ps12 = (SumData.PairwiseSums[(v1 << G.SIZE) | v2] << 2) | (!e12 ? SumData.Doubles[v1 & v2] : 0u);
        int shift = T - 1;
        uint nv2 = SumData.Reverse[((ps01 << G.SIZE) >> shift) & G.ALL_VALUES] & v2;
        uint nv1 = SumData.Reverse[((ps02 << G.SIZE) >> shift) & G.ALL_VALUES] & v1;
        uint nv0 = SumData.Reverse[((ps12 << G.SIZE) >> shift) & G.ALL_VALUES] & v0;
        if (nv0 == 0 || nv1 == 0 || nv2 == 0) return false;
        return Narrow(grid, acc, uIdx[0], uMask[0], nv0)
            && Narrow(grid, acc, uIdx[1], uMask[1], nv1)
            && Narrow(grid, acc, uIdx[2], uMask[2], nv2);
        // circle is already fixed — no Narrow needed for it.
    }

    // n≥4 (or n=3 circle unfixed): ISS's general path.
    // Step 1: _restrictValueRange (per-cell range restriction).
    // Step 2: _restrictCellsWithCoefficients (group-aware seenMin/seenMax, no acc calls).
    private bool NMany(uint[] grid, HandlerAccumulator acc,
                       uint circle, int fixedSum,
                       Span<int> uIdx, Span<uint> uMask, int n)
    {
        int cMin = BitOperations.TrailingZeroCount(circle) + 1;
        int cMax = BitOperations.Log2(circle) + 1;

        Span<int> lo = stackalloc int[n];
        Span<int> hi = stackalloc int[n];
        int sumLo = fixedSum, sumHi = fixedSum;
        for (int i = 0; i < n; i++) {
            lo[i] = BitOperations.TrailingZeroCount(uMask[i]) + 1;
            hi[i] = BitOperations.Log2(uMask[i]) + 1;
            sumLo += lo[i]; sumHi += hi[i];
        }

        // _restrictValueRange for each unfixed arrow cell (coeff=1, same formula as ISS).
        int sumMinusMin = cMax - sumLo;   // = maxCircle - minArrowsSum (same as ISS sumMinusMin for arrows)
        int maxMinusSum = sumHi - cMin;   // = maxArrowsSum - minCircle (same as ISS maxMinusSum for arrows)
        for (int i = 0; i < n; i++) {
            int range = hi[i] - lo[i];
            uint v = uMask[i];
            if (sumMinusMin < range) {
                // Keep only values ≤ lo[i] + sumMinusMin  (v << sumMinusMin, then lowest bit * 2 - 1)
                uint x = v << sumMinusMin;
                v &= ((x & (uint)(-(int)x)) << 1) - 1u;
            }
            if (maxMinusSum < range) {
                // Keep only values ≥ hi[i] - maxMinusSum.
                // Mirrors: v &= -0x80000000 >> (clz32(v) + maxMinusSum) in ISS.
                v &= (uint)(int.MinValue >> (BitOperations.LeadingZeroCount(v) + maxMinusSum));
            }
            if (v == 0) return false;
            if (!Narrow(grid, acc, uIdx[i], uMask[i], v)) return false;
        }
        // _restrictValueRange for circle (coeff=-1): sumMinusMin_c = maxMinusSum, maxMinusSum_c = sumMinusMin.
        {
            int range = cMax - cMin;
            uint cv = circle;
            if (maxMinusSum < range) {
                uint x = cv << maxMinusSum;
                cv &= ((x & (uint)(-(int)x)) << 1) - 1u;
            }
            if (sumMinusMin < range) {
                cv &= (uint)(int.MinValue >> (BitOperations.LeadingZeroCount(cv) + sumMinusMin));
            }
            if (!Narrow(grid, acc, _circle, circle, cv & G.ALL_VALUES)) return false;
            circle = grid[_circle];
        }

        // Step 2: _restrictCellsWithCoefficients (group-aware, no acc).
        return RestrictWithGroups(grid);
    }

    // Mirrors ISS _restrictCellsWithCoefficients.
    // Processes ALL arrow cells (fixed + unfixed) in each group, plus the circle group.
    // Does NOT call acc — matches ISS which modifies grid directly without notifying.
    private bool RestrictWithGroups(uint[] grid)
    {
        // seenMinMaxs[g] = seenMin (bits 0-15) | seenMax_normal (bits 16-31), or 0 if seenMin==seenMax.
        // We process arrow groups (coeff=+1) then circle (coeff=-1).
        int numGroups = _arrowGroups.Length;
        Span<uint> seenMinMaxs = stackalloc uint[numGroups + 1];  // +1 for circle

        int strictMin = 0, strictMax = 0;

        // Arrow groups (coeff=+1).
        for (int g = 0; g < numGroups; g++)
        {
            int[] grp = _arrowGroups[g];
            uint v0 = grid[_arrow[grp[0]]];
            if (v0 == 0) return false;

            uint seenMin = v0 & (uint)(-(int)v0);                            // lowest bit of v0
            uint seenMaxRev = (G.ALL_VALUES + 1u) >> (BitOperations.Log2(v0) + 1);  // reversed-encoding seenMax

            for (int j = 1; j < grp.Length; j++)
            {
                uint v = grid[_arrow[grp[j]]];
                if (v == 0) return false;

                uint loV = v & (uint)(-(int)v);
                uint x = ~(seenMin | (loV - 1u));
                seenMin |= x & (uint)(-(int)x);

                int pj = BitOperations.Log2(v);
                uint maskJ = ~0u << (G.SIZE - 1 - pj);  // bits from (8-pj) upward (mirrors: -1 << (8-pj))
                x = ~seenMaxRev & maskJ;
                seenMaxRev |= x & (uint)(-(int)x);
            }

            if ((seenMin | seenMaxRev) > G.ALL_VALUES) return false;

            uint seenMaxNorm = SumData.Reverse[seenMaxRev];
            int minGroupSum = SumData.Sum[seenMin];
            int maxGroupSum = SumData.Sum[seenMaxNorm];
            strictMin += minGroupSum;
            strictMax += maxGroupSum;
            seenMinMaxs[g] = seenMin != seenMaxNorm ? seenMin | (seenMaxNorm << 16) : 0u;
        }

        // Circle group (coeff=-1).
        {
            uint cv = grid[_circle];
            if (cv == 0) return false;
            uint seenMin = cv & (uint)(-(int)cv);
            uint seenMaxRev = (G.ALL_VALUES + 1u) >> (BitOperations.Log2(cv) + 1);
            if ((seenMin | seenMaxRev) > G.ALL_VALUES) return false;
            uint seenMaxNorm = SumData.Reverse[seenMaxRev];
            int minCircle = SumData.Sum[seenMin];
            int maxCircle = SumData.Sum[seenMaxNorm];
            strictMin += -maxCircle;  // coeff=-1: strictMin += coeff*maxSum
            strictMax += -minCircle;  // coeff=-1: strictMax += coeff*minSum
            seenMinMaxs[numGroups] = seenMin != seenMaxNorm ? seenMin | (seenMaxNorm << 16) : 0u;
        }

        // Degrees of freedom. sum=0 for arrows: circle - arrows = 0.
        int minDof = -strictMin;   // = 0 - strictMin
        int maxDof =  strictMax;   // = strictMax - 0
        if (minDof < 0 || maxDof < 0) return false;

        // dofLim = (G.SIZE - 1) * |coeff| = 8 for all groups here.
        const int dofLim = G.SIZE - 1;
        if (minDof >= dofLim && maxDof >= dofLim) return true;  // no group can be restricted

        // Spread and apply for arrow groups (coeff=+1, minDofSet=minDof, maxDofSet=maxDof).
        for (int g = 0; g < numGroups; g++)
        {
            uint smm = seenMinMaxs[g];
            if (smm == 0) continue;

            uint seenMin = smm & 0xFFFFu;
            uint seenMax = smm >> 16;
            uint valueMask = ~0u;

            if (minDof < dofLim) {
                uint sm = seenMin;
                for (int j = 0; j < minDof; j++) sm |= sm << 1;
                valueMask = sm;
            }
            if (maxDof < dofLim) {
                uint sm = seenMax;
                for (int j = 0; j < maxDof; j++) sm |= sm >> 1;
                valueMask &= sm;
            }

            if ((~valueMask & G.ALL_VALUES) != 0) {
                int[] grp = _arrowGroups[g];
                for (int j = 0; j < grp.Length; j++) {
                    int cell = _arrow[grp[j]];
                    uint nv = grid[cell] & valueMask;
                    if (nv == 0) return false;
                    grid[cell] = nv;
                }
            }
        }

        // Circle group (coeff=-1): minDofSet=maxDof, maxDofSet=minDof (negated swapped).
        {
            uint smm = seenMinMaxs[numGroups];
            if (smm != 0) {
                uint seenMin = smm & 0xFFFFu;
                uint seenMax = smm >> 16;
                uint valueMask = ~0u;

                if (maxDof < dofLim) {    // minDofSet for coeff=-1 = maxDof
                    uint sm = seenMin;
                    for (int j = 0; j < maxDof; j++) sm |= sm << 1;
                    valueMask = sm;
                }
                if (minDof < dofLim) {   // maxDofSet for coeff=-1 = minDof
                    uint sm = seenMax;
                    for (int j = 0; j < minDof; j++) sm |= sm >> 1;
                    valueMask &= sm;
                }

                if ((~valueMask & G.ALL_VALUES) != 0) {
                    uint nv = grid[_circle] & valueMask;
                    if (nv == 0) return false;
                    grid[_circle] = nv;
                }
            }
        }

        return true;
    }

    // ---- Helpers ----

    private static uint RestrictRange(uint mask, int lo, int hi)
    {
        if (lo > 1)      mask &= ~(G.ValueBit(lo) - 1u);
        if (hi < G.SIZE) mask &= G.ValueBit(hi + 1) - 1u;
        return mask & G.ALL_VALUES;
    }

    private static bool Narrow(uint[] grid, HandlerAccumulator acc,
                                int cell, uint old, uint kept)
    {
        if (kept == 0) return false;
        if (kept != old) grid[cell] = kept;
        return true;
    }
}
