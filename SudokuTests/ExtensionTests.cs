namespace SudokuTests;

[TestClass]
public class ExtensionTests
{
    [TestMethod]
    public void IntLengthIsNumberOfCharacters()
    {
        var skip = 1;

        for (var i = 0; i < 100000; i += skip)
        {
            Assert.AreEqual(i.ToString().Length, i.Length());

            skip *= 10;
        }
    }

    [TestMethod]
    public void FirstXDigits()
    {
        Assert.AreEqual(123, 1234.SubInt(0, 3, out int leadingZeros));
        Assert.AreEqual(0, leadingZeros);
    }

    [TestMethod]
    public void LastXDigits()
    {
        Assert.AreEqual(234, 1234.SubInt(1, 3, out int leadingZeros));
        Assert.AreEqual(0, leadingZeros);
    }

    [TestMethod]
    public void LengthGreaterThanDigitsReturnsRest()
    {
        int leadingZeros = 0;

        Assert.AreEqual(2345, 12345.SubInt(1, 10, out leadingZeros));
        Assert.AreEqual(12345, 12345.SubInt(0, 100, out leadingZeros));
    }

    [TestMethod]
    public void RealCase()
    {
        Assert.AreEqual(5, 115.SubInt(2, 1, out int leadingZeros));
        Assert.AreEqual(0, leadingZeros);
    }

    [TestMethod]
    public void LeadingZero()
    {
        Assert.AreEqual(24, 1024.SubInt(1, 3, out int leadingZeros));
        Assert.AreEqual(1, leadingZeros);
    }

    [TestMethod]
    public void WorksForLargeInts()
    {
        Assert.AreEqual(10, Int32.MaxValue.Length());
    }

    [TestMethod]
    public void ZeroIsOneDigit()
    {
        Assert.AreEqual(1, 0.Length());
    }

    [TestMethod]
    public void SubstringZero()
    {
        Assert.AreEqual(0, 1000.SubInt(1, 3, out int leadingZeros));

        // 2 leading 0s ahead of the last 0 which is represented in the return value
        Assert.AreEqual(2, leadingZeros);
    }

    [TestMethod]
    public void LengthWorksForNegatives()
    {
        Assert.AreEqual(3, (-123).Length());
    }

    [TestMethod]
    public void TakeIsSubInt0()
    {
        Assert.AreEqual(12345.SubInt(0, 3, out int leading), 12345.Take(3));
    }

    [TestMethod]
    public void SkipIsRestOfDigits()
    {
        Assert.AreEqual(12045.SubInt(2, 3, out int leading1), 12045.Skip(2, out int leading2));
        Assert.AreEqual(leading1, leading2);
        Assert.AreEqual(1, leading1);
    }

    [TestMethod]
    public void ChainingWorks()
    {
        Assert.AreEqual(123, 567123.Skip(3, out int leading).Take(3));
    }

    [TestMethod]
    public void TakeWorksForNegatives()
    {
        Assert.AreEqual(-123, -12345.Take(3));
    }

    [TestMethod]
    public void SkipWorksForNegatives()
    {
        Assert.AreEqual(-45, -12345.Skip(3, out int leading));
    }

    [TestMethod]
    public void LeadingZerosWorkForNegatives()
    {
        Assert.AreEqual(-45, -123045.Skip(3, out int leading));
        Assert.AreEqual(1, leading);
    }

    /// <summary>
    /// CombinationsBuffered must enumerate exactly what Combinations does, in the same order, for
    /// every (n, k) the solver can reach. Snapshotting each yielded buffer is the point: the
    /// borrowed buffer is only correct if its *contents at yield time* match the fresh list.
    /// </summary>
    [TestMethod]
    public void CombinationsBufferedMatchesCombinations()
    {
        // n up to 9 covers a 9x9 grid's row/col and candidate lists; k up to n covers the tuple
        // sizes the fish, tuple and ALS searches ask for, including the k > n no-op case.
        for (int n = 0; n <= 9; n++)
        {
            List<int> source = new();
            for (int i = 0; i < n; i++)
            {
                source.Add(i * 7 + 1);
            }

            for (int k = 1; k <= n + 1; k++)
            {
                List<List<int>> expected = source.Combinations(k).ToList();
                List<List<int>> actual = source.CombinationsBuffered(k)
                    .Select(buffer => buffer.ToList())
                    .ToList();

                Assert.AreEqual(expected.Count, actual.Count, $"count mismatch for n={n}, k={k}");
                for (int c = 0; c < expected.Count; c++)
                {
                    CollectionAssert.AreEqual(expected[c], actual[c], $"combination {c} differs for n={n}, k={k}");
                }
            }
        }
    }

    /// <summary>
    /// The buffer is allocated per enumerator, which is what makes the nested pair in
    /// FindFinnedFishes safe. If it were shared, the outer combination would be corrupted by the
    /// inner enumeration.
    /// </summary>
    [TestMethod]
    public void CombinationsBufferedNestsWithoutInterference()
    {
        List<int> outerSource = [1, 2, 3, 4];
        List<int> innerSource = [10, 20, 30, 40, 50];

        int outerCount = 0;
        foreach (List<int> outer in outerSource.CombinationsBuffered(2))
        {
            List<int> outerBefore = outer.ToList();
            foreach (List<int> inner in innerSource.CombinationsBuffered(3))
            {
                Assert.AreEqual(3, inner.Count);
                // The outer buffer must be untouched while the inner enumeration runs.
                CollectionAssert.AreEqual(outerBefore, outer);
            }
            CollectionAssert.AreEqual(outerBefore, outer);
            outerCount++;
        }

        Assert.AreEqual(6, outerCount);
    }

    /// <summary>
    /// Documents the borrowed-buffer contract as a behaviour: retaining the yielded reference is
    /// wrong, and this is what it looks like when you do. A caller that stores it ends up with N
    /// references to one list holding only the last combination.
    /// </summary>
    [TestMethod]
    public void CombinationsBufferedYieldsTheSameListInstance()
    {
        List<int> source = [1, 2, 3];
        List<List<int>> retained = source.CombinationsBuffered(2).ToList();

        Assert.AreEqual(3, retained.Count);
        Assert.AreSame(retained[0], retained[1]);
        Assert.AreSame(retained[1], retained[2]);
        CollectionAssert.AreEqual(new List<int> { 2, 3 }, retained[0]);

        // Whereas Combinations gives each caller its own list.
        List<List<int>> fresh = source.Combinations(2).ToList();
        Assert.AreNotSame(fresh[0], fresh[1]);
        CollectionAssert.AreEqual(new List<int> { 1, 2 }, fresh[0]);
    }
}
