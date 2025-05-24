namespace SudokuSolver;

public partial class Solver
{
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

        Solver solver = Clone(willRunNonSinglesLogic: true);
        if (solver.DiscoverWeakLinks() == LogicResult.Invalid)
        {
            return false;
        }
        solver.isBruteForcing = true;

        using FindSolutionState state = new(isRandom, multiThread, cancellationToken);
        if (multiThread)
        {
            if (!state.PushSolver(solver, true))
            {
                throw new Exception("Initial PushSolver failed!");
            }
            state.Wait();
        }
        else
        {
            FindSolutionInternal(solver, state);
        }
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

    private class FindSolutionState : IDisposable
    {
        public CountdownEvent countdownEvent = new(1);
        public uint[] result = null;
        public CancellationToken cancellationToken;
        public bool isRandom = false;
        public bool isMultiThreaded = false;

        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;

        public FindSolutionState(bool isRandom, bool isMultiThreaded, CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            this.isRandom = isRandom;
            this.isMultiThreaded = isMultiThreaded;

            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
        }

        public bool PushSolver(Solver solver, bool isInitialCall = false)
        {
            int newCount = Interlocked.Increment(ref numRunningTasks);
            if (isInitialCall || newCount <= maxRunningTasks)
            {
                // only schedule if we stayed within the limit
                if (!isInitialCall)
                {
                    // countdownEvent cannot start at 0 or it starts already signaled, so the first call to PushSolver
                    // does not increment, since it starts at 1
                    countdownEvent.AddCount();
                }
                Task.Run(() => {
                    try
                    {
                        FindSolutionInternal(solver, this);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref numRunningTasks);
                        countdownEvent.Signal();
                    }
                });
                return true;
            }

            // we overshot: roll back and decline
            Interlocked.Decrement(ref numRunningTasks);
            return false;
        }

        public void Dispose()
        {
            ((IDisposable)countdownEvent).Dispose();
        }

        public void ReportSolution(Solver solver)
        {
            Interlocked.CompareExchange(ref result, solver.board, null);
        }

