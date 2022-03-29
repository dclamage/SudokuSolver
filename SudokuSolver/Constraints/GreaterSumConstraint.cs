using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Greater Sum", ConsoleName = "gtsum")]
    public class GreaterSumConstraint : Constraint
    {
        public readonly List<(int, int)> greatCells;
        public readonly List<(int, int)> smallCells;
        private readonly HashSet<(int, int)> allCells;
        private bool ok;

        public GreaterSumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 2)
            {
                throw new ArgumentException($"Greater sum constraint expects 2 cell groups, got {cellGroups.Count}.");
            }

            greatCells = cellGroups[0];
            smallCells = cellGroups[1];
            allCells = new(greatCells.Concat(smallCells));
        }

        public override string SpecificName => $"Greater sum {CellName(greatCells[0])} > {CellName(smallCells[0])}";

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            ok = false;
            return IsValid(sudokuSolver) ? LogicResult.None : LogicResult.Invalid;
        }

        private bool IsValid(Solver sudokuSolver)
        {
            return ok || MaxSumCells(sudokuSolver, greatCells) > MinSumCells(sudokuSolver, smallCells);
        }

        private static int MaxSumCells(Solver solver, List<(int, int)> cells)
        {
            var sum = 0;
            foreach ((int, int) cell in cells)
            {
                sum += SolverUtility.MaxValue(solver.Board[cell.Item1, cell.Item2]);
            }
            return sum;
        }

        private static int MinSumCells(Solver solver, List<(int, int)> cells)
        {
            var sum = 0;
            foreach ((int, int) cell in cells)
            {
                sum += SolverUtility.MinValue(solver.Board[cell.Item1, cell.Item2]);
            }
            return sum;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            return ok || !allCells.Contains((i, j)) || IsValid(sudokuSolver);
        }

        public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            if (ok)
            {
                return LogicResult.None;
            }

            var minSum = MinSumCells(solver, smallCells);
            var maxSum = MaxSumCells(solver, greatCells);

            if (maxSum <= minSum)
            {
                logicalStepDescription?.Append($"Sum of cells at {CellName(greatCells[0])} is not greater than the sum of cells at {CellName(smallCells[0])}");
                return LogicResult.Invalid;
            }

            if (CellsFilled(solver, greatCells) && CellsFilled(solver, smallCells))
            {
                ok = true;
                return LogicResult.None;
            }

            if (maxSum > minSum + solver.MAX_VALUE - 1) // Sums are not close enough to eliminate candidates, so chill
            {
                return LogicResult.None;
            }

            // Simple candidate removal
            var smallResult = DifferenceToMinCandidatesMustBeLessThan(solver, smallCells, maxSum - minSum);
            var greatResult = DifferenceToMaxCandidatesMustBeLessThan(solver, greatCells, maxSum - minSum);

            return smallResult > greatResult ? smallResult : greatResult;
        }

        private static LogicResult DifferenceToMinCandidatesMustBeLessThan(Solver solver, List<(int, int)> cells, int diff)
        {
            var changed = false;
            foreach ((int, int) cell in cells)
            {
                var mask = solver.Board[cell.Item1, cell.Item2];
                if (SolverUtility.IsValueSet(mask))
                {
                    continue;
                }

                var minValue = SolverUtility.MinValue(mask);
                // Remove all candidates larger than minValue + diff
                // 0000...0011...1
                //          ^^^^^^-- (minValue + diff + 1) ones, so minValue + diff is the highest allowed candidate
                var newMask = mask & ((1 << (minValue + diff + 1)) - 1);
                
                if (newMask == 0)
                {
                    return LogicResult.Invalid;
                }

                if (newMask != mask)
                {
                    solver.Board[cell.Item1, cell.Item2] = mask;
                    changed = true;
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private static LogicResult DifferenceToMaxCandidatesMustBeLessThan(Solver solver, List<(int, int)> cells, int diff)
        {
            var changed = false;
            foreach ((int, int) cell in cells)
            {
                var mask = solver.Board[cell.Item1, cell.Item2];
                if (SolverUtility.IsValueSet(mask))
                {
                    continue;
                }

                var maxValue = SolverUtility.MaxValue(mask);
                // Remove all candidates smaller than maxValue - diff
                // 1111...1100...0
                //          ^^^^^^-- (maxValue - diff - 1) zeroes, so maxValue - diff is the lowest allowed candidate
                var newMask = mask & ~((1 << (maxValue - diff - 1)) - 1); 

                if (newMask == 0)
                {
                    return LogicResult.Invalid;
                }

                if (newMask != mask)
                {
                    solver.Board[cell.Item1, cell.Item2] = mask;
                    changed = true;
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        private static bool CellsFilled(Solver solver, List<(int, int)> cells)
        {
            foreach ((int, int) cell in cells)
            {
                if (!SolverUtility.IsValueSet(solver.Board[cell.Item1, cell.Item2]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
