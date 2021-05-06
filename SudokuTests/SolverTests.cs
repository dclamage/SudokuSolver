using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SudokuSolver;

namespace SudokuTests
{
    [TestClass]
    public class SolverTests
    {
        [TestMethod]
        public void SolveUniqueClassicGivens()
        {
            foreach (var curBoard in Puzzles.uniqueClassics)
            {
                Solver solver = SolverFactory.CreateFromGivens(curBoard.Item1);
                solver.TestUniqueSolution(curBoard.Item2);
            }
        }

        [TestMethod]
        public void SolveInvalidClassicGivens()
        {
            foreach (var curBoard in Puzzles.uniqueClassics)
            {
                StringBuilder givens = new(curBoard.Item1);
                for (int i = 0; i < givens.Length; i++)
                {
                    if (givens[i] < '1' || givens[i] > '9')
                    {
                        for (char v = '1'; v <= '9'; v++)
                        {
                            if (curBoard.Item2[i] == v)
                            {
                                continue;
                            }

                            try
                            {
                                givens[i] = v;
                                Solver testSolver = SolverFactory.CreateFromGivens(givens.ToString());
                                goto do_test;
                            }
                            catch (ArgumentException)
                            {
                            }
                        }
                        break;
                    }
                }

                do_test:
                Solver solver = SolverFactory.CreateFromGivens(givens.ToString());
                solver.TestInvalidSolution();
            }
        }

        [TestMethod]
        public void SolveMultiSolutionClassicGivens()
        {
            // Don't do all 100 as that takes too long.
            for (int p = 0; p < Puzzles.uniqueClassics.Length / 4; p++)
            {
                var curBoard = Puzzles.uniqueClassics[p];
                StringBuilder givens = new(curBoard.Item1);
                for (int i = 0; i < givens.Length; i++)
                {
                    if (givens[i] >= '1' && givens[i] <= '9')
                    {
                        givens[i] = '.';
                        break;
                    }
                }

                Solver solver = SolverFactory.CreateFromGivens(givens.ToString());
                solver.TestMultipleSolution();
            }
        }

        [TestMethod]
        public void SolveUniqueClassicFPuzzles()
        {
            foreach (var curBoard in Puzzles.uniqueClassicFPuzzles)
            {
                Solver solver = SolverFactory.CreateFromFPuzzles(curBoard.Item1);
                solver.TestUniqueSolution(curBoard.Item2);
            }
        }

        [TestMethod]
        public void MiracleCount()
        {
            Solver solver = SolverFactory.CreateFromGivens(Puzzles.blankGrid, new string[]
            {
                "king",
                "knight",
                "difference:neg1",
            });
            Assert.AreEqual(72ul, solver.CountSolutions());
        }

        [TestMethod]
        public void MiracleLogicalSolve()
        {
            string solution = "483726159726159483159483726837261594261594837594837261372615948615948372948372615";

            Solver solver = SolverFactory.CreateFromGivens(Puzzles.blankGrid, new string[]
            {
                "king",
                "knight",
                "difference:neg1",
            });
            Assert.IsTrue(solver.SetValue(4, 2, 1));
            Assert.IsTrue(solver.SetValue(5, 6, 2));
            Assert.AreEqual(LogicResult.PuzzleComplete, solver.ConsolidateBoard());
            Assert.AreEqual(solution, solver.ToGivenString());
        }

        [TestMethod]
        public void SolveUniqueVariantFPuzzles()
        {
            foreach (var curBoard in Puzzles.uniqueVariantFPuzzles)
            {
                Solver solver = SolverFactory.CreateFromFPuzzles(curBoard.Item1);
                solver.TestUniqueSolution(curBoard.Item2);
            }
        }

        [TestMethod]
        public void SolveDiagonalNonconsecutive()
        {
            string solution = "572869431981432765634175829365781942819243657427956318246597183198324576753618294";
            Solver solver = SolverFactory.CreateFromGivens("500000000000000000004000000000080000010200000000956000000000080008304000000000290", new string[]
            {
                "dnc",
            });
            Assert.AreEqual(LogicResult.PuzzleComplete, solver.ConsolidateBoard());
            Assert.AreEqual(solution, solver.ToGivenString());
        }
    }
}
