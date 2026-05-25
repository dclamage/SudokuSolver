namespace SudokuSolverISS;

/// <summary>
/// Tracks which (cell, value) pairs have appeared across solutions.
/// Enables branch pruning: if every candidate in the current grid has already
/// been seen in a prior solution, this branch cannot produce an interesting result.
/// Mirrors ISS JS _seenCandidateSet (engine.js solveAllPossibilities).
/// Activates only after the 2nd solution is found.
/// </summary>
sealed class SeenCandidateSet
{
    private readonly uint[] _candidates = new uint[G.NUM_CELLS];
    private int  _lastInterestingCell;
    private bool _dirty;
    public  bool EnabledInSolver;

    public void Reset()
    {
        if (!_dirty) return;
        _dirty = false;
        EnabledInSolver = false;
        Array.Clear(_candidates);
        _lastInterestingCell = 0;
    }

    public void AddSolutionGrid(uint[] grid)
    {
        for (int c = 0; c < G.NUM_CELLS; c++) _candidates[c] |= grid[c];
        _dirty = true;
    }

    /// <summary>
    /// Returns true if the grid contains at least one candidate value that has
    /// not yet appeared in any recorded solution (i.e., worth exploring).
    /// Uses a cached "last interesting cell" to short-circuit the common case.
    /// </summary>
    public bool HasInterestingSolutions(uint[] grid)
    {
        if ((grid[_lastInterestingCell] & ~_candidates[_lastInterestingCell]) != 0)
            return true;
        for (int c = 0; c < G.NUM_CELLS; c++)
            if ((grid[c] & ~_candidates[c]) != 0) { _lastInterestingCell = c; return true; }
        return false;
    }
}

/// <summary>
/// Phase-2 port of ISS engine.js _runLoop.
/// Pre-allocated grid pool + struct frame stack: zero heap allocation per branch.
/// Counters match ISS JS: guesses, valuesTried, constraintsProcessed,
/// nodesSearched, backtracks, progressRatio.
/// </summary>
sealed class ISSSolver
{
    // ---- Public counters (match ISS JS names) ----
    public long   Guesses              { get; private set; }
    public long   ValuesTried          { get; private set; }
    public long   ConstraintsProcessed { get; private set; }
    public long   NodesSearched        { get; private set; }
    public long   Backtracks           { get; private set; }
    public int    Solutions            { get; private set; }
    /// <summary>
    /// Fraction of weighted search space that reached solutions [0,1].
    /// Set via finalization after solve: branchesIgnored = 1 - ProgressRatio,
    /// then SearchSpaceExplored = NodesSearched / (NodesSearched + BranchesIgnored).
    /// </summary>
    public double ProgressRatio        { get; private set; }
    public double SearchSpaceExplored  =>
        (double)NodesSearched / (NodesSearched + (1.0 - ProgressRatio)) * 100.0;

    private readonly int                _numSearchCells;
    private readonly CandidateSelector  _selector;
    private readonly ConflictScores     _cs;
    private readonly HandlerAccumulator _acc;
    private readonly uint[]             _rootGrid;
    private readonly SeenCandidateSet   _seen = new();

    // Each search cell can create at most 2 frames (continuation + forward).
    private const int MAX_FRAMES = G.NUM_CELLS * 2 + 4;

    // Pre-allocated grid pool.
    private readonly uint[][] _gridPool  = new uint[MAX_FRAMES][];
    private readonly int[]    _freeStack = new int[MAX_FRAMES];
    private int               _freeTop;

    // Pre-allocated frame stack.
    private struct Frame
    {
        public int    GridIdx;
        public int    CellDepth;
        public int    LastContradictionCell;
        public double ProgressRemaining;
        // Custom candidate continuation (mirrors ISS _candidateSelectionFlags):
        // when IsNewNode=false and ContCell>=0, skip normal selection and directly
        // try ContCell=ContValue with count=1 (no guess needed).
        public bool   IsNewNode;
        public int    ContCell;   // -1 = normal; >=0 = saved alt cell
        public uint   ContValue;
    }

    private readonly Frame[] _frames   = new Frame[MAX_FRAMES];
    private int              _frameTop;

