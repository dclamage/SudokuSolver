using System;
using System.Collections.Generic;
using System.Linq;

namespace SudokuSolver
{
    public static class Extensions
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

        private static readonly int[] POWERS_OF_10 = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };

        public static int SubInt(this int subject, int startIndex, int length, out int leadingZeros)
        {
            var subjectLength = subject.Length();
            var final = subject;

            // Constrain length += startIndex to be <= target.Length()
            if (startIndex + length > subjectLength)
            {
                length += (subjectLength - startIndex - length);
            }

            var zeros = 0;

            if (startIndex > 0)
            {
                final -= (subject / POWERS_OF_10[subjectLength - startIndex]) * POWERS_OF_10[subjectLength - startIndex];

                // We might have exposed 1 or more leading 0s.
                zeros = (subjectLength - startIndex) - final.Length();
            }

            // Let the called know if any "characters" have been dropped due to leading zeros.
            leadingZeros = zeros;

            // If our final number is 0 then return now to avoid / 0 error
            return final == 0 ? 0 : final / POWERS_OF_10[final.Length() - (length - zeros)];
        }

        public static int Take(this int subject, int numberDigits)
        {
            return subject.SubInt(0, numberDigits, out int ignore);
        }

        public static int Skip(this int subject, int numberToSkip, out int leadingZeros)
        {
            return subject.SubInt(numberToSkip, subject.Length() - numberToSkip, out leadingZeros);
        }

        public static int Length(this int target)
        {
            if(target < 0) 
            {
                target = Math.Abs(target);
            }

            if (target < 10)                { return 1; }
            else if (target < 100)          { return 2; }
            else if (target < 1000)         { return 3; }
            else if (target < 10000)        { return 4; }
            else if (target < 100000)       { return 5; }
            else if (target < 1000000)      { return 6; }
            else if (target < 10000000)     { return 7; }
            else if (target < 100000000)    { return 8; }
            else if (target < 1000000000)   { return 9; }
            else                            { return 10; }  // Int32.Max = 10
        }
    }
}
