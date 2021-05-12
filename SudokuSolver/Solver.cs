//#define PROFILING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SudokuSolver.Constraints;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver
{
    public record SudokuGroup(string Name, List<(int, int)> Cells)
    {
        public override string ToString() => Name;
    }

    public enum LogicResult
    {
        None,
        Changed,
        Invalid,
        PuzzleComplete
    }

    public class Solver
    {
#if PROFILING
        public static readonly Dictionary<string, Stopwatch> timers = new();
        public static void PrintTimers()
        {
            foreach (var timer in Solver.timers.OrderByDescending(timer => timer.Value.Elapsed))
            {
                if (timer.Key != "Global")
                {
                    Console.WriteLine($"{timer.Key}: {timer.Value.Elapsed.TotalMilliseconds}ms");
                }
            }
        }
#endif

        public readonly int WIDTH;
        public readonly int HEIGHT;
        public readonly int MAX_VALUE;
        public readonly uint ALL_VALUES_MASK;
        public readonly int NUM_CELLS;
        public readonly int[][][] combinations;

        public string Title { get; init; }
        public string Author { get; init; }
        public string Rules { get; init; }

        private uint[,] board;
        private int[,] regions = null;
        public uint[,] Board => board;
        public int[,] Regions => regions;
        // Returns whether two cells cannot be the same value for a specific value
        // i0, j0, i1, j0, value or 0 for any value
        private bool[,,,,] seenMap;
        private bool canHaveUnorthodoxTuples = false;
        public uint[] FlatBoard
        {
            get
            {
                uint[] flatBoard = new uint[NUM_CELLS];
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        flatBoard[i * WIDTH + j] = board[i, j];
                    }
                }
                return flatBoard;
            }
        }

        public string GivenString
        {
            get
            {
                var flatBoard = FlatBoard;
                int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
                string nonGiven = new('0', digitWidth);
                StringBuilder stringBuilder = new(flatBoard.Length);
                foreach (uint mask in flatBoard)
                {
                    if (IsValueSet(mask))
                    {
                        int v = GetValue(mask);
                        if (digitWidth == 2 && v <= 9)
                        {
                            stringBuilder.Append('0');
                        }
                        stringBuilder.Append(v);
                    }
                    else
                    {
                        stringBuilder.Append(nonGiven);
                    }
                }
                return stringBuilder.ToString();
            }
        }

        public string CandidateString
        {
            get
            {
                var flatBoard = FlatBoard;
                int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
                StringBuilder stringBuilder = new(flatBoard.Length);
                foreach (uint mask in flatBoard)
                {
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if ((mask & ValueMask(v)) != 0)
                        {
                            if (digitWidth == 2 && v <= 9)
                            {
                                stringBuilder.Append('0');
                            }
                            stringBuilder.Append(v);
                        }
                        else
                        {
                            stringBuilder.Append('.', digitWidth);
                        }
                    }
                }
                return stringBuilder.ToString();
            }
        }

        public string OutputString => IsComplete ? GivenString : CandidateString;

        private readonly List<Constraint> constraints;

        /// <summary>
        /// Groups which cannot contain more than one of the same digit.
        /// This will at least contain all rows, columns, and boxes.
        /// Will also contain any groups from constraints (such as killer cages).
        /// </summary>
        public List<SudokuGroup> Groups { get; }
        private List<SudokuGroup> smallGroupsBySize = null;

        /// <summary>
        /// Maps a cell to the list of groups which contain that cell.
        /// </summary>
        public Dictionary<(int, int), List<SudokuGroup>> CellToGroupMap { get; }

        /// <summary>
        /// Determines if the board has all values set.
        /// </summary>
        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < WIDTH; i++)
                {
                    for (int j = 0; j < HEIGHT; j++)
                    {
                        if (!IsValueSet(i, j))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public IEnumerable<T> Constraints<T>() where T : Constraint => constraints.Select(c => c as T).Where(c => c != null);

        private bool isBruteForcing = false;

        public Solver(int width, int height, int maxValue)
        {
            if (maxValue <= 0 || maxValue > 31)
            {
                throw new ArgumentException($"Unsupported max value of: {maxValue}");
            }

            WIDTH = width;
            HEIGHT = height;
            MAX_VALUE = maxValue;
            ALL_VALUES_MASK = (1u << MAX_VALUE) - 1;
            NUM_CELLS = width * height;
            combinations = new int[MAX_VALUE][][];
            InitCombinations();

            board = new uint[HEIGHT, WIDTH];
            constraints = new();

            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    board[i, j] = ALL_VALUES_MASK;
                }
            }
            Groups = new();
            CellToGroupMap = new();
        }

        public Solver(Solver other)
        {
            WIDTH = other.WIDTH;
            HEIGHT = other.HEIGHT;
            MAX_VALUE = other.MAX_VALUE;
            ALL_VALUES_MASK = other.ALL_VALUES_MASK;
            NUM_CELLS = other.NUM_CELLS;
            combinations = other.combinations;
            Title = other.Title;
            Author = other.Author;
            Rules = other.Rules;
            board = (uint[,])other.board.Clone();
            regions = other.regions;
            seenMap = other.seenMap;
            canHaveUnorthodoxTuples = other.canHaveUnorthodoxTuples;
            constraints = other.constraints;
            Groups = other.Groups;
            smallGroupsBySize = other.smallGroupsBySize;
            CellToGroupMap = other.CellToGroupMap;
        }

        private void InitCombinations()
        {
            for (int n = 1; n <= combinations.Length; n++)
            {
                combinations[n - 1] = new int[n][];
                for (int k = 1; k <= n; k++)
                {
                    int numCombinations = BinomialCoeff(n, k);
                    combinations[n - 1][k - 1] = new int[numCombinations * k];
                    FillCombinations(combinations[n - 1][k - 1], n, k);
                }
            }
        }

        private void InitStandardGroups()
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                List<(int, int)> cells = new(WIDTH);
                for (int j = 0; j < WIDTH; j++)
                {
                    cells.Add((i, j));
                }
                SudokuGroup group = new($"Row {i + 1}", cells);
                Groups.Add(group);
                InitMapForGroup(group);
            }

            // Add col groups
            for (int j = 0; j < WIDTH; j++)
            {
                List<(int, int)> cells = new(HEIGHT);
                for (int i = 0; i < HEIGHT; i++)
                {
                    cells.Add((i, j));
                }
                SudokuGroup group = new($"Column {j + 1}", cells);
                Groups.Add(group);
                InitMapForGroup(group);
            }

            // Add regions
            for (int region = 0; region < WIDTH; region++)
            {
                List<(int, int)> cells = new(WIDTH);
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        if (regions[i, j] == region)
                        {
                            cells.Add((i, j));
                        }
                    }
                }
                SudokuGroup group = new($"Region {region + 1}", cells);
                Groups.Add(group);
                InitMapForGroup(group);
            }
        }

        private void InitMapForGroup(SudokuGroup group)
        {
            foreach (var pair in group.Cells)
            {
                if (CellToGroupMap.TryGetValue(pair, out var value))
                {
                    value.Add(group);
                }
                else
                {
                    // Reserve 3 entries: row, col, and box
                    CellToGroupMap[pair] = new(3) { group };
                }
            }
        }

        /// <summary>
        /// Set custom regions for the board
        /// Each region is indexed starting with 0
        /// </summary>
        /// <param name="regions"></param>
        public void SetRegions(int[,] regions)
        {
            if (Groups.Count != 0)
            {
                throw new InvalidOperationException("SetRegions can only be called before FinalizeConstraints");
            }
            this.regions = regions;
        }

        /// <summary>
        /// Adds a new constraint to the board.
        /// Only call this before any values have been set onto the board.
        /// </summary>
        /// <param name="constraint"></param>
        public void AddConstraint(Constraint constraint)
        {
#if PROFILING
            if (!timers.ContainsKey(constraint.GetType().FullName))
            {
                timers[constraint.GetType().FullName] = new();
            }
#endif
            constraints.Add(constraint);
        }

        /// <summary>
        /// Call this once after all constraints are set, and before setting any values.
        /// </summary>
        /// <returns>True if the board is still valid. False if the constraints cause there to be trivially no solutions.</returns>
        public bool FinalizeConstraints()
        {
#if PROFILING
            timers["FindNakedSingles"] = new();
            timers["FindHiddenSingle"] = new();
            timers["FindNakedTuples"] = new();
            timers["FindPointingTuples"] = new();
            timers["FindFishes"] = new();
            timers["FindYWings"] = new();
            timers["FindSimpleContradictions"] = new();
            timers["Global"] = Stopwatch.StartNew();
#endif
            if (regions == null)
            {
                regions = DefaultRegions(WIDTH);
            }

            InitStandardGroups();

            bool haveChange = true;
            while (haveChange)
            {
                haveChange = false;
                foreach (var constraint in constraints)
                {
                    LogicResult result = constraint.InitCandidates(this);
                    if (result == LogicResult.Invalid)
                    {
                        return false;
                    }

                    if (result == LogicResult.Changed)
                    {
                        haveChange = true;
                    }
                }
            }

            foreach (var constraint in constraints)
            {
                var cells = constraint.Group;
                if (cells != null)
                {
                    SudokuGroup group = new(constraint.SpecificName, cells.ToList());
                    Groups.Add(group);
                    InitMapForGroup(group);
                }
            }

            smallGroupsBySize = Groups.Where(g => g.Cells.Count < MAX_VALUE).OrderBy(g => g.Cells.Count).ToList();

            seenMap = new bool[HEIGHT, WIDTH, HEIGHT, WIDTH, MAX_VALUE + 1];
            for (int i0 = 0; i0 < HEIGHT; i0++)
            {
                for (int j0 = 0; j0 < WIDTH; j0++)
                {
                    foreach (var (i1, j1) in SeenCells((i0, j0)))
                    {
                        seenMap[i0, j0, i1, j1, 0] = true;
                    }
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        uint mask = ValueMask(v);
                        foreach (var (i1, j1) in SeenCellsByValueMask(mask, (i0, j0)))
                        {
                            seenMap[i0, j0, i1, j1, v] = true;
                        }
                    }
                }
            }

            SetCanHaveUnorthodoxTuples();
            return true;
        }

        private void SetCanHaveUnorthodoxTuples()
        {
            foreach (var constraint in constraints)
            {
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (constraint.SeenCellsByValueMask((i, j), ValueMask(v)).Any())
                            {
                                canHaveUnorthodoxTuples = true;
                                return;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a copy of the board, including all constraints, set values, and candidates.
        /// </summary>
        /// <returns></returns>
        public Solver Clone() => new(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetValue((int, int) cell)
        {
            return SolverUtility.GetValue(board[cell.Item1, cell.Item2]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetValue(uint mask)
        {
            return SolverUtility.GetValue(mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValueSet(int i, int j)
        {
            return SolverUtility.IsValueSet(board[i, j]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValueSet(uint mask)
        {
            return SolverUtility.IsValueSet(mask);
        }

        /// <summary>
        /// Returns which cells must be distinct from the all the inputted cells.
        /// </summary>
        /// <param name="cells"></param>
        /// <returns></returns>
        public HashSet<(int, int)> SeenCells(params (int, int)[] cells)
        {
            HashSet<(int, int)> result = null;
            foreach (var cell in cells)
            {
                if (!CellToGroupMap.TryGetValue(cell, out var groupList) || groupList.Count == 0)
                {
                    return new HashSet<(int, int)>();
                }

                HashSet<(int, int)> curSeen = new(groupList.First().Cells);
                foreach (var group in groupList.Skip(1))
                {
                    curSeen.UnionWith(group.Cells);
                }

                foreach (var constraint in constraints)
                {
                    curSeen.UnionWith(constraint.SeenCells(cell));
                }

                if (result == null)
                {
                    result = curSeen;
                }
                else
                {
                    result.IntersectWith(curSeen);
                }
            }
            if (result != null)
            {
                foreach (var cell in cells)
                {
                    result.Remove(cell);
                }
            }
            return result ?? new HashSet<(int, int)>();
        }

        /// <summary>
        /// Returns which cells must be distinct from the all the inputted cells for a specific set of values.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="cells"></param>
        /// <returns></returns>
        public HashSet<(int, int)> SeenCellsByValueMask(uint mask, params (int, int)[] cells)
        {
            HashSet<(int, int)> result = null;
            foreach (var cell in cells)
            {
                if (!CellToGroupMap.TryGetValue(cell, out var groupList) || groupList.Count == 0)
                {
                    return new HashSet<(int, int)>();
                }

                HashSet<(int, int)> curSeen = new(groupList.First().Cells);
                foreach (var group in groupList.Skip(1))
                {
                    curSeen.UnionWith(group.Cells);
                }

                foreach (var constraint in constraints)
                {
                    curSeen.UnionWith(constraint.SeenCellsByValueMask(cell, mask));
                }

                if (result == null)
                {
                    result = curSeen;
                }
                else
                {
                    result.IntersectWith(curSeen);
                }
            }
            if (result != null)
            {
                foreach (var cell in cells)
                {
                    result.Remove(cell);
                }
            }
            return result ?? new HashSet<(int, int)>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSeen((int, int) cell0, (int, int) cell1)
        {
            return seenMap[cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2, 0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSeenByValue((int, int) cell0, (int, int) cell1, int value)
        {
            return seenMap[cell0.Item1, cell0.Item2, cell1.Item1, cell1.Item2, value];
        }

        public bool IsGroup(List<(int, int)> cells)
        {
            for (int i0 = 0; i0 < cells.Count - 1; i0++)
            {
                var cell0 = cells[i0];
                var seen0 = SeenCells(cell0);
                for (int i1 = i0 + 1; i1 < cells.Count; i1++)
                {
                    var cell1 = cells[i1];
                    if (cell0 != cell1 && !seen0.Contains(cell1))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool IsGroupByValueMask(List<(int, int)> cells, uint valueMask)
        {
            for (int i0 = 0; i0 < cells.Count - 1; i0++)
            {
                var cell0 = cells[i0];
                var seen0 = SeenCellsByValueMask(valueMask, cell0);
                for (int i1 = i0 + 1; i1 < cells.Count; i1++)
                {
                    var cell1 = cells[i1];
                    if (cell0 != cell1 && !seen0.Contains(cell1))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool CanPlaceDigits(List<(int, int)> cells, List<int> values)
        {
            int numCells = cells.Count;
            if (numCells != values.Count)
            {
                throw new ArgumentException($"CanPlaceDigits: Number of cells ({cells.Count}) must match number of values ({values.Count})");
            }

            Span<uint> cellMasks = stackalloc uint[numCells];
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                cellMasks[cellIndex] = board[i, j];
            }

            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                int v = values[cellIndex];
                uint valueMask = ValueMask(v);
                uint mask = cellMasks[cellIndex];
                if ((mask & valueMask) == 0)
                {
                    return false;
                }

                var cell0 = cells[cellIndex];
                uint clearMask = ALL_VALUES_MASK & ~valueMask;
                for (int cellIndex1 = cellIndex + 1; cellIndex1 < numCells; cellIndex1++)
                {
                    var cell1 = cells[cellIndex1];
                    if (IsSeenByValue(cell0, cell1, v))
                    {
                        cellMasks[cellIndex1] &= clearMask;
                    }
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ClearValue(int i, int j, int val)
        {
            uint curMask = board[i, j];
            uint valMask = ValueMask(val);

            if ((curMask & valMask) == 0)
            {
                // Clearing the bit would do nothing
                return true;
            }

            // From this point on, a bit will be cleared
            uint newMask = curMask & ~valMask;
            if ((newMask & ~valueSetMask) == 0)
            {
                // Can't clear the only remaining bit
                if (!IsValueSet(curMask))
                {
                    board[i, j] = 0;
                }
                return false;
            }

            board[i, j] = newMask;
            return true;
        }

        public bool SetValue(int i, int j, int val)
        {
            uint valMask = ValueMask(val);
            if ((board[i, j] & valMask) == 0)
            {
                return false;
            }

            // Check if already set
            if ((board[i, j] & valueSetMask) != 0)
            {
                return true;
            }

            board[i, j] = valueSetMask | valMask;

            // Enforce distinctness in groups
            var setCell = (i, j);
            foreach (var group in CellToGroupMap[setCell])
            {
                foreach (var cell in group.Cells)
                {
                    if (cell != setCell && !ClearValue(cell.Item1, cell.Item2, val))
                    {
                        return false;
                    }
                }
            }

            // Enforce all constraints
            foreach (var constraint in constraints)
            {
                if (!constraint.EnforceConstraint(this, i, j, val))
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SetMask(int i, int j, uint mask)
        {
            if ((mask & ~valueSetMask) == 0)
            {
                return false;
            }

            if (ValueCount(mask) == 1)
            {
                return SetValue(i, j, GetValue(mask));
            }

            board[i, j] = mask;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SetMask(int i, int j, params int[] values)
        {
            uint mask = 0;
            foreach (int v in values)
            {
                mask |= ValueMask(v);
            }
            return SetMask(i, j, mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogicResult KeepMask(int i, int j, uint mask)
        {
            mask &= ALL_VALUES_MASK;
            if (mask == ALL_VALUES_MASK)
            {
                return LogicResult.None;
            }

            uint curMask = board[i, j];
            uint newMask = curMask & mask;
            if (newMask == curMask)
            {
                if ((curMask & valueSetMask) == 0 && ValueCount(curMask) == 1)
                {
                    return SetMask(i, j, curMask) ? LogicResult.Changed : LogicResult.Invalid;
                }

                return LogicResult.None;
            }

            return SetMask(i, j, newMask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogicResult ClearMask(int i, int j, uint mask)
        {
            if (mask == 0)
            {
                return LogicResult.None;
            }

            uint curMask = board[i, j];
            if ((curMask & mask) == 0)
            {
                if ((curMask & valueSetMask) == 0 && ValueCount(curMask) == 1)
                {
                    return SetMask(i, j, curMask) ? LogicResult.Changed : LogicResult.Invalid;
                }

                return LogicResult.None;
            }

            return SetMask(i, j, curMask & ~mask) ? LogicResult.Changed : LogicResult.Invalid;
        }

        private (int, int) GetLeastCandidateCell(bool[] ignoreCell = null)
        {
            int i = -1, j = -1;
            int numCandidates = MAX_VALUE + 1;
            if (smallGroupsBySize != null)
            {
                int lastValidGroupSize = MAX_VALUE + 1;
                foreach (var group in smallGroupsBySize)
                {
                    int groupSize = group.Cells.Count;
                    if (lastValidGroupSize < groupSize)
                    {
                        break;
                    }

                    foreach ((int x, int y) in group.Cells)
                    {
                        if (!IsValueSet(x, y) && (ignoreCell == null || !ignoreCell[x * WIDTH + y]))
                        {
                            int curNumCandidates = ValueCount(board[x, y]);
                            if (curNumCandidates == 2)
                            {
                                return (x, y);
                            }
                            if (curNumCandidates < numCandidates)
                            {
                                lastValidGroupSize = groupSize;
                                numCandidates = curNumCandidates;
                                i = x;
                                j = y;
                            }
                        }
                    }
                }
                if (i != -1)
                {
                    return (i, j);
                }
            }

            for (int x = 0; x < HEIGHT; x++)
            {
                for (int y = 0; y < WIDTH; y++)
                {
                    if (!IsValueSet(x, y) && (ignoreCell == null || !ignoreCell[x * WIDTH + y]))
                    {
                        int curNumCandidates = ValueCount(board[x, y]);
                        if (curNumCandidates == 2)
                        {
                            return (x, y);
                        }
                        if (curNumCandidates < numCandidates)
                        {
                            numCandidates = curNumCandidates;
                            i = x;
                            j = y;
                        }
                    }
                }
            }
            return (i, j);
        }

        private (int, int) GetMostCandidateCell(bool[] ignoreCell = null)
        {
            int i = -1, j = -1;
            int numCandidates = 0;
            if (smallGroupsBySize != null)
            {
                int lastValidGroupSize = MAX_VALUE + 1;
                foreach (var group in smallGroupsBySize)
                {
                    int groupSize = group.Cells.Count;
                    if (lastValidGroupSize < groupSize)
                    {
                        break;
                    }

                    foreach ((int x, int y) in group.Cells)
                    {
                        if (!IsValueSet(x, y) && (ignoreCell == null || !ignoreCell[x * WIDTH + y]))
                        {
                            int curNumCandidates = ValueCount(board[x, y]);
                            if (curNumCandidates == MAX_VALUE)
                            {
                                return (x, y);
                            }
                            if (curNumCandidates > numCandidates)
                            {
                                lastValidGroupSize = groupSize;
                                numCandidates = curNumCandidates;
                                i = x;
                                j = y;
                            }
                        }
                    }
                }
                if (i != -1)
                {
                    return (i, j);
                }
            }

            for (int x = 0; x < HEIGHT; x++)
            {
                for (int y = 0; y < WIDTH; y++)
                {
                    if (!IsValueSet(x, y) && (ignoreCell == null || !ignoreCell[x * WIDTH + y]))
                    {
                        int curNumCandidates = ValueCount(board[x, y]);
                        if (curNumCandidates == MAX_VALUE)
                        {
                            return (x, y);
                        }
                        if (curNumCandidates > numCandidates)
                        {
                            numCandidates = curNumCandidates;
                            i = x;
                            j = y;
                        }
                    }
                }
            }
            return (i, j);
        }

        private int CellPriority(int i, int j)
        {
            if (IsValueSet(i, j))
            {
                return -1;
            }

            int numCandidates = ValueCount(board[i, j]);
            int invNumCandidates = MAX_VALUE - numCandidates + 1;
            int priority = invNumCandidates;
            if (CellToGroupMap.TryGetValue((i, j), out var groups))
            {
                try
                {
                    int smallestGroupSize = groups.Select(g => g.Cells.Count).Where(c => c > 1).Min();
                    int groupPriority = MAX_VALUE - smallestGroupSize + 1;
                    priority += MAX_VALUE * groupPriority;
                }
                catch (Exception) { }
            }

            // Within the same priority level, sort by cell order
            int cellIndex = i * WIDTH + j;
            priority = priority * NUM_CELLS + (NUM_CELLS - cellIndex - 1);
            return priority;
        }

        public int MinimumUniqueValues(IEnumerable<(int, int)> cells)
        {
            var cellList = cells.ToList();
            int numCells = cellList.Count;
            if (numCells == 0)
            {
                return 0;
            }
            if (numCells == 1)
            {
                return 1;
            }

            int[] connectionCount = new int[numCells];
            HashSet<(int, int)> connections = new();
            for (int i0 = 0; i0 < numCells - 1; i0++)
            {
                var curCell = cellList[i0];
                var seen = SeenCells(curCell);
                for (int i1 = i0 + 1; i1 < numCells; i1++)
                {
                    var otherCell = cellList[i1];
                    if (seen.Contains(otherCell))
                    {
                        connections.Add((i0, i1));
                        connectionCount[i0]++;
                        connectionCount[i1]++;
                    }
                }
            }

            int maxGroupSize = connectionCount.Max() + 1;
            for (int groupSize = maxGroupSize; groupSize >= 2; groupSize--)
            {
                foreach (var groupCells in Enumerable.Range(0, numCells).Combinations(groupSize))
                {
                    bool isFullyConnected = true;
                    foreach (var pair in groupCells.Combinations(2))
                    {
                        int i0 = pair[0];
                        int i1 = pair[1];
                        if (i0 > i1)
                        {
                            (i0, i1) = (i1, i0);
                        }
                        if (!connections.Contains((i0, i1)))
                        {
                            isFullyConnected = false;
                            break;
                        }
                    }
                    if (isFullyConnected)
                    {
                        return groupSize;
                    }
                }
            }
            return 1;
        }

        /// <summary>
        /// Performs a single logical step.
        /// </summary>
        /// <param name="progressEvent">An event to report progress whenever a new step is found.</param>
        /// <param name="completedEvent">An event to report the final status of the puzzle (solved, no more logical steps, invalid)</param>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns></returns>
        public void LogicalStep(EventHandler<(string, uint[])> completedEvent)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            StringBuilder logicDescription = new StringBuilder();
            LogicResult result = StepLogic(logicDescription, true);
            switch (result)
            {
                case LogicResult.None:
                    completedEvent?.Invoke(null, ("No more logical steps found.", FlatBoard));
                    return;
                default:
                    completedEvent?.Invoke(null, (logicDescription.ToString(), FlatBoard));
                    break;
            }
        }

        /// <summary>
        /// Performs logical solve steps until no more logic is found.
        /// </summary>
        /// <param name="progressEvent">An event to report progress whenever a new step is found.</param>
        /// <param name="completedEvent">An event to report the final status of the puzzle (solved, no more logical steps, invalid)</param>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns></returns>
        public async Task LogicalSolve(EventHandler<(string, uint[])> progressEvent, EventHandler<(string, uint[])> completedEvent, CancellationToken? cancellationToken)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            Stopwatch timeSinceCheck = Stopwatch.StartNew();
            StringBuilder logicProgress = new();
            while (true)
            {
                if (timeSinceCheck.ElapsedMilliseconds > 1000)
                {
                    progressEvent?.Invoke(null, (logicProgress.ToString(), FlatBoard));
                    logicProgress.Clear();

                    await Task.Delay(1);
                    cancellationToken?.ThrowIfCancellationRequested();
                    timeSinceCheck.Restart();
                }

                StringBuilder logicDescription = new StringBuilder();
                LogicResult result = StepLogic(logicDescription);
                logicProgress.Append(logicDescription).AppendLine();

                switch (result)
                {
                    case LogicResult.None:
                        {
                            logicProgress.Append("No more logical steps found.");
                            completedEvent?.Invoke(null, (logicProgress.ToString(), FlatBoard));
                        }
                        return;
                    case LogicResult.Invalid:
                        {
                            logicProgress.Append("Puzzle has no solutions.");
                            completedEvent?.Invoke(null, (logicProgress.ToString(), FlatBoard));
                        }
                        return;
                    case LogicResult.PuzzleComplete:
                        {
                            completedEvent?.Invoke(null, (logicProgress.ToString(), FlatBoard));
                        }
                        return;
                }
            }
        }

        /// <summary>
        /// Finds a single solution to the board. This may not be the only solution.
        /// For the exact same board inputs, the solution will always be the same.
        /// The board itself is modified to have the solution as its board values.
        /// If no solution is found, the board is left in an invalid state.
        /// </summary>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns>True if a solution is found, otherwise false.</returns>
        public bool FindSolution(bool multiThread = false, CancellationToken? cancellationToken = null, bool isRandom = false)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            Solver solver = Clone();
            solver.isBruteForcing = true;
            if (!multiThread)
            {
                bool solutionFound = FindSolutionSingleThreaded(solver, cancellationToken, isRandom);
                if (solutionFound)
                {
                    board = solver.Board;
                }
                return solutionFound;
            }

            using FindSolutionState state = new(cancellationToken, isRandom);
            FindSolutionMultiThreaded(solver, state);
            state.Wait();
            if (state.result != null)
            {
                board = state.result;
            }
            return state.result != null;
        }

        private static bool FindSolutionSingleThreaded(Solver solver, CancellationToken? cancellationToken, bool isRandom)
        {
            Solver initialSolver = solver;

            var boardStack = new Stack<Solver>();
            while (true)
            {
                cancellationToken?.ThrowIfCancellationRequested();

                var logicResult = solver.ConsolidateBoard();
                if (logicResult == LogicResult.PuzzleComplete)
                {
                    initialSolver.board = solver.board;
                    return true;
                }

                if (logicResult != LogicResult.Invalid)
                {
                    (int i, int j) = solver.GetLeastCandidateCell();
                    if (i < 0)
                    {
                        initialSolver.board = solver.board;
                        return true;
                    }

                    // Try a possible value for this cell
                    int val = isRandom ? GetRandomValue(solver.board[i, j]) : MinValue(solver.board[i, j]);
                    uint valMask = ValueMask(val);

                    // Create a backup board in case it needs to be restored
                    Solver backupBoard = solver.Clone();
                    backupBoard.isBruteForcing = true;
                    backupBoard.board[i, j] &= ~valMask;
                    if (backupBoard.board[i, j] != 0)
                    {
                        boardStack.Push(backupBoard);
                    }

                    // Change the board to only allow this value in the slot
                    if (solver.SetValue(i, j, val))
                    {
                        continue;
                    }
                }

                if (boardStack.Count == 0)
                {
                    return false;
                }
                solver = boardStack.Pop();
            }
        }

        private class FindSolutionState : IDisposable
        {
            public CountdownEvent countdownEvent = new(1);
            public uint[,] result = null;
            public CancellationToken? cancellationToken;
            public object locker = new();
            public bool isRandom = false;

            public FindSolutionState(CancellationToken? cancellationToken, bool isRandom)
            {
                this.cancellationToken = cancellationToken;
                this.isRandom = isRandom;
            }

            public void Dispose()
            {
                ((IDisposable)countdownEvent).Dispose();
            }

            public void ReportSolution(Solver solver)
            {
                lock (locker)
                {
                    if (result == null)
                    {
                        result = solver.Board;
                    }
                }
            }

            public void Wait()
            {
                if (cancellationToken.HasValue)
                {
                    countdownEvent.Wait(cancellationToken.Value);
                }
                else
                {
                    countdownEvent.Wait();
                }
            }
        }

        private static void FindSolutionMultiThreaded(Solver solver, FindSolutionState state)
        {
            while (true)
            {
                state.cancellationToken?.ThrowIfCancellationRequested();
                if (state.result != null)
                {
                    break;
                }

                var logicResult = solver.ConsolidateBoard();
                if (logicResult == LogicResult.PuzzleComplete)
                {
                    state.ReportSolution(solver);
                    break;
                }

                if (logicResult == LogicResult.Invalid)
                {
                    break;
                }

                (int i, int j) = solver.GetLeastCandidateCell();
                if (i < 0)
                {
                    state.ReportSolution(solver);
                    break;
                }

                // Try a possible value for this cell
                int val = state.isRandom ? GetRandomValue(solver.board[i, j]) : MinValue(solver.board[i, j]);
                uint valMask = ValueMask(val);

                // Create a backup board in case it needs to be restored
                Solver newSolver = solver.Clone();
                newSolver.isBruteForcing = true;
                newSolver.board[i, j] &= ~valMask;
                if (newSolver.board[i, j] != 0)
                {
                    state.countdownEvent.AddCount();
                    Task.Run(() => FindSolutionMultiThreaded(newSolver, state));
                }

                // Change the board to only allow this value in the slot
                if (!solver.SetValue(i, j, val))
                {
                    break;
                }
            }
            state.countdownEvent.Signal();
        }

        /// <summary>
        /// Determine how many solutions the board has.
        /// </summary>
        /// <param name="maxSolutions">The maximum number of solutions to find. Pass 0 for no maximum.</param>
        /// <param name="multiThread">Whether to use multiple threads.</param>
        /// <param name="progressEvent">An event to receive the progress count as solutions are found.</param>
        /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
        /// <returns>The solution count found.</returns>
        public ulong CountSolutions(ulong maxSolutions = 0, bool multiThread = false, Action<ulong> progressEvent = null, Action<Solver> solutionEvent = null, CancellationToken? cancellationToken = null)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            using CountSolutionsState state = new(maxSolutions, multiThread, progressEvent, solutionEvent, cancellationToken);
            try
            {
                if (state.multiThread)
                {
                    Solver boardCopy = Clone();
                    boardCopy.isBruteForcing = true;
                    boardCopy.CountSolutionsMultiThreaded(state);
                    state.Wait();
                }
                else
                {
                    CountSolutionsSingleThread(this, state);
                }
            }
            catch (OperationCanceledException) { }

            return state.numSolutions;
        }

        private class CountSolutionsState : IDisposable
        {
            public ulong numSolutions = 0;
            public readonly bool multiThread;
            public readonly ulong maxSolutions;
            public readonly Action<ulong> progressEvent;
            public readonly Action<Solver> solutionEvent;
            public readonly CancellationToken? cancellationToken;
            public readonly CountdownEvent countdownEvent;

            private readonly object solutionLock = new();
            private readonly Stopwatch eventTimer;

            public CountSolutionsState(ulong maxSolutions, bool multiThread, Action<ulong> progressEvent, Action<Solver> solutionEvent, CancellationToken? cancellationToken)
            {
                this.maxSolutions = maxSolutions;
                this.multiThread = multiThread;
                this.progressEvent = progressEvent;
                this.solutionEvent = solutionEvent;
                this.cancellationToken = cancellationToken;
                eventTimer = Stopwatch.StartNew();
                countdownEvent = multiThread ? new CountdownEvent(1) : null;
            }

            public void IncrementSolutions(Solver solver)
            {
                bool invokeProgress = false;
                lock (solutionLock)
                {
                    solutionEvent?.Invoke(solver);

                    numSolutions++;
                    if (eventTimer.ElapsedMilliseconds > 500)
                    {
                        invokeProgress = true;
                        eventTimer.Restart();
                    }
                }
                if (invokeProgress)
                {
                    progressEvent?.Invoke(numSolutions);
                }
            }

            public void Wait()
            {
                if (cancellationToken.HasValue)
                {
                    countdownEvent.Wait(cancellationToken.Value);
                }
                else
                {
                    countdownEvent.Wait();
                }
            }

            public void Dispose()
            {
                ((IDisposable)countdownEvent)?.Dispose();
            }
        }

        private static void CountSolutionsSingleThread(Solver solver, CountSolutionsState state)
        {
            solver = solver.Clone();
            solver.isBruteForcing = true;

            var boardStack = new Stack<Solver>();
            while (true)
            {
                state.cancellationToken?.ThrowIfCancellationRequested();

                var logicResult = solver.ConsolidateBoard();
                if (logicResult == LogicResult.PuzzleComplete)
                {
                    state.IncrementSolutions(solver);
                    if (state.maxSolutions > 0 && state.numSolutions >= state.maxSolutions)
                    {
                        return;
                    }
                }
                else if (logicResult != LogicResult.Invalid)
                {
                    (int i, int j) = solver.GetLeastCandidateCell();
                    if (i < 0)
                    {
                        state.IncrementSolutions(solver);
                        if (state.maxSolutions > 0 && state.numSolutions >= state.maxSolutions)
                        {
                            return;
                        }
                    }
                    else
                    {
                        // Try a possible value for this cell
                        int val = MinValue(solver.board[i, j]);
                        uint valMask = ValueMask(val);

                        // Create a board without this value and push it to the stack
                        // for later processing.
                        Solver newSolver = solver.Clone();
                        newSolver.isBruteForcing = true;
                        newSolver.board[i, j] &= ~valMask;
                        if (newSolver.board[i, j] != 0)
                        {
                            boardStack.Push(newSolver);
                        }

                        if (solver.SetValue(i, j, val))
                        {
                            continue;
                        }
                    }
                }

                if (boardStack.Count == 0)
                {
                    return;
                }
                solver = boardStack.Pop();
            }
        }

        private void CountSolutionsMultiThreaded(CountSolutionsState state)
        {
            while (true)
            {
                // If reached max solutions, bail out
                if (state.maxSolutions > 0 && state.numSolutions >= state.maxSolutions)
                {
                    break;
                }

                state.cancellationToken?.ThrowIfCancellationRequested();

                var logicResult = ConsolidateBoard();
                if (logicResult == LogicResult.PuzzleComplete)
                {
                    state.IncrementSolutions(this);
                    break;
                }

                if (logicResult == LogicResult.Invalid)
                {
                    break;
                }

                // Start with the cell that has the least possible candidates
                (int i, int j) = GetLeastCandidateCell();
                if (i < 0)
                {
                    state.IncrementSolutions(this);
                    break;
                }

                // Try a possible value for this cell
                int val = MinValue(board[i, j]);
                uint valMask = ValueMask(val);

                // Create a solver without this value and start a task for it
                Solver newSolver = Clone();
                newSolver.isBruteForcing = true;
                newSolver.board[i, j] &= ~valMask;
                if (newSolver.board[i, j] != 0)
                {
                    state.countdownEvent.AddCount();
                    Task.Run(() => newSolver.CountSolutionsMultiThreaded(state));
                }

                if (!SetValue(i, j, val))
                {
                    break;
                }
            }
            state.countdownEvent.Signal();
        }

        private class FillRealCandidatesState
        {
            public readonly uint[] fixedBoard;
            public readonly bool[] candidatesFixed;
            public readonly int[] tasksRemainingPerCell;

            public readonly Action<uint[]> progressEvent;
            public readonly Stopwatch eventTimer = Stopwatch.StartNew();

            public readonly CancellationToken? cancellationToken;
            public readonly bool multiThread;
            public bool boardInvalid = false;

            public FillRealCandidatesState(int numCells, Action<uint[]> progressEvent, CancellationToken? cancellationToken, bool multiThread)
            {
                fixedBoard = new uint[numCells];
                candidatesFixed = new bool[numCells];
                tasksRemainingPerCell = new int[numCells];
                this.progressEvent = progressEvent;
                this.cancellationToken = cancellationToken;
                this.multiThread = multiThread;
            }

            public void CheckProgressEvent()
            {
                if (progressEvent == null)
                {
                    return;
                }

                bool doProgressEvent = false;
                if (eventTimer.ElapsedMilliseconds > 2000)
                {
                    doProgressEvent = true;
                    eventTimer.Restart();
                }
                if (doProgressEvent)
                {
                    progressEvent(fixedBoard);
                }
            }
        }

        /// <summary>
        /// Remove any candidates which do not lead to an actual solution to the board.
        /// </summary>
        /// <param name="progressEvent">Recieve progress notifications. Will send 0 through 80 (assume 81 is 100%, though that will never be sent).</param>
        /// <param name="cancellationToken">Pass in to support cancelling.</param>
        /// <returns>True if there are solutions and candidates are filled. False if there are no solutions.</returns>
        public bool FillRealCandidates(bool multiThread = false, bool skipConsolidate = false, Action<uint[]> progressEvent = null, CancellationToken? cancellationToken = null)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            Stopwatch timeSinceCheck = Stopwatch.StartNew();

            if (!skipConsolidate && ConsolidateBoard() == LogicResult.Invalid)
            {
                return false;
            }

            FillRealCandidatesState state = new(NUM_CELLS, progressEvent, cancellationToken, multiThread);
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = i * WIDTH + j;
                    uint cellMask = board[i, j];
                    if (IsValueSet(cellMask))
                    {
                        state.fixedBoard[cellIndex] = cellMask;
                        state.candidatesFixed[cellIndex] = true;
                    }
                }
            }

            List<(int, int, int, int)> cellValuesByPriority = new(NUM_CELLS);
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellPriority = CellPriority(i, j);
                    if (cellPriority < 0)
                    {
                        continue;
                    }

                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        // Don't bother trying the value if it's not a possibility
                        uint valMask = ValueMask(v);
                        if ((board[i, j] & valMask) == 0)
                        {
                            continue;
                        }

                        int cellIndex = i * WIDTH + j;
                        state.tasksRemainingPerCell[cellIndex]++;
                        cellValuesByPriority.Add((cellPriority, i, j, v));
                    }
                }
            }
            cellValuesByPriority.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            try
            {
                foreach (var (p, i, j, v) in cellValuesByPriority)
                {
                    cancellationToken?.ThrowIfCancellationRequested();

                    FillRealCandidateAction(i, j, v, state);
                    if (state.boardInvalid)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                state.boardInvalid = true;
            }

            if (state.boardInvalid)
            {
                return false;
            }

            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = i * WIDTH + j;
                    uint cellMask = board[i, j];
                    if (IsValueSet(cellMask))
                    {
                        state.fixedBoard[cellIndex] = cellMask;
                    }
                }
            }

            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    SetMask(i, j, state.fixedBoard[i * WIDTH + j]);
                }
            }
            return true;
        }

        private void FillRealCandidateAction(int i, int j, int v, FillRealCandidatesState state)
        {
            int cellIndex = i * WIDTH + j;
            uint valMask = ValueMask(v);

            // Don't bother trying this value if it's already confirmed in the fixed board
            if (!state.boardInvalid && (state.fixedBoard[cellIndex] & valMask) == 0)
            {
                // Do the solve on a copy of the board
                Solver boardCopy = Clone();
                boardCopy.isBruteForcing = true;

                // Go through all previous cells and set only their real candidates as possibilities
                for (int fi = 0; fi < HEIGHT; fi++)
                {
                    for (int fj = 0; fj < WIDTH; fj++)
                    {
                        int fixedCellIndex = fi * WIDTH + fj;
                        if (state.candidatesFixed[fixedCellIndex])
                        {
                            boardCopy.board[fi, fj] = state.fixedBoard[fixedCellIndex];
                        }
                    }
                }

                // Set the board to use this candidate's value
                if (boardCopy.SetValue(i, j, v) && boardCopy.FindSolution(multiThread: state.multiThread, cancellationToken: state.cancellationToken, isRandom: true))
                {
                    for (int si = 0; si < HEIGHT; si++)
                    {
                        for (int sj = 0; sj < WIDTH; sj++)
                        {
                            uint solutionValMask = boardCopy.board[si, sj] & ~valueSetMask;
                            state.fixedBoard[si * WIDTH + sj] |= solutionValMask;
                        }
                    }
                }
            }

            if (state.tasksRemainingPerCell != null && --state.tasksRemainingPerCell[cellIndex] == 0)
            {
                // If a cell has no possible candidates then there are no solutions and thus all candidates are empty.
                if (state.fixedBoard[cellIndex] == 0)
                {
                    state.boardInvalid = true;
                }
                state.candidatesFixed[cellIndex] = true;
            }

            state.CheckProgressEvent();
        }

        /// <summary>
        /// Perform a logical solve until either the board is solved or there are no logical steps found.
        /// </summary>
        /// <param name="stepsDescription">Get a full description of all logical steps taken.</param>
        /// <returns></returns>
        public LogicResult ConsolidateBoard(StringBuilder stepsDescription = null)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            LogicResult result;
            do
            {
                StringBuilder stepDescription = stepsDescription != null ? new() : null;
                result = StepLogic(stepDescription);
                stepsDescription?.Append(stepDescription).AppendLine();
            } while (result == LogicResult.Changed);

            return result;
        }

        /// <summary>
        /// Perform one step of a logical solve and fill a description of the step taken.
        /// The description will contain the reason the board is invalid if that is what is returned.
        /// </summary>
        /// <param name="stepDescription"></param>
        /// <returns></returns>
        public LogicResult StepLogic(StringBuilder stepDescription, bool humanStepping = false)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            LogicResult result = LogicResult.None;

#if PROFILING
            timers["FindNakedSingles"].Start();
#endif
            result = FindNakedSingles(stepDescription, humanStepping);
#if PROFILING
            timers["FindNakedSingles"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindHiddenSingle"].Start();
#endif
            result = FindHiddenSingle(stepDescription);
#if PROFILING
            timers["FindHiddenSingle"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

            foreach (var constraint in constraints)
            {
                string constraintName = constraint.GetType().FullName;
#if PROFILING
                timers[constraintName].Start();
#endif
                result = constraint.StepLogic(this, stepDescription, isBruteForcing);
#if PROFILING
                timers[constraintName].Stop();
#endif
                if (result != LogicResult.None)
                {
                    if (stepDescription != null)
                    {
                        stepDescription.Insert(0, $"{constraint.SpecificName}: ");
                    }
                    return result;
                }
            }

            if (isBruteForcing)
            {
                return LogicResult.None;
            }

#if PROFILING
            timers["FindNakedTuples"].Start();
#endif
            result = FindNakedTuples(stepDescription);
#if PROFILING
            timers["FindNakedTuples"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindPointingTuples"].Start();
#endif
            result = FindPointingTuples(stepDescription);
#if PROFILING
            timers["FindPointingTuples"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindUnorthodoxTuples"].Start();
#endif
            result = FindUnorthodoxTuples(stepDescription);
#if PROFILING
            timers["FindUnorthodoxTuples"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindFishes"].Start();
#endif
            result = FindFishes(stepDescription);
#if PROFILING
            timers["FindFishes"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindYWings"].Start();
#endif
            result = FindYWings(stepDescription);
#if PROFILING
            timers["FindYWings"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindSimpleContradictions"].Start();
#endif
            result = FindSimpleContradictions(stepDescription);
#if PROFILING
            timers["FindSimpleContradictions"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

            return LogicResult.None;
        }

        private LogicResult FindNakedSingles(StringBuilder stepDescription, bool humanStepping)
        {
            if (humanStepping)
            {
                return FindNakedSinglesHelper(stepDescription, humanStepping);
            }

            bool haveChange = false;
            while (true)
            {
                StringBuilder curStepDescription = stepDescription != null ? new() : null;
                LogicResult findResult = FindNakedSinglesHelper(curStepDescription, humanStepping);
                if (curStepDescription != null && curStepDescription.Length > 0)
                {
                    if (stepDescription.Length > 0)
                    {
                        stepDescription.AppendLine();
                    }
                    stepDescription.Append(curStepDescription);
                }
                switch (findResult)
                {
                    case LogicResult.None:
                        return haveChange ? LogicResult.Changed : LogicResult.None;
                    case LogicResult.Changed:
                        haveChange = true;
                        break;
                    default:
                        return findResult;
                }
            }
        }

        private LogicResult FindNakedSinglesHelper(StringBuilder stepDescription, bool humanStepping)
        {
            string stepPrefix = humanStepping ? "Naked Single:" : "Naked Single(s):";

            bool hasUnsetCells = false;
            bool hadChanges = false;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];

                    // If there are no possibilies on a square, then bail out
                    if (mask == 0)
                    {
                        if (stepDescription != null)
                        {
                            stepDescription.AppendLine();
                            stepDescription.Append($"{CellName(i, j)} has no possible values.");
                        }
                        return LogicResult.Invalid;
                    }

                    if (!IsValueSet(mask))
                    {
                        hasUnsetCells = true;

                        if (ValueCount(mask) == 1)
                        {
                            int value = GetValue(mask);
                            if (!hadChanges)
                            {
                                stepDescription?.Append($"{stepPrefix} {CellName(i, j)} = {value}");
                                hadChanges = true;
                                if (humanStepping)
                                {
                                    return LogicResult.Changed;
                                }
                            }
                            else
                            {
                                stepDescription?.Append($", {CellName(i, j)} = {value}");
                            }

                            if (!SetValue(i, j, value))
                            {
                                for (int ci = 0; ci < HEIGHT; ci++)
                                {
                                    for (int cj = 0; cj < WIDTH; cj++)
                                    {
                                        if (board[ci, cj] == 0)
                                        {
                                            stepDescription?.AppendLine().Append($"{CellName(ci, cj)} has no candidates remaining.");
                                            return LogicResult.Invalid;
                                        }
                                    }
                                }
                                stepDescription?.AppendLine().Append($"{CellName(i, j)} cannot be {value}.");
                                return LogicResult.Invalid;
                            }
                        }
                    }
                }
            }
            if (!hasUnsetCells)
            {
                if (stepDescription != null)
                {
                    if (stepDescription.Length > 0)
                    {
                        stepDescription.AppendLine();
                    }
                    stepDescription.Append("Solution found!");
                }
                return LogicResult.PuzzleComplete;
            }
            return hadChanges ? LogicResult.Changed : LogicResult.None;
        }

        private LogicResult FindHiddenSingle(StringBuilder stepDescription)
        {
            LogicResult finalFindResult = LogicResult.None;
            Span<int> valueCounts = stackalloc int[MAX_VALUE];
            foreach (var group in Groups)
            {
                var groupCells = group.Cells;
                int numCells = group.Cells.Count;
                if (numCells != MAX_VALUE)
                {
                    continue;
                }

                for (int valIndex = 0; valIndex < MAX_VALUE; valIndex++)
                {
                    valueCounts[valIndex] = 0;
                }
                for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
                {
                    var (i, j) = groupCells[cellIndex];
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        int valIndex = GetValue(mask) - 1;
                        valueCounts[valIndex] = -1;
                    }
                    else
                    {
                        for (int valIndex = 0; valIndex < MAX_VALUE; valIndex++)
                        {
                            if ((mask & (1u << valIndex)) != 0)
                            {
                                valueCounts[valIndex]++;
                            }
                        }
                    }
                }

                int singleValIndex = -1;
                int zeroValIndex = -1;
                for (int valIndex = 0; valIndex < MAX_VALUE; valIndex++)
                {
                    int curValCount = valueCounts[valIndex];
                    if (curValCount == 1)
                    {
                        singleValIndex = valIndex;
                    }
                    else if (curValCount == 0)
                    {
                        zeroValIndex = valIndex;
                        break;
                    }
                }

                if (zeroValIndex >= 0)
                {
                    if (stepDescription != null)
                    {
                        stepDescription.Clear();
                        stepDescription.Append($"{group.Name} has nowhere to place {zeroValIndex + 1}.");
                    }
                    return LogicResult.Invalid;
                }

                if (singleValIndex >= 0)
                {
                    int val = singleValIndex + 1;
                    uint valMask = 1u << singleValIndex;
                    int vali = 0;
                    int valj = 0;
                    foreach (var (i, j) in group.Cells)
                    {
                        uint mask = board[i, j];
                        if ((board[i, j] & valMask) != 0)
                        {
                            vali = i;
                            valj = j;
                            break;
                        }
                    }

                    if (!SetValue(vali, valj, val))
                    {
                        if (stepDescription != null)
                        {
                            stepDescription.Clear();
                            stepDescription.Append($"Hidden single {val} in {group.Name} {CellName(vali, valj)}, but it cannot be set to that value.");
                        }
                        return LogicResult.Invalid;
                    }
                    stepDescription?.Append($"Hidden single {val} in {group.Name} {CellName(vali, valj)}");
                    return LogicResult.Changed;
                }
            }
            return finalFindResult;
        }

        private LogicResult FindNakedTuples(StringBuilder stepDescription)
        {
            List<(int, int)> unsetCells = new(MAX_VALUE);
            for (int tupleSize = 2; tupleSize < MAX_VALUE; tupleSize++)
            {
                foreach (var group in Groups)
                {
                    // Make a list of pairs for the group which aren't already filled
                    unsetCells.Clear();
                    foreach (var cell in group.Cells)
                    {
                        uint cellMask = board[cell.Item1, cell.Item2];
                        if (!IsValueSet(cellMask) && ValueCount(cellMask) <= tupleSize)
                        {
                            unsetCells.Add(cell);
                        }
                    }
                    if (unsetCells.Count < tupleSize)
                    {
                        continue;
                    }

                    int[] cellCombinations = combinations[unsetCells.Count - 1][tupleSize - 1];
                    int numCombinations = cellCombinations.Length / tupleSize;
                    for (int combinationIndex = 0; combinationIndex < numCombinations; combinationIndex++)
                    {
                        Span<int> curCombination = new(cellCombinations, combinationIndex * tupleSize, tupleSize);

                        uint combinationMask = 0;
                        foreach (int cellIndex in curCombination)
                        {
                            var curCell = unsetCells[cellIndex];
                            combinationMask |= board[curCell.Item1, curCell.Item2];
                        }

                        if (ValueCount(combinationMask) == tupleSize)
                        {
                            uint invCombinationMask = ~combinationMask;

                            bool changed = false;
                            (int, int)[] tupleCells = new (int, int)[tupleSize];
                            int tupleCellIndex = 0;
                            foreach (int cellIndex in curCombination)
                            {
                                tupleCells[tupleCellIndex++] = unsetCells[cellIndex];
                            }

                            foreach (var curCell in SeenCellsByValueMask(combinationMask, tupleCells))
                            {
                                var clearResult = ClearMask(curCell.Item1, curCell.Item2, combinationMask);
                                if (clearResult == LogicResult.Invalid)
                                {
                                    if (stepDescription != null)
                                    {
                                        stepDescription.Clear();
                                        stepDescription.Append($"{group} has too many cells which can only have {MaskToString(combinationMask)}");
                                    }
                                    return LogicResult.Invalid;
                                }
                                if (clearResult == LogicResult.Changed)
                                {
                                    if (!changed)
                                    {
                                        stepDescription?.Append($"{group} has tuple {MaskToString(combinationMask)}, removing those values from {CellName(curCell)}");
                                        changed = true;
                                    }
                                    else
                                    {
                                        stepDescription?.Append($", {CellName(curCell)}");
                                    }
                                }
                            }
                            if (changed)
                            {
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindPointingTuples(StringBuilder stepDescription)
        {
            foreach (var group in Groups)
            {
                if (group.Cells.Count != MAX_VALUE)
                {
                    continue;
                }

                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if (group.Cells.Any(cell => IsValueSet(cell.Item1, cell.Item2) && GetValue(cell) == v))
                    {
                        continue;
                    }

                    (int, int)[] cellsWithValue = group.Cells.Where(cell => HasValue(board[cell.Item1, cell.Item2], v)).ToArray();
                    if (cellsWithValue.Length <= 1 || cellsWithValue.Length > 3)
                    {
                        continue;
                    }

                    uint valueMask = ValueMask(v);
                    var seenCells = SeenCellsByValueMask(valueMask, cellsWithValue);
                    if (seenCells.Count == 0)
                    {
                        continue;
                    }

                    StringBuilder cellsWithValueStringBuilder = null;
                    bool changed = false;
                    foreach ((int i, int j) in seenCells)
                    {
                        if (cellsWithValue.Contains((i, j)))
                        {
                            continue;
                        }
                        if ((board[i, j] & valueMask) != 0)
                        {
                            if (cellsWithValueStringBuilder == null)
                            {
                                cellsWithValueStringBuilder = new();
                                foreach (var cell in cellsWithValue)
                                {
                                    if (cellsWithValueStringBuilder.Length != 0)
                                    {
                                        cellsWithValueStringBuilder.Append(", ");
                                    }
                                    cellsWithValueStringBuilder.Append(CellName(cell));
                                }
                            }

                            if (!ClearValue(i, j, v))
                            {
                                if (stepDescription != null)
                                {
                                    stepDescription.Clear();
                                    stepDescription.Append($"{v} is limited to {cellsWithValueStringBuilder} in {group}, but that value cannot be removed from {CellName(i, j)}");
                                }
                                return LogicResult.Invalid;
                            }
                            if (stepDescription != null)
                            {
                                if (!changed)
                                {
                                    stepDescription.Append($"{v} is limited to {cellsWithValueStringBuilder} in {group}, which removes that value from {CellName((i, j))}");
                                }
                                else
                                {
                                    stepDescription.Append($", {CellName((i, j))}");
                                }
                            }
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        return LogicResult.Changed;
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindUnorthodoxTuples(StringBuilder stepDescription)
        {
            for (int tupleSize = 2; tupleSize < MAX_VALUE / 2; tupleSize++)
            {
                // Go through every value combination for this tuple size
                foreach (var tupleValues in Enumerable.Range(1, MAX_VALUE).Combinations(tupleSize))
                {
                    uint tupleValuesMask = 0;
                    foreach (int v in tupleValues)
                    {
                        tupleValuesMask |= ValueMask(v);
                    }
                    uint invTupleValuesMask = ~tupleValuesMask;

                    List<(int, int)> possibleTupleCells = new();
                    for (int i = 0; i < HEIGHT; i++)
                    {
                        for (int j = 0; j < WIDTH; j++)
                        {
                            uint mask = board[i, j];
                            if ((mask & invTupleValuesMask) == 0)
                            {
                                possibleTupleCells.Add((i, j));
                            }
                        }
                    }

                    // Look for sets of cells which form a tuple of this size
                    foreach (var possibleCells in possibleTupleCells.Combinations(tupleSize))
                    {
                        if (!IsGroupByValueMask(possibleCells, tupleValuesMask))
                        {
                            continue;
                        }

                        bool changed = false;
                        foreach (var curCell in SeenCellsByValueMask(tupleValuesMask, possibleCells.ToArray()))
                        {
                            var clearResult = ClearMask(curCell.Item1, curCell.Item2, tupleValuesMask);
                            if (clearResult == LogicResult.Invalid)
                            {
                                if (stepDescription != null)
                                {
                                    stepDescription.Clear();
                                    stepDescription.Append($"Cells {possibleCells.CellNames()} form an unorthodox tuple {MaskToString(tupleValuesMask)} and all see {CellName(curCell)}, clearing all candidates from it.");
                                }
                                return LogicResult.Invalid;
                            }
                            if (clearResult == LogicResult.Changed)
                            {
                                if (!changed)
                                {
                                    stepDescription?.Append($"Cells {possibleCells.CellNames()} form an unorthodox tuple {MaskToString(tupleValuesMask)} clearing those candidates from: {CellName(curCell)}");
                                    changed = true;
                                }
                                else
                                {
                                    stepDescription?.Append($", {CellName(curCell)}");
                                }
                            }
                        }
                        if (changed)
                        {
                            return LogicResult.Changed;
                        }
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindFishes(StringBuilder stepDescription)
        {
#pragma warning disable CS0162
            if (WIDTH != MAX_VALUE || HEIGHT != MAX_VALUE)
            {
                return LogicResult.None;
            }
#pragma warning restore CS0162

            for (int n = 2; n <= 4; n++)
            {
                for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
                {
                    bool isCol = rowOrCol != 0;
                    int height = isCol ? WIDTH : HEIGHT;
                    int width = isCol ? HEIGHT : WIDTH;
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        uint[] rows = new uint[height];
                        for (int curRow = 0; curRow < height; curRow++)
                        {
                            for (int curCol = 0; curCol < width; curCol++)
                            {
                                int i = isCol ? curCol : curRow;
                                int j = isCol ? curRow : curCol;
                                uint curMask = board[i, j];
                                if ((curMask & (1u << (v - 1))) != 0)
                                {
                                    rows[curRow] |= 1u << curCol;
                                }
                            }
                            int rowCount = ValueCount(rows[curRow]);
                            if (rowCount == 1 || rowCount > n)
                            {
                                rows[curRow] = 0;
                            }
                        }

                        int[] rowCombinations = combinations[height - 1][n - 1];
                        int numCombinations = rowCombinations.Length / n;
                        for (int combinationIndex = 0; combinationIndex < numCombinations; combinationIndex++)
                        {
                            Span<int> curCombination = new(rowCombinations, combinationIndex * n, n);
                            bool validCombination = true;
                            uint rowMask = 0;
                            uint colMask = 0;
                            foreach (int rowIndex in curCombination)
                            {
                                uint curColMask = rows[rowIndex];
                                if (curColMask == 0)
                                {
                                    validCombination = false;
                                    break;
                                }
                                rowMask |= 1u << rowIndex;
                                colMask |= curColMask;
                            }
                            if (!validCombination)
                            {
                                continue;
                            }

                            int colCount = ValueCount(colMask);
                            if (colCount > n)
                            {
                                continue;
                            }
                            if (colCount < n)
                            {
                                if (stepDescription != null)
                                {
                                    string rowName = isCol ? "Cols" : "Rows";

                                    string rowList = "";
                                    foreach (int rowIndex in curCombination)
                                    {
                                        if (rowList.Length > 0)
                                        {
                                            rowList += ", ";
                                        }
                                        rowList += (char)('0' + (rowIndex + 1));
                                    }
                                    stepDescription.Clear();
                                    stepDescription.Append($"{rowName} {rowList} have too few locations for {v}");
                                }
                                return LogicResult.Invalid;
                            }

                            uint valueMask = ValueMask(v);
                            uint invRowMask = ~rowMask;
                            bool changed = false;
                            string fishDesc = null;
                            for (int curRow = 0; curRow < height; curRow++)
                            {
                                if ((invRowMask & (1u << curRow)) == 0)
                                {
                                    continue;
                                }

                                for (int curCol = 0; curCol < width; curCol++)
                                {
                                    if ((colMask & (1u << curCol)) == 0)
                                    {
                                        continue;
                                    }

                                    int i = isCol ? curCol : curRow;
                                    int j = isCol ? curRow : curCol;
                                    if ((board[i, j] & valueMask) == 0)
                                    {
                                        continue;
                                    }

                                    bool clearValueSucceeded = ClearValue(i, j, v);
                                    if (stepDescription != null)
                                    {
                                        if (fishDesc == null)
                                        {
                                            string rowName = isCol ? "c" : "r";
                                            string desc = "";
                                            foreach (int fishRow in curCombination)
                                            {
                                                desc = $"{desc}{rowName}{fishRow + 1}";
                                            }

                                            string techniqueName = n switch
                                            {
                                                2 => "X-Wing",
                                                3 => "Swordfish",
                                                4 => "Jellyfish",
                                                _ => $"{n}-Fish",
                                            };

                                            fishDesc = $"{techniqueName} on {desc} for value {v}";
                                        }

                                        if (!clearValueSucceeded)
                                        {
                                            stepDescription.Clear();
                                            stepDescription.Append($"{fishDesc}, but it cannot be removed from {CellName(i, j)}");
                                            return LogicResult.Invalid;
                                        }

                                        if (!changed)
                                        {
                                            stepDescription.Append($"{fishDesc}, removing that value from {CellName(i, j)}");
                                        }
                                        else
                                        {
                                            stepDescription.Append($", {CellName(i, j)}");
                                        }
                                    }
                                    else if (!clearValueSucceeded)
                                    {
                                        return LogicResult.Invalid;
                                    }

                                    changed = true;
                                }
                            }
                            if (changed)
                            {
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            return LogicResult.None;
        }

        private LogicResult FindYWings(StringBuilder stepDescription)
        {
            if (isBruteForcing)
            {
                return LogicResult.None;
            }

            // A y-wing always involves three cells with two candidates remaining.
            // The three cells have 3 candidates between them, and one cell sees both of them.
            // Any cell seen by the "wings" that don't see each other cannot be the candidate that's
            // not in the "hinge" cell.
            List<(int, int)> candidateCells = new();
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        continue;
                    }
                    if (ValueCount(mask) == 2)
                    {
                        candidateCells.Add((i, j));
                    }
                }
            }

            // Look for Y-Wings
            for (int c0 = 0; c0 < candidateCells.Count - 2; c0++)
            {
                var (i0, j0) = candidateCells[c0];
                uint mask0 = board[i0, j0];
                for (int c1 = c0 + 1; c1 < candidateCells.Count - 1; c1++)
                {
                    var (i1, j1) = candidateCells[c1];
                    uint mask1 = board[i1, j1];
                    if (mask0 == mask1 || ValueCount(mask0 | mask1) != 3)
                    {
                        continue;
                    }

                    for (int c2 = c1 + 1; c2 < candidateCells.Count; c2++)
                    {
                        var (i2, j2) = candidateCells[c2];
                        uint mask2 = board[i2, j2];
                        if (mask0 == mask2 || mask1 == mask2)
                        {
                            continue;
                        }

                        uint combinedMask = mask0 | mask1 | mask2;
                        if (ValueCount(combinedMask) == 3)
                        {
                            var seen0 = SeenCellsByValueMask(mask0, (i0, j0));
                            var seen1 = SeenCellsByValueMask(mask1, (i1, j1));
                            var seen2 = SeenCellsByValueMask(mask2, (i2, j2));
                            (int, int) pivot = (0, 0);
                            (int, int) pincer0 = (0, 0);
                            (int, int) pincer1 = (0, 0);
                            uint pivotMask = 0;
                            uint pincer0Mask = 0;
                            uint pincer1Mask = 0;
                            uint removeMask = 0;
                            HashSet<(int, int)> removeFrom = null;
                            if (seen0.Contains((i1, j1)) && seen0.Contains((i2, j2)) && !seen1.Contains((i2, j2)))
                            {
                                // Hinge is 0, the shared value is the value not in cell 0
                                removeMask = combinedMask & ~mask0;
                                removeFrom = SeenCellsByValueMask(removeMask, (i1, j1), (i2, j2));
                                pivot = (i0, j0);
                                pincer0 = (i1, j1);
                                pincer1 = (i2, j2);
                                pivotMask = mask0;
                                pincer0Mask = mask1;
                                pincer1Mask = mask2;
                            }
                            else if (seen1.Contains((i0, j0)) && seen1.Contains((i2, j2)) && !seen0.Contains((i2, j2)))
                            {
                                // Hinge is 1, the shared value is the value not in cell 1
                                removeMask = combinedMask & ~mask1;
                                removeFrom = SeenCellsByValueMask(removeMask, (i0, j0), (i2, j2));
                                pivot = (i1, j1);
                                pincer0 = (i0, j0);
                                pincer1 = (i2, j2);
                                pivotMask = mask1;
                                pincer0Mask = mask0;
                                pincer1Mask = mask2;
                            }
                            else if (seen2.Contains((i0, j0)) && seen2.Contains((i1, j1)) && !seen0.Contains((i1, j1)))
                            {
                                // Hinge is 2, the shared value is the value not in cell 2
                                removeMask = combinedMask & ~mask2;
                                removeFrom = SeenCellsByValueMask(removeMask, (i0, j0), (i1, j1));
                                pivot = (i2, j2);
                                pincer0 = (i0, j0);
                                pincer1 = (i1, j1);
                                pivotMask = mask2;
                                pincer0Mask = mask0;
                                pincer1Mask = mask1;
                            }

                            if (removeFrom != null)
                            {
                                List<(int, int)> removedFrom = new();
                                foreach (var (ri, rj) in removeFrom)
                                {
                                    LogicResult removeResult = ClearMask(ri, rj, removeMask);
                                    if (removeResult == LogicResult.Invalid)
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    if (removeResult == LogicResult.Changed)
                                    {
                                        removedFrom.Add((ri, rj));
                                    }
                                }
                                if (removedFrom.Count > 0)
                                {
                                    if (stepDescription != null)
                                    {
                                        stepDescription.Append($"Y-Wing with pivot at {CellName(pivot)} ({MaskToString(pivotMask)}) and pincers at {CellName(pincer0)} ({MaskToString(pincer0Mask)}), {CellName(pincer1)} ({MaskToString(pincer1Mask)}) clears candidate {GetValue(removeMask)} from cell{(removedFrom.Count == 1 ? "" : "s")}: ");
                                        bool needComma = false;
                                        foreach (var cell in removedFrom)
                                        {
                                            if (needComma)
                                            {
                                                stepDescription.Append(", ");
                                            }
                                            stepDescription.Append($"{CellName(cell)}");
                                            needComma = true;
                                        }
                                    }
                                    return LogicResult.Changed;
                                }
                            }
                        }
                    }
                }
            }

            // Look for XYZ-Wings
            for (int c0 = 0; c0 < candidateCells.Count - 1; c0++)
            {
                var (i0, j0) = candidateCells[c0];
                uint mask0 = board[i0, j0];
                for (int c1 = c0 + 1; c1 < candidateCells.Count; c1++)
                {
                    var (i1, j1) = candidateCells[c1];
                    uint mask1 = board[i1, j1];
                    if (mask0 == mask1)
                    {
                        continue;
                    }

                    uint combMask = mask0 | mask1;
                    if (ValueCount(combMask) != 3)
                    {
                        continue;
                    }

                    uint removeMask = mask0 & mask1;

                    // Look for a cells seen by both of these pincers that contains these exact 3 candidates:
                    foreach (var (pi, pj) in SeenCellsByValueMask(combMask, (i0, j0), (i1, j1)))
                    {
                        if (board[pi, pj] == combMask)
                        {
                            // Check for cells seen by all three
                            List<(int, int)> removedFrom = new();
                            foreach (var (ri, rj) in SeenCellsByValueMask(combMask, (i0, j0), (i1, j1), (pi, pj)))
                            {
                                LogicResult removeResult = ClearMask(ri, rj, removeMask);
                                if (removeResult == LogicResult.Invalid)
                                {
                                    return LogicResult.Invalid;
                                }
                                if (removeResult == LogicResult.Changed)
                                {
                                    removedFrom.Add((ri, rj));
                                }
                            }
                            if (removedFrom.Count > 0)
                            {
                                if (stepDescription != null)
                                {
                                    stepDescription?.Append($"XYZ-Wing with pivot at {CellName((pi, pj))} ({MaskToString(combMask)}) and pincers at {CellName((i0, j0))} ({MaskToString(mask0)}), {CellName((i1, j1))} ({MaskToString(mask1)}) clears candidate {GetValue(removeMask)} from cell{(removedFrom.Count == 1 ? "" : "s")}: ");
                                    bool needComma = false;
                                    foreach (var cell in removedFrom)
                                    {
                                        if (needComma)
                                        {
                                            stepDescription?.Append(", ");
                                        }
                                        stepDescription?.Append($"{CellName(cell)}");
                                        needComma = true;
                                    }
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            // Look for WXYZ-Wings
            candidateCells.Clear();
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        continue;
                    }
                    if (ValueCount(mask) <= 3)
                    {
                        candidateCells.Add((i, j));
                    }
                }
            }

            for (int c0 = 0; c0 < candidateCells.Count - 2; c0++)
            {
                var (i0, j0) = candidateCells[c0];
                uint mask0 = board[i0, j0];
                for (int c1 = c0 + 1; c1 < candidateCells.Count - 1; c1++)
                {
                    var (i1, j1) = candidateCells[c1];
                    uint mask1 = board[i1, j1];
                    for (int c2 = c1 + 1; c2 < candidateCells.Count; c2++)
                    {
                        var (i2, j2) = candidateCells[c2];
                        uint mask2 = board[i2, j2];

                        uint removeMask = mask0 & mask1 & mask2;
                        if (removeMask == 0 || ValueCount(removeMask) != 1)
                        {
                            continue;
                        }

                        uint combMask = mask0 | mask1 | mask2;
                        if (ValueCount(combMask) != 4)
                        {
                            continue;
                        }

                        int count0 = ValueCount(mask0);
                        int count1 = ValueCount(mask1);
                        int count2 = ValueCount(mask2);
                        // If all three pincers have three candidates it can't be valid
                        if (count0 == 3 && count1 == 3 && count2 == 3)
                        {
                            continue;
                        }

                        // If all three pincers have two candidates it's always valid
                        if (count0 != 2 || count1 != 2 || count2 != 2)
                        {
                            bool seen01 = SeenCellsByValueMask(combMask, (i0, j0)).Contains((i1, j1));
                            bool seen02 = SeenCellsByValueMask(combMask, (i0, j0)).Contains((i2, j2));
                            bool seen12 = SeenCellsByValueMask(combMask, (i1, j1)).Contains((i2, j2));

                            // If two pincers have three candidates, then they must have equal candidates and see each other
                            if (count0 == 3 && count1 == 3 && (mask0 != mask1 || !seen01) ||
                                count0 == 3 && count2 == 3 && (mask0 != mask2 || !seen02) ||
                                count1 == 3 && count2 == 3 && (mask1 != mask2 || !seen12))
                            {
                                continue;
                            }
                            // If one pincer has three candidates, it must see one of the other pincers and have all the candidates of that pincer
                            else if (count0 == 3 && count1 == 2 && count2 == 2)
                            {
                                if (seen01 && seen02)
                                {
                                    if ((mask0 & mask1) != mask1 && (mask0 & mask2) != mask2)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen01)
                                {
                                    if ((mask0 & mask1) != mask1)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen02)
                                {
                                    if ((mask0 & mask2) != mask2)
                                    {
                                        continue;
                                    }
                                }
                                continue;
                            }
                            else if (count1 == 3 && count0 == 2 && count2 == 2)
                            {
                                if (seen01 && seen12)
                                {
                                    if ((mask1 & mask0) != mask0 && (mask1 & mask2) != mask2)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen01)
                                {
                                    if ((mask1 & mask0) != mask0)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen12)
                                {
                                    if ((mask1 & mask2) != mask2)
                                    {
                                        continue;
                                    }
                                }
                                continue;
                            }
                            else if (count2 == 3 && count0 == 2 && count1 == 2)
                            {
                                if (seen02 && seen12)
                                {
                                    if ((mask2 & mask0) != mask0 && (mask2 & mask1) != mask1)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen02)
                                {
                                    if ((mask2 & mask0) != mask0)
                                    {
                                        continue;
                                    }
                                }
                                else if (seen12)
                                {
                                    if ((mask2 & mask1) != mask1)
                                    {
                                        continue;
                                    }
                                }
                                continue;
                            }
                        }

                        // Look for a pivot that sees all three of these pincers and has all only the candidates present in the pincers
                        foreach (var (pi, pj) in SeenCellsByValueMask(combMask, (i0, j0), (i1, j1), (i2, j2)))
                        {
                            uint maskp = board[pi, pj];
                            if (IsValueSet(maskp) || (maskp & combMask) != maskp)
                            {
                                continue;
                            }

                            List<(int, int)> removedFrom = new();
                            if ((maskp & removeMask) == 0)
                            {
                                // The pivot does not contain the shared digit among the pincers.
                                // This means any cells that just the pincers see can have that shared digit removed
                                foreach (var (ri, rj) in SeenCellsByValueMask(combMask, (i0, j0), (i1, j1), (i2, j2)))
                                {
                                    LogicResult removeResult = ClearMask(ri, rj, removeMask);
                                    if (removeResult == LogicResult.Invalid)
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    if (removeResult == LogicResult.Changed)
                                    {
                                        removedFrom.Add((ri, rj));
                                    }
                                }
                            }
                            else
                            {
                                // The pivot does contain the shared digit among the pincers.
                                foreach (var (ri, rj) in SeenCellsByValueMask(combMask, (i0, j0), (i1, j1), (i2, j2), (pi, pj)))
                                {
                                    LogicResult removeResult = ClearMask(ri, rj, removeMask);
                                    if (removeResult == LogicResult.Invalid)
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    if (removeResult == LogicResult.Changed)
                                    {
                                        removedFrom.Add((ri, rj));
                                    }
                                }
                            }
                            if (removedFrom.Count > 0)
                            {
                                if (stepDescription != null)
                                {
                                    stepDescription.Append($"WXYZ-Wing with pivot at {CellName((pi, pj))} ({MaskToString(maskp)}) and pincers at {CellName((i0, j0))} ({MaskToString(mask0)}), {CellName((i1, j1))} ({MaskToString(mask1)}), {CellName((i2, j2))} ({MaskToString(mask2)}) clears candidate {GetValue(removeMask)} from cell{(removedFrom.Count == 1 ? "" : "s")}: ");
                                    bool needComma = false;
                                    foreach (var cell in removedFrom)
                                    {
                                        if (needComma)
                                        {
                                            stepDescription.Append(", ");
                                        }
                                        stepDescription.Append($"{CellName(cell)}");
                                        needComma = true;
                                    }
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            return LogicResult.None;
        }

        private LogicResult FindSimpleContradictions(StringBuilder stepDescription)
        {
            for (int allowedValueCount = 2; allowedValueCount <= MAX_VALUE; allowedValueCount++)
            {
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        uint cellMask = board[i, j];
                        if (!IsValueSet(cellMask) && ValueCount(cellMask) == allowedValueCount)
                        {
                            for (int v = 1; v <= MAX_VALUE; v++)
                            {
                                uint valueMask = ValueMask(v);
                                if ((cellMask & valueMask) != 0)
                                {
                                    Solver boardCopy = Clone();
                                    boardCopy.isBruteForcing = true;

                                    StringBuilder contradictionReason = stepDescription != null ? new() : null;
                                    if (!boardCopy.SetValue(i, j, v) || boardCopy.ConsolidateBoard(contradictionReason) == LogicResult.Invalid)
                                    {
                                        if (stepDescription != null)
                                        {
                                            StringBuilder formattedContraditionReason = new();
                                            if (contradictionReason.Length > 0)
                                            {
                                                foreach (string line in contradictionReason.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                                {
                                                    formattedContraditionReason.Append("  ").Append(line).AppendLine();
                                                }
                                            }
                                            else
                                            {
                                                formattedContraditionReason.Append("  ").Append("(For trivial reasons).").AppendLine();
                                            }

                                            stepDescription.Append($"Setting {CellName(i, j)} to {v} causes a contradiction:")
                                                .AppendLine()
                                                .Append(formattedContraditionReason);
                                        }
                                        if (!ClearValue(i, j, v))
                                        {
                                            if (stepDescription != null)
                                            {
                                                stepDescription.AppendLine();
                                                stepDescription.Append($"This clears the last candidate from {CellName(i, j)}.");
                                            }
                                            return LogicResult.Invalid;
                                        }
                                        return LogicResult.Changed;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return LogicResult.None;
        }
    }
}
