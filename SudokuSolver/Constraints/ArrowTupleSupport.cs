namespace SudokuSolver.Constraints;

/// <summary>
/// Precomputed full-tuple support propagation for circle arrows during brute-force search.
/// </summary>
internal sealed class ArrowTupleSupport
{
    private readonly int[] cellIndices;
    private readonly uint[] tupleMasks;
    private readonly int width;

    private ArrowTupleSupport(int[] cellIndices, uint[] tupleMasks)
    {
        this.cellIndices = cellIndices;
        this.tupleMasks = tupleMasks;
        width = cellIndices.Length;
    }

    /// <summary>
    /// Builds tuple-support data for a single-circle arrow.
    /// </summary>
    /// <param name="solver">The finalized solver used for cell indexes and peer checks.</param>
    /// <param name="circleCells">The arrow circle cells; exactly one is required.</param>
    /// <param name="arrowCells">The arrow shaft cells.</param>
    /// <returns>Tuple-support data, or <c>null</c> when the arrow shape is unsupported.</returns>
    internal static ArrowTupleSupport Build(Solver solver, List<(int, int)> circleCells, List<(int, int)> arrowCells)
    {
        if (circleCells.Count != 1 || arrowCells.Count == 0)
        {
            return null;
        }

        int width = 1 + arrowCells.Count;
        int[] cellIndices = new int[width];
        cellIndices[0] = solver.CellIndex(circleCells[0]);
        for (int i = 0; i < arrowCells.Count; i++)
        {
            cellIndices[i + 1] = solver.CellIndex(arrowCells[i]);
        }

        for (int i = 0; i < cellIndices.Length - 1; i++)
        {
            for (int j = i + 1; j < cellIndices.Length; j++)
            {
                if (cellIndices[i] == cellIndices[j])
                {
                    return null;
                }
            }
        }

        bool[] peers = new bool[width * width];
        for (int i = 0; i < width - 1; i++)
        {
            for (int j = i + 1; j < width; j++)
            {
                bool isPeer = solver.IsSeen(cellIndices[i], cellIndices[j]);
                peers[i * width + j] = isPeer;
                peers[j * width + i] = isPeer;
            }
        }

        int[] values = new int[width];
        List<uint> tuples = [];
        for (int circleValue = 1; circleValue <= solver.MAX_VALUE; circleValue++)
        {
            values[0] = circleValue;
            BuildTuples(solver.MAX_VALUE, width, peers, values, tuples, index: 1, remainingSum: circleValue);
        }

        return tuples.Count == 0 ? null : new ArrowTupleSupport(cellIndices, tuples.ToArray());
    }