    public ISSSolver(
        uint[]             initialGrid,
        int[]              searchCells,
        ConflictScores     conflictScores,
        HandlerAccumulator handlerAccumulator,
        int[][]            houseCells)
    {
        _numSearchCells = searchCells.Length;
        _rootGrid       = (uint[])initialGrid.Clone();
        _cs             = conflictScores;
        _acc            = handlerAccumulator;
        _selector       = new CandidateSelector(searchCells, conflictScores, houseCells);

        for (int i = 0; i < MAX_FRAMES; i++)
        {
            _gridPool[i]  = new uint[G.NUM_CELLS];
            _freeStack[i] = i;
        }
        _freeTop  = MAX_FRAMES - 1;
        _frameTop = 0;
    }

    // ---- Entry points ----

    /// <summary>Returns the first solution grid, or null if unsolvable.</summary>
    public uint[]? FindSolution()
    {
        ResetState();
        if (!SeedRoot()) return null;

        uint[]? result = null;
        RunLoop(ref result, maxSolutions: 1);
        return result;
    }

    /// <summary>
    /// Counts all solutions up to <paramref name="maxSolutions"/> (0 = unlimited).
    /// Returns the count.
    /// </summary>
    public int CountSolutions(int maxSolutions = 0)
    {
        ResetState();
        if (!SeedRoot()) return 0;

        uint[]? unused = null;
        RunLoop(ref unused, maxSolutions);
        return Solutions;
    }

    // ---- Core loop ----

    private const bool DebugTrace = true;
    private int _debugStep = 0;
    private const int DebugTraceLimit = 60;

    private void RunLoop(ref uint[]? firstSolution, int maxSolutions)
    {
        while (_frameTop > 0)
        {
            Frame frame = _frames[--_frameTop];
            int    gIdx  = frame.GridIdx;
            uint[] grid  = _gridPool[gIdx];
            int    depth = frame.CellDepth;
            int    lastContradiction = frame.LastContradictionCell;
            double progress = frame.ProgressRemaining;

            int    nextDepth, count;
            uint   value;
            int    altCell;

            if (!frame.IsNewNode && frame.ContCell >= 0)
            {
                // Saved house-bivalue continuation: try the alt cell with count=1.
                // Mirrors ISS _candidateSelectionFlags mechanism.
                var cont = _selector.SelectContinuationCandidate(
                    depth, grid, frame.ContCell, frame.ContValue);
                if (cont.count != -1)
                {
                    nextDepth = cont.nextDepth; value = cont.value; count = cont.count;
                    altCell   = -1;
                }
                else
                {
                    // Alt cell already resolved — fall through to normal selection.
                    // isNewNode=false mirrors ISS: no custom candidates on continuation.
                    var res = _selector.SelectNextCandidate(depth, grid, isNewNode: false);
                    nextDepth = res.nextDepth; value = res.value;
                    count = res.count;         altCell = res.altCell;
                }
            }
            else
            {
                var res = _selector.SelectNextCandidate(depth, grid, frame.IsNewNode);
                nextDepth = res.nextDepth; value = res.value;
                count = res.count;         altCell = res.altCell;
            }

            if (count == 0)
            {
                FreeGrid(gIdx);
                Backtracks++;
                continue;
            }

            ValuesTried += nextDepth - depth;

            _acc.Reset();
            for (int i = depth; i < nextDepth; i++)
                _acc.AddForFixedCell(_selector.GetCellAtDepth(i));

            if (lastContradiction >= 0)
                _acc.AddForCell(lastContradiction);

            int cell = _selector.GetCellAtDepth(depth);

            double progressDelta = count == 1 ? progress : progress / count;

            if (count != 1)
            {
                if (DebugTrace && _debugStep < DebugTraceLimit)
                {
                    _debugStep++;
                    int r = G.Row(cell) + 1, c = G.Col(cell) + 1;
                    int v = G.SingletonValue(value);
                    int cs = _cs.Scores[cell];
                    Console.Error.WriteLine($"{_debugStep}\t{depth}\t{r}\t{c}\t{count}\t{cs}\t{v}");
                }

                int contIdx = AllocGrid();
                Array.Copy(grid, _gridPool[contIdx], G.NUM_CELLS);
                _gridPool[contIdx][cell] ^= value;

                // For house-bivalue custom candidates, save the alt cell so the
                // continuation can try it with count=1 (mirrors ISS _candidateSelectionFlags).
                PushFrame(contIdx, depth, -1, progress - progressDelta,
                          isNewNode: false, contCell: altCell, contValue: value);
                Guesses++;
            }

            grid[cell] = value;

            if (!Propagate(grid))
            {
                _cs.Increment(cell, value);
                ProgressRatio += progressDelta;  // mirrors ISS line 957
                if (_frameTop > 0)
                    _frames[_frameTop - 1].LastContradictionCell = cell;
                FreeGrid(gIdx);
                Backtracks++;
                continue;
            }

            if (nextDepth == _numSearchCells)
            {
                Solutions++;
                Backtracks++;   // ISS increments backtracks at solution find (line 996)
                ProgressRatio += progressDelta;
                if (firstSolution == null)
                    firstSolution = (uint[])grid.Clone();
                _seen.AddSolutionGrid(grid);
                if (Solutions == 2) _seen.EnabledInSolver = true;
                FreeGrid(gIdx);
                if (maxSolutions > 0 && Solutions >= maxSolutions)
                    break;
                continue;
            }

            // Prune branch if every candidate in this grid has already appeared
            // in a prior solution — no new information can come from this subtree.
            if (_seen.EnabledInSolver && !_seen.HasInterestingSolutions(grid))
            {
                ProgressRatio += progressDelta;
                FreeGrid(gIdx);
                continue;
            }

            NodesSearched++;    // new node (mirrors ISS line 1022)
            PushFrame(gIdx, nextDepth, -1, progressDelta, isNewNode: true);
        }

        // Free any frames remaining in the stack (early exit or end of search).
        while (_frameTop > 0)
            FreeGrid(_frames[--_frameTop].GridIdx);
    }

