using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Quadruple", ConsoleName = "quad", FPuzzlesName = "quadruple")]
    public class QuadrupleConstraint : Constraint
    {
        private readonly List<(int, int)> cells = null;
        private readonly HashSet<(int, int)> cellsLookup;
        private readonly uint requiredMask = 0;
        private readonly int numRequiredValues;
        private bool isGroup = false;

        public override string SpecificName => $"Quadruple at {CellName(cells[0])}";

        public override List<(int, int)> Group => isGroup ? cells : null;

        public QuadrupleConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            foreach (var group in options.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(group, out int value))
                {
                    requiredMask |= ValueMask(value);
                }
                else
                {
                    var cellGroups = ParseCells(group);
                    if (cells != null)
                    {
                        throw new ArgumentException($"Quadruple constraint expects only one cell group.");
                    }
                    cells = cellGroups[0];
                }
            }

            if (cells == null)
            {
                throw new ArgumentException($"Quadruple constraint expects a cell group.");
            }

            numRequiredValues = ValueCount(requiredMask);
            cellsLookup = new(cells);
        }

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            if (cells == null || requiredMask == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            int numCellsSupportingValues = 0;
            uint availableMask = 0;
            foreach (var (i, j) in cells)
            {
                uint cellMask = board[i, j];
                if ((cellMask & requiredMask) != 0)
                {
                    numCellsSupportingValues++;
                }
                availableMask |= cellMask;
            }

            if ((availableMask & requiredMask) != requiredMask)
            {
                return LogicResult.Invalid;
            }

            if (numCellsSupportingValues < numRequiredValues)
            {
                return LogicResult.Invalid;
            }

            bool changed = false;
            if (numCellsSupportingValues == numRequiredValues)
            {
                foreach (var (i, j) in cells)
                {
                    uint clearMask = ~requiredMask & ALL_VALUES_MASK;
                    var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
                    if (clearResult == LogicResult.Invalid)
                    {
                        return LogicResult.Invalid;
                    }
                    changed |= clearResult == LogicResult.Changed;
                }
                isGroup = true;
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (cells == null || requiredMask == 0)
            {
                return true;
            }

            var board = sudokuSolver.Board;
            if (cellsLookup.Contains((i, j)))
            {
                uint availableMask = 0;
                foreach (var cell in cells)
                {
                    availableMask |= board[cell.Item1, cell.Item2];
                }

                if ((availableMask & requiredMask) != requiredMask)
                {
                    return false;
                }
            }

            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            if (cells == null || requiredMask == 0)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;

            uint remainingRequiredMask = requiredMask;
            foreach (var (i, j) in cells)
            {
                uint cellMask = board[i, j];
                if (IsValueSet(cellMask))
                {
                    remainingRequiredMask &= ~cellMask;
                }
            }
            if (remainingRequiredMask == 0)
            {
                return LogicResult.None;
            }
            int numRemainingRequired = ValueCount(remainingRequiredMask);

            int numCellsSupportingValues = 0;
            uint availableMask = 0;
            foreach (var (i, j) in cells)
            {
                uint cellMask = board[i, j];
                if (IsValueSet(cellMask))
                {
                    continue;
                }

                if ((cellMask & remainingRequiredMask) != 0)
                {
                    numCellsSupportingValues++;
                }
                availableMask |= cellMask;
            }

            if ((availableMask & remainingRequiredMask) != remainingRequiredMask)
            {
                logicalStepDescription?.Append($"Can no longer fulfill all required values.");
                return LogicResult.Invalid;
            }

            if (numCellsSupportingValues < numRemainingRequired)
            {
                logicalStepDescription?.Append($"Can no longer fulfill all required values.");
                return LogicResult.Invalid;
            }

            if (numCellsSupportingValues == numRemainingRequired)
            {
                bool changed = false;
                foreach (var (i, j) in cells)
                {
                    uint cellMask = board[i, j];
                    if (!IsValueSet(cellMask) && (cellMask & remainingRequiredMask) != 0)
                    {
                        var result = sudokuSolver.ClearMask(i, j, ~remainingRequiredMask);
                        if (result == LogicResult.Invalid)
                        {
                            if (logicalStepDescription != null)
                            {
                                logicalStepDescription.Clear();
                                logicalStepDescription.Append($"{CellName(i, j)} must be one of the remaining quadruple values {MaskToString(remainingRequiredMask)} but it cannot be those values.");
                            }
                            return LogicResult.Invalid;
                        }

                        if (result == LogicResult.Changed)
                        {
                            if (logicalStepDescription != null)
                            {
                                if (changed)
                                {
                                    logicalStepDescription.Append($"The remaining value{(numRemainingRequired != 1 ? "s" : "")} {MaskToString(remainingRequiredMask)} must be in {CellName(i, j)}");
                                }
                                else
                                {
                                    logicalStepDescription.Append($", {CellName(i, j)}");
                                }
                            }
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    return LogicResult.Changed;
                }
            }

            // Check if only one cell can fulfill a value (hidden single, essentially)
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint valueMask = ValueMask(v);
                if ((remainingRequiredMask & valueMask) == 0)
                {
                    continue;
                }

                int numCellsAvailable = 0;
                (int, int) setCell = (-1, -1);
                foreach (var (i, j) in cells)
                {
                    uint cellMask = board[i, j];
                    if (!IsValueSet(cellMask) && (cellMask & valueMask) != 0)
                    {
                        numCellsAvailable++;
                        setCell = (i, j);
                    }
                }

                if (numCellsAvailable == 1)
                {
                    if (!sudokuSolver.SetValue(setCell.Item1, setCell.Item2, v))
                    {
                        logicalStepDescription?.Append($"{CellName(setCell)} is the only cell that can be the quadruple value {v} but it cannot be set to this value.");
                        return LogicResult.Invalid;
                    }

                    logicalStepDescription?.Append($"{CellName(setCell)} is the only cell that can be the quadruple value {v} and so it must be that value.");
                    return LogicResult.Changed;
                }
            }

            // TODO: Can also look for hidden tuples

            return LogicResult.None;
        }
    }
}
