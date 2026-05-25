namespace SudokuSolverISS;

/// <summary>
/// Precomputed lookup tables for sum constraint propagation, mirroring
/// ISS's SumData / LookupTables objects.
/// </summary>
static class SumData
{
    // reverse[mask]: bit-reversal of a 9-bit mask.
    // If bit k is set in mask (value k+1), then bit (8-k) is set in reverse[mask].
    // Used by _enforceTwoRemainingCells:
    //   v1 &= (ushort)((reverse[v0] << (targetSum-1)) >> SIZE);
    public static readonly ushort[] Reverse = BuildReverse();

    // killerCageSums[numCells][targetSum] = array of bitmask combinations.
    // Each bitmask has exactly numCells bits set, corresponding to distinct
    // values 1-9 that sum to targetSum.
    // Index 0 unused; sums range from min to max for the cell count.
    public static readonly uint[][][] KillerCageSums = BuildKillerCageSums();

    // pairwiseSums[v0 << SIZE | v1]: bit k = sum (k+3) achievable by choosing
    // one distinct value from v0 and one from v1.
    // Used by _enforceThreeRemainingCells.
    public static readonly uint[] PairwiseSums = BuildPairwiseSums();

    // doubles[mask]: bit (2k-2) set iff value k is in mask (i.e., sum 2k achievable by v+v).
    // Used in _enforceThreeRemainingCells for non-exclusive cell pairs (cells that can
    // repeat the same value because they don't share a house).
    // Mirrors ISS SumData.doubles.
    public static readonly uint[] Doubles = BuildDoubles();

    // sum[mask]: sum of all set-bit values (bit k set → value k+1 contributes k+1).
    // Mirrors ISS LookupTables.sum.
    public static readonly byte[] Sum = BuildSum();

    private static ushort[] BuildReverse()
    {
        int size = 1 << G.SIZE;  // 512
        ushort[] table = new ushort[size];
        for (int mask = 0; mask < size; mask++)
        {
            ushort r = 0;
            for (int k = 0; k < G.SIZE; k++)
                if ((mask & (1 << k)) != 0)
                    r |= (ushort)(1 << (G.SIZE - 1 - k));
            table[mask] = r;
        }
        return table;
    }

    private static uint[][][] BuildKillerCageSums()
    {
        // killerCageSums[n][s] for n in 1..9, s in 0..45.
        int maxN = G.SIZE;
        int maxS = G.SIZE * (G.SIZE + 1) / 2;  // 45

        var result = new uint[maxN + 1][][];
        for (int n = 1; n <= maxN; n++)
        {
            result[n] = new uint[maxS + 1][];
            var lists = new List<uint>[maxS + 1];
            for (int s = 0; s <= maxS; s++)
                lists[s] = [];

            // Enumerate all C(9,n) combinations of distinct values 1-9.
            EnumCombinations(0, 1, n, 0, 0u, lists);

            for (int s = 0; s <= maxS; s++)
                result[n][s] = lists[s].Count > 0 ? [.. lists[s]] : [];
        }
        return result;
    }

    private static void EnumCombinations(
        int chosen, int nextVal, int need, int sumSoFar, uint maskSoFar,
        List<uint>[] output)
    {
        if (chosen == need)
        {
            output[sumSoFar].Add(maskSoFar);
            return;
        }
        int remaining = need - chosen;
        for (int v = nextVal; v <= G.SIZE - remaining + 1; v++)
            EnumCombinations(chosen + 1, v + 1, need, sumSoFar + v,
                             maskSoFar | G.ValueBit(v), output);
    }

    private static uint[] BuildPairwiseSums()
    {
        // Index: (v0 << SIZE) | v1 where v0,v1 are 9-bit candidate masks.
        // Result: bit k means sum (k+3) is achievable with one value from v0
        //         and one value from v1 (must be distinct).
        int sz = 1 << G.SIZE;
        uint[] table = new uint[sz * sz];
        for (int v0 = 1; v0 < sz; v0++)
        {
            for (int v1 = 1; v1 < sz; v1++)
            {
                uint sums = 0;
                for (int a = 1; a <= G.SIZE; a++)
                {
                    if ((v0 & G.ValueBit(a)) == 0) continue;
                    for (int b = 1; b <= G.SIZE; b++)
                    {
                        if (b == a) continue;  // distinct values
                        if ((v1 & G.ValueBit(b)) == 0) continue;
                        int s = a + b;  // 2..18, but distinct so 3..17
                        sums |= 1u << (s - 3);  // bit k = sum k+3
                    }
                }
                table[(v0 << G.SIZE) | v1] = sums;
            }
        }
        return table;
    }

    private static byte[] BuildSum()
    {
        int sz = 1 << G.SIZE;
        var table = new byte[sz];
        for (int mask = 0; mask < sz; mask++)
        {
            byte s = 0;
            for (int k = 1; k <= G.SIZE; k++)
                if ((mask & G.ValueBit(k)) != 0) s += (byte)k;
            table[mask] = s;
        }
        return table;
    }

    private static uint[] BuildDoubles()
    {
        // doubles[mask]: for each value k set in mask, set bit (2k-2).
        // This represents "sum 2k achievable by using value k twice".
        // Mirrors ISS: doubles[j] has bit (2k-1) set iff value k is in j,
        // corresponding to sum 2k (stored at bit 2k-2 after the << 2 / subtract-3 encoding).
        // ISS stores at bit (2k-1), and the pairwise encoding shifts << 2 before use.
        // We mirror exactly: doubles[mask] = OR over each set value k of (1 << (2k-1)).
        int sz = 1 << G.SIZE;
        uint[] table = new uint[sz];
        for (int mask = 0; mask < sz; mask++)
        {
            uint result = 0;
            for (int k = 1; k <= G.SIZE; k++)
                if ((mask & G.ValueBit(k)) != 0)
                    result |= 1u << (2 * k - 1);
            table[mask] = result;
        }
        return table;
    }
}
