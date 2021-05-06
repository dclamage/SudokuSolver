using System;
using System.Collections.Generic;
using System.Linq;

namespace SudokuSolver
{
    static class Extensions
    {
        private static void SetIndexes(int[] indexes, int lastIndex, int count)
        {
            indexes[lastIndex]++;
            if (lastIndex > 0 && indexes[lastIndex] == count)
            {
                SetIndexes(indexes, lastIndex - 1, count - 1);
                indexes[lastIndex] = indexes[lastIndex - 1] + 1;
            }
        }

        private static bool AllPlacesChecked(int[] indexes, int places)
        {
            for (int i = indexes.Length - 1; i >= 0; i--)
            {
                if (indexes[i] != places)
                    return false;
                places--;
            }
            return true;
        }

        public static IEnumerable<List<T>> Combinations<T>(this IEnumerable<T> c, int count)
        {
            if (c is not List<T> collection)
            {
                collection = c.ToList();
            }
            int listCount = collection.Count;

            if (count <= listCount)
            {
                int[] indexes = Enumerable.Range(0, count).ToArray();
                do
                {
                    yield return indexes.Select(i => collection[i]).ToList();

                    SetIndexes(indexes, indexes.Length - 1, listCount);
                }
                while (!AllPlacesChecked(indexes, listCount));
            }
        }

        public static IEnumerable<List<T>> Permuatations<T>(this IEnumerable<T> c)
        {
            var clist = c.ToList();
            return PermutationsHelper(clist, 0);
        }

        private static IEnumerable<List<T>> PermutationsHelper<T>(List<T> list, int i)
        {
            if (i + 1 == list.Count)
            {
                yield return list;
            }
            else
            {
                foreach (var v in PermutationsHelper(list, i + 1))
                {
                    yield return v;
                }

                for (int i1 = i + 1; i1 < list.Count; i1++)
                {
                    (list[i], list[i1]) = (list[i1], list[i]);
                    foreach (var v in PermutationsHelper(list, i + 1))
                    {
                        yield return v;
                    }
                    (list[i], list[i1]) = (list[i1], list[i]);
                }
            }
        }

        public static string ReplaceChars(this string s, string from, string to)
        {
            if (from.Length != to.Length)
            {
                throw new ArgumentException("from and to length must match");
            }

            string res = s;
            for (int i = 0; i < from.Length; i++)
            {
                res = res.Replace(from[i], to[i]);
            }
            return res;
        }

        public static List<T> ToSortedList<T>(this IEnumerable<T> c)
        {
            var list = c.ToList();
            list.Sort();
            return list;
        }
    }
}
