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
        private uint[,] board;
        public uint[,] Board => board;
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

        private readonly List<Constraint> constraints;

        /// <summary>
        /// Groups which cannot contain more than one of the same digit.
        /// This will at least contain all rows, columns, and boxes.
        /// Will also contain any groups from constraints (such as killer cages).
        /// </summary>
        public List<SudokuGroup> Groups { get; }

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

        public Solver()
        {
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
            InitStandardGroups();
        }

        public Solver(Solver other)
        {
            board = (uint[,])other.board.Clone();
            constraints = other.constraints;
            Groups = other.Groups;
            CellToGroupMap = other.CellToGroupMap;
        }

        private void InitStandardGroups()
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                List<(int, int)> cells = new(9);
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
                List<(int, int)> cells = new(9);
                for (int i = 0; i < HEIGHT; i++)
                {
                    cells.Add((i, j));
                }
                SudokuGroup group = new($"Column {j + 1}", cells);
                Groups.Add(group);
                InitMapForGroup(group);
            }

            // Add box groups
            for (int boxi = 0; boxi < NUM_BOXES_HEIGHT; boxi++)
            {
                int basei = boxi * BOX_HEIGHT;
                for (int boxj = 0; boxj < NUM_BOXES_WIDTH; boxj++)
                {
                    int basej = boxj * BOX_WIDTH;

                    List<(int, int)> cells = new(9);
                    for (int offi = 0; offi < BOX_HEIGHT; offi++)
                    {
                        int i = basei + offi;
                        for (int offj = 0; offj < BOX_WIDTH; offj++)
                        {
                            int j = basej + offj;
                            cells.Add((i, j));
                        }
                    }
                    SudokuGroup group = new($"Box {boxi * 3 + boxj + 1}", cells);
                    Groups.Add(group);
                    InitMapForGroup(group);
                }
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
        /// Adds a new constraint to the board.
        /// Only call this before any values have been set onto the board.
        /// </summary>
        /// <param name="constraint"></param>
        public void AddConstraint(Constraint constraint)
        {
            constraints.Add(constraint);

            var cells = constraint.Group;
            if (cells != null)
            {
                SudokuGroup group = new(constraint.SpecificName, cells.ToList());
                Groups.Add(group);
                InitMapForGroup(group);
            }
        }

        /// <summary>
        /// Call this once after all constraints are set, and before setting any values.
        /// </summary>
        /// <returns>True if the board is still valid. False if the constraints cause there to be trivially no solutions.</returns>
        public bool FinalizeConstraints()
        {
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

            return true;
        }

        /// <summary>
        /// Creates a copy of the board, including all constraints, set values, and candidates.
        /// </summary>
        /// <returns></returns>
        public Solver Clone() => new Solver(this);

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

        private (int, int) GetLeastCandidateCell()
        {
            int i = -1, j = -1;
            int numCandidates = MAX_VALUE + 1;
            for (int x = 0; x < HEIGHT; x++)
            {
                for (int y = 0; y < WIDTH; y++)
                {
                    if (!IsValueSet(x, y))
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


        /// <summary>
        /// Performs a single logical step.
        /// </summary>
        /// <param name="progressEvent">An event to report progress whenever a new step is found.</param>
        /// <param name="completedEvent">An event to report the final status of the puzzle (solved, no more logical steps, invalid)</param>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns></returns>
        public void LogicalStep(EventHandler<(string, uint[])> completedEvent)
        {
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
        public async Task<bool> FindSolution(CancellationToken? cancellationToken = null)
        {
            Stopwatch timeSinceCheck = Stopwatch.StartNew();

            bool wasBruteForcing = isBruteForcing;
            isBruteForcing = true;

            var boardStack = new Stack<Solver>();
            while (true)
            {
                if (timeSinceCheck.ElapsedMilliseconds > 1000)
                {
                    await Task.Delay(1);
                    cancellationToken?.ThrowIfCancellationRequested();
                    timeSinceCheck.Restart();
                }

                if (ConsolidateBoard())
                {
                    (int i, int j) = GetLeastCandidateCell();
                    if (i < 0)
                    {
                        isBruteForcing = wasBruteForcing;
                        return true;
                    }

                    // Try a possible value for this cell
                    int val = MinValue(board[i, j]);
                    uint valMask = ValueMask(val);

                    // Create a backup board in case it needs to be restored
                    Solver backupBoard = Clone();
                    backupBoard.isBruteForcing = true;
                    backupBoard.board[i, j] &= ~valMask;
                    if (backupBoard.board[i, j] != 0)
                    {
                        boardStack.Push(backupBoard);
                    }

                    // Change the board to only allow this value in the slot
                    if (SetValue(i, j, val))
                    {
                        continue;
                    }
                }

                if (boardStack.Count == 0)
                {
                    isBruteForcing = wasBruteForcing;
                    return false;
                }
                board = boardStack.Pop().board;
            }
        }

        /// <summary>
        /// Finds a single random solution to the board. This may not be the only solution.
        /// The board itself is modified to have the solution as its board values.
        /// If no solution is found, the board is left in an invalid state.
        /// </summary>
        /// <param name="cancellationToken">Pass in to support cancelling the solve.</param>
        /// <returns>True if a solution is found, otherwise false.</returns>
        public async Task<bool> FindRandomSolution(CancellationToken? cancellationToken = null)
        {
            Stopwatch timeSinceCheck = Stopwatch.StartNew();

            Random rand = new Random();

            bool wasBruteForcing = isBruteForcing;
            isBruteForcing = true;

            var boardStack = new Stack<Solver>();
            while (true)
            {
                if (timeSinceCheck.ElapsedMilliseconds > 1000)
                {
                    await Task.Delay(1);
                    cancellationToken?.ThrowIfCancellationRequested();
                    timeSinceCheck.Restart();
                }

                if (ConsolidateBoard())
                {
                    (int i, int j) = GetLeastCandidateCell();
                    if (i < 0)
                    {
                        isBruteForcing = wasBruteForcing;
                        return true;
                    }

                    // Try a possible value for this cell
                    uint cellMask = board[i, j];
                    int numCellVals = ValueCount(cellMask);
                    int targetValIndex = rand.Next(0, numCellVals);

                    int valIndex = 0;
                    int val = 0;
                    uint valMask = 0;
                    for (int curVal = 1; curVal <= MAX_VALUE; curVal++)
                    {
                        val = curVal;
                        valMask = ValueMask(curVal);

                        // Don't bother trying the value if it's not a possibility
                        if ((board[i, j] & valMask) != 0)
                        {
                            if (valIndex == targetValIndex)
                            {
                                break;
                            }
                            valIndex++;
                        }
                    }

                    // Create a backup board in case it needs to be restored
                    Solver backupBoard = Clone();
                    backupBoard.isBruteForcing = true;
                    backupBoard.board[i, j] &= ~valMask;
                    if (backupBoard.board[i, j] != 0)
                    {
                        boardStack.Push(backupBoard);
                    }

                    // Change the board to only allow this value in the slot
                    if (SetValue(i, j, val))
                    {
                        continue;
                    }
                }

                if (boardStack.Count == 0)
                {
                    isBruteForcing = wasBruteForcing;
                    return false;
                }
                board = boardStack.Pop().board;
            }
        }

        /// <summary>
        /// Determine how many solutions the board has.
        /// </summary>
        /// <param name="maxSolutions">The maximum number of solutions to find. Pass 0 for no maximum.</param>
        /// <param name="progressEvent">An event to receive the progress count as solutions are found.</param>
        /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
        /// <returns>The solution count found.</returns>
        public async Task<ulong> CountSolutions(ulong maxSolutions = 0, EventHandler<ulong> progressEvent = null, CancellationToken? cancellationToken = null)
        {
            bool wasBruteForcing = isBruteForcing;
            isBruteForcing = true;

            CountSolutionsState state = new CountSolutionsState(maxSolutions, progressEvent, cancellationToken);
            try
            {
                await CountSolutions(0, state);
            }
            catch (OperationCanceledException) { }

            isBruteForcing = wasBruteForcing;
            return state.numSolutions;
        }

        private class CountSolutionsState
        {
            public ulong numSolutions = 0;
            public ulong nextNumSolutionsEvent = 1;
            public ulong numSolutionsEventIncrement = 1;
            public readonly Stopwatch timeSinceCheck = Stopwatch.StartNew();

            public readonly ulong maxSolutions;
            public readonly EventHandler<ulong> progressEvent;
            public readonly CancellationToken? cancellationToken;

            public CountSolutionsState(ulong maxSolutions, EventHandler<ulong> progressEvent, CancellationToken? cancellationToken)
            {
                this.maxSolutions = maxSolutions;
                this.progressEvent = progressEvent;
                this.cancellationToken = cancellationToken;
            }

            public void IncrementSolutions()
            {
                numSolutions++;
                if (nextNumSolutionsEvent == numSolutions)
                {
                    progressEvent?.Invoke(null, numSolutions);
                    if (numSolutionsEventIncrement < 1000 && numSolutions / numSolutionsEventIncrement >= 10)
                    {
                        numSolutionsEventIncrement *= 10;
                    }
                    nextNumSolutionsEvent += numSolutionsEventIncrement;
                }
            }
        }

        private async Task CountSolutions(int cell, CountSolutionsState state)
        {
            int i = cell / WIDTH;
            int j = cell % WIDTH;

            // Skip cells until one that is not already filled
            while (cell < NUM_CELLS && IsValueSet(i, j))
            {
                cell++;
                i = cell / WIDTH;
                j = cell % WIDTH;
            }
            if (cell >= NUM_CELLS)
            {
                // Found a solution
                state.IncrementSolutions();
                return;
            }

            // If there are no possible values then a contradiction was reached
            if (board[i, j] == 0)
            {
                return;
            }

            // Try all possible values for this cell, recording how many solutions exist for that value
            for (int val = 1; val <= MAX_VALUE; val++)
            {
                uint valMask = ValueMask(val);

                // Don't bother trying the value if it's not a possibility
                if ((board[i, j] & valMask) == 0)
                {
                    continue;
                }

                // Check for cancel
                if (state.timeSinceCheck.ElapsedMilliseconds > 1000)
                {
                    await Task.Delay(1);
                    state.cancellationToken?.ThrowIfCancellationRequested();
                    state.timeSinceCheck.Restart();
                }

                // Create a duplicate board with one guess and count the solutions for that one
                Solver boardCopy = Clone();
                boardCopy.isBruteForcing = true;

                // Change the board to only allow this value in the slot
                if (boardCopy.SetValue(i, j, val) && boardCopy.ConsolidateBoard())
                {
                    // Accumulate how many solutions there are with this cell value
                    await boardCopy.CountSolutions(cell + 1, state);
                    if (state.maxSolutions > 0 && state.numSolutions >= state.maxSolutions)
                    {
                        return;
                    }
                }

                // Mark the value as not possible since all solutions with that value are already recorded
                board[i, j] &= ~valMask;

                // If that was the last possible change, then this board has no more solutions
                if (board[i, j] == 0)
                {
                    return;
                }

                // If consolodating this board reaches a contradiction, then none of the remaining values have solutions.
                if (!ConsolidateBoard())
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Remove any candidates which do not lead to an actual solution to the board.
        /// </summary>
        /// <param name="progressEvent">Recieve progress notifications. Will send 0 through 80 (assume 81 is 100%, though that will never be sent).</param>
        /// <param name="cancellationToken">Pass in to support cancelling.</param>
        /// <returns>True if there are solutions and candidates are filled. False if there are no solutions.</returns>
        public async Task FillRealCandidates(EventHandler<(int, uint[])> progressEvent = null, EventHandler<uint[]> completionEvent = null, CancellationToken? cancellationToken = null)
        {
            Stopwatch timeSinceCheck = Stopwatch.StartNew();
            bool wasBruteForcing = isBruteForcing;
            isBruteForcing = true;

            if (!ConsolidateBoard())
            {
                completionEvent?.Invoke(null, null);
                isBruteForcing = wasBruteForcing;
                return;
            }

            uint[] fixedBoard = new uint[NUM_CELLS];
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int cellIndex = i * WIDTH + j;

                    if (IsValueSet(i, j))
                    {
                        fixedBoard[cellIndex] = board[i, j];
                        continue;
                    }

                    for (int val = 1; val <= MAX_VALUE; val++)
                    {
                        uint valMask = ValueMask(val);

                        // Don't bother trying the value if it's not a possibility
                        if ((board[i, j] & valMask) == 0)
                        {
                            continue;
                        }

                        // Don't bother trying this value if it's already confirmed in the fixed board
                        if ((fixedBoard[cellIndex] & valMask) != 0)
                        {
                            continue;
                        }

                        // Check for cancellation and send progress updates once per second
                        if (timeSinceCheck.ElapsedMilliseconds > 1000)
                        {
                            await Task.Delay(1);
                            cancellationToken?.ThrowIfCancellationRequested();

                            progressEvent?.Invoke(null, (cellIndex, fixedBoard));
                            timeSinceCheck.Restart();
                        }

                        // Do the solve on a copy of the board
                        Solver boardCopy = Clone();
                        boardCopy.isBruteForcing = true;

                        // Go through all previous cells and set only their real candidates as possibilities
                        for (int fixedCellIndex = 0; fixedCellIndex < cellIndex; fixedCellIndex++)
                        {
                            if (ValueCount(fixedBoard[fixedCellIndex]) > 1)
                            {
                                int fi = fixedCellIndex / WIDTH;
                                int fj = fixedCellIndex % WIDTH;
                                boardCopy.board[fi, fj] = fixedBoard[fixedCellIndex];
                            }
                        }
                        for (int fixedCellIndex = 0; fixedCellIndex < cellIndex; fixedCellIndex++)
                        {
                            if (ValueCount(fixedBoard[fixedCellIndex]) == 1)
                            {
                                int fi = fixedCellIndex / WIDTH;
                                int fj = fixedCellIndex % WIDTH;
                                if (!boardCopy.IsValueSet(fi, fj))
                                {
                                    boardCopy.SetValue(fi, fj, GetValue(fixedBoard[fixedCellIndex]));
                                }
                            }
                        }

                        // Set the board to use this candidate's value
                        if (boardCopy.SetValue(i, j, val) && await boardCopy.FindSolution(cancellationToken))
                        {
                            for (int si = 0; si < HEIGHT; si++)
                            {
                                for (int sj = 0; sj < WIDTH; sj++)
                                {
                                    uint solutionValMask = boardCopy.board[si, sj] & ~valueSetMask;
                                    fixedBoard[si * WIDTH + sj] |= solutionValMask;
                                }
                            }
                        }
                    }

                    // If a cell has no possible candidates then there are no solutions and thus all candidates are empty.
                    // This will really only happen on the first cell attempted.
                    if (fixedBoard[cellIndex] == 0)
                    {
                        completionEvent?.Invoke(null, null);
                        isBruteForcing = wasBruteForcing;
                        return;
                    }
                }
            }

            completionEvent?.Invoke(null, fixedBoard);
            isBruteForcing = wasBruteForcing;
        }

        /// <summary>
        /// Perform a logical solve until either the board is solved or there are no logical steps found.
        /// </summary>
        /// <param name="stepsDescription">Get a full description of all logical steps taken.</param>
        /// <returns></returns>
        public bool ConsolidateBoard(StringBuilder stepsDescription = null)
        {
            LogicResult result;
            do
            {
                StringBuilder stepDescription = new StringBuilder();
                result = StepLogic(stepDescription);
                stepsDescription?.Append(stepDescription).AppendLine();
            } while (result == LogicResult.Changed);

            return result != LogicResult.Invalid;
        }

        /// <summary>
        /// Perform one step of a logical solve and fill a description of the step taken.
        /// The description will contain the reason the board is invalid if that is what is returned.
        /// </summary>
        /// <param name="stepDescription"></param>
        /// <returns></returns>
        public LogicResult StepLogic(StringBuilder stepDescription, bool humanStepping = false)
        {
            LogicResult result = FindNakedSingles(stepDescription, humanStepping);
            if (result != LogicResult.None)
            {
                return result;
            }

            result = FindHiddenSingle(stepDescription);
            if (result != LogicResult.None)
            {
                return result;
            }

            result = FindNakedTuples(stepDescription);
            if (result != LogicResult.None)
            {
                return result;
            }

            result = FindPointingTuples(stepDescription);
            if (result != LogicResult.None)
            {
                return result;
            }

            foreach (var constraint in constraints)
            {
                result = constraint.StepLogic(this, stepDescription, isBruteForcing);
                if (result != LogicResult.None)
                {
                    stepDescription.Insert(0, $"{constraint.SpecificName}: ");
                    return result;
                }
            }

            if (isBruteForcing)
            {
                return LogicResult.None;
            }

            result = FindFishes(stepDescription);
            if (result != LogicResult.None)
            {
                return result;
            }

            result = FindYWings(stepDescription);
            if (result != LogicResult.None)
            {
                return result;
            }

            result = FindSimpleContradictions(stepDescription);
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
                StringBuilder curStepDescription = new StringBuilder();
                LogicResult findResult = FindNakedSinglesHelper(curStepDescription, humanStepping);
                if (stepDescription != null)
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
                        stepDescription.AppendLine();
                        stepDescription.Append($"{CellName(i, j)} has no possible values.");
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
                                stepDescription.Append($"{stepPrefix} {CellName(i, j)} = {value}");
                                hadChanges = true;
                                if (humanStepping)
                                {
                                    return LogicResult.Changed;
                                }
                            }
                            else
                            {
                                stepDescription.Append($", {CellName(i, j)} = {value}");
                            }

                            if (!SetValue(i, j, value))
                            {
                                for (int ci = 0; ci < 9; ci++)
                                {
                                    for (int cj = 0; cj < 9; cj++)
                                    {
                                        if (board[ci, cj] == 0)
                                        {
                                            stepDescription.AppendLine().Append($"{CellName(ci, cj)} has no candidates remaining.");
                                            return LogicResult.Invalid;
                                        }
                                    }
                                }
                                stepDescription.AppendLine().Append($"{CellName(i, j)} cannot be {value}.");
                                return LogicResult.Invalid;
                            }
                        }
                    }
                }
            }
            if (!hasUnsetCells)
            {
                if (stepDescription.Length > 0)
                {
                    stepDescription.AppendLine();
                }
                stepDescription.Append("Solution found!");
                return LogicResult.PuzzleComplete;
            }
            return hadChanges ? LogicResult.Changed : LogicResult.None;
        }

        private LogicResult FindHiddenSingle(StringBuilder stepDescription)
        {
            LogicResult finalFindResult = LogicResult.None;
            foreach (var group in Groups)
            {
                if (group.Cells.Count != MAX_VALUE)
                {
                    continue;
                }

                for (int val = 1; val <= MAX_VALUE; val++)
                {
                    uint valMask = 1u << (val - 1);
                    int numWithVal = 0;
                    int vali = 0;
                    int valj = 0;
                    foreach (var pair in group.Cells)
                    {
                        int i = pair.Item1;
                        int j = pair.Item2;
                        if ((board[i, j] & valMask) != 0)
                        {
                            numWithVal++;
                            vali = i;
                            valj = j;
                        }
                    }
                    if (numWithVal == 1 && !IsValueSet(vali, valj))
                    {
                        if (!SetValue(vali, valj, val))
                        {
                            stepDescription.Clear();
                            stepDescription.Append($"Hidden single {val} in {group.Name} {CellName(vali, valj)}, but it cannot be set to that value.");
                            return LogicResult.Invalid;
                        }
                        stepDescription.Append($"Hidden single {val} in {group.Name} {CellName(vali, valj)}");
                        return LogicResult.Changed;
                    }
                    else if (numWithVal == 0)
                    {
                        stepDescription.Clear();
                        stepDescription.Append($"{group.Name} has nowhere to place {val}.");
                        return LogicResult.Invalid;
                    }
                }
            }
            return finalFindResult;
        }

        private LogicResult FindNakedTuples(StringBuilder stepDescription)
        {
            List<(int, int)> unsetCells = new List<(int, int)>(MAX_VALUE);
            for (int tupleSize = 2; tupleSize < 8; tupleSize++)
            {
                foreach (var group in Groups)
                {
                    // Make a list of pairs for the group which aren't already filled
                    unsetCells.Clear();
                    foreach (var pair in group.Cells)
                    {
                        uint cellMask = board[pair.Item1, pair.Item2];
                        if (!IsValueSet(cellMask) && ValueCount(cellMask) <= tupleSize)
                        {
                            unsetCells.Add(pair);
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
                            uint[,] oldBoard = (uint[,])board.Clone();

                            uint invCombinationMask = ~combinationMask;

                            bool changed = false;
                            int numMatching = 0;
                            foreach (var curCell in group.Cells)
                            {
                                uint curMask = board[curCell.Item1, curCell.Item2];
                                uint remainingMask = curMask & invCombinationMask;
                                if (remainingMask != 0)
                                {
                                    if (remainingMask != curMask)
                                    {
                                        board[curCell.Item1, curCell.Item2] = remainingMask;
                                        if (!changed)
                                        {
                                            stepDescription.Append($"{group} has tuple {MaskToString(combinationMask)}, removing those values from {CellName(curCell)}");
                                            changed = true;
                                        }
                                        else
                                        {
                                            stepDescription.Append($", {CellName(curCell)}");
                                        }
                                    }
                                }
                                else
                                {
                                    numMatching++;
                                }
                            }

                            if (numMatching > tupleSize)
                            {
                                stepDescription.Clear();
                                stepDescription.Append($"{group} has too many cells ({tupleSize}) which can only have {MaskToString(combinationMask)}");
                                return LogicResult.Invalid;
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

                    var seenCells = SeenCells(cellsWithValue);
                    if (seenCells.Count == 0)
                    {
                        continue;
                    }

                    StringBuilder cellsWithValueStringBuilder = null;
                    uint valueMask = ValueMask(v);
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
                                stepDescription.Clear();
                                stepDescription.Append($"{v} is limited to {cellsWithValueStringBuilder} in {group}, but that value cannot be removed from {CellName(i, j)}");
                                return LogicResult.Invalid;
                            }
                            if (!changed)
                            {
                                stepDescription.Append($"{v} is limited to {cellsWithValueStringBuilder} in {group}, which removes that value from {CellName((i, j))}");
                                changed = true;
                            }
                            else
                            {
                                stepDescription.Append($", {CellName((i, j))}");
                            }
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

                                    if (!ClearValue(i, j, v))
                                    {
                                        stepDescription.Clear();
                                        stepDescription.Append($"{fishDesc}, but it cannot be removed from {CellName(i, j)}");
                                        return LogicResult.Invalid;
}

                                    if (!changed)
                                    {
                                        stepDescription.Append($"{fishDesc}, removing that value from {CellName(i, j)}");
                                        changed = true;
                                    }
                                    else
                                    {
                                        stepDescription.Append($", {CellName(i, j)}");
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
                            var seen0 = SeenCells((i0, j0));
                            var seen1 = SeenCells((i1, j1));
                            var seen2 = SeenCells((i2, j2));
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
                                removeFrom = SeenCells((i1, j1), (i2, j2));
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
                                removeFrom = SeenCells((i0, j0), (i2, j2));
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
                                removeFrom = SeenCells((i0, j0), (i1, j1));
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
                    foreach (var (pi, pj) in SeenCells((i0, j0), (i1, j1)))
                    {
                        if (board[pi, pj] == combMask)
                        {
                            // Check for cells seen by all three
                            List<(int, int)> removedFrom = new();
                            foreach (var (ri, rj) in SeenCells((i0, j0), (i1, j1), (pi, pj)))
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
                                stepDescription.Append($"XYZ-Wing with pivot at {CellName((pi, pj))} ({MaskToString(combMask)}) and pincers at {CellName((i0, j0))} ({MaskToString(mask0)}), {CellName((i1, j1))} ({MaskToString(mask1)}) clears candidate {GetValue(removeMask)} from cell{(removedFrom.Count == 1 ? "" : "s")}: ");
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
                            bool seen01 = SeenCells((i0, j0)).Contains((i1, j1));
                            bool seen02 = SeenCells((i0, j0)).Contains((i2, j2));
                            bool seen12 = SeenCells((i1, j1)).Contains((i2, j2));

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
                                else if(seen02)
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
                        foreach (var (pi, pj) in SeenCells((i0, j0), (i1, j1), (i2, j2)))
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
                                foreach (var (ri, rj) in SeenCells((i0, j0), (i1, j1), (i2, j2)))
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
                                foreach (var (ri, rj) in SeenCells((i0, j0), (i1, j1), (i2, j2), (pi, pj)))
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

                                    StringBuilder contradictionReason = new();
                                    if (!boardCopy.SetValue(i, j, v) || !boardCopy.ConsolidateBoard(contradictionReason))
                                    {
                                        StringBuilder formattedContraditionReason = new StringBuilder();
                                        foreach (string line in contradictionReason.ToString().Split('\n'))
                                        {
                                            if (!string.IsNullOrWhiteSpace(line))
                                            {
                                                formattedContraditionReason.Append("  ").Append(line).AppendLine();
                                            }
                                        }

                                        stepDescription.Append($"Setting {CellName(i, j)} to {v} causes a contradiction:");
                                        stepDescription.AppendLine();
                                        stepDescription.Append(formattedContraditionReason);
                                        if (!ClearValue(i, j, v))
                                        {
                                            stepDescription.AppendLine();
                                            stepDescription.Append($"This clears the last candidate from {CellName(i, j)}.");
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
