using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Thermometer", ConsoleName ="thermo", FPuzzlesName = "thermometer")]
    public class ThermometerConstraint : Constraint
    {
        public readonly List<(int, int)> cells;
        private readonly HashSet<(int, int)> cellsSet;

        public ThermometerConstraint(string options)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 1)
            {
                throw new ArgumentException($"Thermometer constraint expects 1 cell group, got {cellGroups.Count}.");
            }

            cells = cellGroups[0];
            cellsSet = new(cells);
        }

        public override string SpecificName => $"Thermometer at {cells[0]}";

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            if (cells.Count == 0)
            {
                return LogicResult.None;
            }

            bool changed = false;
            var (firsti, firstj) = cells[0];
            var (lasti, lastj) = cells[^1];
            uint firstMask = sudokuSolver.Board[firsti, firstj];
            uint lastMask = sudokuSolver.Board[lasti, lastj];
            int minVal = MinValue(firstMask & ~valueSetMask);
            int maxVal = MaxValue(lastMask & ~valueSetMask) - cells.Count + 1;
            uint clearMask = ALL_VALUES_MASK;
            for (int val = minVal; val <= maxVal; val++)
            {
                clearMask &= ~ValueMask(val);
            }
            foreach (var (i, j) in cells)
            {
                var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= clearResult == LogicResult.Changed;
                clearMask = (clearMask << 1) | 1u;
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (cells.Count == 0)
            {
                return true;
            }

            if (cellsSet.Contains((i, j)))
            {
                var board = sudokuSolver.Board;
                for (int ti = 0; ti < cells.Count - 1; ti++)
                {
                    var curCell = cells[ti];
                    var nextCell = cells[ti + 1];
                    uint curMask = board[curCell.Item1, curCell.Item2];
                    uint nextMask = board[nextCell.Item1, nextCell.Item2];
                    bool curValueSet = IsValueSet(curMask);
                    bool nextValueSet = IsValueSet(nextMask);

                    int clearNextValStart = curValueSet ? GetValue(curMask) : MinValue(curMask);
                    for (int clearVal = clearNextValStart; clearVal > 0; clearVal--)
                    {
                        if (!sudokuSolver.ClearValue(nextCell.Item1, nextCell.Item2, clearVal))
                        {
                            return false;
                        }
                    }

                    int clearCurValStart = nextValueSet ? GetValue(nextMask) : MaxValue(nextMask);
                    for (int clearVal = clearCurValStart; clearVal <= MAX_VALUE; clearVal++)
                    {
                        if (!sudokuSolver.ClearValue(curCell.Item1, curCell.Item2, clearVal))
                        {
                            return false;
                        }
                    }
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
            for (int ti = 0; ti < cells.Count - 1; ti++)
            {
                var curCell = cells[ti];
                var nextCell = cells[ti + 1];
                uint curMask = board[curCell.Item1, curCell.Item2];
                uint nextMask = board[nextCell.Item1, nextCell.Item2];
                bool curValueSet = IsValueSet(curMask);
                bool nextValueSet = IsValueSet(nextMask);

                int clearNextValStart = curValueSet ? GetValue(curMask) : MinValue(curMask);
                uint clearMask = 0;
                for (int clearVal = clearNextValStart; clearVal > 0; clearVal--)
                {
                    clearMask |= ValueMask(clearVal);
                }
                LogicResult clearResult = sudokuSolver.ClearMask(nextCell.Item1, nextCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    logicalStepDescription.AppendLine($"Cleared values {MaskToString(clearMask)} from {CellName(nextCell)}");
                    changed = true;
                }

                int clearCurValStart = nextValueSet ? GetValue(nextMask) : MaxValue(nextMask);
                clearMask = 0;
                for (int clearVal = clearCurValStart; clearVal <= MAX_VALUE; clearVal++)
                {
                    clearMask |= ValueMask(clearVal);
                }
                clearResult = sudokuSolver.ClearMask(curCell.Item1, curCell.Item2, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (clearResult == LogicResult.Changed)
                {
                    logicalStepDescription.AppendLine($"Cleared values {MaskToString(clearMask)} from {CellName(curCell)}");
                    changed = true;
                }
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override List<(int, int)> Group => cells;
    }
}
