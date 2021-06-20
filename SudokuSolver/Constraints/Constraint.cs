using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    public abstract class Constraint
    {
        protected readonly int WIDTH;
        protected readonly int HEIGHT;
        protected readonly int MAX_VALUE;
        protected readonly uint ALL_VALUES_MASK;
        protected readonly int NUM_CELLS;

        public Constraint(Solver sudokuSolver)
        {
            WIDTH = sudokuSolver.WIDTH;
            HEIGHT = sudokuSolver.HEIGHT;
            MAX_VALUE = sudokuSolver.MAX_VALUE;
            ALL_VALUES_MASK = sudokuSolver.ALL_VALUES_MASK;
            NUM_CELLS = sudokuSolver.NUM_CELLS;
        }

        /// <summary>
        /// Gets the name from the Constraint attribute
        /// </summary>
        public string Name => (Attribute.GetCustomAttribute(GetType(), typeof(ConstraintAttribute)) as ConstraintAttribute)?.DisplayName ?? GetType().Name;

        /// <summary>
        /// Override if there is a more specific name for this constraint instance, such as "Killer Cage at r1c1".
        /// </summary>
        public virtual string SpecificName => Name;

        /// <summary>
        /// Return an enumerable of cells which cannot be the same digit as this cell.
        /// Only need to return cells which wouldn't be seen by normal sudoku rules.
        /// Also no need to return any cells if the Group property is used.
        /// </summary>
        /// <param name="cell">The cell which is seeing other cells.</param>
        /// <returns>All cells which are seen by this cell.</returns>
        public virtual IEnumerable<(int, int)> SeenCells((int, int) cell) => Enumerable.Empty<(int, int)>();

        /// <summary>
        /// Return an enumerable of cells which cannot be the same digit as this cell when
        /// this cell is set to a specific value.
        /// Only need to return cells which wouldn't be seen by normal sudoku rules.
        /// </summary>
        /// <param name="cell">The cell which is seeing other cells.</param>
        /// <param name="mask">The value mask to consider for the cell.</param>
        /// <returns>All cells which are seen by this cell.</returns>
        public virtual IEnumerable<(int, int)> SeenCellsByValueMask((int, int) cell, uint mask) => SeenCells(cell);

        /// <summary>
        /// Called once all constraints are finalized on the board.
        /// This is the initial opportunity to remove candidates from the empty board before any values are set to it.
        /// For example, a two cell killer cage with sum of 10 might remove the 9 candidate from its two cells.
        /// Each constraint gets a round of inits until all of them return LogicResult.None.
        /// </summary>
        /// <returns>
        /// LogicResult.None: Board is unchanged.
        /// LogicResult.Changed: Board is changed.
        /// LogicResult.Invalid: This constraint has made the solve impossible.
        /// LogicResult.PuzzleComplete: Avoid returning this. It is used internally by the solver.
        /// </returns>
        public virtual LogicResult InitCandidates(Solver sudokuSolver) { return LogicResult.None; }

        /// <summary>
        /// Called when a value has just been set on the board.
        /// The job of this function is twofold:
        ///   1) Remove candidates from any other cells that are no longer possible because this value was set.
        ///   2) Determine if setting this value is a simple rules violation.
        ///   
        /// Avoid complex logic in this function. Just enforcement of the direct, actual rule is advised.
        /// 
        /// There is no need to specifically enforce distinct digits in groups as long as the Groups property is provided.
        /// By the time this function is called, group distinctness will already have been enforced.
        /// 
        /// For example, a nonconsecutive constraint would remove the consecutive candidates from the
        /// cells adjacent to [i,j] and return false if any of those cells end up with no candidates left.
        /// </summary>
        /// <param name="sudokuSolver">The main Sudoku solver.</param>
        /// <param name="i">The row index (0-8)</param>
        /// <param name="j">The col index (0-8)</param>
        /// <param name="val">The value which has been set in the cell (1-9)</param>
        /// <returns>True if the board is still valid; false otherwise.</returns>
        public abstract bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val);

        /// <summary>
        /// Called during logical solving.
        /// Go through the board and perform a single step of logic related to this constraint.
        /// For example, a nonconsecutive constraint might look for a cell with only two consecutive
        /// candidates left and eliminate those candidates from all adjacent cells.
        /// 
        /// Use your judgement and testing to determine if any of the logic should occur during brute force
        /// solving. The brute force solving boolean is set to true when this logic is not going to be
        /// visible to the end-user and so anything done during brute forcing is only advised if it's faster
        /// than guessing.
        /// 
        /// Do not attempt to do any logic which isn't relevant to this constraint.
        /// </summary>
        /// <param name="sudokuSolver">The Sudoku board.</param>
        /// <param name="logicalStepDescription">If not null and a logical step is found, store a human-readable description of what was performed here.</param>
        /// <param name="isBruteForcing">Whether the solver is currently brute forcing a solution.</param>
        /// <returns>
        /// LogicResult.None: No logic found.
        /// LogicResult.Changed: Logic found which changed the board.
        /// LogicResult.Invalid: Your logic has determined that there are no solutions (such as when removing the last candidate from a cell).
        /// LogicResult.PuzzleComplete: Avoid returning this. It is used internally by the solver.
        /// </returns>
        public abstract LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing);

        /// <summary>
        /// Provide a lists of cells in which all digits must be distinct.
        /// For example, a killer cage would provide all its cells.
        /// A little killer clue would provide nothing, as it does not enforce distinctness.
        /// The list contents are expected to remain the same over the lifetime of the object.
        /// </summary>
        public virtual List<(int, int)> Group => null;

        /// <summary>
        /// Returns a list of cells which must contain the given value.
        /// </summary>
        /// <param name="sudokuSolver">The solver.</param>
        /// <param name="value">The value which must by contained</param>
        /// <returns>A list of cells which must contain that value, or null if none.</returns>
        public virtual List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => null;

        /// <summary>
        /// Useful for constraints that just need to enforce seen cells.
        /// </summary>
        /// <param name="sudokuSolver"></param>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="val"></param>
        /// <returns></returns>
        protected bool EnforceConstraintBasedOnSeenCells(Solver sudokuSolver, int i, int j, int val)
        {
            foreach (var cell in SeenCells((i, j)))
            {
                if (!sudokuSolver.ClearValue(cell.Item1, cell.Item2, val))
                {
                    return false;
                }
            }
            return true;
        }

        public List<List<(int, int)>> ParseCells(string cellString)
        {
            List<List<(int, int)>> cellGroups = new();
            foreach (string cellGroup in cellString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (cellGroup.Length < 4 || (cellGroup[0] != 'r' && cellGroup[0] != 'R'))
                {
                    throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                }

                List<(int, int)> cells = new();

                List<int> rows = new();
                List<int> cols = new();
                bool addingRows = true;
                bool valueStart = true;
                bool lastAddedDirections = false;
                int curValStart = 0;
                int curValEnd = 0;
                for (int i = 1; i < cellGroup.Length; i++)
                {
                    lastAddedDirections = false;

                    char curChar = cellGroup[i];
                    if (curChar == 'r' || curChar == 'R')
                    {
                        if (addingRows || !AddCells(cols, curValStart, curValEnd))
                        {
                            throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                        }
                        AddCells(cells, rows, cols);
                        rows.Clear();
                        cols.Clear();
                        addingRows = true;
                        valueStart = true;
                        curValStart = 0;
                        curValEnd = 0;
                    }
                    else if (curChar == 'c' || curChar == 'C')
                    {
                        if (!addingRows || !AddCells(rows, curValStart, curValEnd) || !AddCells(cells, rows, cols))
                        {
                            throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                        }
                        addingRows = false;
                        valueStart = true;
                        curValStart = 0;
                        curValEnd = 0;
                    }
                    else if (curChar == 'd' || curChar == 'D')
                    {
                        if (addingRows || !AddCells(cols, curValStart, curValEnd) || !AddCells(cells, rows, cols))
                        {
                            throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                        }
                        rows.Clear();
                        cols.Clear();
                        addingRows = true;
                        valueStart = true;
                        curValStart = 0;
                        curValEnd = 0;

                        i++;
                        bool complete = false;
                        while (i < cellGroup.Length && !complete)
                        {
                            var (r, c) = cells[^1];
                            char dirChar = cellGroup[i];
                            (int, int) toAdd = (r, c);
                            switch (dirChar)
                            {
                                case '1':
                                    toAdd = (r + 1, c - 1);
                                    break;
                                case '2':
                                    toAdd = (r + 1, c);
                                    break;
                                case '3':
                                    toAdd = (r + 1, c + 1);
                                    break;
                                case '4':
                                    toAdd = (r, c - 1);
                                    break;
                                case '5':
                                    toAdd = (r, c);
                                    break;
                                case '6':
                                    toAdd = (r, c + 1);
                                    break;
                                case '7':
                                    toAdd = (r - 1, c - 1);
                                    break;
                                case '8':
                                    toAdd = (r - 1, c);
                                    break;
                                case '9':
                                    toAdd = (r - 1, c + 1);
                                    break;
                                case 'r':
                                case 'R':
                                    complete = true;
                                    break;
                                default:
                                    throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                            }
                            if (toAdd.Item1 <= 0 || toAdd.Item2 <= 0 || toAdd.Item1 > HEIGHT || toAdd.Item2 > WIDTH)
                            {
                                throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                            }
                            cells.Add(toAdd);
                            lastAddedDirections = true;
                            i++;
                        }
                        i--;
                    }
                    else if (curChar >= '0' && curChar <= '9')
                    {
                        if (valueStart)
                        {
                            curValStart = curValStart * 10 + (curChar - '0');
                        }
                        else
                        {
                            curValEnd = curValEnd * 10 + (curChar - '0');
                        }
                    }
                    else if (curChar == '-')
                    {
                        if (!valueStart)
                        {
                            throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                        }
                        valueStart = false;
                    }
                    else if (curChar == ',')
                    {
                        if (!AddCells(addingRows ? rows : cols, curValStart, curValEnd))
                        {
                            throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                        }
                        valueStart = true;
                        curValStart = 0;
                        curValEnd = 0;
                    }
                }

                if (!lastAddedDirections)
                {
                    if (addingRows || !AddCells(cols, curValStart, curValEnd) || !AddCells(cells, rows, cols) || cells.Count == 0)
                    {
                        throw new ArgumentException($"{cellGroup} is not a valid cell specifier.");
                    }
                }
                cellGroups.Add(cells.Select(c => (c.Item1 - 1, c.Item2 - 1)).ToList());
            }
            return cellGroups;
        }

        private static bool AddCells(List<int> list, int start, int end)
        {
            if (start == 0)
            {
                return false;
            }

            if (end == 0)
            {
                list.Add(start);
            }
            else
            {
                if (end < start)
                {
                    (start, end) = (end, start);
                }

                for (int i = start; i <= end; i++)
                {
                    list.Add(i);
                }
            }
            return true;
        }

        private bool AddCells(List<(int, int)> list, List<int> rows, List<int> cols)
        {
            foreach (int r in rows)
            {
                foreach (int c in cols)
                {
                    if (r <= 0 || c <= 0 || r > HEIGHT || c > WIDTH)
                    {
                        return false;
                    }
                    list.Add((r, c));
                }
            }
            return true;
        }

        protected IEnumerable<(int, int)> AdjacentCells(int i, int j)
        {
            if (i > 0)
            {
                yield return (i - 1, j);
            }
            if (i < HEIGHT - 1)
            {
                yield return (i + 1, j);
            }
            if (j > 0)
            {
                yield return (i, j - 1);
            }
            if (j < WIDTH - 1)
            {
                yield return (i, j + 1);
            }
        }

        protected IEnumerable<(int, int)> DiagonalCells(int i, int j)
        {
            if (i > 0 && j > 0)
            {
                yield return (i - 1, j - 1);
            }
            if (i < HEIGHT - 1 && j > 0)
            {
                yield return (i + 1, j - 1);
            }
            if (i > 0 && j < WIDTH - 1)
            {
                yield return (i - 1, j + 1);
            }
            if (i < HEIGHT - 1 && j < WIDTH - 1)
            {
                yield return (i + 1, j + 1);
            }
        }

        protected int FlatIndex((int, int) cell) => cell.Item1 * WIDTH + cell.Item2;
        protected (int, int, int, int) CellPair((int, int) cell0, (int, int) cell1)
        {
            return FlatIndex(cell0) <= FlatIndex(cell1) ? (cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2) : (cell1.Item1, cell1.Item2, cell0.Item1, cell0.Item2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskStrictlyLower(int v) => (1u << (v - 1)) - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskValAndLower(int v) => (1u << v) - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskStrictlyHigher(int v) => ALL_VALUES_MASK & ~((1u << v) - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskValAndHigher(int v) => ALL_VALUES_MASK & ~((1u << (v - 1)) - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskBetweenInclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskStrictlyLower(v0) | MaskStrictlyHigher(v1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint MaskBetweenExclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskValAndLower(v0) | MaskValAndHigher(v1));
    }

    /// <summary>
    /// Use to group multiple constraints together under one console or fpuzzles name.
    /// </summary>
    public interface IConstraintGroup
    {
        /// <summary>
        /// Add the multiple constraints to the given solver.
        /// </summary>
        /// <param name="solver"></param>
        void AddConstraints(Solver solver);
    }

    /// <summary>
    /// This attribute is required on all constraints to associate a name for instantiating the constraint.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConstraintAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public string ConsoleName { get; set; }
    }
}
