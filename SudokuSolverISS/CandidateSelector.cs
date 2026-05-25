namespace SudokuSolverISS;

/// <summary>
/// Port of ISS candidate_selector.js.
/// Maintains _cellOrder (dynamic order reordered in-place).
/// selectNextCandidate returns (nextDepth, value, count):
///   nextDepth = new cellDepth after moving singletons to front
///   value     = single-bit mask of the value to try
///   count     = candidate count of chosen cell (0 = contradiction, 1 = forced)
///
/// Additionally implements _findCustomCandidates: when the best cell has
/// 3+ candidates and a non-zero conflict score, scan house finders for a
/// "bivalue" branching opportunity (value in exactly 2 cells) that has a
/// better score than the raw cell branching.
/// </summary>
sealed class CandidateSelector
{
    private readonly int[]   _cellOrder;
    private readonly int     _numSearchCells;
    private readonly ConflictScores _cs;

    private readonly HouseCandidateFinder[] _houseFinders;
    private readonly int[][] _cellToHouses;   // cell → indices into _houseFinders
    private readonly bool[]  _houseProcessed; // cleared per TryFindCustomCandidates call

    public CandidateSelector(
        int[]               initialCellOrder,
        ConflictScores      conflictScores,
        int[][]             houseCells)
    {
        _cellOrder      = (int[])initialCellOrder.Clone();
        _numSearchCells = initialCellOrder.Length;
        _cs             = conflictScores;

        _houseFinders   = Array.ConvertAll(houseCells, h => new HouseCandidateFinder(h));
        _houseProcessed = new bool[_houseFinders.Length];

        // Build reverse map: cell → house indices.
        var lists = new List<int>[G.NUM_CELLS];
        for (int i = 0; i < G.NUM_CELLS; i++) lists[i] = [];
        for (int h = 0; h < _houseFinders.Length; h++)
            foreach (int cell in _houseFinders[h].HouseCells)
                lists[cell].Add(h);
        _cellToHouses = Array.ConvertAll(lists, l => l.ToArray());
    }

    public int GetCellAtDepth(int depth) => _cellOrder[depth];

    /// <summary>
    /// ISS selectNextCandidate.
    /// Returns (nextDepth, valueMask, count).
    /// count==0 means contradiction; nextDepth==_numSearchCells means all cells fixed.
    /// </summary>
    /// <summary>
    /// Normal entry: select best candidate for a NEW node.
    /// Returns (nextDepth, value, count, altCell) where altCell ≥ 0 means a
    /// house-bivalue custom candidate was chosen and altCell is the second option
    /// (to be stored in the continuation frame for ISS-style count-1 retry).
    /// </summary>
    public (int nextDepth, uint value, int count, int altCell) SelectNextCandidate(
        int cellDepth, uint[] grid, bool isNewNode = true)
    {
        var (cellOffset, value, count, altCell) = SelectBestCandidate(grid, cellDepth, isNewNode);
        int nextDepth = UpdateCellOrder(cellDepth, cellOffset, count, grid);
        return (nextDepth, value, count, altCell);
    }

    /// <summary>
    /// Continuation entry: the frame saved altCell/altValue from a prior house-bivalue
    /// decision.  Moves altCell to the front of cellOrder and returns (nextDepth, altValue, 1).
    /// Returns count=-1 if the alt cell was already resolved (fall through to normal selection).
    /// </summary>
    public (int nextDepth, uint value, int count) SelectContinuationCandidate(
        int cellDepth, uint[] grid, int contCell, uint contValue)
    {
        // Find the alt cell in the remaining search cells and move it to front.
        int cellOffset = FindCellOffset(contCell, cellDepth);
        if (cellOffset < 0)
        {
            // Alt cell was already resolved by propagation.
            // Signal caller to fall through to normal candidate selection.
            return (0, 0, -1);
        }
        // Use count=1 so that UpdateCellOrder scans for additional singletons.
        int nextDepth = UpdateCellOrder(cellDepth, cellOffset, 1, grid);
        return (nextDepth, contValue, 1);
    }

    // ---- SelectBestCandidate ------------------------------------------------

    private (int cellOffset, uint value, int count, int altCell) SelectBestCandidate(
        uint[] grid, int cellDepth, bool isNewNode)
    {
        int  cellOffset = SelectBestCell(grid, cellDepth);
        uint cellMask   = grid[_cellOrder[cellOffset]];
        int  count      = cellMask == 0 ? 0 : G.Count(cellMask);
        uint value      = G.LowestBit(cellMask);

        // Mirrors ISS: custom candidates only on new nodes (isNewNode guard at line 315).
        // On continuation frames (isNewNode=false), ISS uses saved state or skips entirely.
        if (isNewNode && count > 2)
        {
            int cs = _cs.Scores[_cellOrder[cellOffset]];
            if (cs > 0)
            {
                var result = new CustomCandidateResult
                {
                    Score = cs / (double)count,
                    Cell  = -1,
                };
                if (TryFindCustomCandidates(grid, cellDepth, ref result))
                {
                    int customOffset = FindCellOffset(result.Cell, cellDepth);
                    if (customOffset >= 0)
                        return (customOffset, result.Value, 2, result.AltCell);
                }
            }
        }

        return (cellOffset, value, count, -1);
    }

