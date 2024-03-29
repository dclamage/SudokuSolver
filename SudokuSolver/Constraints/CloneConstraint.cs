﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Clone", ConsoleName = "clone")]
    public class CloneConstraint : Constraint
    {
        public readonly List<((int, int), (int, int))> cellPairs = new();
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

                cellPairs.Add((cell0, cell1));
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
                    }
                }
            }
            return LogicResult.None;
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
                        logicalStepDescription?.Append($"{CellName(i0, j0)} has value {GetValue(cellMask0)} but its clone at {CellName(i1, j1)} has value {GetValue(cellMask1)}");
                        return LogicResult.Invalid;
                    }

                    if (cellSet0)
                    {
                        if (!sudokuSolver.SetValue(i1, j1, GetValue(cellMask0)))
                        {
                            logicalStepDescription?.Append($"{CellName(i0, j0)} has value {GetValue(cellMask0)} but its clone at {CellName(i1, j1)} cannot have this value.");
                            return LogicResult.Invalid;
                        }
                        logicalStepDescription?.Append($"{CellName(i0, j0)} with value {GetValue(cellMask0)} is cloned into {CellName(i1, j1)}");
                        return LogicResult.Changed;
                    }
                    else if (cellSet1)
                    {
                        if (!sudokuSolver.SetValue(i0, j0, GetValue(cellMask1)))
                        {
                            logicalStepDescription?.Append($"{CellName(i1, j1)} has value {GetValue(cellMask1)} but its clone at {CellName(i0, j0)} cannot have this value.");
                            return LogicResult.Invalid;
                        }
                        logicalStepDescription?.Append($"{CellName(i1, j1)} with value {GetValue(cellMask1)} is cloned into {CellName(i0, j0)}");
                        return LogicResult.Changed;
                    }
                    else
                    {
                        uint combinedMask = cellMask0 & cellMask1;
                        if (combinedMask == 0)
                        {
                            logicalStepDescription?.Append($"No value can go into both {CellName(i0, j0)} with candidates {MaskToString(cellMask0)} and its clone at {CellName(i1, j1)} with candidates {MaskToString(cellMask1)}.");
                            return LogicResult.Invalid;
                        }

                        uint removed0 = (cellMask0 & ~combinedMask);
                        uint removed1 = (cellMask1 & ~combinedMask);
                        if (removed0 != 0 || removed1 != 0)
                        {
                            if (logicalStepDescription != null)
                            {
                                if (removed0 != 0)
                                {
                                    logicalStepDescription.Append($"Candidate {MaskToString(removed0)} removed from {CellName(i0, j0)} (not in {CellName(i1, j1)})");
                                }
                                if (removed1 != 0)
                                {
                                    if (removed0 == 0)
                                    {
                                        logicalStepDescription.Append($"Candidate ");
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($"; ");
                                    }
                                    logicalStepDescription.Append($"{MaskToString(removed1)} removed from {CellName(i1, j1)} (not in {CellName(i0, j0)})");
                                }
                            }
                            if (!sudokuSolver.SetMask(i0, j0, combinedMask))
                            {
                                return LogicResult.Invalid;
                            }
                            if (!sudokuSolver.SetMask(i1, j1, combinedMask))
                            {
                                return LogicResult.Invalid;
                            }
                            return LogicResult.Changed;
                        }
                    }
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }
    }
}
