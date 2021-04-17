using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SudokuSolver
{
    public static class SolverUtility
    {
        static SolverUtility()
        {
            InitCombinations();
        }

        public const int WIDTH = 9;
        public const int HEIGHT = 9;
        public const int MAX_EXTENT = WIDTH > HEIGHT ? WIDTH : HEIGHT;
        public const int NUM_CELLS = WIDTH * HEIGHT;

        public const int MAX_VALUE = 9;
        public const uint ALL_VALUES_MASK = (1u << MAX_VALUE) - 1;

        public const int BOX_WIDTH = 3;
        public const int BOX_HEIGHT = 3;
        public const int BOX_CELL_COUNT = BOX_WIDTH * BOX_HEIGHT;
        public const int NUM_BOXES_WIDTH = WIDTH / BOX_WIDTH;
        public const int NUM_BOXES_HEIGHT = HEIGHT / BOX_HEIGHT;
        public const int NUM_BOXES = NUM_BOXES_WIDTH * NUM_BOXES_HEIGHT;

        // These are compile-time asserts
        private const byte ASSERT_VALUES_MIN = (MAX_VALUE >= 1) ? 0 : -1;
        private const byte ASSERT_VALUES_MAX = (MAX_VALUE <= 9) ? 0 : -1; // No support for more than 9 values yet
        private const byte ASSERT_BOX_SIZE = BOX_CELL_COUNT == MAX_VALUE ? 0 : -1;
        private const byte ASSERT_BOX_WIDTH_MAX = BOX_WIDTH <= WIDTH ? 0 : -1;
        private const byte ASSERT_BOX_HEIGHT_MAX = BOX_HEIGHT <= HEIGHT ? 0 : -1;
        private const byte ASSERT_BOX_WIDTH_DIVISIBILITY = (WIDTH % BOX_WIDTH) == 0 ? 0 : -1;
        private const byte ASSERT_BOX_HEIGHT_DIVISIBILITY = (HEIGHT % BOX_HEIGHT) == 0 ? 0 : -1;

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
        public static (int, int) BoxCoord(int i, int j)
        {
            return (i / BOX_HEIGHT, j / BOX_WIDTH);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BoxIndex(int i, int j)
        {
            var (bi, bj) = BoxCoord(i, j);
            return bi * NUM_BOXES_WIDTH + bj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSameBox(int i0, int j0, int i1, int j1)
        {
            return BoxCoord(i0, j0) == BoxCoord(i1, j1);
        }

        public static string MaskToString(uint mask)
        {
            StringBuilder sb = new();
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                if (HasValue(mask, v))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append((char)('0' + v));
                }
            }

            return sb.ToString();
        }

        private static int Gcd(int a, int b)
        {
            // Everything divides 0 
            if (a == 0 || b == 0)
                return 0;

            // base case 
            if (a == b)
                return a;

            // a is greater 
            if (a > b)
                return Gcd(a - b, b);

            return Gcd(a, b - a);
        }

        public static IEnumerable<(int, int)> AdjacentCells(int i, int j)
        {
            if (i > 0)
            {
                yield return (i - 1, j);
            }
            if (i < HEIGHT - 1)
            {
                yield return (i + 1, j);
            }
            if (j > 0)
            {
                yield return (i, j - 1);
            }
            if (j < WIDTH - 1)
            {
                yield return (i, j + 1);
            }
        }

        public static IEnumerable<(int, int)> DiagonalCells(int i, int j, bool sameBox = false)
        {
            if (i > 0 && j > 0)
            {
                if (!sameBox || IsSameBox(i, j, i - 1, j - 1))
                {
                    yield return (i - 1, j - 1);
                }
            }
            if (i < HEIGHT - 1 && j > 0)
            {
                if (!sameBox || IsSameBox(i, j, i + 1, j - 1))
                {
                    yield return (i + 1, j - 1);
                }
            }
            if (i > 0 && j < WIDTH - 1)
            {
                if (!sameBox || IsSameBox(i, j, i - 1, j + 1))
                {
                    yield return (i - 1, j + 1);
                }
            }
            if (i < HEIGHT - 1 && j < WIDTH - 1)
            {
                if (!sameBox || IsSameBox(i, j, i + 1, j + 1))
                {
                    yield return (i + 1, j + 1);
                }
            }
        }

        public static int TaxicabDistance(int i0, int j0, int i1, int j1) => Math.Abs(i0 - i1) + Math.Abs(j0 - j1);
        public static bool IsAdjacent(int i0, int j0, int i1, int j1) => i0 == i1 && Math.Abs(j0 - j1) <= 1 || j0 == j1 && Math.Abs(i0 - i1) <= 1;
        public static bool IsDiagonal(int i0, int j0, int i1, int j1) => (i0 == i1 - 1 || i0 == i1 + 1) && (j0 == j1 - 1 || j0 == j1 + 1);
        public static string CellName((int, int) cell) => $"r{cell.Item1 + 1}c{cell.Item2 + 1}";
        public static string CellName(int i, int j) => CellName((i, j));
        public static (int, int) CellValue(string cellName) => cellName.Length == 4 ? (cellName[1] - '1', cellName[3] - '1') : (-1, -1);
        public static int FlatIndex((int, int) cell) => cell.Item1 * WIDTH + cell.Item2;
        public static (int, int, int, int) CellPair((int, int) cell0, (int, int) cell1)
        {
            return FlatIndex(cell0) <= FlatIndex(cell1) ? (cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2) : (cell1.Item1, cell1.Item2, cell0.Item1, cell0.Item2);
        }

        public static readonly int[][][] combinations = new int[MAX_VALUE][][];
        private static void InitCombinations()
        {
            for (int n = 1; n <= combinations.Length; n++)
            {
                combinations[n - 1] = new int[n][];
                for (int k = 1; k <= n; k++)
                {
                    int numCombinations = BinomialCoeff(n, k);
                    combinations[n - 1][k - 1] = new int[numCombinations * k];
                    FillCombinations(combinations[n - 1][k - 1], n, k);
                }
            }
        }

        private static int BinomialCoeff(int n, int k)
        {
            return
                (k > n) ? 0 :          // out of range
                (k == 0 || k == n) ? 1 :          // edge
                (k == 1 || k == n - 1) ? n :          // first
                (k + k < n) ?              // recursive:
                (BinomialCoeff(n - 1, k - 1) * n) / k :       //  path to k=1   is faster
                (BinomialCoeff(n - 1, k) * n) / (n - k);      //  path to k=n-1 is faster
        }

        private static void FillCombinations(int[] combinations, int n, int k, ref int numCombinations, int offset, int[] curCombination, int curCombinationSize)
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

        private static void FillCombinations(int[] combinations, int n, int k)
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
    }
}