    // ---- port of ISS _selectBestCell ----------------------------------------

    private int SelectBestCell(uint[] grid, int cellDepth)
    {
        var   scores     = _cs.Scores;
        var   (maxValBit, maxValScore) = _cs.GetMaxValueScore();
        float maxScore   = -1f;
        int   bestOffset = cellDepth;

        for (int i = cellDepth; i < _numSearchCells; i++)
        {
            int  cell  = _cellOrder[i];
            uint mask  = grid[cell];
            int  count = G.Count(mask);
            if (count <= 1) { bestOffset = i; break; }   // singleton or empty — take it

            float sc = scores[cell];
            // Prefer cells that contain the hot value (ISS: += maxValueScore * 0.2).
            if ((mask & maxValBit) != 0) sc += maxValScore * 0.2f;

            if (sc > maxScore * count)
            {
                bestOffset = i;
                maxScore   = sc / count;
            }
        }

        // If maxScore == 0 (no conflict history), fall back to pure MRV.
        if (maxScore == 0f)
            bestOffset = MinCountIndex(grid, cellDepth);

        return bestOffset;
    }

    private int MinCountIndex(uint[] grid, int cellDepth)
    {
        int bestOff = cellDepth, bestCount = int.MaxValue;
        for (int i = cellDepth; i < _numSearchCells; i++)
        {
            int c = G.Count(grid[_cellOrder[i]]);
            if (c < bestCount) { bestCount = c; bestOff = i; if (c == 2) break; }
        }
        return bestOff;
    }

    // ---- port of ISS _findCustomCandidates ----------------------------------

    private bool TryFindCustomCandidates(
        uint[] grid, int cellDepth, ref CustomCandidateResult result)
    {
        var scores = _cs.Scores;
        Array.Clear(_houseProcessed);

        // minCS: minimum conflict score for a cell to potentially yield a
        // house finder that beats the current result.
        // House score = maxCS * 0.5; need maxCS * 0.5 > result.Score
        // → maxCS > 2 * result.Score → minCS = ceil(2 * result.Score).
        int minCS = (int)Math.Ceiling(result.Score * 2.0);
        bool found = false;

        for (int i = cellDepth; i < _numSearchCells; i++)
        {
            int cell = _cellOrder[i];
            if (scores[cell] < minCS) continue;

            foreach (int hIdx in _cellToHouses[cell])
            {
                if (_houseProcessed[hIdx]) continue;
                _houseProcessed[hIdx] = true;

                double prevScore = result.Score;
                _houseFinders[hIdx].MaybeFindCandidate(grid, scores, ref result);
                if (result.Score > prevScore)
                {
                    found = true;
                    minCS = (int)Math.Ceiling(result.Score * 2.0);
                }
            }
        }

        return found && result.Cell >= 0;
    }

    private int FindCellOffset(int cell, int cellDepth)
    {
        for (int i = cellDepth; i < _numSearchCells; i++)
            if (_cellOrder[i] == cell) return i;
        return -1;
    }

    // ---- port of ISS _updateCellOrder ---------------------------------------
    // Swaps the chosen cell to cellOrder[cellDepth], then moves all singletons
    // (count==1) forward.  Returns the new frontOffset (nextDepth).

    private int UpdateCellOrder(int cellDepth, int cellOffset, int count, uint[] grid)
    {
        var co = _cellOrder;
        int front = cellDepth;

        // Swap chosen cell to front.
        (co[cellOffset], co[front]) = (co[front], co[cellOffset]);
        front++;
        cellOffset++;

        // If count > 1, no singletons to move.
        if (count > 1) return front;

        // Move all singletons to the front (ISS exact algorithm).
        while (cellOffset < _numSearchCells)
        {
            // Skip items already at the frontier.
            while (cellOffset == front && cellOffset < _numSearchCells)
            {
                uint v = grid[co[cellOffset++]];
                if (G.IsSingleton(v) || v == 0) { front++; if (v == 0) return 0; }
                else break;
            }
            if (cellOffset >= _numSearchCells) break;
            uint cv = grid[co[cellOffset]];
            if (G.IsSingleton(cv) || cv == 0)
            {
                if (cv == 0) return 0;
                (co[cellOffset], co[front]) = (co[front], co[cellOffset]);
                front++;
            }
            cellOffset++;
        }
        return front;
    }
}
