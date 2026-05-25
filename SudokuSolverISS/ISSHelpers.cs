namespace SudokuSolverISS;

/// <summary>
/// Grid encoding mirrors ISS's Uint16Array:
///   bit k (0-indexed) = value (k+1) is a candidate.
///   ALL_VALUES = (1 &lt;&lt; numValues) - 1 = 0x1FF for 9×9.
///   Fixed cell  = exactly 1 bit set  (IsSingleton).
///   Empty cell  = 0                  (contradiction).
/// </summary>
static class G
{
    public const int  SIZE       = 9;
    public const int  NUM_CELLS  = SIZE * SIZE;
    public const uint ALL_VALUES = (1u << SIZE) - 1;  // 0x1FF

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSingleton(uint v) => v != 0 && (v & (v - 1)) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  Count(uint v) => BitOperations.PopCount(v);

    // Extract the lowest set bit (the value ISS tries first).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LowestBit(uint v) => v & (uint)(-(int)v);

    // 0-indexed bit position → 1-indexed value.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  BitToValue(uint singleBit) => BitOperations.TrailingZeroCount(singleBit) + 1;

    // 1-indexed value → single-bit mask.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ValueBit(int value) => 1u << (value - 1);

    // Sum of all candidate values in a fixed cell (IsSingleton must be true).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  SingletonValue(uint v) => BitOperations.TrailingZeroCount(v) + 1;

    // Cell index from (row, col) both 0-indexed.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  CellIndex(int row, int col) => row * SIZE + col;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  Row(int cellIndex) => cellIndex / SIZE;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  Col(int cellIndex) => cellIndex % SIZE;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int  Box(int cellIndex) =>
        (Row(cellIndex) / 3) * 3 + Col(cellIndex) / 3;
}
