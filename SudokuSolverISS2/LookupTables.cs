// Port of lookup_tables.js — static utility methods + precomputed tables for numValues=9.
// JS note: 'v & -v' (lowest set bit) becomes (v & -v) for int since C# uses two's complement.
// JS note: 'countOnes16bit' = BitOperations.PopCount(v & 0xFFFF).
// JS note: '32 - Math.clz32(v)' = BitOperations.Log2(v)+1 (highest bit position, 1-indexed), clz32(0)=32 so toValue(0)=0.
using System.Numerics;

namespace SudokuSolverISS2;

static class LookupTables
{
    public const int NUM_VALUES   = 9;
    public const int NUM_CELLS    = 81;
    public const int COMBINATIONS = 1 << NUM_VALUES;   // 512
    public const int ALL_VALUES   = COMBINATIONS - 1;  // 0x1FF

    // sum[mask] = sum of set-bit digit values (bit k set → digit k+1).
    // Port of JS: table[i] = table[i & (i-1)] + toValue(i & -i)
    public static readonly byte[] Sum;

    // rangeInfo[mask]: packed (isFixed<<24 | fixed<<16 | min<<8 | max).
    // Port of JS rangeInfo table.
    public static readonly uint[] RangeInfo;

    // reverse[mask] = bit-reversal within 9 bits (value k ↔ value 10-k).
    // Port of JS reverse table.
    public static readonly ushort[] Reverse;

    static LookupTables()
    {
        Sum      = new byte[COMBINATIONS];
        RangeInfo = new uint[COMBINATIONS];
        Reverse  = new ushort[COMBINATIONS];

        // sum table
        for (int i = 1; i < COMBINATIONS; i++)
            Sum[i] = (byte)(Sum[i & (i - 1)] + ToValue(i & -i));

        // rangeInfo table
        for (int i = 1; i < COMBINATIONS; i++)
        {
            int max    = MaxValue(i);
            int min    = MinValue(i);
            int isFixed = (i & (i - 1)) == 0 ? 1 : 0;
            int fixed_  = isFixed != 0 ? ToValue(i) : 0;
            RangeInfo[i] = (uint)((isFixed << 24) | (fixed_ << 16) | (min << 8) | max);
        }
        // If no values: set high isFixed to signal invalid (matches JS table[0] = numValues << 24).
        RangeInfo[0] = (uint)(NUM_VALUES << 24);

        // reverse table
        for (int i = 1; i <= NUM_VALUES; i++)
            Reverse[FromValue(i)] = (ushort)FromValue(NUM_VALUES + 1 - i);
        for (int i = 1; i < COMBINATIONS; i++)
            Reverse[i] = (ushort)(Reverse[i & (i - 1)] | Reverse[i & -i]);
    }

    // toValue: index of highest set bit + 1 (JS: 32 - Math.clz32(v)).
    // toValue(0) = 0.
    public static int ToValue(int v)
        => v == 0 ? 0 : BitOperations.Log2((uint)v) + 1;

    // maxValue = same as toValue.
    public static int MaxValue(int v) => ToValue(v);

    // minValue: index of lowest set bit + 1 (JS: 32 - Math.clz32(v & -v)).
    public static int MinValue(int v)
        => v == 0 ? 0 : BitOperations.TrailingZeroCount((uint)v) + 1;

    // fromValue: bit mask for digit d (1-indexed). JS: 1 << (d-1).
    public static int FromValue(int d) => 1 << (d - 1);

    // allValues bitmask for numValues bits.
    public static int AllValues(int numValues) => (1 << numValues) - 1;

    // countOnes: popcount of lower 16 bits (JS countOnes16bit).
    public static int CountOnes(int v) => BitOperations.PopCount((uint)(v & 0xFFFF));

    // toIndex: bit position of highest set bit (0-indexed). JS: 31 - Math.clz32(v).
    public static int ToIndex(int v) => BitOperations.Log2((uint)v);

    // minMax16bitValue: packed [min:16, max:16] (JS minMax16bitValue).
    // Layout: min in upper 16 bits, max in lower 16 bits.
    // JS: 0x200020 - (Math.clz32(v & -v) << 16) - Math.clz32(v)
    public static int MinMax16bitValue(int v)
        => 0x200020 - (BitOperations.LeadingZeroCount((uint)(v & -v)) << 16)
                    - (int)BitOperations.LeadingZeroCount((uint)v);
}
