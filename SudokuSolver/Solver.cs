//#define PROFILING
//#define INFINITE_LOOP_CHECK

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
    public record SudokuGroup(string Name, List<(int, int)> Cells, Constraint FromConstraint)
    {
        public override string ToString() => Name;

        public bool MustContain(Solver solver, int val)
        {
            if (Cells.Count == solver.MAX_VALUE)
            {
                return true;
            }

            var mustContain = FromConstraint?.CellsMustContain(solver, val);
            return mustContain != null && mustContain.Count > 0;
        }

        public List<(int, int)> CellsMustContain(Solver solver, int val)
        {
            var board = solver.Board;
            if (Cells.Count == solver.MAX_VALUE)
            {
                return Cells.Where(cell => HasValue(board[cell.Item1, cell.Item2], val)).ToList();
            }
            if (FromConstraint != null)
            {
                return FromConstraint.CellsMustContain(solver, val);
            }
            return null;
        }
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
            foreach (var timer in timers.OrderByDescending(timer => timer.Value.Elapsed))
            {
                //if (timer.Key != "Global")
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
        public bool DisableTuples { get; set; } = false;
        public bool DisablePointing { get; set; } = false;
        public bool DisableFishes { get; set; } = false;
        public bool DisableWings { get; set; } = false;
        public bool DisableAIC { get; set; } = false;
        public bool DisableContradictions { get; set; } = false;
        public bool DisableFindShortestContradiction { get; set; } = false;
        public void SetToBasicsOnly()
        {
            DisableTuples = false;
            DisablePointing = false;
            DisableFishes = true;
            DisableWings = true;
            DisableAIC = true;
            DisableContradictions = true;
        }

        private uint[,] board;
        private int[,] regions = null;
        private readonly SortedSet<int>[] weakLinks;
        private SortedSet<int>[] CloneWeakLinks()
        {
            int numCandidates = HEIGHT * WIDTH * MAX_VALUE;
            SortedSet<int>[] newWeakLinks = new SortedSet<int>[numCandidates];
            for (int ci = 0; ci < numCandidates; ci++)
            {
                newWeakLinks[ci] = new(weakLinks[ci]);
            }
            return newWeakLinks;
        }

        public uint[,] Board => board;
        public int[,] Regions => regions;
        public SortedSet<int>[] WeakLinks => weakLinks;
        public Dictionary<string, object> customInfo = new();
        // Returns whether two cells cannot be the same value for a specific value
        // i0, j0, i1, j0, value or 0 for any value
        private bool[,,,,] seenMap;
        private bool canHaveUnorthodoxTuples = false;
        private bool isInSetValue = false;
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
                StringBuilder stringBuilder = new(flatBoard.Length * digitWidth);
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

        public string DistinguishedCandidateString
        {
            get
            {
                var flatBoard = FlatBoard;
                int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
                StringBuilder stringBuilder = new(flatBoard.Length * digitWidth);
                foreach (uint mask in flatBoard)
                {
                    if (IsValueSet(mask))
                    {
                        int setValue = GetValue(mask);
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (digitWidth == 2 && setValue <= 9)
                            {
                                stringBuilder.Append('0');
                            }
                            stringBuilder.Append(setValue);
                        }
                    }
                    else
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

            int numCandidates = HEIGHT * WIDTH * MAX_VALUE;
            weakLinks = new SortedSet<int>[numCandidates];
            for (int ci = 0; ci < numCandidates; ci++)
            {
                weakLinks[ci] = new();
            }
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
            DisableTuples = other.DisableTuples;
            DisablePointing = other.DisablePointing;
            DisableFishes = other.DisableFishes;
            DisableWings = other.DisableWings;
            DisableAIC = other.DisableAIC;
            DisableContradictions = other.DisableContradictions;
            DisableFindShortestContradiction = other.DisableFindShortestContradiction;
            board = (uint[,])other.board.Clone();
            regions = other.regions;
            seenMap = other.seenMap;
            canHaveUnorthodoxTuples = other.canHaveUnorthodoxTuples;
            constraints = other.constraints;
            Groups = other.Groups;
            smallGroupsBySize = other.smallGroupsBySize;
            CellToGroupMap = other.CellToGroupMap;
            customInfo = other.customInfo;

            // For now, weak links are constant after initialization
            // Constraints like arrow, killer, LK and the like could add more weak links during the solve,
            // but for now they will not.
            weakLinks = other.weakLinks;
            //weakLinks = other.CloneWeakLinks();
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
                SudokuGroup group = new($"Row {i + 1}", cells, null);
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
                SudokuGroup group = new($"Column {j + 1}", cells, null);
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
                SudokuGroup group = new($"Region {region + 1}", cells, null);
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
            string constraintName = constraint.GetType().FullName;
            if (!timers.ContainsKey(constraintName))
            {
                timers[constraintName] = new();
            }
#endif
            constraints.Add(constraint);
        }

        public void AddWeakLink(int candIndex0, int candIndex1)
        {
            if (candIndex0 != candIndex1)
            {
                var (i0, j0, v0) = CandIndexToCoord(candIndex0);
                var (i1, j1, v1) = CandIndexToCoord(candIndex1);
                if (HasValue(board[i0, j0], v0) && HasValue(board[i1, j1], v1))
                {
                    weakLinks[candIndex0].Add(candIndex1);
                    weakLinks[candIndex1].Add(candIndex0);
                }
            }
        }

        /// <summary>
        /// Call this once after all constraints are set, and before setting any values.
        /// </summary>
        /// <returns>True if the board is still valid. False if the constraints cause there to be trivially no solutions.</returns>
        public bool FinalizeConstraints()
        {
#if PROFILING
            if (!timers.ContainsKey("Global"))
            {
                timers["Global"] = Stopwatch.StartNew();

                timers["FindNakedSingles"] = new();
                timers["FindHiddenSingle"] = new();
                timers["FindDirectCellForcing"] = new();
                timers["FindNakedTuples"] = new();
                timers["FindPointingTuples"] = new();
                timers["FindUnorthodoxTuples"] = new();
                timers["FindFishes"] = new();
                timers["FindWings"] = new();
                timers["FindAIC"] = new();
                timers["FindSimpleContradictions"] = new();
            }
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
                    SudokuGroup group = new(constraint.SpecificName, cells.ToList(), constraint);
                    Groups.Add(group);
                    InitMapForGroup(group);
                }
            }

            smallGroupsBySize = Groups.Where(g => g.Cells.Count < MAX_VALUE).OrderBy(g => g.Cells.Count).ToList();
            if (smallGroupsBySize.Count == 0)
            {
                smallGroupsBySize = null;
            }

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

            for (int i0 = 0; i0 < HEIGHT; i0++)
            {
                for (int j0 = 0; j0 < WIDTH; j0++)
                {
                    int cellIndex = i0 * WIDTH + j0;
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        uint mask = ValueMask(v);
                        int candIndex0 = cellIndex * MAX_VALUE + v - 1;

                        // Add weak links to all seen cells
                        foreach (var (i1, j1) in SeenCellsByValueMask(mask, (i0, j0)))
                        {
                            int candIndex1 = CandidateIndex((i1, j1), v);
                            AddWeakLink(candIndex0, candIndex1);
                        }

                        // Add weak links to all other candidates within the same cell
                        for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                        {
                            if (v != v1)
                            {
                                int candIndex1 = CandidateIndex((i0, j0), v1);
                                AddWeakLink(candIndex0, candIndex1);
                            }
                        }
                    }
                }
            }

            foreach (var constraint in constraints)
            {
                constraint.InitLinks(this);
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

        public bool IsGroup(IEnumerable<(int, int)> cells, int value)
        {
            List<int> candIndexes = CandidateIndexes(ValueMask(value), cells);
            if (candIndexes.Count <= 1)
            {
                return true;
            }

            for (int i0 = 0; i0 < candIndexes.Count - 1; i0++)
            {
                int cand0 = candIndexes[i0];
                var weakLinks0 = weakLinks[cand0];
                for (int i1 = i0 + 1; i1 < candIndexes.Count; i1++)
                {
                    int cand1 = candIndexes[i1];
                    if (cand0 != cand1 && !weakLinks0.Contains(cand1))
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
        public bool ClearValue(int i, int j, int v)
        {
            board[i, j] &= ~ValueMask(v);
            return (board[i, j] & ~valueSetMask) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ClearCandidate(int candidate)
        {
            var (i, j, v) = CandIndexToCoord(candidate);
            return ClearValue(i, j, v);
        }

        internal bool ClearCandidates(IEnumerable<int> candidates)
        {
            bool valid = true;
            foreach (int c in candidates)
            {
                if (!ClearCandidate(c))
                {
                    valid = false;
                }
            }
            return valid;
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

            if (isInSetValue)
            {
                board[i, j] = valMask;
                return true;
            }

            isInSetValue = true;

            board[i, j] = valueSetMask | valMask;

            // Apply all weak links
            int setCandidateIndex = CandidateIndex((i, j), val);
            foreach (int elimCandIndex in weakLinks[setCandidateIndex])
            {
                var (i1, j1, v1) = CandIndexToCoord(elimCandIndex);
                if (!ClearValue(i1, j1, v1))
                {
                    return false;
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
            isInSetValue = false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SetMask(int i, int j, uint mask)
        {
            if ((mask & ~valueSetMask) == 0)
            {
                return false;
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
        public bool SetMask(int i, int j, IEnumerable<int> values)
        {
            return SetMask(i, j, values.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogicResult KeepMask(int i, int j, uint mask)
        {
            mask &= ALL_VALUES_MASK;
            if (mask == ALL_VALUES_MASK)
            {
                return LogicResult.None;
            }

            isInSetValue = true;

            LogicResult result = LogicResult.None;
            uint curMask = board[i, j] & ~valueSetMask;
            uint newMask = curMask & mask;
            if (newMask != curMask)
            {
                result = SetMask(i, j, newMask) ? LogicResult.Changed : LogicResult.Invalid;
            }

            isInSetValue = false;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogicResult ClearMask(int i, int j, uint mask)
        {
            mask &= ALL_VALUES_MASK;
            if (mask == 0)
            {
                return LogicResult.None;
            }
            isInSetValue = true;

            LogicResult result = LogicResult.None;
            uint curMask = board[i, j];
            uint newMask = curMask & ~mask;
            if (newMask != curMask)
            {
                result = SetMask(i, j, newMask) ? LogicResult.Changed : LogicResult.Invalid;
            }

            isInSetValue = false;
            return result;
        }

        private (int, int, int) GetLeastCandidateCell(bool[] ignoreCell = null)
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
                                return (x, y, 0);
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
                    return (i, j, 0);
                }
            }

            if (ignoreCell == null)
            {
                for (int x = 0; x < HEIGHT; x++)
                {
                    for (int y = 0; y < WIDTH; y++)
                    {
                        if (!IsValueSet(x, y))
                        {
                            int curNumCandidates = ValueCount(board[x, y]);
                            if (curNumCandidates == 2)
                            {
                                return (x, y, 0);
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
            }
            else
            {
                for (int x = 0; x < HEIGHT; x++)
                {
                    for (int y = 0; y < WIDTH; y++)
                    {
                        if (!IsValueSet(x, y) && !ignoreCell[x * WIDTH + y])
                        {
                            int curNumCandidates = ValueCount(board[x, y]);
                            if (curNumCandidates == 2)
                            {
                                return (x, y, 0);
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
            }

            if (numCandidates > 3)
            {
                var (bi, bj, bval) = FindBilocalValue();
                if (bval > 0)
                {
                    return (bi, bj, bval);
                }
            }

            return (i, j, 0);
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
        /// Finds a single solution to the board. This may not be the only solution.
        /// For the exact same board inputs, the solution will always be the same.
        /// The board itself is modified to have the solution as its board values.
        /// If no solution is found, the board is left in an invalid state.
        /// </summary>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns>True if a solution is found, otherwise false.</returns>
        public bool FindSolution(bool multiThread = false, CancellationToken cancellationToken = default, bool isRandom = false)
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

            using FindSolutionState state = new(isRandom, cancellationToken);
            FindSolutionMultiThreaded(solver, state);
            state.Wait();
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            if (state.result != null)
            {
                board = state.result;
            }
            return state.result != null;
        }

        private static bool FindSolutionSingleThreaded(Solver solver, CancellationToken cancellationToken, bool isRandom)
        {
            Solver initialSolver = solver;

            var boardStack = new Stack<Solver>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var logicResult = solver.ConsolidateBoard();
                if (logicResult == LogicResult.PuzzleComplete)
                {
                    initialSolver.board = solver.board;
                    return true;
                }

                if (logicResult != LogicResult.Invalid)
                {
                    (int i, int j, int v) = solver.GetLeastCandidateCell();
                    if (i < 0)
                    {
                        initialSolver.board = solver.board;
                        return true;
                    }

                    // Try a possible value for this cell
                    int val = v != 0 ? v : isRandom ? GetRandomValue(solver.board[i, j]) : MinValue(solver.board[i, j]);
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
            public CancellationToken cancellationToken;
            public object locker = new();
            public bool isRandom = false;

            private int numRunningTasks = 0;
            private readonly Stack<Solver> pendingSolvers = new();
            private readonly int maxRunningTasks;

            public FindSolutionState(bool isRandom, CancellationToken cancellationToken)
            {
                this.cancellationToken = cancellationToken;
                this.isRandom = isRandom;

                maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
            }

            public void PushSolver(Solver solver)
            {
                lock (locker)
                {
                    if (numRunningTasks < maxRunningTasks)
                    {
                        numRunningTasks++;
                        Task.Run(() => FindSolutionMultiThreaded(solver, this), cancellationToken);
                        countdownEvent.AddCount();
                        return;
                    }

                    pendingSolvers.Push(solver);
                }
            }

            public void TaskComplete()
            {
                lock (locker)
                {
                    if (pendingSolvers.TryPop(out Solver solver))
                    {
                        Task.Run(() => FindSolutionMultiThreaded(solver, this), cancellationToken);
                    }
                    else
                    {
                        numRunningTasks--;
                        countdownEvent.Signal();
                    }
                }
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
                countdownEvent.Wait(cancellationToken);
            }
        }

        private static void FindSolutionMultiThreaded(Solver solver, FindSolutionState state)
        {
            while (true)
            {
                state.cancellationToken.ThrowIfCancellationRequested();
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

                (int i, int j, int v) = solver.GetLeastCandidateCell();
                if (i < 0)
                {
                    state.ReportSolution(solver);
                    break;
                }

                // Try a possible value for this cell
                int val = v != 0 ? v : state.isRandom ? GetRandomValue(solver.board[i, j]) : MinValue(solver.board[i, j]);
                uint valMask = ValueMask(val);

                // Create a backup board in case it needs to be restored
                Solver newSolver = solver.Clone();
                newSolver.isBruteForcing = true;
                newSolver.board[i, j] &= ~valMask;
                if (newSolver.board[i, j] != 0)
                {
                    state.PushSolver(newSolver);
                }

                // Change the board to only allow this value in the slot
                if (!solver.SetValue(i, j, val))
                {
                    break;
                }
            }
            state.TaskComplete();
        }

        /// <summary>
        /// Determine how many solutions the board has.
        /// </summary>
        /// <param name="maxSolutions">The maximum number of solutions to find. Pass 0 for no maximum.</param>
        /// <param name="multiThread">Whether to use multiple threads.</param>
        /// <param name="progressEvent">An event to receive the progress count as solutions are found.</param>
        /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
        /// <returns>The solution count found.</returns>
        public ulong CountSolutions(ulong maxSolutions = 0, bool multiThread = false, Action<ulong> progressEvent = null, Action<Solver> solutionEvent = null, HashSet<string> skipSolutions = null, CancellationToken cancellationToken = default)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            using CountSolutionsState state = new(maxSolutions, multiThread, progressEvent, solutionEvent, skipSolutions, cancellationToken);
            try
            {
                Solver boardCopy = Clone();
                boardCopy.isBruteForcing = true;
                if (state.multiThread)
                {
                    CountSolutionsMultiThreaded(boardCopy, state);
                    state.Wait();
                }
                else
                {
                    CountSolutionsSingleThread(boardCopy, state);
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
            public readonly HashSet<string> skipSolutions;
            public readonly CancellationToken cancellationToken;
            public readonly CountdownEvent countdownEvent;

            private readonly object solutionLock = new();
            private readonly Stopwatch eventTimer;

            private int numRunningTasks = 0;
            private readonly Stack<Solver> pendingSolvers;
            private readonly int maxRunningTasks;
            private readonly bool fastIncrement;

            public CountSolutionsState(ulong maxSolutions, bool multiThread, Action<ulong> progressEvent, Action<Solver> solutionEvent, HashSet<string> skipSolutions, CancellationToken cancellationToken)
            {
                this.maxSolutions = maxSolutions;
                this.multiThread = multiThread;
                this.progressEvent = progressEvent;
                this.solutionEvent = solutionEvent;
                this.skipSolutions = skipSolutions;
                this.cancellationToken = cancellationToken;
                eventTimer = Stopwatch.StartNew();
                countdownEvent = multiThread ? new CountdownEvent(1) : null;
                pendingSolvers = multiThread ? new Stack<Solver>() : null;
                maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
                fastIncrement = skipSolutions == null && solutionEvent == null;
            }

            public void IncrementSolutions(Solver solver)
            {
                bool invokeProgress = false;
                if (fastIncrement)
                {
                    Interlocked.Increment(ref numSolutions);
                    if (eventTimer.ElapsedMilliseconds > 500)
                    {
                        lock (solutionLock)
                        {
                            if (eventTimer.ElapsedMilliseconds > 500)
                            {
                                invokeProgress = true;
                                eventTimer.Restart();
                            }
                        }
                    }
                }
                else
                {
                    lock (solutionLock)
                    {
                        if (skipSolutions != null && skipSolutions.Contains(solver.GivenString))
                        {
                            return;
                        }

                        solutionEvent?.Invoke(solver);

                        numSolutions++;
                        if (eventTimer.ElapsedMilliseconds > 500)
                        {
                            invokeProgress = true;
                            eventTimer.Restart();
                        }
                    }
                }
                if (invokeProgress)
                {
                    progressEvent?.Invoke(numSolutions);
                }
            }

            public void PushSolver(Solver solver)
            {
                lock (solutionLock)
                {
                    if (numRunningTasks < maxRunningTasks)
                    {
                        numRunningTasks++;
                        Task.Run(() => CountSolutionsMultiThreaded(solver, this));
                        countdownEvent.AddCount();
                        return;
                    }

                    pendingSolvers.Push(solver);
                }
            }

            public Solver TaskComplete()
            {
                Solver solver = null;
                lock (solutionLock)
                {
                    if (!(maxSolutions <= 0 || numSolutions < maxSolutions) || !pendingSolvers.TryPop(out solver))
                    {
                        numRunningTasks--;
                        countdownEvent.Signal();
                    }
                }
                return solver;
            }

            public void Wait()
            {
                countdownEvent.Wait(cancellationToken);
            }

            public void Dispose()
            {
                ((IDisposable)countdownEvent)?.Dispose();
            }
        }

        private static void CountSolutionsSingleThread(Solver solver, CountSolutionsState state)
        {
            var boardStack = new Stack<Solver>();
            while (true)
            {
                state.cancellationToken.ThrowIfCancellationRequested();

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
                    (int i, int j, int v) = solver.GetLeastCandidateCell();
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
                        int val = v != 0 ? v : MinValue(solver.board[i, j]);
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

        private static void CountSolutionsMultiThreaded(Solver solver, CountSolutionsState state)
        {
            while (true)
            {
                while (true)
                {
                    // If reached max solutions, bail out
                    if (state.maxSolutions > 0 && state.numSolutions >= state.maxSolutions)
                    {
                        break;
                    }

                    state.cancellationToken.ThrowIfCancellationRequested();

                    var logicResult = solver.ConsolidateBoard();
                    if (logicResult == LogicResult.PuzzleComplete)
                    {
                        state.IncrementSolutions(solver);
                        break;
                    }

                    if (logicResult == LogicResult.Invalid)
                    {
                        break;
                    }

                    // Start with the cell that has the least possible candidates
                    (int i, int j, int v) = solver.GetLeastCandidateCell();
                    if (i < 0)
                    {
                        state.IncrementSolutions(solver);
                        break;
                    }

                    // Try a possible value for this cell
                    int val = v != 0 ? v : state.skipSolutions != null ? GetRandomValue(solver.board[i, j]) : MinValue(solver.board[i, j]);
                    uint valMask = ValueMask(val);

                    // Create a solver without this value and start a task for it
                    Solver newSolver = solver.Clone();
                    newSolver.isBruteForcing = true;
                    newSolver.board[i, j] &= ~valMask;
                    if (newSolver.board[i, j] != 0)
                    {
                        state.PushSolver(newSolver);
                    }

                    if (!solver.SetValue(i, j, val))
                    {
                        break;
                    }
                }

                solver = state.TaskComplete();
                if (solver == null)
                {
                    break;
                }
            }
        }

        private class FillRealCandidatesState
        {
            public readonly uint[] fixedBoard;
            public readonly bool[] candidatesFixed;
            public readonly int[] tasksRemainingPerCell;
            public readonly int[] numSolutions;
            public readonly HashSet<string> solutionsCounted;

            public readonly Action<uint[]> progressEvent;
            public readonly Stopwatch eventTimer = Stopwatch.StartNew();

            public readonly CancellationToken cancellationToken;
            public readonly bool multiThread;
            public bool boardInvalid = false;

            public FillRealCandidatesState(bool multiThread, int numCells, Action<uint[]> progressEvent, int[] numSolutions, CancellationToken cancellationToken)
            {
                fixedBoard = new uint[numCells];
                candidatesFixed = new bool[numCells];
                tasksRemainingPerCell = new int[numCells];
                this.progressEvent = progressEvent;
                this.cancellationToken = cancellationToken;
                this.multiThread = multiThread;
                this.numSolutions = numSolutions;
                solutionsCounted = (numSolutions != null) ? new() : null;
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
        /// <param name="multiThread">If true, uses multiple threads to calculate.</param>
        /// <param name="skipConsolidate">If true, the initial consolidate board is skipped.</param>
        /// <param name="progressEvent">Recieve progress notifications. Sends the true candidates currently found.</param>
        /// <param name="numSolutions">Expected to be HEIGHT * WIDTH * MAX_VALUE in size. The number of solutions per candidate, capped to 9.</param>
        /// <param name="cancellationToken">Pass in to support cancelling.</param>
        /// <returns>True if there are solutions and candidates are filled. False if there are no solutions.</returns>
        public bool FillRealCandidates(bool multiThread = false, Action<uint[]> progressEvent = null, int[] numSolutions = null, CancellationToken cancellationToken = default)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            if (numSolutions != null && numSolutions.Length != HEIGHT * WIDTH * MAX_VALUE)
            {
                throw new InvalidOperationException($"numSolutions was incorrect size. Expected {HEIGHT * WIDTH * MAX_VALUE} got {numSolutions.Length}");
            }

            Stopwatch timeSinceCheck = Stopwatch.StartNew();

            LogicResult logicResult = PrepForBruteForce();
            if (logicResult == LogicResult.Invalid)
            {
                return false;
            }

            isBruteForcing = true;
            FillRealCandidatesState state = new(multiThread, NUM_CELLS, progressEvent, numSolutions, cancellationToken);
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

            int numUnsetCells = logicResult == LogicResult.PuzzleComplete ? 0 : NUM_CELLS - NumSetValues;
            List<(int, int, int, int)> cellValuesByPriority = null;
            if (numUnsetCells > 0)
            {
                cellValuesByPriority = new(numUnsetCells);
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
            }

            if (cellValuesByPriority == null || cellValuesByPriority.Count == 0)
            {
                if (numSolutions != null)
                {
                    for (int i = 0; i < HEIGHT; i++)
                    {
                        for (int j = 0; j < WIDTH; j++)
                        {
                            uint mask = Board[i, j];
                            int value = GetValue(mask);
                            numSolutions[(i * WIDTH + j) * MAX_VALUE + (value - 1)] = 1;
                        }
                    }
                }
            }
            else
            {
                cellValuesByPriority.Sort((a, b) => b.Item1.CompareTo(a.Item1));

                try
                {
                    foreach (var (p, i, j, v) in cellValuesByPriority)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

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
                    isBruteForcing = false;
                    return false;
                }
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
                    uint mask = state.fixedBoard[i * WIDTH + j];
                    if (!IsValueSet(mask) && ValueCount(mask) == 1)
                    {
                        SetValue(i, j, GetValue(mask));
                    }
                    else
                    {
                        SetMask(i, j, state.fixedBoard[i * WIDTH + j]);
                    }
                }
            }
            isBruteForcing = false;
            return true;
        }

        private void FillRealCandidateAction(int i, int j, int v, FillRealCandidatesState state)
        {
            int cellIndex = i * WIDTH + j;
            int numSolutionsIndex = cellIndex * MAX_VALUE + (v - 1);
            uint valMask = ValueMask(v);

            // Don't bother trying this value if it's already confirmed in the fixed board
            if (!state.boardInvalid && ((state.fixedBoard[cellIndex] & valMask) == 0 || state.numSolutions != null && state.numSolutions[numSolutionsIndex] < 8))
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
                if (boardCopy.SetValue(i, j, v))
                {
                    if (state.numSolutions == null)
                    {
                        if (boardCopy.FindSolution(multiThread: state.multiThread, cancellationToken: state.cancellationToken, isRandom: true))
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
                    else
                    {
                        int numSolutionsNeeded = 8 - state.numSolutions[numSolutionsIndex];
                        int numSolutions = (int)boardCopy.CountSolutions(
                            maxSolutions: (uint)numSolutionsNeeded,
                            multiThread: state.multiThread,
                            skipSolutions: state.solutionsCounted,
                            cancellationToken: state.cancellationToken,
                            solutionEvent: (curSolutionBoard) =>
                            {
                                state.solutionsCounted.Add(curSolutionBoard.GivenString);

                                for (int si = 0; si < HEIGHT; si++)
                                {
                                    for (int sj = 0; sj < WIDTH; sj++)
                                    {
                                        uint solutionValMask = curSolutionBoard.board[si, sj] & ~valueSetMask;
                                        int cellIndex = si * WIDTH + sj;
                                        state.fixedBoard[cellIndex] |= solutionValMask;
                                        state.numSolutions[cellIndex * MAX_VALUE + GetValue(curSolutionBoard.board[si, sj]) - 1]++;
                                    }
                                }
                            }
                        );
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

#if INFINITE_LOOP_CHECK
        public bool IsSame(Solver other)
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    if (other.Board[i, j] != Board[i, j])
                    {
                        return false;
                    }
                }
            }
            return true;
        }
#endif

        /// <summary>
        /// Perform a logical solve until either the board is solved or there are no logical steps found.
        /// </summary>
        /// <param name="stepsDescription">Get a full description of all logical steps taken.</param>
        /// <returns></returns>
        public LogicResult ConsolidateBoard(List<LogicalStepDesc> logicalStepDescs = null)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            bool changed = false;
            LogicResult result;
            do
            {
#if INFINITE_LOOP_CHECK
                Solver clone = Clone();
#endif
                if (!IsBoardValid(logicalStepDescs))
                {
                    result = LogicResult.Invalid;
                }
                else
                {
                    result = StepLogic(logicalStepDescs);
                }
#if INFINITE_LOOP_CHECK
                if (result == LogicResult.Changed && IsSame(clone))
                {
                    throw new InvalidOperationException("Logic step returned a change, but no changed to candidates occured.");
                }
#endif
                changed |= result == LogicResult.Changed;
            } while (result == LogicResult.Changed);

            return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
        }

        public LogicResult PrepForBruteForce()
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            DisableTuples = false;
            DisablePointing = false;
            DisableFishes = false;
            DisableWings = true;
            DisableAIC = true;
            DisableContradictions = false;
            DisableFindShortestContradiction = true;

            bool changed = false;
            LogicResult result;
            do
            {
#if INFINITE_LOOP_CHECK
                Solver clone = Clone();
#endif
                result = StepLogic(null);
#if INFINITE_LOOP_CHECK
                if (result == LogicResult.Changed && IsSame(clone))
                {
                    throw new InvalidOperationException("Logic step returned a change, but no changed to candidates occured.");
                }
#endif
                changed |= result == LogicResult.Changed;
            } while (result == LogicResult.Changed);

            return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
        }

        public LogicResult ApplySingles()
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            bool changed = false;
            LogicResult result;
            do
            {
                result = FindSingles();
                changed |= result == LogicResult.Changed;
            } while (result == LogicResult.Changed);

            return (result == LogicResult.None && changed) ? LogicResult.Changed : result;
        }

        private LogicResult FindSingles()
        {
            LogicResult result = FindNakedSingles(null);
            if (result == LogicResult.None)
            {
                result = FindHiddenSingle(null);
            }
            return result;
        }

        /// <summary>
        /// Perform one step of a logical solve and fill a description of the step taken.
        /// The description will contain the reason the board is invalid if that is what is returned.
        /// </summary>
        /// <param name="stepDescription"></param>
        /// <returns></returns>
        public LogicResult StepLogic(List<LogicalStepDesc> logicalStepDescs)
        {
            if (seenMap == null)
            {
                throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
            }

            LogicResult result = LogicResult.None;

#if PROFILING
            timers["FindNakedSingles"].Start();
#endif
            result = FindNakedSingles(logicalStepDescs);
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
            result = FindHiddenSingle(logicalStepDescs);
#if PROFILING
            timers["FindHiddenSingle"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

#if PROFILING
            timers["FindDirectCellForcing"].Start();
#endif
            result = FindDirectCellForcing(logicalStepDescs);
#if PROFILING
            timers["FindDirectCellForcing"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

            foreach (var constraint in constraints)
            {
#if PROFILING
                string constraintName = constraint.GetType().FullName;
                timers[constraintName].Start();
#endif
                result = constraint.StepLogic(this, logicalStepDescs, isBruteForcing);
#if PROFILING
                timers[constraintName].Stop();
#endif
                if (result != LogicResult.None)
                {
                    if (logicalStepDescs != null && logicalStepDescs.Count > 0)
                    {
                        logicalStepDescs[^1] = logicalStepDescs[^1].WithPrefix($"[{constraint.SpecificName}] ");
                    }
                    return result;
                }
            }

            if (isBruteForcing)
            {
                return LogicResult.None;
            }

            if (!DisableTuples)
            {
#if PROFILING
                timers["FindNakedTuples"].Start();
#endif
                result = FindNakedTuples(logicalStepDescs);
#if PROFILING
                timers["FindNakedTuples"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

            if (!DisablePointing)
            {
#if PROFILING
                timers["FindPointingTuples"].Start();
#endif
                result = FindPointingTuples(logicalStepDescs);
#if PROFILING
                timers["FindPointingTuples"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

            if (!DisableTuples)
            {
#if PROFILING
                timers["FindUnorthodoxTuples"].Start();
#endif
                result = FindUnorthodoxTuples(logicalStepDescs);
#if PROFILING
                timers["FindUnorthodoxTuples"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

            if (!DisableFishes)
            {
#if PROFILING
                timers["FindFishes"].Start();
#endif
                result = FindFishes(logicalStepDescs);
#if PROFILING
                timers["FindFishes"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

            if (!DisableWings)
            {
#if PROFILING
                timers["FindWings"].Start();
#endif
                result = FindWings(logicalStepDescs);
#if PROFILING
                timers["FindWings"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

            if (!DisableAIC)
            {
#if PROFILING
                timers["FindAIC"].Start();
#endif
                result = FindAIC(logicalStepDescs);
#if PROFILING
                timers["FindAIC"].Stop();
#endif
                if (result != LogicResult.None)
                {
                    return result;
                }
            }

#if PROFILING
            timers["FindSimpleContradictions"].Start();
#endif
            result = FindSimpleContradictions(logicalStepDescs);
#if PROFILING
            timers["FindSimpleContradictions"].Stop();
#endif
            if (result != LogicResult.None)
            {
                return result;
            }

            return LogicResult.None;
        }

        private bool IsBoardValid(List<LogicalStepDesc> logicalStepDescs)
        {
            // Check for empty cells
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    if ((board[i, j] & ~valueSetMask) == 0)
                    {
                        logicalStepDescs?.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                        return false;
                    }
                }
            }

            // Check for values that groups must contain but don't
            foreach (var group in Groups)
            {
                var groupCells = group.Cells;
                int numCells = group.Cells.Count;
                if (numCells != MAX_VALUE)
                {
                    continue;
                }

                uint atLeastOnce = 0;
                for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
                {
                    var (i, j) = groupCells[cellIndex];
                    atLeastOnce |= board[i, j];
                }
                atLeastOnce &= ~valueSetMask;

                if (atLeastOnce != ALL_VALUES_MASK && numCells == MAX_VALUE)
                {
                    logicalStepDescs?.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~atLeastOnce)}.", group.Cells));
                    return false;
                }
            }

            if (isBruteForcing)
            {
                return true;
            }

            // Check for groups which contain too many cells with a tuple that is too small
            List<(int, int)> unsetCells = new(MAX_VALUE);
            foreach (var group in Groups)
            {
                for (int tupleSize = 2; tupleSize < MAX_VALUE; tupleSize++)
                {
                    unsetCells.Clear();
                    foreach (var cell in group.Cells)
                    {
                        uint cellMask = board[cell.Item1, cell.Item2];
                        if (!IsValueSet(cellMask))
                        {
                            unsetCells.Add(cell);
                        }
                    }

                    if (unsetCells.Count < tupleSize)
                    {
                        continue;
                    }

                    foreach (var tupleCells in unsetCells.Combinations(tupleSize))
                    {
                        uint tupleMask = CandidateMask(tupleCells);
                        if (ValueCount(tupleMask) < tupleSize)
                        {
                            logicalStepDescs?.Add(new($"{CompactName(tupleCells)} in {group} are {tupleCells.Count} cells with only {ValueCount(tupleMask)} candidates available ({MaskToString(tupleMask)}).", tupleCells));
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private LogicResult FindNakedSingles(List<LogicalStepDesc> logicalStepDescs)
        {
            bool hasUnsetCells = false;
            if (logicalStepDescs == null)
            {
                bool changed = false;
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        uint mask = board[i, j];
                        if ((mask & ~valueSetMask) == 0)
                        {
                            return LogicResult.Invalid;
                        }

                        if (!IsValueSet(mask))
                        {
                            hasUnsetCells = true;

                            if (ValueCount(mask) == 1)
                            {
                                int value = GetValue(mask);
                                if (!SetValue(i, j, value))
                                {
                                    return LogicResult.Invalid;
                                }
                                changed = true;
                            }
                        }
                    }
                }
                if (changed)
                {
                    return LogicResult.Changed;
                }
            }
            else
            {
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        uint mask = board[i, j];
                        if ((mask & ~valueSetMask) == 0)
                        {
                            logicalStepDescs.Add(new($"{CellName(i, j)} has no possible values.", (i, j)));
                            return LogicResult.Invalid;
                        }

                        if (!IsValueSet(mask))
                        {
                            hasUnsetCells = true;

                            if (ValueCount(mask) == 1)
                            {
                                int value = GetValue(mask);
                                if (!SetValue(i, j, value))
                                {
                                    logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)} cannot be set to {value}.", (i, j)));
                                    return LogicResult.Invalid;
                                }
                                logicalStepDescs.Add(new($"Naked Single: {CellName(i, j)}={value}", CandidateIndex((i, j), value).ToEnumerable(), null, isSingle: true));
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }
            return !hasUnsetCells ? LogicResult.PuzzleComplete : LogicResult.None;
        }

        private LogicResult FindHiddenSingle(List<LogicalStepDesc> logicalStepDescs)
        {
            foreach (var group in Groups)
            {
                var groupCells = group.Cells;
                int numCells = group.Cells.Count;
                if (numCells != MAX_VALUE && (isBruteForcing || group.FromConstraint == null))
                {
                    continue;
                }

                uint atLeastOnce = 0;
                uint moreThanOnce = 0;
                uint setMask = 0;
                for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
                {
                    var (i, j) = groupCells[cellIndex];
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        setMask |= mask;
                    }
                    else
                    {
                        moreThanOnce |= atLeastOnce & mask;
                        atLeastOnce |= mask;
                    }
                }
                setMask &= ~valueSetMask;
                if (numCells == MAX_VALUE && (atLeastOnce | setMask) != ALL_VALUES_MASK)
                {
                    logicalStepDescs?.Add(new($"{group} has nowhere to place {MaskToString(ALL_VALUES_MASK & ~(atLeastOnce | setMask))}.", group.Cells));
                    return LogicResult.Invalid;
                }

                uint exactlyOnce = atLeastOnce & ~moreThanOnce;
                if (exactlyOnce != 0)
                {
                    int val = 0;
                    int vali = -1;
                    int valj = -1;
                    if (numCells == MAX_VALUE)
                    {
                        val = MinValue(exactlyOnce);
                        uint valMask = ValueMask(val);
                        foreach (var (i, j) in group.Cells)
                        {
                            if ((board[i, j] & valMask) != 0)
                            {
                                vali = i;
                                valj = j;
                                break;
                            }
                        }
                    }
                    else
                    {
                        int minValue = MinValue(exactlyOnce);
                        int maxValue = MaxValue(exactlyOnce);
                        for (int v = minValue; v <= maxValue; v++)
                        {
                            if ((exactlyOnce & v) != 0)
                            {
                                List<(int, int)> cellsMustContain = group.FromConstraint?.CellsMustContain(this, val);
                                if (cellsMustContain != null && cellsMustContain.Count == 1)
                                {
                                    val = v;
                                    vali = cellsMustContain[0].Item1;
                                    valj = cellsMustContain[0].Item2;
                                    break;
                                }
                            }
                        }
                    }

                    if (vali >= 0)
                    {
                        if (!SetValue(vali, valj, val))
                        {
                            logicalStepDescs?.Add(new($"Hidden Single in {group}: {CellName(vali, valj)} cannot be set to {val}.", (vali, valj)));
                            return LogicResult.Invalid;
                        }
                        logicalStepDescs?.Add(new($"Hidden Single in {group}: {CellName(vali, valj)}={val}", CandidateIndex((vali, valj), val).ToEnumerable(), null, isSingle: true));
                        return LogicResult.Changed;
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindDirectCellForcing(List<LogicalStepDesc> logicalStepDescs)
        {
            if (isBruteForcing)
            {
                return LogicResult.None;
            }

            SortedSet<int> elimSet = new();
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (IsValueSet(mask))
                    {
                        continue;
                    }

                    bool isFirstElimArray = true;
                    elimSet.Clear();
                    int candBase = (i * HEIGHT + j) * MAX_VALUE;
                    int minVal = MinValue(mask);
                    int maxVal = MaxValue(mask);
                    for (int v = minVal; v <= maxVal; v++)
                    {
                        if (HasValue(mask, v))
                        {
                            int candIndex = candBase + v - 1;
                            if (isFirstElimArray)
                            {
                                elimSet.UnionWith(weakLinks[candIndex]);
                                isFirstElimArray = false;
                            }
                            else
                            {
                                elimSet.IntersectWith(weakLinks[candIndex]);
                            }

                            if (elimSet.Count == 0)
                            {
                                break;
                            }
                        }
                    }

                    if (elimSet.Count > 0)
                    {
                        List<int> elims = elimSet.Where(IsCandIndexValid).ToList();
                        if (elims.Count > 0)
                        {
                            List<(int, int)> sourceCell = new() { (i, j) };
                            logicalStepDescs?.Add(new(
                                desc: $"Direct Cell Forcing: {CompactName(mask, sourceCell)} => {DescribeElims(elims)}",
                                sourceCandidates: CandidateIndexes(mask, sourceCell),
                                elimCandidates: elims
                            ));
                            if (!ClearCandidates(elims))
                            {
                                return LogicResult.Invalid;
                            }
                            return LogicResult.Changed;
                        }
                    }
                }
            }
            return LogicResult.None;
        }

        private (int, int, int) FindBilocalValue()
        {
            foreach (var group in Groups)
            {
                var groupCells = group.Cells;
                int numCells = group.Cells.Count;
                if (numCells != MAX_VALUE)
                {
                    continue;
                }

                uint atLeastOnce = 0;
                uint atLeastTwice = 0;
                uint moreThanTwice = 0;
                for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
                {
                    var (i, j) = groupCells[cellIndex];
                    uint mask = board[i, j];
                    moreThanTwice |= atLeastTwice & mask;
                    atLeastTwice |= atLeastOnce & mask;
                    atLeastOnce |= mask;
                }

                uint exactlyTwice = atLeastTwice & ~moreThanTwice & ~valueSetMask;
                if (exactlyTwice != 0)
                {
                    int val = MinValue(exactlyTwice);
                    uint valMask = ValueMask(val);
                    foreach (var (i, j) in group.Cells)
                    {
                        if ((board[i, j] & valMask) != 0)
                        {
                            return (i, j, val);
                        }
                    }
                }
            }
            return (-1, -1, 0);
        }

        private LogicResult FindNakedTuples(List<LogicalStepDesc> logicalStepDescs)
        {
            List<(int, int)> unsetCells = new(MAX_VALUE);
            for (int tupleSize = 2; tupleSize < MAX_VALUE; tupleSize++)
            {
                foreach (var group in Groups)
                {
                    if (group.Cells.Count < tupleSize)
                    {
                        continue;
                    }

                    // Make a list of cells which aren't already set and contain fewer candidates than the tuple size
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

                    foreach (var tupleCells in unsetCells.Combinations(tupleSize))
                    {
                        uint tupleMask = CandidateMask(tupleCells);
                        if (ValueCount(tupleMask) == tupleSize)
                        {
                            var elims = CalcElims(tupleMask, tupleCells);
                            if (elims.Count > 0)
                            {
                                logicalStepDescs?.Add(new(
                                    desc: $"Tuple: {CompactName(tupleMask, tupleCells)} in {group} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(tupleMask, tupleCells),
                                    elimCandidates: elims
                                ));
                                if (!ClearCandidates(elims))
                                {
                                    return LogicResult.Invalid;
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindPointingTuples(List<LogicalStepDesc> logicalStepDescs)
        {
            foreach (var group in Groups)
            {
                if (group.Cells.Count != MAX_VALUE && group.FromConstraint == null)
                {
                    continue;
                }

                uint setValuesMask = 0;
                uint unsetValuesMask = 0;
                foreach (var cell in group.Cells)
                {
                    uint mask = board[cell.Item1, cell.Item2];
                    if (ValueCount(mask) == 1)
                    {
                        setValuesMask |= mask;
                    }
                    else
                    {
                        unsetValuesMask |= mask;
                    }
                }

                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if (HasValue(setValuesMask, v) || !HasValue(unsetValuesMask, v))
                    {
                        continue;
                    }

                    List<(int, int)> cellsMustContain = group.CellsMustContain(this, v);
                    if (cellsMustContain == null || cellsMustContain.Count <= 1)
                    {
                        continue;
                    }

                    uint valueMask = ValueMask(v);
                    var elims = CalcElims(valueMask, cellsMustContain);
                    if (elims == null || elims.Count == 0)
                    {
                        continue;
                    }

                    logicalStepDescs?.Add(new(
                                    desc: $"Pointing: {v}{CompactName(cellsMustContain)} in {group} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(valueMask, cellsMustContain),
                                    elimCandidates: elims
                                ));
                    if (!ClearCandidates(elims))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindUnorthodoxTuples(List<LogicalStepDesc> logicalStepDescs)
        {
            if (!canHaveUnorthodoxTuples)
            {
                return LogicResult.None;
            }

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
                        if (!IsGroup(possibleCells))
                        {
                            continue;
                        }

                        var elims = CalcElims(tupleValuesMask, possibleCells);
                        if (elims == null || elims.Count == 0)
                        {
                            continue;
                        }

                        logicalStepDescs?.Add(new(
                                    desc: $"Unorthodox Tuple: {CompactName(tupleValuesMask, possibleCells)} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(tupleValuesMask, possibleCells),
                                    elimCandidates: elims
                                ));
                        if (!ClearCandidates(elims))
                        {
                            return LogicResult.Invalid;
                        }
                        return LogicResult.Changed;
                    }
                }
            }
            return LogicResult.None;
        }

        private LogicResult FindFishes(List<LogicalStepDesc> logicalStepDescs)
        {
            if (WIDTH != MAX_VALUE || HEIGHT != MAX_VALUE)
            {
                return LogicResult.None;
            }
            // Since these are all guaranteed equal at this point, only MAX_VALUE will be used for all three purposes.

            // Construct a transformed lookup of which values are in which rows/cols
            uint[][,] rowcolIndexByValue = new uint[2][,];
            rowcolIndexByValue[0] = new uint[MAX_VALUE, MAX_VALUE];
            rowcolIndexByValue[1] = new uint[MAX_VALUE, MAX_VALUE];
            for (int i = 0; i < MAX_VALUE; i++)
            {
                for (int j = 0; j < MAX_VALUE; j++)
                {
                    uint mask = board[i, j] & ~valueSetMask;
                    int minVal = MinValue(mask);
                    int maxVal = MaxValue(mask);
                    for (int v = minVal; v <= maxVal; v++)
                    {
                        if (HasValue(mask, v))
                        {
                            rowcolIndexByValue[0][v - 1, j] |= (1u << i);
                            rowcolIndexByValue[1][v - 1, i] |= (1u << j);
                        }
                    }
                }
            }

            List<int> unsetRowOrCols = new(MAX_VALUE);
            for (int tupleSize = 2; tupleSize <= MAX_VALUE / 2; tupleSize++)
            {
                for (int rowOrCol = 0; rowOrCol < 2; rowOrCol++)
                {
                    uint[,] indexByValue = rowcolIndexByValue[rowOrCol];

                    for (int valueIndex = 0; valueIndex < MAX_VALUE; valueIndex++)
                    {
                        int value = valueIndex + 1;

                        // Make a list of pairs for the row/col which aren't already filled
                        unsetRowOrCols.Clear();
                        for (int j = 0; j < MAX_VALUE; j++)
                        {
                            uint cellMask = indexByValue[valueIndex, j];
                            int valueCount = ValueCount(cellMask);
                            if (valueCount > 1 && valueCount <= tupleSize)
                            {
                                unsetRowOrCols.Add(j);
                            }
                        }
                        if (unsetRowOrCols.Count < tupleSize)
                        {
                            continue;
                        }

                        foreach (var tupleRowOrCols in unsetRowOrCols.Combinations(tupleSize))
                        {
                            uint tupleMask = 0;
                            foreach (int j in tupleRowOrCols)
                            {
                                tupleMask |= indexByValue[valueIndex, j];
                            }
                            if (ValueCount(tupleMask) == tupleSize)
                            {
                                List<int> elims = null;
                                for (int j = 0; j < MAX_VALUE; j++)
                                {
                                    if (tupleRowOrCols.Contains(j))
                                    {
                                        continue;
                                    }

                                    uint mask = indexByValue[valueIndex, j];
                                    uint elimMask = mask & tupleMask;
                                    if (elimMask != 0)
                                    {
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((elimMask & (1u << i)) != 0)
                                            {
                                                elims ??= new();
                                                elims.Add(CandidateIndex(rowOrCol == 0 ? (i, j) : (j, i), value));
                                            }
                                        }
                                    }
                                }

                                if (elims != null && elims.Count > 0)
                                {
                                    string techniqueName = tupleSize switch
                                    {
                                        2 => "X-Wing",
                                        3 => "Swordfish",
                                        4 => "Jellyfish",
                                        _ => $"{tupleSize}-Fish",
                                    };

                                    List<(int, int)> fishCells = new();
                                    foreach (int j in tupleRowOrCols)
                                    {
                                        uint mask = indexByValue[valueIndex, j];
                                        for (int i = 0; i < MAX_VALUE; i++)
                                        {
                                            if ((mask & (1u << i)) != 0)
                                            {
                                                fishCells.Add(rowOrCol == 0 ? (i, j) : (j, i));
                                            }
                                        }
                                    }

                                    logicalStepDescs?.Add(new(
                                        desc: $"{techniqueName}: {value} {CompactName(fishCells)} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(ValueMask(value), fishCells),
                                        elimCandidates: elims
                                    ));
                                    if (!ClearCandidates(elims))
                                    {
                                        return LogicResult.Invalid;
                                    }
                                    return LogicResult.Changed;
                                }
                            }
                        }
                    }
                }
            }

            return LogicResult.None;
        }

        private LogicResult FindWings(List<LogicalStepDesc> logicalStepDescs)
        {
            if (isBruteForcing)
            {
                return LogicResult.None;
            }

            // A y-wing always involves three bivalue cells.
            // The three cells have 3 candidates between them, and one cell called the "pivot" sees the other two "pincers".
            // A strong link is formed between the common candidate between the two "pincer" cells.
            List<(int, int)> candidateCells = new();
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (!IsValueSet(mask) && ValueCount(mask) == 2)
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
                            int value01 = GetValue(mask0 & mask1);
                            int value02 = GetValue(mask0 & mask2);
                            int value12 = GetValue(mask1 & mask2);
                            int cand01_0 = CandidateIndex((i0, j0), value01);
                            int cand01_1 = CandidateIndex((i1, j1), value01);
                            int cand02_0 = CandidateIndex((i0, j0), value02);
                            int cand02_2 = CandidateIndex((i2, j2), value02);
                            int cand12_1 = CandidateIndex((i1, j1), value12);
                            int cand12_2 = CandidateIndex((i2, j2), value12);
                            bool weak01 = weakLinks[cand01_0].Contains(cand01_1);
                            bool weak02 = weakLinks[cand02_0].Contains(cand02_2);
                            bool weak12 = weakLinks[cand12_1].Contains(cand12_2);
                            int weakCount = (weak01 ? 1 : 0) + (weak02 ? 1 : 0) + (weak12 ? 1 : 0);
                            if (weakCount != 2)
                            {
                                continue;
                            }

                            List<int> elims;
                            if (weak01 && weak02)
                            {
                                // Pivot is 0, eliminate from the shared 12 value
                                elims = CalcElims(cand12_1, cand12_2).ToList();
                            }
                            else if (weak01 && weak12)
                            {
                                // Pivot is 1, eliminate from the shared 02 value
                                elims = CalcElims(cand02_0, cand02_2).ToList();
                            }
                            else
                            {
                                // Pivot is 2, elimiate from the shared 01 value
                                elims = CalcElims(cand01_0, cand01_1).ToList();
                            }
                            if (elims.Count > 0)
                            {
                                List<(int, int)> cells = new() { (i0, j0), (i1, j1), (i2, j2) };
                                logicalStepDescs?.Add(new(
                                        desc: $"Y-Wing: {MaskToString(combinedMask)} in {CompactName(cells)} => {DescribeElims(elims)}",
                                        sourceCandidates: CandidateIndexes(combinedMask, cells),
                                        elimCandidates: elims
                                    ));
                                if (!ClearCandidates(elims))
                                {
                                    return LogicResult.Invalid;
                                }
                                return LogicResult.Changed;
                            }
                        }
                    }
                }
            }

            // Look for (N)-Wings [XYZ-Wings, WXYZ-Wings, VWXYZ-Wings, etc]
            // An (N)-Wing is N candidates limited to N cells.
            // Looking at each candidate, all but one of them cannot repeat within those cells.
            // This implies that any cell seen by the instances of that last candidate can be eliminated.
            for (int wingSize = 3; wingSize <= MAX_VALUE; wingSize++)
            {
                var logicResult = FindNWing(logicalStepDescs, wingSize);
                if (logicResult != LogicResult.None)
                {
                    return logicResult;
                }
            }

            return LogicResult.None;
        }

        private LogicResult FindNWing(List<LogicalStepDesc> logicalStepDescs, int wingSize)
        {
            List<(int, int)> candidateCells = new();
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    int valueCount = ValueCount(mask);
                    if (!IsValueSet(mask) && valueCount > 1 && valueCount <= wingSize)
                    {
                        candidateCells.Add((i, j));
                    }
                }
            }

            int numCandidateCells = candidateCells.Count;
            if (numCandidateCells < wingSize)
            {
                return LogicResult.None;
            }

            int UngroupedValue(uint valuesMask, List<(int, int)> wingCells, int curUngroupedValue)
            {
                if (curUngroupedValue < 0 || ValueCount(valuesMask) > wingSize)
                {
                    return -1;
                }

                if (wingCells.Count <= 1)
                {
                    return 0;
                }
                bool checkForSingle = wingCells.Count == wingSize;
                if (checkForSingle && ValueCount(valuesMask) != wingSize)
                {
                    return -1;
                }

                valuesMask &= ~ValueMask(curUngroupedValue);

                int ungroupedValue = curUngroupedValue;
                int minValue = MinValue(valuesMask);
                int maxValue = MaxValue(valuesMask);
                for (int v = minValue; v <= maxValue; v++)
                {
                    uint valueMask = ValueMask(v);
                    if ((valuesMask & valueMask) != 0)
                    {
                        int numWithCandidate = 0;
                        for (int k = 0; k < wingCells.Count; k++)
                        {
                            var (i, j) = wingCells[k];
                            if ((board[i, j] & valueMask) != 0)
                            {
                                numWithCandidate++;
                            }
                        }

                        if (checkForSingle && numWithCandidate == 1)
                        {
                            return -1;
                        }

                        if (numWithCandidate > 1 && !IsGroup(wingCells, v))
                        {
                            if (ungroupedValue == 0)
                            {
                                ungroupedValue = v;
                            }
                            else
                            {
                                return -1;
                            }
                        }
                    }
                }
                return ungroupedValue;
            }

            List<(int, int)> wingCells = new(wingSize);
            (int index, (int i, int j) coord, uint accumMask, int ungroupedValue)[] wingInfoArray = new (int, (int, int), uint, int)[wingSize];

            // Initialize the wing info array with the first combination even if it's invalid
            {
                uint accumMask = 0;
                for (int k = 0; k < wingSize; k++)
                {
                    var coord = candidateCells[k];
                    accumMask |= board[coord.Item1, coord.Item2];
                    wingCells.Add(coord);

                    int ungroupedValue = UngroupedValue(accumMask, wingCells, k == 0 ? 0 : wingInfoArray[k - 1].ungroupedValue);
                    wingInfoArray[k] = (k, coord, accumMask, ungroupedValue);
                }
            }

            while (true)
            {
                // Check if this wing info is valid and performs eliminations
                var (_, _, accumMask, ungroupedValue) = wingInfoArray[wingSize - 1];
                if (ValueCount(accumMask) == wingSize && ungroupedValue > 0)
                {
                    // At least one of the ungrouped candidates is true, so eliminate any candidates with a weak link to all of them.
                    var elims = CalcElims(ValueMask(ungroupedValue), wingCells);
                    if (elims != null && elims.Count > 0)
                    {
                        if (logicalStepDescs != null)
                        {
                            StringBuilder wingName = new();
                            for (int k = wingSize - 1; k >= 0; k--)
                            {
                                wingName.Append((char)('Z' - k));
                            }
                            logicalStepDescs?.Add(new(
                                    desc: $"{wingName}-Wing: {MaskToString(accumMask)} in {CompactName(wingCells)} => {DescribeElims(elims)}",
                                    sourceCandidates: CandidateIndexes(accumMask, wingCells),
                                    elimCandidates: elims
                                ));
                        }
                        if (!ClearCandidates(elims))
                        {
                            return LogicResult.Invalid;
                        }
                        return LogicResult.Changed;
                    }
                }

                // Find the first invalid index. This is the minimum index to increment.
                int firstInvalid = wingSize;
                for (int k = 0; k < wingSize; k++)
                {
                    var wingInfo = wingInfoArray[k];
                    if (wingInfo.ungroupedValue < 0)
                    {
                        firstInvalid = k;
                        break;
                    }
                }

                // Find the last index which can be incremented
                int lastCanIncrement = -1;
                for (int k = wingSize - 1; k >= 0; k--)
                {
                    var wingInfo = wingInfoArray[k];
                    if (wingInfo.index + 1 < numCandidateCells - (wingSize - k))
                    {
                        lastCanIncrement = k;
                        break;
                    }
                }

                // Check if done
                if (lastCanIncrement == -1)
                {
                    break;
                }

                // Increment the smaller of the first invalid one or the last that can increment.
                int k0 = Math.Min(lastCanIncrement, firstInvalid);

                wingCells.Clear();
                for (int k = 0; k < k0; k++)
                {
                    wingCells.Add(wingInfoArray[k].coord);
                }

                // Increment to the next combination
                int nextIndex = wingInfoArray[k0].index + 1;
                var nextCoord = candidateCells[nextIndex];
                uint nextAccumMask = board[nextCoord.Item1, nextCoord.Item2];
                if (k0 > 0)
                {
                    nextAccumMask |= wingInfoArray[k0 - 1].accumMask;
                }
                wingCells.Add(nextCoord);
                wingInfoArray[k0] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, k0 == 0 ? 0 : wingInfoArray[k0 - 1].ungroupedValue));

                for (int k1 = k0 + 1; k1 < wingSize; k1++)
                {
                    nextIndex = wingInfoArray[k1 - 1].index + 1;
                    nextCoord = candidateCells[nextIndex];
                    nextAccumMask |= board[nextCoord.Item1, nextCoord.Item2];
                    wingCells.Add(nextCoord);
                    wingInfoArray[k1] = (nextIndex, nextCoord, nextAccumMask, UngroupedValue(nextAccumMask, wingCells, wingInfoArray[k1 - 1].ungroupedValue));
                }
            }

            return LogicResult.None;
        }

        internal string CompactName(uint mask, List<(int, int)> cells) =>
            string.Join(MAX_VALUE <= 9 ? "" : ",", Enumerable.Range(1, MAX_VALUE).Where(v => HasValue(mask, v))) + CompactName(cells);

        internal string CompactName(List<(int, int)> cells)
        {
            string cellSep = MAX_VALUE <= 9 ? string.Empty : ",";
            char groupSep = ',';

            if (cells.Count == 0)
            {
                return "";
            }

            if (cells.Count == 1)
            {
                return CellName(cells[0]);
            }

            if (cells.All(cell => cell.Item1 == cells[0].Item1))
            {
                // All share a row
                return $"r{cells[0].Item1 + 1}c{string.Join(cellSep, cells.Select(cell => cell.Item2 + 1).OrderBy(x => x))}";
            }

            if (cells.All(cell => cell.Item2 == cells[0].Item2))
            {
                // All share a column
                return $"r{string.Join(cellSep, cells.Select(cell => cell.Item1 + 1).OrderBy(x => x))}c{cells[0].Item2 + 1}";
            }

            List<int>[] colsPerRow = new List<int>[HEIGHT];
            for (int i = 0; i < HEIGHT; i++)
            {
                colsPerRow[i] = new();
            }
            foreach (var cell in cells)
            {
                colsPerRow[cell.Item1].Add(cell.Item2 + 1);
            }
            for (int i = 0; i < HEIGHT; i++)
            {
                colsPerRow[i].Sort();
            }

            List<string> groups = new();
            for (int i = 0; i < HEIGHT; i++)
            {
                if (colsPerRow[i].Count == 0)
                {
                    continue;
                }

                List<int> rowsInGroup = new() { i + 1 };
                for (int j = i + 1; j < HEIGHT; j++)
                {
                    if (colsPerRow[j].SequenceEqual(colsPerRow[i]))
                    {
                        rowsInGroup.Add(j + 1);
                        colsPerRow[j].Clear();
                    }
                }

                groups.Add($"r{string.Join(cellSep, rowsInGroup)}c{string.Join(cellSep, colsPerRow[i])}");
            }

            return string.Join(groupSep, groups);
        }

        private readonly struct StrongLinkDesc
        {
            public readonly string humanDesc;
            public readonly List<(int, int)> alsCells;

            public StrongLinkDesc(string humanDesc, IEnumerable<(int, int)> alsCells = null)
            {
                this.humanDesc = humanDesc;
                this.alsCells = alsCells != null ? new(alsCells) : null;
            }

            public static StrongLinkDesc Empty => new(string.Empty, null);
        }
        private Dictionary<int, StrongLinkDesc>[] FindStrongLinks()
        {
            Dictionary<int, StrongLinkDesc>[] strongLinks = new Dictionary<int, StrongLinkDesc>[NUM_CELLS * MAX_VALUE];
            for (int candIndex = 0; candIndex < strongLinks.Length; candIndex++)
            {
                strongLinks[candIndex] = new();
            }

            void AddStrongLink(int cand0, int cand1, StrongLinkDesc desc)
            {
                if (cand0 != cand1)
                {
                    if (!strongLinks[cand0].ContainsKey(cand1))
                    {
                        strongLinks[cand0][cand1] = desc;
                    }
                    if (!strongLinks[cand1].ContainsKey(cand0))
                    {
                        strongLinks[cand1][cand0] = desc;
                    }
                }
            }

            // Add bivalue strong links
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    uint mask = board[i, j];
                    if (!IsValueSet(mask) && ValueCount(mask) == 2)
                    {
                        int v0 = MinValue(mask);
                        int v1 = MaxValue(mask);
                        int cand0 = CandidateIndex((i, j), v0);
                        int cand1 = CandidateIndex((i, j), v1);
                        AddStrongLink(cand0, cand1, StrongLinkDesc.Empty);
                    }
                }
            }

            // Add bilocal strong links
            foreach (var group in Groups)
            {
                if (group.Cells.Count == MAX_VALUE)
                {
                    int[] valueCount = new int[MAX_VALUE];
                    foreach (var (i, j) in group.Cells)
                    {
                        uint mask = board[i, j];
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if ((mask & ValueMask(v)) != 0)
                            {
                                valueCount[v - 1]++;
                            }
                        }
                    }

                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (valueCount[v - 1] == 2)
                        {
                            (int, int) cell0 = (-1, -1);
                            (int, int) cell1 = (-1, -1);
                            foreach (var (i, j) in group.Cells)
                            {
                                uint mask = board[i, j];
                                if ((mask & ValueMask(v)) != 0)
                                {
                                    if (cell0.Item1 == -1)
                                    {
                                        cell0 = (i, j);
                                    }
                                    else
                                    {
                                        cell1 = (i, j);
                                        break;
                                    }
                                }
                            }

                            int cand0 = CandidateIndex(cell0, v);
                            int cand1 = CandidateIndex(cell1, v);
                            AddStrongLink(cand0, cand1, StrongLinkDesc.Empty);
                        }
                    }
                }
                else if (group.FromConstraint != null)
                {
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        var cells = group.FromConstraint.CellsMustContain(this, v);
                        if (cells != null && cells.Count == 2)
                        {
                            int cand0 = CandidateIndex(cells[0], v);
                            int cand1 = CandidateIndex(cells[1], v);
                            string constraintName = group.FromConstraint.SpecificName;
                            StrongLinkDesc strongLinkDesc = new(constraintName);
                            AddStrongLink(cand0, cand1, strongLinkDesc);
                        }
                    }
                }
            }

            // Add ALS (Almost Locked Set) strong links
            // These occur when n cells in the same group have n+1 total candidates,
            // and two of those candidates only appear once.
            // There is a strong link between those two candidates.
            // (If both were missing, then there would be n-1 candidates for n cells).
            foreach (var group in Groups)
            {
                var unsetCells = group.Cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();

                for (int alsSize = 2; alsSize < unsetCells.Count; alsSize++)
                {
                    foreach (var combination in unsetCells.Combinations(alsSize))
                    {
                        uint totalMask = 0;
                        foreach (var cell in combination)
                        {
                            totalMask |= board[cell.Item1, cell.Item2];
                        }

                        if (ValueCount(totalMask) != alsSize + 1)
                        {
                            continue;
                        }

                        List<int>[] candIndexPerValue = new List<int>[MAX_VALUE];
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            candIndexPerValue[v - 1] = new();
                        }
                        foreach (var (i, j) in combination)
                        {
                            uint mask = board[i, j];
                            for (int v = 1; v <= MAX_VALUE; v++)
                            {
                                if ((mask & ValueMask(v)) != 0)
                                {
                                    int candIndex = CandidateIndex((i, j), v);
                                    candIndexPerValue[v - 1].Add(candIndex);
                                }
                            }
                        }

                        List<int> singleValues = new();
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (candIndexPerValue[v - 1].Count == 1)
                            {
                                singleValues.Add(candIndexPerValue[v - 1][0]);
                            }
                        }

                        if (singleValues.Count > 1)
                        {
                            foreach (var candIndices in singleValues.Combinations(2))
                            {
                                int cand0 = candIndices[0];
                                int cand1 = candIndices[1];

                                string valSep = MAX_VALUE <= 9 ? string.Empty : ",";
                                StringBuilder alsDesc = new();
                                alsDesc.Append("ALS:");
                                alsDesc.Append(CompactName(totalMask, combination));

                                string alsDescStr = alsDesc.ToString();
                                StrongLinkDesc strongLinkDesc = new(alsDescStr, combination);
                                AddStrongLink(cand0, cand1, strongLinkDesc);
                            }
                        }
                    }
                }
            }

            return strongLinks;
        }

        public uint CandidateMask(IEnumerable<(int, int)> cells)
        {
            uint mask = 0;
            foreach (var curCell in cells)
            {
                mask |= board[curCell.Item1, curCell.Item2];
            }
            return mask & ~valueSetMask;
        }

        internal int CandidateIndex((int, int) cell, int v) => (cell.Item1 * WIDTH + cell.Item2) * MAX_VALUE + v - 1;

        internal List<int> CandidateIndexes(uint valueMask, IEnumerable<(int, int)> cells)
        {
            List<int> result = new();
            foreach (var cell in cells)
            {
                uint mask = board[cell.Item1, cell.Item2] & valueMask;
                if (mask != 0)
                {
                    int minVal = MinValue(mask);
                    int maxVal = MaxValue(mask);
                    for (int v = minVal; v <= maxVal; v++)
                    {
                        if (HasValue(mask, v))
                        {
                            result.Add(CandidateIndex(cell, v));
                        }
                    }
                }
            }
            return result;
        }

        internal (int, int, int) CandIndexToCoord(int candIndex)
        {
            int v = (candIndex % MAX_VALUE) + 1;
            candIndex /= MAX_VALUE;

            int j = candIndex % WIDTH;
            candIndex /= WIDTH;

            int i = candIndex;
            return (i, j, v);
        }

        internal string CandIndexDesc(int candIndex)
        {
            var (i, j, v) = CandIndexToCoord(candIndex);
            return $"{v}{CellName(i, j)}";
        }

        internal bool IsCandIndexValid(int candIndex)
        {
            var (i, j, v) = CandIndexToCoord(candIndex);
            uint mask = board[i, j];
            return !IsValueSet(mask) && HasValue(mask, v);
        }

        // Returns true if cell 0 has the ability to eliminate all candidates from cell 1
        internal bool HasFullWeakLinks((int, int) cell0, (int, int) cell1)
        {
            uint mask0 = board[cell0.Item1, cell0.Item2];
            uint mask1 = board[cell1.Item1, cell1.Item2];
            uint sharedMask = mask0 & mask1;
            if (sharedMask != mask1)
            {
                return false;
            }
            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                uint valueMask0 = ValueMask(v0);
                if ((mask0 & valueMask0) == 0)
                {
                    continue;
                }
                int cand0 = CandidateIndex(cell0, v0);
                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                {
                    uint valueMask1 = ValueMask(v1);
                    if ((mask1 & valueMask1) == 0)
                    {
                        continue;
                    }
                    int cand1 = CandidateIndex(cell1, v1);
                    if (weakLinks[cand0].Contains(cand1))
                    {
                        mask1 &= ~valueMask1;
                        if (mask1 == 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal IEnumerable<int> CalcElims(int candIndex0, int candIndex1) =>
            weakLinks[candIndex0].Where(IsCandIndexValid).Intersect(weakLinks[candIndex1].Where(IsCandIndexValid));

        internal IEnumerable<int> CalcElims(params int[] candIndexes) => CalcElims(candIndexes);

        internal IEnumerable<int> CalcElims(IEnumerable<int> candIndexes)
        {
            IEnumerable<int> result = null;
            foreach (int candIndex in candIndexes)
            {
                IEnumerable<int> curElims = weakLinks[candIndex].Where(IsCandIndexValid);
                if (result == null)
                {
                    result = curElims;
                }
                else
                {
                    result = result.Intersect(curElims);
                }
            }
            return result;
        }

        internal HashSet<int> CalcElims(uint clearMask, List<(int, int)> cells)
        {
            HashSet<int> elims = null;
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                if (!HasValue(clearMask, v))
                {
                    continue;
                }

                var curElims = CalcElims(cells.Where(cell => HasValue(board[cell.Item1, cell.Item2], v)).Select(cell => CandidateIndex(cell, v)));
                if (curElims != null)
                {
                    if (elims == null)
                    {
                        elims = curElims.ToHashSet();
                    }
                    else
                    {
                        elims.UnionWith(curElims);
                    }
                }
            }
            return elims;
        }

        internal void CalcElims(HashSet<int> outElims, uint clearMask, List<(int, int)> cells)
        {
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                if (!HasValue(clearMask, v))
                {
                    continue;
                }

                var curElims = CalcElims(cells.Where(cell => HasValue(board[cell.Item1, cell.Item2], v)).Select(cell => CandidateIndex(cell, v)));
                if (curElims != null)
                {
                    outElims.UnionWith(curElims);
                }
            }
        }

        internal string DescribeElims(IEnumerable<int> elims)
        {
            List<(int, int)>[] elimsByVal = new List<(int, int)>[MAX_VALUE];
            foreach (int elimCandIndex in elims)
            {
                var (i, j, v) = CandIndexToCoord(elimCandIndex);
                elimsByVal[v - 1] ??= new();
                elimsByVal[v - 1].Add((i, j));
            }

            List<string> elimDescs = new();
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                var elimCells = elimsByVal[v - 1];
                if (elimCells != null && elimCells.Count > 0)
                {
                    elimCells.Sort();
                    elimDescs.Add($"-{v}{CompactName(elimCells)}");
                }
            }
            return string.Join(';', elimDescs);
        }

        // 0 = 1 - 2 = 3 - 4 = 5 - 6 = 7 - 8 = 9
        // 0 = 3, 0 = 5, 0 = 7, 0 = 9
        // 2 = 5, 2 = 7, 2 = 9
        // 4 = 7, 4 = 9
        // 6 = 9
        private HashSet<int> CalcStrongElims(List<int> chain)
        {
            HashSet<int> elims = new();
            for (int chainIndex0 = 0; chainIndex0 < chain.Count; chainIndex0 += 2)
            {
                int cand0 = chain[chainIndex0];
                for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
                {
                    int cand1 = chain[chainIndex1];
                    elims.UnionWith(CalcElims(cand0, cand1));
                }
            }
            return elims;
        }

        // 0 = 1 - 2 = 3 - 4 = 5 - 0
        // 1 - 2, 1 - 4, 1 - 0
        // 3 - 4, 3 - 0
        // 5 - 0
        private HashSet<int> CalcWeakToStrongElims(List<int> chain)
        {
            HashSet<int> elims = new();
            for (int chainIndex0 = 1; chainIndex0 < chain.Count; chainIndex0 += 2)
            {
                int cand0 = chain[chainIndex0];
                for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
                {
                    int cand1 = chain[chainIndex1];
                    elims.UnionWith(CalcElims(cand0, cand1));
                }
            }
            return elims;
        }

        // For CNLs, all strong links convert to also be weak links.
        // If those weak links are part of an ALS, the other candidates
        // in the ALS must be present.
        private HashSet<int> CalcStrongToWeakElims(Dictionary<int, StrongLinkDesc>[] strongLinks, List<int> chain)
        {
            HashSet<int> elims = new();
            for (int chainIndex0 = 0; chainIndex0 < chain.Count; chainIndex0 += 2)
            {
                int cand0 = chain[chainIndex0];
                for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
                {
                    int cand1 = chain[chainIndex1];
                    var (_, _, v0) = CandIndexToCoord(cand0);
                    var (_, _, v1) = CandIndexToCoord(cand1);
                    if (strongLinks[cand0].TryGetValue(cand1, out StrongLinkDesc strongLinkDescOut) && strongLinkDescOut.alsCells != null)
                    {
                        uint totalMask = 0;
                        foreach (var cell in strongLinkDescOut.alsCells)
                        {
                            totalMask |= board[cell.Item1, cell.Item2];
                        }
                        uint clearMask = totalMask & ~ValueMask(v0) & ~ValueMask(v1) & ~valueSetMask;
                        CalcElims(elims, clearMask, strongLinkDescOut.alsCells);
                    }
                }

            }
            return elims;
        }

        private int NumSetValues
        {
            get
            {
                int numSetValues = 0;
                for (int i = 0; i < HEIGHT; i++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        if (IsValueSet(i, j))
                        {
                            numSetValues++;
                        }
                    }
                }
                return numSetValues;
            }
        }

        private struct ChainQueueEntry
        {
            public readonly List<int> chain;
            public readonly bool allowALS;

            public ChainQueueEntry(List<int> chain, bool allowALS = true)
            {
                this.chain = chain;
                this.allowALS = allowALS;
            }
        }

        private LogicResult FindAIC(List<LogicalStepDesc> logicalStepDescs)
        {
            const int chainCapacity = 16;

            var strongLinks = FindStrongLinks();

            // Keep track of all dangling chains to process
            Queue<ChainQueueEntry> chainQueue = new();

            // Seed the chain stack with all candidates which have a strong link
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = (i * WIDTH + j) * MAX_VALUE;
                    for (int v = 1; v < MAX_VALUE; v++)
                    {
                        int candIndex0 = cellIndex + v - 1;
                        if (IsCandIndexValid(candIndex0) && strongLinks[candIndex0].Count > 0)
                        {
                            chainQueue.Enqueue(new(new() { candIndex0 }));
                        }
                    }
                }
            }

            // Process each chain found, adding more chains if still viable
            List<int> bestChain = null;
            List<int> bestChainElims = null;
            int bestChainDirectSingles = 0;
            int bestChainSinglesAfterBasics = 0;
            string bestChainDescPrefix = null;
            int maxChainSize = 0;
            bool bestChainCausesInvalidBoard = false;

            void CheckBestChain(List<int> chain, List<int> chainElims, string chainDescPrefix)
            {
                if (chainElims.Count == 0)
                {
                    return;
                }

                // Apply the eliminations to a board clone
                Solver directSinglesSolver = Clone();
                foreach (int elimCandIndex in chainElims)
                {
                    var (i, j, v) = CandIndexToCoord(elimCandIndex);
                    if (!directSinglesSolver.ClearValue(i, j, v))
                    {
                        bestChain = new(chain);
                        bestChainElims = new(chainElims);
                        bestChainDirectSingles = 0;
                        bestChainSinglesAfterBasics = 0;
                        bestChainDescPrefix = chainDescPrefix;
                        bestChainCausesInvalidBoard = true;
                        return;
                    }
                }
                if (directSinglesSolver.ApplySingles() == LogicResult.Invalid)
                {
                    bestChain = new(chain);
                    bestChainElims = new(chainElims);
                    bestChainDirectSingles = 0;
                    bestChainSinglesAfterBasics = 0;
                    bestChainDescPrefix = chainDescPrefix;
                    bestChainCausesInvalidBoard = true;
                    return;
                }

                Solver singlesAfterBasicsSolver = directSinglesSolver.Clone();
                singlesAfterBasicsSolver.SetToBasicsOnly();
                if (singlesAfterBasicsSolver.ConsolidateBoard() == LogicResult.Invalid)
                {
                    bestChain = new(chain);
                    bestChainElims = new(chainElims);
                    bestChainDirectSingles = 0;
                    bestChainSinglesAfterBasics = 0;
                    bestChainDescPrefix = chainDescPrefix;
                    bestChainCausesInvalidBoard = true;
                    return;
                }

                int numDirectSingles = directSinglesSolver.NumSetValues;
                int numSinglesAfterBasics = singlesAfterBasicsSolver.NumSetValues;
                (int, int, int, int) chainVals = (numSinglesAfterBasics, numDirectSingles, chainElims.Count, -chain.Count);
                (int, int, int, int) bestChainVals = bestChain != null ? (bestChainSinglesAfterBasics, bestChainDirectSingles, bestChainElims.Count, -bestChain.Count) : default;
                if (bestChain == null || chainVals.CompareTo(bestChainVals) > 0)
                {
                    if (bestChain == null)
                    {
                        maxChainSize = chain.Count + 4;
                    }
                    bestChain = new(chain);
                    bestChainElims = new(chainElims);
                    bestChainDirectSingles = numDirectSingles;
                    bestChainSinglesAfterBasics = numSinglesAfterBasics;
                    bestChainDescPrefix = chainDescPrefix;

                    if (bestChainDirectSingles == NUM_CELLS)
                    {
                        maxChainSize = chain.Count;
                    }
                }
            }

            while (chainQueue.Count > 0 && !bestChainCausesInvalidBoard)
            {
                var chainEntry = chainQueue.Dequeue();
                var chain = chainEntry.chain;
                if (bestChain != null && maxChainSize > 0 && chain.Count + 1 > maxChainSize)
                {
                    // Prefer reporting shorter chains
                    break;
                }

                // Append a strong link to each weak link and see if this causes eliminations.
                foreach (int strongIndexEnd in strongLinks[chain[^1]].Keys)
                {
                    if (!IsCandIndexValid(strongIndexEnd))
                    {
                        continue;
                    }

                    // Reject any strong links to repeated nodes, unless it's the first node that's repeated
                    if (chain.IndexOf(strongIndexEnd) > 0)
                    {
                        continue;
                    }

                    List<int> newChain = new(chainCapacity);
                    newChain.AddRange(chain);
                    newChain.Add(strongIndexEnd);

                    bool isDNL = newChain[0] == strongIndexEnd;

                    bool haveALSElim = false;
                    if (chainEntry.allowALS)
                    {
                        var chainElims = CalcStrongElims(newChain);
                        if (chainElims.Count > 0)
                        {
                            haveALSElim = true;

                            CheckBestChain(newChain, chainElims.ToList(), isDNL ? "DNL: " : "AIC: ");
                            if (bestChainCausesInvalidBoard)
                            {
                                break;
                            }
                        }
                    }

                    if (isDNL || maxChainSize > 0 && newChain.Count + 1 > maxChainSize)
                    {
                        continue;
                    }

                    // Add a placeholder weak link
                    newChain.Add(-1);

                    // Check for a CNL
                    if (weakLinks[strongIndexEnd].Contains(newChain[0]))
                    {
                        newChain[^1] = newChain[0];

                        var chainElims = CalcStrongElims(newChain);
                        chainElims.UnionWith(CalcWeakToStrongElims(newChain));
                        chainElims.UnionWith(CalcStrongToWeakElims(strongLinks, newChain));
                        if (chainElims.Count > 0)
                        {
                            CheckBestChain(newChain, chainElims.ToList(), "CNL: ");
                        }
                    }

                    // Add all chain continuations
                    if (!bestChainCausesInvalidBoard && (maxChainSize == 0 || newChain.Count < maxChainSize))
                    {
                        foreach (int weakIndexEnd in weakLinks[strongIndexEnd])
                        {
                            if (IsCandIndexValid(weakIndexEnd) && !newChain.Contains(weakIndexEnd))
                            {
                                newChain[^1] = weakIndexEnd;
                                chainQueue.Enqueue(new(newChain, chainEntry.allowALS && !haveALSElim));
                            }
                        }
                    }
                }
            }

            if (bestChain != null)
            {
                // Form the description string
                StringBuilder stepDescription = null;
                if (logicalStepDescs != null)
                {
                    stepDescription = new();
                    stepDescription.Append(bestChainDescPrefix);

                    bool strong = false;
                    for (int ci = 0; ci < bestChain.Count; ci++, strong = !strong)
                    {
                        if (ci > 0)
                        {
                            if (strong)
                            {
                                int candIndex0 = bestChain[ci - 1];
                                int candIndex1 = bestChain[ci];
                                if (strongLinks[candIndex0].TryGetValue(candIndex1, out StrongLinkDesc strongLinkDescOut) && !string.IsNullOrWhiteSpace(strongLinkDescOut.humanDesc))
                                {
                                    stepDescription.Append($" = [{strongLinkDescOut.humanDesc}]");
                                }
                                else
                                {
                                    stepDescription.Append($" = ");
                                }
                            }
                            else
                            {
                                stepDescription.Append(" - ");
                            }
                        }

                        if (ci + 1 < bestChain.Count)
                        {
                            int candIndex0 = bestChain[ci];
                            int candIndex1 = bestChain[ci + 1];
                            var (i0, j0, v0) = CandIndexToCoord(candIndex0);
                            var (i1, j1, v1) = CandIndexToCoord(candIndex1);
                            if (i0 == i1 && j0 == j1)
                            {
                                stepDescription.Append($"({v0}{(strong ? "-" : $"=")}{v1}){CellName(i0, j0)}");
                                strong = !strong;
                                ci++;
                                continue;
                            }
                        }

                        if (ci > 0)
                        {
                            int candIndex0 = bestChain[ci - 1];
                            int candIndex1 = bestChain[ci];
                            var (i0, j0, v0) = CandIndexToCoord(candIndex0);
                            var (i1, j1, v1) = CandIndexToCoord(candIndex1);

                            if (v0 == v1)
                            {
                                stepDescription.Append(CellName(i1, j1));
                                continue;
                            }
                        }

                        stepDescription.Append(CandIndexDesc(bestChain[ci]));
                    }

                    stepDescription.Append(" => ");
                    stepDescription.Append(DescribeElims(bestChainElims));

                    logicalStepDescs.Add(new(
                        desc: stepDescription.ToString(),
                        sourceCandidates: bestChain,
                        elimCandidates: bestChainElims,
                        sourceIsAIC: true));
                }

                // Perform the eliminations
                foreach (int elimCandIndex in bestChainElims)
                {
                    var (i, j, v) = CandIndexToCoord(elimCandIndex);
                    if (!ClearValue(i, j, v))
                    {
                        return LogicResult.Invalid;
                    }
                }

                return LogicResult.Changed;
            }
            return LogicResult.None;
        }

        private LogicResult FindSimpleContradictions(List<LogicalStepDesc> logicalStepDescs)
        {
            for (int allowedValueCount = 2; allowedValueCount <= MAX_VALUE; allowedValueCount++)
            {
                ContradictionResult bestContradiction = null;
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

                                    List<LogicalStepDesc> contradictionSteps = logicalStepDescs != null ? new() : null;
                                    if (!boardCopy.SetValue(i, j, v) || boardCopy.ConsolidateBoard(contradictionSteps) == LogicResult.Invalid)
                                    {
                                        bool isTrivial = contradictionSteps != null && contradictionSteps.Count == 0;
                                        if (isTrivial)
                                        {
                                            contradictionSteps.Add(new LogicalStepDesc(
                                                desc: "Immediately violates a constraint.",
                                                sourceCandidates: Enumerable.Empty<int>(),
                                                elimCandidates: Enumerable.Empty<int>()));
                                        }

                                        // Trivial contradictions will always be as "easy" or "easier" than any other contradiction.
                                        if (isTrivial || DisableFindShortestContradiction || contradictionSteps == null)
                                        {
                                            logicalStepDescs?.Add(new(
                                                desc: $"Setting {CellName(i, j)} to {v} causes a contradiction:",
                                                sourceCandidates: Enumerable.Empty<int>(),
                                                elimCandidates: CandidateIndex((i, j), v).ToEnumerable(),
                                                subSteps: contradictionSteps
                                            ));

                                            if (!ClearValue(i, j, v))
                                            {
                                                return LogicResult.Invalid;
                                            }
                                            return LogicResult.Changed;
                                        }
                                        if (!DisableContradictions)
                                        {
                                            int changes = boardCopy.AmountCellsFilled() - this.AmountCellsFilled();
                                            if (bestContradiction == null || changes < bestContradiction.Changes)
                                            {
                                                bestContradiction = new ContradictionResult(changes, boardCopy, i, j, v, contradictionSteps);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (bestContradiction != null)
                {
                    logicalStepDescs?.Add(new(
                        desc: $"Setting {CellName(bestContradiction.I, bestContradiction.J)} to {bestContradiction.V} causes a contradiction:",
                        sourceCandidates: Enumerable.Empty<int>(),
                        elimCandidates: CandidateIndex((bestContradiction.I, bestContradiction.J), bestContradiction.V).ToEnumerable(),
                        subSteps: bestContradiction.ContraditionSteps
                    ));

                    if (!ClearValue(bestContradiction.I, bestContradiction.J, bestContradiction.V))
                    {
                        return LogicResult.Invalid;
                    }
                    return LogicResult.Changed;
                }
            }

            return LogicResult.None;
        }

        private int AmountCellsFilled()
        {
            int counter = 0;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    counter += IsValueSet(i, j) ? 1 : 0;
                }
            }
            return counter;
        }

        private record ContradictionResult
        (
            int Changes,
            Solver BoardCopy,
            int I,
            int J,
            int V,
            List<LogicalStepDesc> ContraditionSteps);
    }
}
