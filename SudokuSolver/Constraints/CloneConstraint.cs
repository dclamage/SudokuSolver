using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Clone", ConsoleName = "clone", FPuzzlesName = "clone")]
    public class CloneConstraint : Constraint
    {
        private readonly Dictionary<(int, int), List<(int, int)>> cellToClones = new();

        public CloneConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count == 0)
            {
                throw new ArgumentException($"Clone constraint expects at least 1 cell group.");
            }

            foreach (var group in cellGroups)
            {
                if (group.Count != 2)
                {
                    throw new ArgumentException($"Clone cell groups should have exactly 2 cells ({group.Count} in group).");
                }
                var cell0 = group[0];
                var cell1 = group[1];
                if (cell0 == cell1)
                {
                    throw new ArgumentException($"Clone cells need to be distinct ({CellName(cell0)}).");
                }

                cellToClones.AddToList(cell0, cell1);
                cellToClones.AddToList(cell1, cell0);
            }
        }

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            if (cellToClones.Count == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            bool changed = false;
            foreach (var (cell0, cellList) in cellToClones)
            {
                var (i0, j0) = cell0;
                foreach (var cell1 in cellList)
                {
                    var (i1, j1) = cell1;
                    if (sudokuSolver.SeenCells(cell0).Contains(cell1))
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
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (cellToClones.Count == 0)
            {
                return true;
            }

            if (cellToClones.TryGetValue((i, j), out var cloneCellList))
            {
                foreach (var cloneCell in cloneCellList)
                {
                    uint clearMask = ALL_VALUES_MASK & ~ValueMask(val);
                    var clearResult = sudokuSolver.ClearMask(cloneCell.Item1, cloneCell.Item2, clearMask);
                    if (clearResult == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            if (cellToClones.Count == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            bool changed = false;
            foreach (var (cell0, cellList) in cellToClones)
            {
                var (i0, j0) = cell0;
                foreach (var cell1 in cellList)
                {
                    var (i1, j1) = cell1;
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
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }
    }
}
