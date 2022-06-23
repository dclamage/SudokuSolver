using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Numbered Room", ConsoleName = "nr")]
    public class NumberedRoomConstraint : Constraint
    {

        public readonly int clue;
        public readonly (int, int) cellStart;
        private readonly uint clueMask;
        private readonly List<(int, int)> cells;
        private readonly (int, int) referenceCell;
        private readonly HashSet<(int, int)> cellsLookup;
        private string specificName;

        public override string SpecificName => specificName;

        private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
        public NumberedRoomConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var match = optionsRegex.Match(options);
            if (!match.Success)
            {
                throw new ArgumentException($"Numbered Room options \"{options}\" invalid. Expecting: \"SrXcY\"");
            }

            clue = int.Parse(match.Groups[1].Value);
            cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

            if (clue < 1 || (clue > MAX_VALUE))
            {
                throw new ArgumentException($"Numbered Room options \"{options}\" invalid. Clue must be between 1 and {MAX_VALUE}");
            }
            clueMask = ValueMask(clue);

            bool rowInGrid = 0 <= cellStart.Item1 && cellStart.Item1 < MAX_VALUE;
            bool colInGrid = 0 <= cellStart.Item2 && cellStart.Item2 < MAX_VALUE;

            bool isNorth = cellStart.Item1 == -1 && colInGrid;
            bool isEast = cellStart.Item2 == MAX_VALUE && rowInGrid;
            bool isSouth = cellStart.Item1 == MAX_VALUE && colInGrid;
            bool isWest = cellStart.Item2 == -1 && rowInGrid;

            if (!(isNorth || isEast || isSouth || isWest))
            {
                throw new ArgumentException($"Numbered Room options \"{options}\" has invalid location.");
            }

            cells = new();
            if (isNorth)
            {
                int j = cellStart.Item2;
                for (int i = 0; i < HEIGHT; i++)
                {
                    cells.Add((i, j));
                }
            }
            else if (isEast)
            {
                int i = cellStart.Item1;
                for (int j = HEIGHT - 1; j >= 0; j--)
                {
                    cells.Add((i, j));
                }
            }
            else if (isSouth)
            {
                int j = cellStart.Item2;
                for (int i = WIDTH - 1; i >= 0; i--)
                {
                    cells.Add((i, j));
                }
            }
            else if (isWest)
            {
                int i = cellStart.Item1;
                for (int j = 0; j < WIDTH; j++)
                {
                    cells.Add((i, j));
                }
            }
            cellsLookup = new(cells);
            referenceCell = cells[0];

            specificName = $"Numbered Room {clue} at {CellName(cellStart)}";
        }

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            if (clue == 1)
            {
                // A 1 clue can be placed and referenced everywhere, so we can't eliminate any digit.
                return LogicResult.None;
            }
            bool changed = false;

            // If the clue (N) is bigger than 1, than the N-th cell can not be N.
            (int, int) nthCell = cells[clue - 1];
            var clearResult = sudokuSolver.ClearMask(nthCell.Item1, nthCell.Item2, clueMask);
            if (clearResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= clearResult == LogicResult.Changed;

            // If the clue (N) is bigger than 1, than the 1st cell can not be 1 or N.
            uint clearMask = ValueMask(1) | clueMask;
            clearResult = sudokuSolver.ClearMask(referenceCell.Item1, referenceCell.Item2, clearMask);
            if (clearResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            changed |= clearResult == LogicResult.Changed;
            
            return changed? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            if (!cellsLookup.Contains((i, j)))
            {
                // The filled cell is not in the row or column of the given clue, so not interesting to us.
                return true;
            }
            int distance = cells.IndexOf((i, j));

            if (distance == 0)
            {
                // Found the reference cell, fill in the referenced cell
                return sudokuSolver.SetValue(cells[val-1].Item1,cells[val-1].Item2,clue);
            }
            else if (val==clue)
            {
                // Found the referenced cell, fill in the reference cell.
                return sudokuSolver.SetValue(referenceCell.Item1, referenceCell.Item2, distance + 1);
            }
            else
            {
                // The cell that has been set is not the referenced cell, remove it as candidate from the reference cell.
                return sudokuSolver.ClearValue(referenceCell.Item1, referenceCell.Item2, distance + 1);
            }
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            bool changed = false;
            // Remove clue as candidate from all cells that can not be referenced anymore.
            for (int i = 0; i < MAX_VALUE; i++)
            {
                if(!HasValue(sudokuSolver.Board[referenceCell.Item1,referenceCell.Item2],i+1))
                {
                    var logicResult = sudokuSolver.ClearMask(cells[i].Item1, cells[i].Item2, clueMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"{CellName(cells[i].Item1, cells[i].Item2)} has value {clue}, while {CellName(referenceCell.Item1, referenceCell.Item2)} can not be {i + 1} anymore.");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"{CellName(referenceCell.Item1, referenceCell.Item2)} can not be {i + 1}, removing {clue} from {CellName(cells[i].Item1, cells[i].Item2)}");
                        }
                        changed = true;
                    }
                }
            }

            // Remove each invalid reference as candidate from referenceCell.
            for (int i = 0; i < MAX_VALUE; i++)
            {
                uint potentialReferencedCell = sudokuSolver.Board[cells[i].Item1, cells[i].Item2];
                if (!HasValue(potentialReferencedCell, clue))
                {
                    var logicResult = sudokuSolver.ClearMask(referenceCell.Item1, referenceCell.Item2, ValueMask(i+1));
                    if (logicResult == LogicResult.Invalid)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"{CellName(referenceCell.Item1, referenceCell.Item2)} has value {i+1}, while {CellName(cells[i].Item1, cells[i].Item2)} can not be {clue} anymore.");
                        }
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        if (logicalStepDescription != null)
                        {
                            logicalStepDescription.Clear();
                            logicalStepDescription.Append($"{CellName(cells[i].Item1, cells[i].Item2)} can not be {clue}, removing {i+1} from {CellName(referenceCell.Item1, referenceCell.Item2)}");
                        }
                        changed = true;
                    }
                }
            }

            return changed ? LogicResult.Changed : LogicResult.None;
        }
    }
}
