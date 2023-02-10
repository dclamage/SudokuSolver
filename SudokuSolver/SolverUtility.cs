namespace SudokuSolver;

public static class SolverUtility
{
    public const uint valueSetMask = 1u << 31;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ValueCount(uint mask)
    {
        return BitOperations.PopCount(mask & ~valueSetMask);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetValue(uint mask)
    {
        return BitOperations.Log2(mask & ~valueSetMask) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValueSet(uint mask)
    {
        return (mask & valueSetMask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ValueMask(int val)
    {
        return 1u << (val - 1);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ValuesMask(params int[] vals)
    {
        uint mask = 0;
        foreach (int val in vals)
        {
            mask |= ValueMask(val);
        }
        return mask;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasValue(uint mask, int val)
    {
        uint valueMask = ValueMask(val);
        return (mask & valueMask) != 0;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MinValue(uint mask)
    {
        return BitOperations.TrailingZeroCount(mask) + 1;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MaxValue(uint mask)
    {
        return 32 - BitOperations.LeadingZeroCount(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint MaskStrictlyLower(int v) => (1u << (v - 1)) - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint MaskValAndLower(int v) => (1u << v) - 1;

    public static string MaskToString(uint mask)
    {
        StringBuilder sb = new();
        mask &= ~valueSetMask;
        if (mask != 0)
        {
            int minValue = MinValue(mask);
            int maxValue = MaxValue(mask);
            for (int v = minValue; v <= maxValue; v++)
            {
                if (HasValue(mask, v))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(v);
                }
            }
        }

        return sb.ToString();
    }

    public static int TaxicabDistance(int i0, int j0, int i1, int j1) => Math.Abs(i0 - i1) + Math.Abs(j0 - j1);
    public static bool IsAdjacent(int i0, int j0, int i1, int j1) => i0 == i1 && Math.Abs(j0 - j1) <= 1 || j0 == j1 && Math.Abs(i0 - i1) <= 1;
    public static bool IsDiagonal(int i0, int j0, int i1, int j1) => (i0 == i1 - 1 || i0 == i1 + 1) && (j0 == j1 - 1 || j0 == j1 + 1);
    public static string CellName((int, int) cell) => $"r{cell.Item1 + 1}c{cell.Item2 + 1}";
    public static string CellName(int i, int j) => CellName((i, j));
    public static string CellNames(this IEnumerable<(int, int)> cells, string separator = ", ") => string.Join(separator, cells.Select(CellName));

    public static int BinomialCoeff(int n, int k)
    {
        return
            (k > n) ? 0 :          // out of range
            (k == 0 || k == n) ? 1 :          // edge
            (k == 1 || k == n - 1) ? n :          // first
            (k + k < n) ?              // recursive:
            (BinomialCoeff(n - 1, k - 1) * n) / k :       //  path to k=1   is faster
            (BinomialCoeff(n - 1, k) * n) / (n - k);      //  path to k=n-1 is faster
    }

    public static void FillCombinations(int[] combinations, int n, int k, ref int numCombinations, int offset, int[] curCombination, int curCombinationSize)
    {
        if (k == 0)
        {
            for (int i = 0; i < curCombinationSize; i++)
            {
                combinations[numCombinations * curCombinationSize + i] = curCombination[i];
            }
            numCombinations++;
            return;
        }
        for (int i = offset; i <= n - k; ++i)
        {
            curCombination[curCombinationSize] = i;
            FillCombinations(combinations, n, k - 1, ref numCombinations, i + 1, curCombination, curCombinationSize + 1);
        }
    }

    public static void FillCombinations(int[] combinations, int n, int k)
    {
        int numCombinations = 0;
        int[] curCombination = new int[k];
        FillCombinations(combinations, n, k, ref numCombinations, 0, curCombination, 0);
    }

    public static void AddToList<K, V>(this Dictionary<K, List<V>> dictionary, K key, V value)
    {
        if (dictionary.TryGetValue(key, out var list))
        {
            list.Add(value);
        }
        else
        {
            dictionary[key] = new() { value };
        }
    }

    public static int[,] DefaultRegions(int size)
    {
        if (size <= 0 || size > 31)
        {
            throw new ArgumentException($"Error calculating default regions. Size must be between 1 and 31, got: {size}");
        }

        int[,] regions = new int[size, size];
        int i, j;
        switch (size)
        {
            case 1:
            case 2:
            case 3:
            case 5:
            case 7:
            case 11:
            case 13:
            case 17:
            case 19:
            case 23:
            case 29:
            case 31:
                // Cannot have regions for prime number sizes, so make them overlap exactly with rows
                for (i = 0; i < size; i++)
                {
                    for (j = 0; j < size; j++)
                    {
                        regions[i, j] = i;
                    }
                }
                break;
            case 4:
            case 9:
            case 16:
            case 25:
                // Perfect square regions
                int regionSize = (int)Math.Sqrt(size);
                for (i = 0; i < size; i++)
                {
                    for (j = 0; j < size; j++)
                    {
                        regions[i, j] = (i / regionSize) * (size / regionSize) + (j / regionSize);
                    }
                }
                break;
            case 6:
            case 8:
            case 10:
            case 14:
            case 22:
            case 26:
                // Regions are two rows tall, half board width wide
                {
                    int regionWidth = size / 2;
                    for (i = 0; i < size; i++)
                    {
                        for (j = 0; j < size; j++)
                        {
                            regions[i, j] = (i / 2) * 2 + (j / regionWidth);
                        }
                    }
                }
                break;
            case 12:
            case 15:
            case 18:
            case 21:
            case 27:
                // Regions are three rows tall, 1/3rd board width wide
                {
                    int regionWidth = size / 3;
                    for (i = 0; i < size; i++)
                    {
                        for (j = 0; j < size; j++)
                        {
                            regions[i, j] = (i / 3) * 3 + (j / regionWidth);
                        }
                    }
                }
                break;
            case 20:
            case 24:
            case 28:
                // Regions are four rows tall, 1/4th board width wide
                {
                    int regionWidth = size / 4;
                    for (i = 0; i < size; i++)
                    {
                        for (j = 0; j < size; j++)
                        {
                            regions[i, j] = (i / 4) * 4 + (j / regionWidth);
                        }
                    }
                }
                break;
            case 30:
                // Regions are five rows tall, 1/5th board width wide
                {
                    int regionWidth = size / 5;
                    for (i = 0; i < size; i++)
                    {
                        for (j = 0; j < size; j++)
                        {
                            regions[i, j] = (i / 5) * 5 + (j / regionWidth);
                        }
                    }
                }
                break;
            default:
                throw new ArgumentException($"Error calculating default regions. Unsupported size: {size}");
        }

        return regions;
    }

    // Thread safe random
    private static readonly Random _global = new();
    [ThreadStatic] private static Random _local;
    public static int RandomNext(int minValue, int maxValue)
    {
        if (_local == null)
        {
            lock (_global)
            {
                if (_local == null)
                {
                    int seed = _global.Next();
                    _local = new Random(seed);
                }
            }
        }

        return _local.Next(minValue, maxValue);
    }

    public static int GetRandomValue(uint mask)
    {
        int numCellVals = ValueCount(mask);
        if (numCellVals == 1)
        {
            return GetValue(mask);
        }

        int minVal = MinValue(mask);
        int maxVal = MaxValue(mask);
        int targetValIndex = RandomNext(0, numCellVals);

        int valIndex = 0;
        int val = 0;
        for (int curVal = minVal; curVal <= maxVal; curVal++)
        {
            val = curVal;
            uint valMask = ValueMask(curVal);

            // Don't bother trying the value if it's not a possibility
            if ((mask & valMask) != 0)
            {
                if (valIndex == targetValIndex)
                {
                    break;
                }
                valIndex++;
            }
        }
        return val;
    }

    // Helpers for memo keys
    public static string CellsKey(string prefix, IEnumerable<(int, int)> cells)
    {
        return new StringBuilder()
            .Append(prefix)
            .AppendCellNames(cells)
            .ToString();
    }

    public static StringBuilder AppendInts(this StringBuilder builder, IEnumerable<int> ints)
    {
        foreach (int cur in ints)
        {
            builder.Append('|').Append(cur);
        }
        return builder;
    }

    public static StringBuilder AppendCellNames(this StringBuilder builder, IEnumerable<(int, int)> cells)
    {
        foreach (var cell in cells)
        {
            builder.Append('|').Append(CellName(cell));
        }
        return builder;
    }

    public static StringBuilder AppendCellValueKey(this StringBuilder builder, Solver solver, IEnumerable<(int, int)> cells)
    {
        var board = solver.Board;
        foreach (var (i, j) in cells)
        {
            uint mask = board[i, j];
            builder.Append(IsValueSet(mask) ? "|s" : "|").AppendFormat("{0:x}", mask & ~valueSetMask);
        }
        return builder;
    }
}

public class ScopedStopwatch : IDisposable
{
    public ScopedStopwatch(Stopwatch stopwatch)
    {
        this.stopwatch = stopwatch;
        stopwatch.Start();
    }

    public void Dispose()
    {
        stopwatch.Stop();
    }

    public readonly Stopwatch stopwatch;
}

