using System.Collections.Concurrent;

namespace SudokuSolver;

// Precomputed tables for sum constraint fast-path.
// KillerCageSums[k][s] = bitmasks with exactly k distinct 1-indexed values summing to s.
// Values are encoded as bit i = value (i+1), e.g. value 3 = bit 2 = mask 0b100.
// Capped at numValues <= 20 (tableSize = 2^20 = 1M entries).
internal sealed class SumData
{
    private static readonly ConcurrentDictionary<int, SumData> _cache = new();

    public static SumData Get(int numValues)
    {
        if (numValues < 1 || numValues > 20) return null;
        return _cache.GetOrAdd(numValues, static n => new SumData(n));
    }

    // SumTable[mask] = sum of 1-indexed values encoded in mask
    public readonly int[] SumTable;

    // KillerCageSums[k][s] = bitmasks with exactly k bits set summing to s (1-indexed values)
    // k ranges 1..numValues; s ranges 0..maxSumForK (sparse: empty arrays for unreachable sums)
    public readonly uint[][][] KillerCageSums;

    private SumData(int numValues)
    {
        int tableSize = 1 << numValues;
        SumTable = new int[tableSize];
        for (int mask = 1; mask < tableSize; mask++)
        {
            int sum = 0, m = mask;
            while (m != 0)
            {
                sum += BitOperations.TrailingZeroCount(m) + 1;
                m &= m - 1;
            }
            SumTable[mask] = sum;
        }

        var tempLists = new List<uint>[numValues + 1][];
        for (int k = 1; k <= numValues; k++)
        {
            int maxS = k * numValues - k * (k - 1) / 2;
            tempLists[k] = new List<uint>[maxS + 1];
            int minS = k * (k + 1) / 2;
            for (int s = minS; s <= maxS; s++)
                tempLists[k][s] = [];
        }

        for (int mask = 1; mask < tableSize; mask++)
        {
            int k = BitOperations.PopCount((uint)mask);
            tempLists[k][SumTable[mask]].Add((uint)mask);
        }

        KillerCageSums = new uint[numValues + 1][][];
        for (int k = 1; k <= numValues; k++)
        {
            int maxS = k * numValues - k * (k - 1) / 2;
            KillerCageSums[k] = new uint[maxS + 1][];
            for (int s = 0; s <= maxS; s++)
                KillerCageSums[k][s] = tempLists[k][s]?.ToArray() ?? [];
        }
    }
}
