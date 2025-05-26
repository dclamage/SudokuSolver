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
                Task.Run(() =>
                {
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
        Stack<Solver> stack = new();
        stack.Push(root);

        while (state.result is null && stack.TryPop(out Solver solver))
        {
            state.cancellationToken.ThrowIfCancellationRequested();
            if (state.result != null)
            {
                continue;
            }

            LogicResult logicResult = solver.BruteForcePropagate();
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
    /// <param name="maxSolutions">The maximum number of solutions to find. Pass 0 or a negative value for no maximum.</param>
    /// <param name="multiThread">Whether to use multiple threads.</param>
    /// <param name="progressEvent">An event to receive the progress count as solutions are found.</param>
    /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
    /// <returns>The solution count found.</returns>
    public long CountSolutions(long maxSolutions = 0, bool multiThread = false, Action<long> progressEvent = null, Action<Solver> solutionEvent = null, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        // Any negative count is treated as infinite
        maxSolutions = Math.Max(maxSolutions, 0);

        using CountSolutionsState state = new(maxSolutions, multiThread, progressEvent, solutionEvent, cancellationToken);
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

        return maxSolutions > 0 && state.numSolutions > maxSolutions ? maxSolutions : state.numSolutions;
    }

    private class CountSolutionsState : IDisposable
    {
        public long numSolutions = 0;
        public readonly bool multiThread;
        public readonly long maxSolutions;
        public readonly Action<long> progressEvent;
        public readonly Action<Solver> solutionEvent;
        public readonly CancellationToken cancellationToken;
        public readonly CountdownEvent countdownEvent;

        private readonly object solutionLock = new();
        private readonly Stopwatch eventTimer;

        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;

        public CountSolutionsState(long maxSolutions, bool multiThread, Action<long> progressEvent, Action<Solver> solutionEvent, CancellationToken cancellationToken)
        {
            this.maxSolutions = maxSolutions;
            this.multiThread = multiThread;
            this.progressEvent = progressEvent;
            this.solutionEvent = solutionEvent;
            this.cancellationToken = cancellationToken;
            eventTimer = Stopwatch.StartNew();
            countdownEvent = multiThread ? new CountdownEvent(1) : null;
            maxRunningTasks = Math.Max(1, Environment.ProcessorCount - 1);
            MaxSolutionsReached = false;
        }

        public bool MaxSolutionsReached { get; private set; }

        public void IncrementSolutions(Solver solver)
        {
            long newNumSolutions = Interlocked.Increment(ref numSolutions);
            if (maxSolutions > 0 && newNumSolutions >= maxSolutions)
            {
                MaxSolutionsReached = true;
                return;
            }

            if (solutionEvent != null || eventTimer.ElapsedMilliseconds > 500)
            {
                lock (solutionLock)
                {
                    solutionEvent?.Invoke(solver);
                    if (eventTimer.ElapsedMilliseconds > 500)
                    {
                        progressEvent?.Invoke(numSolutions);
                        eventTimer.Restart();
                    }
                }
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
                Task.Run(() =>
                {
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

        Stack<Solver> stack = new();
        stack.Push(root);

        while (stack.TryPop(out Solver solver) && !state.MaxSolutionsReached)
        {
            state.cancellationToken.ThrowIfCancellationRequested();

            LogicResult logicResult = solver.BruteForcePropagate();
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
            int val = v != 0 ? v : MinValue(solver.board[cellIndex]);
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

    public long[] TrueCandidates(bool multiThread = false, Action<long[]> progressEvent = null, long numSolutionsCap = 8, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        numSolutionsCap = Math.Max(numSolutionsCap, 0);

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
        public readonly long[] candidateSolutionCounts;
        public readonly long numSolutionsCap;

        public readonly Action<long[]> progressEvent;
        private readonly object progressLock = new();
        public readonly Stopwatch eventTimer = Stopwatch.StartNew();

        public readonly CancellationToken cancellationToken;
        public readonly bool multiThread;
        public bool boardInvalid = false;

        public readonly CountdownEvent countdownEvent;
        private int numRunningTasks = 0;
        private readonly int maxRunningTasks;

        public TrueCandidatesState(Solver initialSolver, bool multiThread, Action<long[]> progressEvent, long numSolutionsCap, CancellationToken cancellationToken)
        {
            if (numSolutionsCap > 0)
            {
                needCandidateMask = new uint[initialSolver.NUM_CELLS];
                for (int i = 0; i < initialSolver.NUM_CELLS; i++)
                {
                    needCandidateMask[i] = initialSolver.board[i] & ~valueSetMask;
                }
            }
            else
            {
                needCandidateMask = null;
            }
            candidateSolutionCounts = new long[initialSolver.NUM_CANDIDATES];

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
                long newCount = Interlocked.Increment(ref candidateSolutionCounts[candidateIndex]);
                if (needCandidateMask != null && newCount >= numSolutionsCap)
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
            if (needCandidateMask == null)
            {
                // With no solution cap, we need to visit all branches
                return true;
            }

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
                Task.Run(() =>
                {
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
        LogicResult logicResult = BruteForcePropagate();
        if (logicResult == LogicResult.Invalid)
        {
            return false;
        }
        if (logicResult == LogicResult.PuzzleComplete)
        {
            state.IncrementSolutions(this);
            return false;
        }
        return state.IsUseful(this);
    }

    private static void TrueCandidatesInternal(Solver root, TrueCandidatesState state)
    {
        // Now do a standard DFS backtracking pass
        bool isMultithreaded = state.multiThread;
        Stack<Solver> stack = new();
        stack.Push(root);
        while (stack.TryPop(out Solver solver))
        {
            state.cancellationToken.ThrowIfCancellationRequested();

            if (!solver.TrueCandidatesSinglesPass(state))
            {
                continue;
            }

            // Search for the next cell to try
            int cellIndex;
            int val;
            if (state.needCandidateMask != null)
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

                // Try a possible value for this cell, preferring a value that is still needed, if possible
                uint cellMask = solver.board[cellIndex];
                uint neededMask = state.needCandidateMask[cellIndex] & cellMask;
                val = MinValue(neededMask != 0 ? neededMask : cellMask);
            }
            else
            {
                // Start with the cell that has the least possible candidates
                (cellIndex, int v) = solver.GetLeastCandidateCell();
                if (cellIndex < 0)
                {
                    state.IncrementSolutions(solver);
                    continue;
                }

                // Try a possible value for this cell
                val = v != 0 ? v : MinValue(solver.board[cellIndex]);
            }

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

    /// <summary>
    /// Estimate how many solutions the board has.
    /// </summary>
    /// <param name="numIterations">The number of iterations, or 0 to go until canceled.</param>
    /// <param name="multiThread">Whether to use multiple threads.</param>
    /// <param name="progressEvent">An event to receive the estimate as it improves plus the standard error metric.</param>
    /// <param name="cancellationToken">Pass in to support cancelling the count.</param>
    public void EstimateSolutions(long numIterations, Action<(double estimate, double stderr, long iterations)> progressEvent, bool multiThread = false, CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        // Any negative count is treated as infinite
        numIterations = Math.Max(numIterations, 0);
        if (numIterations == 0)
        {
            numIterations = long.MaxValue;
        }

        EstimateSolutionsState state = new(multiThread, progressEvent, cancellationToken);
        try
        {
            Solver root = Clone(willRunNonSinglesLogic: true);
            if (root.DiscoverWeakLinks() == LogicResult.Invalid)
            {
                progressEvent((0, 0, 0));
                return;
            }
            root.isBruteForcing = true;
            if (state.multiThread && Environment.ProcessorCount > 1)
            {
                ParallelOptions options = new()
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount - 1
                };

                Parallel.For(0, numIterations, options, (_) =>
                {
                    Solver solver = root.Clone(willRunNonSinglesLogic: false);
                    EstimateSolutionsInternal(solver, state);
                });
            }
            else
            {
                for (long i = 0; i < numIterations; i++)
                {
                    Solver solver = root.Clone(willRunNonSinglesLogic: false);
                    EstimateSolutionsInternal(root, state);
                }
            }
        }
        catch (OperationCanceledException) { }

        state.SendEvent();
    }

    private class EstimateSolutionsState
    {
        public readonly bool multiThread;
        public readonly Action<(double estimate, double stderr, long iterations)> progressEvent;
        public readonly CancellationToken cancellationToken;
        public readonly CountdownEvent countdownEvent;
        private readonly Stopwatch eventTimer;

        private readonly object estimateLock = new();
        private double currentEstimate = 0.0;
        private long iterationsCompleted = 0;
        private double m2 = 0.0; // Σ (xi-μ)²   for Welford

        public EstimateSolutionsState(bool multiThread, Action<(double estimate, double stderr, long iterations)> progressEvent, CancellationToken cancellationToken)
        {
            this.multiThread = multiThread;
            this.progressEvent = progressEvent;
            this.cancellationToken = cancellationToken;
            eventTimer = Stopwatch.StartNew();
            countdownEvent = multiThread ? new CountdownEvent(1) : null;
        }

        public void NewSample(double sample)
        {
            lock (estimateLock)
            {
                iterationsCompleted++;
                double delta = sample - currentEstimate;
                currentEstimate += delta / iterationsCompleted;   // new μ
                m2 += delta * (sample - currentEstimate);         // update Σ (xi-μ)²

                if (eventTimer.ElapsedMilliseconds > 500)
                {
                    SendEvent();
                    eventTimer.Restart();
                }
            }
        }

        public void SendEvent()
        {
            double stderr = iterationsCompleted > 1
                ? Math.Sqrt(m2 / (iterationsCompleted - 1) / iterationsCompleted)
                : double.PositiveInfinity;

            progressEvent?.Invoke((currentEstimate, stderr, iterationsCompleted));
        }
    }

    private static void EstimateSolutionsInternal(Solver root, EstimateSolutionsState state)
    {
        const int tinyBranchThreshold = 25;   // "exact" cut-off
        const double uniformMix = 0.30; // α
        Random rng = ThreadLocalRandom.Instance;

        int maxVal = root.MAX_VALUE;
        bool[] isExact = new bool[maxVal];
        double[] heuristic = new double[maxVal];
        double[] probCache = new double[maxVal];
        Solver[] childSolvers = new Solver[maxVal];

        Stack<EstimationFrame> stack = new(capacity: 64);
        stack.Push(new EstimationFrame(root, PathProb: 1.0, ExactOffset: 0.0));

        while (stack.Count > 0)
        {
            state.cancellationToken.ThrowIfCancellationRequested();
            EstimationFrame frame = stack.Pop();

            Solver solver = frame.Board;
            double pathProb = frame.PathProb;
            double exactCarry = frame.ExactOffset;

            //------------------------------------------------------------
            // 1.  Propagate singles
            //------------------------------------------------------------
            LogicResult lr = solver.BruteForcePropagate();
            if (lr == LogicResult.Invalid)
            {
                state.NewSample(exactCarry); // contributes 0
                return;
            }
            if (lr == LogicResult.PuzzleComplete)
            {
                state.NewSample(exactCarry + 1.0 / pathProb);
                return;
            }

            //------------------------------------------------------------
            // 2.  Choose MRV cell
            //------------------------------------------------------------
            (int cell, _) = solver.GetLeastCandidateCell(allowBilocals: false);
            if (cell < 0)
            {
                // Defensive: treat as solved
                state.NewSample(exactCarry + 1.0 / pathProb);
                return;
            }

            //------------------------------------------------------------
            // 3.  Build weights & exact subtotals
            //------------------------------------------------------------
            uint cellMask = solver.board[cell];
            double H = 0.0;
            int kOpen = 0;
            double exactSum = 0.0;

            Array.Clear(heuristic);
            Array.Clear(childSolvers);

            for (int val = 1; val <= solver.MAX_VALUE; val++)
            {
                int idx = val - 1;

                if ((cellMask & ValueMask(val)) == 0)
                {
                    // digit forbidden
                    continue;
                }

                Solver childSolver = solver.Clone(willRunNonSinglesLogic: false);
                if (!childSolver.SetValue(cell, val))
                {
                    // contradiction
                    continue;
                }

                LogicResult childResult = childSolver.BruteForcePropagate();
                if (childResult == LogicResult.Invalid)
                {
                    // contradiction
                    continue;
                }
                if (childResult == LogicResult.PuzzleComplete)
                {
                    // solved instantly
                    exactSum += 1.0;
                    continue;
                }

                int remaining = childSolver.CountCandidatesForNonGivens();
                if (remaining <= tinyBranchThreshold)
                {
                    // exact enumeration for tiny branch
                    long exactCnt = childSolver.CountSolutions(cancellationToken: state.cancellationToken);
                    exactSum += exactCnt;
                    continue;
                }

                // --- Monte-Carlo child ---
                childSolvers[idx] = childSolver;
                heuristic[idx] = remaining; // h_i
                H += remaining;
                kOpen++;
            }

            //------------------------------------------------------------
            // 4.  If no MC children left, emit deterministic total
            //------------------------------------------------------------
            if (kOpen == 0)
            {
                state.NewSample(exactCarry + exactSum / pathProb);
                return;
            }

            //------------------------------------------------------------
            // 5.  Convert h_i to probabilities
            //------------------------------------------------------------
            for (int idx = 0; idx < maxVal; idx++)
            {
                if (heuristic[idx] == 0) { probCache[idx] = 0; continue; }

                probCache[idx] = uniformMix / kOpen
                               + (1.0 - uniformMix) * (heuristic[idx] / H);
            }

            //------------------------------------------------------------
            // 6.  Roulette-wheel selection
            //------------------------------------------------------------
            double r = rng.NextDouble();
            double acc = 0.0;
            int chosenIdx = -1;

            for (int idx = 0; idx < maxVal; idx++)
            {
                if (probCache[idx] == 0)
                {
                    continue;
                }

                acc += probCache[idx];

                if (r <= acc || idx == maxVal - 1) // fallback on last open child
                {
                    chosenIdx = idx;
                    break;
                }
            }

            int chosenVal = chosenIdx + 1;

            //------------------------------------------------------------
            // 7.  Recurse on chosen child
            //------------------------------------------------------------
            double newExactCarry = exactCarry + exactSum / pathProb;
            double newPathProb = pathProb * probCache[chosenIdx];
            stack.Push(new EstimationFrame(childSolvers[chosenIdx], newPathProb, newExactCarry));
        }
    }

    // Frame record used by the explicit stack
    private readonly record struct EstimationFrame(
        Solver Board,
        double PathProb,
        double ExactOffset);


    // Simple ThreadLocal RNG to avoid Guid overhead
    private static class ThreadLocalRandom
    {
        public static readonly ThreadLocal<Random> _rng =
            new(() => new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId)));
        public static Random Instance => _rng.Value!;
    }
}