    // ---- Helpers ----

    private void ResetState()
    {
        Guesses = ValuesTried = ConstraintsProcessed = NodesSearched = Backtracks = 0;
        Solutions = 0;
        ProgressRatio = 0;
        _frameTop = 0;
        _seen.Reset();
        // Return all pool slots to free stack.
        _freeTop = MAX_FRAMES - 1;
        for (int i = 0; i < MAX_FRAMES; i++) _freeStack[i] = i;
    }

    /// <summary>
    /// Seeds propagation on the root grid and pushes the initial frame.
    /// Returns false if the puzzle is immediately unsolvable.
    /// Mirrors ISS: phase 1 propagates given cells (singletons) with full cascade;
    /// phase 2 runs one pass of ordinary handlers (Sum + AllDifferent) for all cells
    /// without cascade, matching ISS _initRun.
    /// </summary>
    private bool SeedRoot()
    {
        _acc.Reset();

        int rootIdx = AllocGrid();
        Array.Copy(_rootGrid, _gridPool[rootIdx], G.NUM_CELLS);
        uint[] grid = _gridPool[rootIdx];

        // Phase 1: propagate any given (singleton) cells with full cascade.
        for (int i = 0; i < G.NUM_CELLS; i++)
            if (G.IsSingleton(grid[i])) _acc.AddForFixedCell(i);

        if (!_acc.IsEmpty() && !Propagate(grid))
        {
            FreeGrid(rootIdx);
            Backtracks++;
            return false;
        }

        // Phase 2: mirrors ISS _initRun — queue ordinary handlers for every cell,
        // then propagate (ordinary handlers only; no singleton exclusion since constraint
        // handlers now call AddForCell, not AddForFixedCell, matching ISS exactly).
        for (int i = 0; i < G.NUM_CELLS; i++)
            _acc.AddForCell(i);

        bool ok = Propagate(grid);

        if (!ok)
        {
            FreeGrid(rootIdx);
            Backtracks++;
            return false;
        }

        NodesSearched = 1;  // root node (mirrors ISS line 842)
        PushFrame(rootIdx, 0, -1, 1.0, isNewNode: true, contCell: -1, contValue: 0);
        return true;
    }

    private int AllocGrid() => _freeStack[_freeTop--];
    private void FreeGrid(int idx) => _freeStack[++_freeTop] = idx;

    private void PushFrame(
        int gridIdx, int depth, int contradictionCell, double progressRemaining,
        bool isNewNode = true, int contCell = -1, uint contValue = 0)
    {
        ref Frame f = ref _frames[_frameTop++];
        f.GridIdx               = gridIdx;
        f.CellDepth             = depth;
        f.LastContradictionCell = contradictionCell;
        f.ProgressRemaining     = progressRemaining;
        f.IsNewNode             = isNewNode;
        f.ContCell              = contCell;
        f.ContValue             = contValue;
    }

    private bool Propagate(uint[] grid)
    {
        while (!_acc.IsEmpty())
        {
            ConstraintsProcessed++;
            if (!_acc.TakeNext().EnforceConsistency(grid, _acc)) return false;
        }
        return true;
    }
}