    /// <summary>
    /// Restricts all cells to values supported by at least one currently valid tuple.
    /// </summary>
    /// <param name="solver">The solver state to inspect and update.</param>
    /// <returns>The propagation result after applying tuple-supported masks.</returns>
    internal LogicResult StepLogic(Solver solver)
    {
        Span<uint> supports = stackalloc uint[width];
        uint[] board = solver.BoardArray;

        for (int offset = 0; offset < tupleMasks.Length; offset += width)
        {
            bool valid = true;
            for (int i = 0; i < width; i++)
            {
                if ((board[cellIndices[i]] & tupleMasks[offset + i]) == 0)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                continue;
            }

            for (int i = 0; i < width; i++)
            {
                supports[i] |= tupleMasks[offset + i];
            }
        }

        LogicResult result = LogicResult.None;
        for (int i = 0; i < width; i++)
        {
            if (supports[i] == 0)
            {
                return LogicResult.Invalid;
            }

            var keepResult = solver.KeepMask(cellIndices[i], supports[i]);
            if (keepResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (keepResult == LogicResult.Changed)
            {
                result = LogicResult.Changed;
            }
        }

        return result;
    }

    /// <summary>
    /// Registers weak links between candidate pairs that cannot appear together in any currently valid tuple.
    /// </summary>
    /// <param name="solver">The solver state to inspect and update.</param>
    /// <returns>The result of adding the direct tuple incompatibility links.</returns>
    internal LogicResult InitLinks(Solver solver)
    {
        LogicResult result = LogicResult.None;
        uint[] board = solver.BoardArray;

        for (int left = 0; left < width - 1; left++)
        {
            uint leftMask = board[cellIndices[left]] & solver.ALL_VALUES_MASK;
            while (leftMask != 0)
            {
                int leftValue = MinValue(leftMask);
                uint leftValueMask = ValueMask(leftValue);
                leftMask &= ~leftValueMask;

                int leftCandidate = solver.CandidateIndex(cellIndices[left], leftValue);
                for (int right = left + 1; right < width; right++)
                {
                    uint rightMask = board[cellIndices[right]] & solver.ALL_VALUES_MASK;
                    uint supportedRightMask = SupportedMaskForTuplePair(board, left, leftValueMask, right);
                    uint elimMask = rightMask & ~supportedRightMask;
                    while (elimMask != 0)
                    {
                        int rightValue = MinValue(elimMask);
                        elimMask &= ~ValueMask(rightValue);

                        LogicResult linkResult = solver.AddWeakLink(leftCandidate, solver.CandidateIndex(cellIndices[right], rightValue));
                        if (linkResult == LogicResult.Invalid)
                        {
                            return LogicResult.Invalid;
                        }
                        if (linkResult == LogicResult.Changed)
                        {
                            result = LogicResult.Changed;
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets whether this tuple support watches the given cell.
    /// </summary>
    /// <param name="cellIndex">The cell index to test.</param>
    /// <returns><c>true</c> when the cell participates in this arrow.</returns>
    internal bool ContainsCell(int cellIndex)
    {
        for (int i = 0; i < cellIndices.Length; i++)
        {
            if (cellIndices[i] == cellIndex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether at least one precomputed tuple remains compatible with the current board masks.
    /// </summary>
    /// <param name="solver">The solver state to inspect.</param>
    /// <returns><c>true</c> when a compatible tuple exists.</returns>
    internal bool HasValidTuple(Solver solver)
    {
        uint[] board = solver.BoardArray;
        for (int offset = 0; offset < tupleMasks.Length; offset += width)
        {
            bool valid = true;
            for (int i = 0; i < width; i++)
            {
                if ((board[cellIndices[i]] & tupleMasks[offset + i]) == 0)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                return true;
            }
        }

        return false;
    }

    private uint SupportedMaskForTuplePair(uint[] board, int left, uint leftValueMask, int right)
    {
        uint result = 0;
        for (int offset = 0; offset < tupleMasks.Length; offset += width)
        {
            if ((tupleMasks[offset + left] & leftValueMask) == 0 || !IsTupleValid(board, offset))
            {
                continue;
            }

            result |= tupleMasks[offset + right];
        }

        return result;
    }

    private bool IsTupleValid(uint[] board, int offset)
    {
        for (int i = 0; i < width; i++)
        {
            if ((board[cellIndices[i]] & tupleMasks[offset + i]) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void BuildTuples(int maxValue, int width, bool[] peers, int[] values, List<uint> tuples, int index, int remainingSum)
    {
        if (index == width)
        {
            if (remainingSum == 0)
            {
                for (int i = 0; i < width; i++)
                {
                    tuples.Add(ValueMask(values[i]));
                }
            }
            return;
        }

        int remainingCells = width - index - 1;
        int minRemaining = remainingCells;
        int maxRemaining = remainingCells * maxValue;
        int maxCurrentValue = Math.Min(maxValue, remainingSum - minRemaining);

        for (int value = 1; value <= maxCurrentValue; value++)
        {
            int nextRemaining = remainingSum - value;
            if (nextRemaining < minRemaining || nextRemaining > maxRemaining)
            {
                continue;
            }

            if (!RespectsPreviousPeerValues(width, peers, values, index, value))
            {
                continue;
            }

            values[index] = value;
            BuildTuples(maxValue, width, peers, values, tuples, index + 1, nextRemaining);
        }
    }

    private static bool RespectsPreviousPeerValues(int width, bool[] peers, int[] values, int index, int value)
    {
        for (int previousIndex = 0; previousIndex < index; previousIndex++)
        {
            if (values[previousIndex] == value && peers[index * width + previousIndex])
            {
                return false;
            }
        }

        return true;
    }
}