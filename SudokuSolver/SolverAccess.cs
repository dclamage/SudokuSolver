namespace SudokuSolver;

public partial class Solver
{
    public uint DisabledLogicFlags
    {
        get
        {
            uint disabledLogicFlags = 0;
            if (DisableTuples)
            {
                disabledLogicFlags |= (1u << 0);
            }
            if (DisablePointing)
            {
                disabledLogicFlags |= (1u << 1);
            }
            if (DisableFishes)
            {
                disabledLogicFlags |= (1u << 2);
            }
            if (DisableWings)
            {
                disabledLogicFlags |= (1u << 3);
            }
            if (DisableAIC)
            {
                disabledLogicFlags |= (1u << 4);
            }
            if (DisableContradictions)
            {
                disabledLogicFlags |= (1u << 5);
            }
            return disabledLogicFlags;
        }
    }

    public IReadOnlyList<int> Regions => regions;
    public List<int>[] WeakLinks => weakLinks;

    private List<int>[] CloneWeakLinks()
    {
        List<int>[] newWeakLinks = new List<int>[NUM_CANDIDATES];
        for (int ci = 0; ci < NUM_CANDIDATES; ci++)
        {
            newWeakLinks[ci] = [.. weakLinks[ci]];
        }
        return newWeakLinks;
    }

    public IReadOnlyList<uint> FlatBoard => board;
    public BoardView Board => new(board, WIDTH, HEIGHT);
    public uint[] BoardClone
    {
        get
        {
            uint[] boardClone = new uint[board.Length];
            board.AsSpan().CopyTo(boardClone);
            return boardClone;
        }
    }

    public uint this[int row, int col]
    {
        get
        {
            if ((uint)row >= (uint)HEIGHT || (uint)col >= (uint)WIDTH)
                throw new IndexOutOfRangeException();
            return board[row * WIDTH + col];
        }
    }
    
    public IEnumerable<T> Constraints<T>() where T : Constraint => constraints.Select(c => c as T).Where(c => c != null);

    public string GivenString
    {
        get
        {
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            string nonGiven = new('0', digitWidth);
            StringBuilder stringBuilder = new(board.Length);
            foreach (uint mask in board)
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
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            StringBuilder stringBuilder = new(board.Length * digitWidth);
            foreach (uint mask in board)
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
            int digitWidth = MAX_VALUE >= 10 ? 2 : 1;
            StringBuilder stringBuilder = new(board.Length * digitWidth);
            foreach (uint mask in board)
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

    /// <summary>
    /// Determines if the board has all values set.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                if (!IsValueSet(cellIndex))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public bool IsBruteForcing => isBruteForcing;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetValue((int, int) cell)
    {
        return SolverUtility.GetValue(board[CellIndex(cell.Item1, cell.Item2)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetValue(uint mask)
    {
        return SolverUtility.GetValue(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValueSet(int cellIndex)
    {
        return SolverUtility.IsValueSet(board[cellIndex]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValueSet(int i, int j) => IsValueSet(CellIndex(i, j));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValueSet(uint mask)
    {
        return SolverUtility.IsValueSet(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskStrictlyHigher(int v) => ALL_VALUES_MASK & ~((1u << v) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskValAndHigher(int v) => ALL_VALUES_MASK & ~((1u << (v - 1)) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskBetweenInclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskStrictlyLower(v0) | MaskStrictlyHigher(v1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MaskBetweenExclusive(int v0, int v1) => ALL_VALUES_MASK & ~(MaskValAndLower(v0) | MaskValAndHigher(v1));

    internal string ValueNames(uint mask) =>
        string.Join(MAX_VALUE <= 9 ? "" : ",", Enumerable.Range(1, MAX_VALUE).Where(v => HasValue(mask, v)));

    internal string CompactName(uint mask, IReadOnlyList<int> cells) =>
        ValueNames(mask) + CompactName([.. cells.Select(CellIndexToCoord)]);

    internal string CompactName(uint mask, IReadOnlyList<(int, int)> cells) =>
        ValueNames(mask) + CompactName(cells);

    internal string CompactName(IReadOnlyList<int> cells) =>
        CompactName(cells.Select(cellIndex => CellIndexToCoord(cellIndex)).ToList());

    internal string CompactName(IReadOnlyList<(int, int)> cells)
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

    public uint CandidateMask(IEnumerable<int> cells)
    {
        uint mask = 0;
        foreach (int curCellIndex in cells)
        {
            mask |= board[curCellIndex];
        }
        return mask & ~valueSetMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CellIndex(int i, int j) => (i * WIDTH + j);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CellIndex((int, int) cell) => (cell.Item1 * WIDTH + cell.Item2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int) CellIndexToCoord(int cellIndex) => (cellIndex / WIDTH, cellIndex % WIDTH);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex(int cellIndex, int v) => cellIndex * MAX_VALUE + v - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex(int i, int j, int v) => (i * WIDTH + j) * MAX_VALUE + v - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex((int, int) cell, int v) => (cell.Item1 * WIDTH + cell.Item2) * MAX_VALUE + v - 1;

    internal string CellIndexName(int cellIndex) => CellName(CellIndexToCoord(cellIndex));

    internal List<int> CandidateIndexes(uint valueMask, IEnumerable<(int, int)> cells)
    {
        List<int> result = new();
        foreach (var cell in cells)
        {
            uint mask = this[cell.Item1, cell.Item2] & valueMask;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int) CandIndexToCellAndValue(int candIndex)
    {
        return candidateToCellAndValueLookup[candIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int, int, int) CandIndexToCoord(int candIndex)
    {
        return candidateToCoordValueLookup[candIndex];
    }

    internal string CandIndexDesc(int candIndex)
    {
        var (i, j, v) = CandIndexToCoord(candIndex);
        return $"{v}{CellName(i, j)}";
    }

    internal bool HasCandidate(int candIndex)
    {
        var (cell, v) = CandIndexToCellAndValue(candIndex);
        uint mask = board[cell];
        return HasValue(mask, v);
    }

    internal bool IsCandIndexValid(int candIndex)
    {
        var (cell, v) = CandIndexToCellAndValue(candIndex);
        uint mask = board[cell];
        return !IsValueSet(mask) && HasValue(mask, v);
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

        List<(List<int>, string)> elimDescs = new();
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            var elimCells = elimsByVal[v - 1];
            if (elimCells != null && elimCells.Count > 0)
            {
                elimCells.Sort();
                elimDescs.Add(([v], CompactName(elimCells)));
            }
        }

        // Check for elim descriptions that differ only by value
        for (int i1 = 0; i1 < elimDescs.Count; i1++)
        {
            for (int i2 = i1 + 1; i2 < elimDescs.Count; i2++)
            {
                if (elimDescs[i1].Item2 == elimDescs[i2].Item2)
                {
                    elimDescs[i1].Item1.AddRange(elimDescs[i2].Item1);
                    elimDescs.RemoveAt(i2);
                    i2--;
                }
            }
        }

        List<string> elimDescsFinal = elimDescs.Select(desc => $"-{string.Join("", desc.Item1)}{desc.Item2}").ToList();
        return string.Join(';', elimDescsFinal);
    }

    internal int NumSetValues
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

    private int AmountCellsFilled()
    {
        int counter = 0;
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            counter += IsValueSet(cellIndex) ? 1 : 0;
        }
        return counter;
    }
}
