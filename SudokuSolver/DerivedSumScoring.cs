using System.Numerics;

namespace SudokuSolver;

/// <summary>
/// A scored candidate derived-sum constraint produced by discovery. Carries only what the budget
/// selector needs plus a deferred builder, so scoring/selection is a pure, solver-free operation.
/// </summary>
internal sealed class DerivedSumCandidate
{
    /// <summary>Usefulness (see <see cref="DerivedSumScoring"/>): how much the relation restricts the parts.</summary>
    internal required double Gain { get; init; }

    /// <summary>Gain divided by estimated recurring enforcement cost; the ranking key.</summary>
    internal required double Score { get; init; }

    /// <summary>The cells this candidate would watch (for the per-cell and incidence budgets).</summary>
    internal required IReadOnlyList<int> WatchedCells { get; init; }

    /// <summary>Builds the actual constraint if the candidate is admitted. Not called during selection.</summary>
    internal Func<DerivedSumConstraint> Build { get; init; }
}

/// <summary>
/// Pure static scoring for candidate derived sum relations, plus the greedy budgeted selection.
/// Everything here works on 64-bit achievable-sum masks (bit i set = total i is achievable) and
/// simple metadata, so it needs no solver and is fully unit-testable.
/// </summary>
internal static class DerivedSumScoring
{
    internal const int MaxDerivedConstraints = 8;
    internal const int MaxWatchedCellIncidences = 48;
    internal const int MaxDerivedWatchersPerCell = 2;
    internal const double MinGain = 1.0;

    /// <summary>
    /// Gain of the difference relation sum(P) - sum(N) = targetDiff, given each side's achievable-sum
    /// mask. <paramref name="contradiction"/> is set when the relation has no joint solution (a side's
    /// supported mask is empty). Returns 0 for a contradiction or when the relation removes nothing.
    /// </summary>
    internal static double ScoreDifferenceGain(ulong maskP, ulong maskN, int targetDiff, out bool contradiction)
    {
        ulong supportedP = maskP & Shift(maskN, targetDiff);
        ulong supportedN = maskN & Shift(maskP, -targetDiff);

        contradiction = supportedP == 0 || supportedN == 0;
        if (contradiction)
        {
            return 0.0;
        }

        if (supportedP == maskP && supportedN == maskN)
        {
            return 0.0;
        }

        return SideGain(maskP, supportedP) + SideGain(maskN, supportedN);
    }

    /// <summary>
    /// Gain of restricting a term whose achievable sums are <paramref name="termMask"/> to the allowed
    /// totals <paramref name="allowedMask"/> (e.g. a combined little-killer fixed sum).
    /// </summary>
    internal static double ScoreFixedSumGain(ulong termMask, ulong allowedMask, out bool contradiction)
    {
        ulong supported = termMask & allowedMask;
        contradiction = supported == 0;
        if (contradiction)
        {
            return 0.0;
        }

        if (supported == termMask)
        {
            return 0.0;
        }

        return SideGain(termMask, supported);
    }

    /// <summary>
    /// Range-based gain of pinning a term whose achievable total spans [min, max] to a single
    /// <paramref name="target"/>. Used for large terms (sum &gt; 63 / cells * MAX_VALUE &gt; 63) that cannot
    /// use the 64-bit mask. Approximates the mask-based gain using the range width instead of an exact
    /// popcount, so it may over-estimate when the target is achievable many ways.
    /// </summary>
    internal static double ScoreFixedSumRangeGain(int min, int max, int target, out bool contradiction)
    {
        contradiction = target < min || target > max;
        if (contradiction)
        {
            return 0.0;
        }

        if (min == max)
        {
            return 0.0;
        }

        double width = max - min + 1;
        double informationGain = Math.Log2(width);
        double edgeCut = ((target - min) + (max - target)) / width; // = (max - min) / width, ~1 for a point target
        return informationGain + 2.0 * edgeCut;
    }

    /// <summary>
    /// Estimated recurring enforcement cost of a candidate: proportional to how many cells it watches,
    /// scaled by how many sum groups it spans and how wide its masks are.
    /// </summary>
    internal static double Cost(int watchedCellCount, int groupCount, int popcountP, int popcountN)
        => watchedCellCount * (1.0 + groupCount + (popcountP + popcountN) / 16.0);

    /// <summary>
    /// Greedily admits candidates by descending score, subject to the minimum gain and the derived-
    /// constraint / watched-cell-incidence / per-cell budgets. Returns the admitted candidates.
    /// </summary>
    internal static List<DerivedSumCandidate> SelectWithinBudget(IReadOnlyList<DerivedSumCandidate> candidates)
    {
        var selected = new List<DerivedSumCandidate>();
        var watchersPerCell = new Dictionary<int, int>();
        int totalIncidences = 0;

        foreach (var candidate in candidates
            .Where(c => c.Gain >= MinGain)
            .OrderByDescending(c => c.Score))
        {
            if (selected.Count >= MaxDerivedConstraints)
            {
                break;
            }

            if (totalIncidences + candidate.WatchedCells.Count > MaxWatchedCellIncidences)
            {
                continue;
            }

            bool cellOverBudget = false;
            foreach (int cell in candidate.WatchedCells)
            {
                if (watchersPerCell.TryGetValue(cell, out int count) && count >= MaxDerivedWatchersPerCell)
                {
                    cellOverBudget = true;
                    break;
                }
            }
            if (cellOverBudget)
            {
                continue;
            }

            selected.Add(candidate);
            totalIncidences += candidate.WatchedCells.Count;
            foreach (int cell in candidate.WatchedCells)
            {
                watchersPerCell[cell] = watchersPerCell.GetValueOrDefault(cell) + 1;
            }
        }

        return selected;
    }

    private static double SideGain(ulong fullMask, ulong supportedMask)
        => InformationGain(fullMask, supportedMask) + 2.0 * EdgeCut(fullMask, supportedMask);

    private static double InformationGain(ulong fullMask, ulong supportedMask)
    {
        int full = BitOperations.PopCount(fullMask);
        int supported = BitOperations.PopCount(supportedMask);
        if (full == 0 || supported == 0)
        {
            return 0.0;
        }
        return Math.Log2((double)full / supported);
    }

    /// <summary>
    /// Fraction of the achievable-sum range removed at the edges (min/max), weighted heavily because
    /// the multi-group sum propagation restricts primarily by the minimum/maximum degrees of freedom.
    /// </summary>
    private static double EdgeCut(ulong fullMask, ulong supportedMask)
    {
        if (fullMask == 0 || supportedMask == 0)
        {
            return 0.0;
        }

        int minFull = BitOperations.TrailingZeroCount(fullMask);
        int maxFull = 63 - BitOperations.LeadingZeroCount(fullMask);
        int minSupported = BitOperations.TrailingZeroCount(supportedMask);
        int maxSupported = 63 - BitOperations.LeadingZeroCount(supportedMask);

        double span = maxFull - minFull + 1;
        return ((minSupported - minFull) + (maxFull - maxSupported)) / span;
    }

    /// <summary>Shifts a sum mask by a signed amount, yielding 0 when shifted entirely out of range.</summary>
    private static ulong Shift(ulong mask, int by)
    {
        if (by >= 64 || by <= -64)
        {
            return 0;
        }
        return by >= 0 ? mask << by : mask >> (-by);
    }
}
