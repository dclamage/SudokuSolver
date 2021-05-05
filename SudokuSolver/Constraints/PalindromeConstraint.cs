using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Palindrome", ConsoleName = "palindrome", FPuzzlesName = "palindrome")]
    public class PalindromeConstraint : Constraint
    {
        public readonly List<(int, int)> cells;
        private readonly Dictionary<(int, int), (int, int)> cellToClone;

        public PalindromeConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 1)
            {
                throw new ArgumentException($"Palindrome constraint expects 1 cell group, got {cellGroups.Count}.");
            }

            cells = cellGroups[0];
            cellToClone = new(cells.Count);
            for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
            {
                var cell0 = cells[cellIndex];
                var cell1 = cells[^(cellIndex + 1)];
                cellToClone[cell0] = cell1;
                cellToClone[cell1] = cell0;
            }
        }

        public override string SpecificName => $"Palindrome at {cells[0]}";

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            if (cells.Count == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            bool changed = false;
            for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
            {
                var (i0, j0) = cells[cellIndex];
                var (i1, j1) = cells[^(cellIndex + 1)];
                if (sudokuSolver.SeenCells((i0, j0)).Contains((i1, j1)))
                {
                    return LogicResult.Invalid;
                }

                uint cellMask0 = board[i0, j0];
                uint cellMask1 = board[i1, j1];
                if (cellMask0 != cellMask1)
                {
                    bool cellSet0 = IsValueSet(cellMask0);
                    bool cellSet1 = IsValueSet(cellMask1);
                    if (cellSet0 && cellSet1)
                    {
                        return LogicResult.Invalid;
                    }
                    if (cellSet0)
                    {
                        if (!sudokuSolver.SetValue(i1, j1, GetValue(cellMask0)))
                        {
                            return LogicResult.Invalid;
                        }
                    }
                    else if (cellSet1)
                    {
                        if (!sudokuSolver.SetValue(i0, j0, GetValue(cellMask1)))
                        {
                            return LogicResult.Invalid;
                        }
                    }
                    else
                    {
                        uint combinedMask = cellMask0 & cellMask1;
                        if (combinedMask == 0)
                        {
                            return LogicResult.Invalid;
                        }
                        if (!sudokuSolver.SetMask(i0, j0, combinedMask))
                        {
                            return LogicResult.Invalid;
                        }
                        if (!sudokuSolver.SetMask(i1, j1, combinedMask))
                        {
                            return LogicResult.Invalid;
                        }

                    }

                    changed = true;
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (cells.Count == 0)
            {
                return true;
            }

            (int, int) cloneCell;
            if (cellToClone.TryGetValue((i, j), out cloneCell))
            {
                uint clearMask = ALL_VALUES_MASK & ~ValueMask(val);
                var clearResult = sudokuSolver.ClearMask(cloneCell.Item1, cloneCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return false;
                }
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            if (cells.Count == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            bool changed = false;
            for (int cellIndex = 0; cellIndex < cells.Count / 2; cellIndex++)
            {
                var (i0, j0) = cells[cellIndex];
                var (i1, j1) = cells[^(cellIndex + 1)];
                uint cellMask0 = board[i0, j0];
                uint cellMask1 = board[i1, j1];
                if (cellMask0 == cellMask1)
                {
                    continue;
                }

                bool cellSet0 = IsValueSet(cellMask0);
                bool cellSet1 = IsValueSet(cellMask1);
                if (cellSet0 && cellSet1)
                {
                    return LogicResult.Invalid;
                }

                if (cellSet0)
                {
                    if (!sudokuSolver.SetValue(i1, j1, GetValue(cellMask0)))
                    {
                        return LogicResult.Invalid;
                    }
                }
                else if (cellSet1)
                {
                    if (!sudokuSolver.SetValue(i0, j0, GetValue(cellMask1)))
                    {
                        return LogicResult.Invalid;
                    }
                }
                else
                {
                    uint combinedMask = cellMask0 & cellMask1;
                    if (combinedMask == 0)
                    {
                        return LogicResult.Invalid;
                    }
                    if (!sudokuSolver.SetMask(i0, j0, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }
                    if (!sudokuSolver.SetMask(i1, j1, combinedMask))
                    {
                        return LogicResult.Invalid;
                    }
                }

                changed = true;
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }
    }
}
