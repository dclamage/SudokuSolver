using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using SudokuSolver;

namespace SudokuTests
{
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
    }
}
