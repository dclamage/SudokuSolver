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
        private readonly SumCellsHelper greatCellsSumHelper;
        private readonly SumCellsHelper smallCellsSumHelper;

        private readonly HashSet<(int, int)> allCells;

        public GreaterSumConstraint(Solver solver, string options) : base(solver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 2)
            {
                throw new ArgumentException($"Greater sum constraint expects 2 cell groups, got {cellGroups.Count}.");
            }

            greatCells = cellGroups[0];
            smallCells = cellGroups[1];
            greatCellsSumHelper = new(solver, greatCells);
            smallCellsSumHelper = new(solver, smallCells);

            allCells = new(greatCells.Concat(smallCells));
        }

        public override string SpecificName => $"Greater sum {CellName(greatCells[0])} > {CellName(smallCells[0])}";

        public override LogicResult InitCandidates(Solver solver)
        {
            var greatResult = greatCellsSumHelper.Init(solver, AtLeast(greatCells.Count, greatCells.Count, MAX_VALUE));
            var smallResult = smallCellsSumHelper.Init(solver, AtLeast(smallCells.Count, smallCells.Count, MAX_VALUE));

            return !IsValid(solver) ? LogicResult.Invalid : greatResult > smallResult ? greatResult : smallResult;
        }

        private bool IsValid(Solver solver)
        {
            var maxGreat = MaxGreat(solver);
            var minSmall = MinSmall(solver);
            return maxGreat != 0 && minSmall != 0 && maxGreat > minSmall;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int MinSmall(Solver solver)
        {
            return smallCellsSumHelper.SumRange(solver).Item1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int MaxGreat(Solver solver)
        {
            return greatCellsSumHelper.SumRange(solver).Item2;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            return !allCells.Contains((i, j)) || IsValid(sudokuSolver);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<int> AtMost(int maxValue, int nCells)
        {
            return Enumerable.Range(nCells, maxValue - nCells + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<int> AtLeast(int minValue, int nCells, int MAX_VALUE)
        {
            return Enumerable.Range(minValue, nCells * MAX_VALUE - minValue + 1);
        }

        public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var minSum = MinSmall(solver);
            var maxSum = MaxGreat(solver);

            if (maxSum <= minSum)
            {
                logicalStepDescription?.Append($"Sum of cells at {CellName(greatCells[0])} can not be greater than the sum of cells at {CellName(smallCells[0])}");
                return LogicResult.Invalid;
            }

            if (CellsFilled(solver, greatCells) && CellsFilled(solver, smallCells))
            {
                return LogicResult.None;
            }

            var greatSumResult = greatCellsSumHelper.StepLogic(solver, AtLeast(minSum, greatCells.Count, MAX_VALUE), logicalStepDescription);
            if (greatSumResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }

            var smallSumResult = smallCellsSumHelper.StepLogic(solver, AtMost(maxSum, smallCells.Count), logicalStepDescription);
            return smallSumResult > greatSumResult ? smallSumResult : greatSumResult;
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