        public void Wait()
        {
            countdownEvent.Wait(cancellationToken);
        }
    }

    private static void FindSolutionInternal(Solver root, FindSolutionState state)
    {
        var stack = new Stack<Solver>();
        stack.Push(root);

        while (state.result is null && stack.TryPop(out var solver))
        {
            state.cancellationToken.ThrowIfCancellationRequested();
            if (state.result != null)
            {
                continue;
            }

            var logicResult = solver.StepBruteForceLogic();
            if (logicResult == LogicResult.PuzzleComplete)
            {
                state.ReportSolution(solver);
                continue;
            }

            if (logicResult == LogicResult.Invalid)
            {
                continue;
            }

            (int cellIndex, int v) = solver.GetLeastCandidateCell();
            if (cellIndex < 0)
            {
                state.ReportSolution(solver);
                continue;
            }

            // Try a possible value for this cell
            int val = v != 0 ? v : state.isRandom ? GetRandomValue(solver.board[cellIndex]) : MinValue(solver.board[cellIndex]);
            uint valMask = ValueMask(val);

            // Create a backup board in case it needs to be restored
            Solver newSolver = solver.Clone(willRunNonSinglesLogic: false);
            newSolver.isBruteForcing = true;
            newSolver.board[cellIndex] &= ~valMask;
            if (newSolver.board[cellIndex] != 0)
            {
                if (!state.isMultiThreaded || !state.PushSolver(newSolver))
                {
                    stack.Push(newSolver);
                }
            }

            // Change the board to only allow this value in the slot
            if (solver.SetValue(cellIndex, val))
            {
                stack.Push(solver);
            }
        }
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
            Solver boardCopy = Clone(willRunNonSinglesLogic: true);
            if (boardCopy.DiscoverWeakLinks() == LogicResult.Invalid)
            {
                return 0;
            }
            boardCopy.isBruteForcing = true;
            if (state.multiThread)
            {
                if (!state.PushSolver(boardCopy, isInitialCall: true))
                {
                    throw new Exception("Initial PushSolver failed!");
                }
                state.Wait();
            }
            else
            {
                CountSolutionsInternal(boardCopy, state);
            }
        }
        catch (OperationCanceledException) { }

        if (maxSolutions > 0 && state.numSolutions > maxSolutions)
        {
            return maxSolutions;
        }
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
        private readonly int maxRunningTasks;
        private readonly bool fastIncrement;
        private bool maxSolutionsReached;

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
            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
            fastIncrement = skipSolutions == null && solutionEvent == null;
            maxSolutionsReached = false;
        }

        public bool MaxSolutionsReached => maxSolutionsReached;

        public void IncrementSolutions(Solver solver)
        {
            bool invokeProgress = false;
            if (fastIncrement)
            {
                ulong newNumSolutions = Interlocked.Increment(ref numSolutions);
                if (maxSolutions > 0 && newNumSolutions >= maxSolutions)
                {
                    maxSolutionsReached = true;
                    return;
                }

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
                if (skipSolutions != null && skipSolutions.Contains(solver.GivenString))
                {
                    return;
                }

                ulong newNumSolutions = Interlocked.Increment(ref numSolutions);
                if (maxSolutions > 0 && newNumSolutions >= maxSolutions)
                {
                    maxSolutionsReached = true;
                    return;
                }

                if (solutionEvent != null || eventTimer.ElapsedMilliseconds > 500)
                {
                    lock (solutionLock)
                    {
                        solutionEvent?.Invoke(solver);
                        if (eventTimer.ElapsedMilliseconds > 500)
                        {
                            invokeProgress = true;
                            eventTimer.Restart();
                        }
                    }
                }
            }
            if (invokeProgress)
            {
                progressEvent?.Invoke(numSolutions);
            }
        }

        public bool PushSolver(Solver solver, bool isInitialCall = false)
        {
            int newNumRunningTasks = Interlocked.Increment(ref numRunningTasks);
            if (isInitialCall || newNumRunningTasks <= maxRunningTasks)
            {
                if (!isInitialCall)
                {
                    // countdownEvent cannot start at 0 or it starts already signaled, so the first call to PushSolver
                    // does not increment, since it starts at 1
                    countdownEvent.AddCount();
                }
                Task.Run(() => {
                    try
                    {
                        CountSolutionsInternal(solver, this);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref numRunningTasks);
                        countdownEvent.Signal();
                    }
                });
                return true;
            }
            else
            {
                Interlocked.Decrement(ref numRunningTasks);
            }

            return false;
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

    private static void CountSolutionsInternal(Solver root, CountSolutionsState state)
    {
        bool isMultithreaded = state.multiThread;

        var stack = new Stack<Solver>();
        stack.Push(root);

        while (stack.TryPop(out var solver) && !state.MaxSolutionsReached)
        {
            state.cancellationToken.ThrowIfCancellationRequested();

            var logicResult = solver.StepBruteForceLogic();
            if (logicResult == LogicResult.PuzzleComplete)
            {
                state.IncrementSolutions(solver);
                continue;
            }

            if (logicResult == LogicResult.Invalid)
            {
                continue;
            }

            // Start with the cell that has the least possible candidates
            (int cellIndex, int v) = solver.GetLeastCandidateCell();
            if (cellIndex < 0)
            {
                state.IncrementSolutions(solver);
                continue;
            }

            // Try a possible value for this cell
            int val = v != 0 ? v : state.skipSolutions != null ? GetRandomValue(solver.board[cellIndex]) : MinValue(solver.board[cellIndex]);
            uint valMask = ValueMask(val);

            // Create a solver without this value and start a task for it
            Solver newSolver = solver.Clone(willRunNonSinglesLogic: false);
            newSolver.isBruteForcing = true;
            newSolver.board[cellIndex] &= ~valMask;
            if (newSolver.board[cellIndex] != 0)
            {
                if (!isMultithreaded || !state.PushSolver(newSolver))
                {
                    stack.Push(newSolver);
                }
            }

            if (solver.SetValue(cellIndex, val))
            {
                stack.Push(solver);
            }
        }
    }

    public int[] TrueCandidates(bool multiThread = false, Action<int[]> progressEvent = null, int numSolutionsCap = 8, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        using TrueCandidatesState state = new(this, multiThread, progressEvent, numSolutionsCap, cancellationToken);
        try
        {
            Solver boardCopy = Clone(willRunNonSinglesLogic: false);
            if (boardCopy.DiscoverWeakLinks() == LogicResult.Invalid)
            {
                return state.candidateSolutionCounts;
            }
            boardCopy.isBruteForcing = true;
            if (state.multiThread)
            {
                if (!state.PushSolver(boardCopy, isInitialCall: true))
                {
                    throw new Exception("Initial PushSolver failed!");
                }
                state.Wait();
            }
            else
            {
                TrueCandidatesInternal(boardCopy, state);
            }
        }
        catch (OperationCanceledException) { }

        return state.candidateSolutionCounts;
    }

    private class TrueCandidatesState : IDisposable
    {
        public readonly uint[] needCandidateMask;
        public readonly int[] candidateSolutionCounts;
        public readonly int numSolutionsCap;

        public readonly Action<int[]> progressEvent;
        private readonly object progressLock = new();
        public readonly Stopwatch eventTimer = Stopwatch.StartNew();

        public readonly CancellationToken cancellationToken;
        public readonly bool multiThread;
        public bool boardInvalid = false;

        public readonly CountdownEvent countdownEvent;
        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;

        public TrueCandidatesState(Solver initialSolver, bool multiThread, Action<int[]> progressEvent, int numSolutionsCap, CancellationToken cancellationToken)
        {
            needCandidateMask = new uint[initialSolver.NUM_CELLS];
            for (int i = 0; i < initialSolver.NUM_CELLS; i++)
            {
                needCandidateMask[i] = initialSolver.board[i] & ~valueSetMask;
            }
            candidateSolutionCounts = new int[initialSolver.NUM_CANDIDATES];

            this.progressEvent = progressEvent;
            this.cancellationToken = cancellationToken;
            this.multiThread = multiThread;
            this.numSolutionsCap = numSolutionsCap;

            eventTimer = Stopwatch.StartNew();
            countdownEvent = multiThread ? new CountdownEvent(1) : null;
            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
        }

        public void IncrementSolutions(Solver solver)
        {
            int numCells = solver.NUM_CELLS;
            int maxVal = solver.MAX_VALUE;
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                int baseIndex = cellIndex * maxVal;
                uint cellMask = solver.board[cellIndex] & ~valueSetMask;
                int value = GetValue(cellMask);
                int candidateIndex = baseIndex + value - 1;
                int newCount = Interlocked.Increment(ref candidateSolutionCounts[candidateIndex]);
                if (newCount >= numSolutionsCap)
                {
                    // Don't need this candidate anymore
                    needCandidateMask[cellIndex] &= ~cellMask;
                }
            }

            if (eventTimer.ElapsedMilliseconds > 1000)
            {
                lock (progressLock)
                {
                    if (eventTimer.ElapsedMilliseconds > 1000)
                    {
                        progressEvent?.Invoke(candidateSolutionCounts);
                        eventTimer.Restart();
                    }
                }
            }
        }

        public bool IsUseful(Solver solver)
        {
            int numCells = solver.NUM_CELLS;
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                if ((solver.board[cellIndex] & needCandidateMask[cellIndex]) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        public bool PushSolver(Solver solver, bool isInitialCall = false)
        {
            int newNumRunningTasks = Interlocked.Increment(ref numRunningTasks);
            if (isInitialCall || newNumRunningTasks <= maxRunningTasks)
            {
                if (!isInitialCall)
                {
                    // countdownEvent cannot start at 0 or it starts already signaled, so the first call to PushSolver
                    // does not increment, since it starts at 1
                    countdownEvent.AddCount();
                }
                Task.Run(() => {
                    try
                    {
                        TrueCandidatesInternal(solver, this);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref numRunningTasks);
                        countdownEvent.Signal();
                    }
                });
                return true;
            }
            else
            {
                Interlocked.Decrement(ref numRunningTasks);
            }

            return false;
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

    private bool TrueCandidatesSinglesPass(TrueCandidatesState state)
    {
        // Depth 0 pass to see if it's trivially invalid, solved, or useless
        var logicResult = BruteForcePropogate();
        if (logicResult == LogicResult.Invalid)
        {
            return false;
        }
        if (logicResult == LogicResult.PuzzleComplete)
        {
            state.IncrementSolutions(this);
            return false;
        }
        if (!state.IsUseful(this))
        {
            return false;
        }

        return true;
    }

    private static void TrueCandidatesInternal(Solver root, TrueCandidatesState state)
    {
        // Now do a standard DFS backtracking pass
        bool isMultithreaded = state.multiThread;
        Stack<Solver> stack = new();
        stack.Push(root);
        while (stack.TryPop(out var solver))
        {
            state.cancellationToken.ThrowIfCancellationRequested();

            if (!solver.TrueCandidatesSinglesPass(state))
            {
                continue;
            }

            // Search for the next cell to try
            int cellIndex;
            {
                List<int> bestCellIndices = null;
                int bestCellIndicesCount = int.MaxValue;
                int numCells = solver.NUM_CELLS;
                for (int curCellIndex = 0; curCellIndex < numCells; curCellIndex++)
                {
                    uint curCellMask = solver.board[curCellIndex];
                    if ((curCellMask & valueSetMask) != 0)
                    {
                        continue;
                    }

                    int candidateCount = ValueCount(curCellMask);
                    if (candidateCount < bestCellIndicesCount)
                    {
                        bestCellIndices = [];
                        bestCellIndicesCount = candidateCount;
                    }

                    if (candidateCount <= bestCellIndicesCount)
                    {
                        int neededCandidateCount = ValueCount(curCellMask & state.needCandidateMask[curCellIndex]);
                        // Purposefully looping one extra time so even non-needed cells have a chance to be picked
                        for (int i = 0; i <= neededCandidateCount; i++)
                        {
                            bestCellIndices.Add(curCellIndex);
                        }
                    }
                }

                if (bestCellIndices == null)
                {
                    // This is actually a solution (this really shouldn't be happening, it will be caught earlier)
                    state.IncrementSolutions(solver);
                    continue;
                }

                cellIndex = bestCellIndices[RandomNext(0, bestCellIndices.Count)];
            }

            // Try a possible value for this cell, preferring a value that is still needed, if possible
            uint cellMask = solver.board[cellIndex];
            uint neededMask = state.needCandidateMask[cellIndex] & cellMask;
            int val = GetRandomValue(neededMask != 0 ? neededMask : cellMask);
            uint valMask = ValueMask(val);

            // Create a solver without this value and start a task for it
            Solver newSolver = solver.Clone(willRunNonSinglesLogic: false);
            newSolver.isBruteForcing = true;
            newSolver.board[cellIndex] &= ~valMask;
            if (newSolver.board[cellIndex] != 0)
            {
                if (!isMultithreaded || !state.PushSolver(newSolver))
                {
                    stack.Push(newSolver);
                }
            }

            if (solver.SetValue(cellIndex, val))
            {
                stack.Push(solver);
            }
        }
    }
}
