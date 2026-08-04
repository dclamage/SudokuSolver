using System.Collections.Concurrent;

namespace SudokuSolver;

/// <summary>
/// Precomputed sets of distinct values grouped by how many values they contain and what they sum to,
/// as candidate bitmasks. Lets a constraint ask "which value sets of size k sum to S?" with a lookup
/// instead of enumerating every combination and filtering by sum.
/// </summary>
/// <remarks>
/// <para>
/// The point is where the work happens. Enumerating combinations at solve time costs
/// C(candidates, k) per call, and <c>SandwichConstraint</c> was doing that at every search node —
/// 4.2 million combinations in a single count of the ISS puzzle <c>blPgSzctUMg</c>, at 280 µs per
/// node. Every one of those enumerations produces the same answer for the same (k, S), so it belongs
/// in a table built once per grid size. This mirrors what ISS's <c>Lunchbox</c> handler does with its
/// memoized <c>_combinationsFor</c> table; see docs/pathological-outliers.md.
/// </para>
/// <para>
/// A caller still has to intersect against what is actually available, which is one test per
/// candidate set: <c>(mask &amp; ~availableMask) != 0</c> means the set needs a value no cell can take.
/// </para>
/// <para>
/// The table enumerates all 2^maxValue value sets, so it is only built for
/// <see cref="MAX_TABULATED_VALUE"/> and below. Above that <see cref="MasksFor"/> returns
/// <see langword="null"/> and callers must keep their own enumeration path.
/// </para>
/// </remarks>
internal static class ValueCombinationTable
{
    /// <summary>
    /// Largest grid size that gets a table. 16 covers every realistic grid (2^16 = 65,536 sets,
    /// a few hundred KB) while keeping 17..31 — which <see cref="Solver"/> permits but no real
    /// puzzle uses — off a 2-billion-entry cliff.
    /// </summary>
    internal const int MAX_TABULATED_VALUE = 16;

    private static readonly ConcurrentDictionary<int, uint[][]> tablesByMaxValue = new();

    /// <summary>
    /// The value sets of exactly <paramref name="count"/> distinct values from 1..<paramref name="maxValue"/>
    /// that sum to <paramref name="sum"/>, as candidate bitmasks. Empty when none exist;
    /// <see langword="null"/> when <paramref name="maxValue"/> is too large to tabulate.
    /// </summary>
    public static uint[] MasksFor(int maxValue, int count, int sum)
    {
        if (maxValue > MAX_TABULATED_VALUE || maxValue < 1)
        {
            return null;
        }

        uint[][] table = tablesByMaxValue.GetOrAdd(maxValue, Build);
        int maxSum = MaxSum(maxValue);
        if (count < 0 || count > maxValue || sum < 0 || sum > maxSum)
        {
            return Array.Empty<uint>();
        }
        return table[count * (maxSum + 1) + sum];
    }

    private static int MaxSum(int maxValue) => maxValue * (maxValue + 1) / 2;

    private static uint[][] Build(int maxValue)
    {
        int maxSum = MaxSum(maxValue);
        int numBuckets = (maxValue + 1) * (maxSum + 1);

        // Two passes so each bucket is exactly sized: count first, then fill.
        int[] counts = new int[numBuckets];
        uint allMasks = (1u << maxValue) - 1;
        for (uint mask = 0; mask <= allMasks; mask++)
        {
            counts[BucketOf(mask, maxValue, maxSum)]++;
        }

        uint[][] table = new uint[numBuckets][];
        for (int b = 0; b < numBuckets; b++)
        {
            table[b] = counts[b] == 0 ? Array.Empty<uint>() : new uint[counts[b]];
        }

        int[] next = new int[numBuckets];
        for (uint mask = 0; mask <= allMasks; mask++)
        {
            int b = BucketOf(mask, maxValue, maxSum);
            table[b][next[b]++] = mask;
        }

        return table;
    }

    private static int BucketOf(uint mask, int maxValue, int maxSum)
    {
        int count = 0;
        int sum = 0;
        for (int v = 1; v <= maxValue; v++)
        {
            if ((mask & (1u << (v - 1))) != 0)
            {
                count++;
                sum += v;
            }
        }
        return count * (maxSum + 1) + sum;
    }
}
