namespace SudokuSolver;

/// THROWAWAY PROTOTYPE -- see docs/weak-link-bitmatrix-exploration.md. Dense candidate x candidate
/// weak-link bitmatrix, one row of wlMatrixWords ulongs per candidate, built from the authoritative
/// lists and gated on SUDOKU_WL_MATRIX=1.
public partial class Solver
{
    internal static readonly bool WlMatrixEnabled =
        Environment.GetEnvironmentVariable("SUDOKU_WL_MATRIX") == "1";

    /// <summary>Cell forcing implementation: <c>csr</c> (default) or <c>matrix</c>.</summary>
    internal static readonly bool CellForcingMatrixPath =
        Environment.GetEnvironmentVariable("SUDOKU_CF_PATH") == "matrix";

    /// <summary>Skyscraper support-search blocked test: <c>list</c> (default), <c>matrix</c>, <c>counts</c>.</summary>
    internal static readonly bool SkyscraperCountsPath =
        Environment.GetEnvironmentVariable("SUDOKU_SKY_PATH") == "counts";

    /// <summary>The authoritative weak-link list for a candidate, for callers maintaining their own index.</summary>
    internal List<int> WeakLinksFor(int candIndex) => weakLinks[candIndex];

    internal int CandidateCount => NUM_CANDIDATES;

    private ulong[] wlMatrix;
    private int wlMatrixWords;

    /// <summary>
    /// Builds the bitmatrix. Rows are pruned to live target candidates, matching the cell-forcing
    /// table's dead-target prune, so the two paths are compared on equal terms.
    /// </summary>
    internal void CompileWlBitmatrix()
    {
        if (weakLinks == null || wlMatrix != null || !WlMatrixEnabled || MAX_VALUE > 12)
        {
            return;
        }

        wlMatrixWords = (NUM_CANDIDATES + 63) >> 6;

        // Live target candidates, so a row never carries a bit that could not be eliminated.
        ulong[] live = new ulong[wlMatrixWords];
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint cand = board[cellIndex] & ~valueSetMask;
            while (cand != 0)
            {
                int v = MinValue(cand);
                cand &= ~ValueMask(v);
                int candIndex = CandidateIndex(cellIndex, v);
                live[candIndex >> 6] |= 1UL << (candIndex & 63);
            }
        }

        ulong[] matrix = new ulong[NUM_CANDIDATES * wlMatrixWords];
        for (int candIndex = 0; candIndex < NUM_CANDIDATES; candIndex++)
        {
            int rowBase = candIndex * wlMatrixWords;
            foreach (int target in weakLinks[candIndex])
            {
                matrix[rowBase + (target >> 6)] |= 1UL << (target & 63);
            }
            for (int w = 0; w < wlMatrixWords; w++)
            {
                matrix[rowBase + w] &= live[w];
            }
        }

        wlMatrix = matrix;
    }

    /// <summary>Whether the matrix is available for a caller to use.</summary>
    internal bool HasWlMatrix => wlMatrix != null;

    /// <summary>
    /// Whether <paramml/>candIndex is weakly linked to any candidate in <paramref name="set"/>, as
    /// one AND per word instead of one binary search per member.
    /// </summary>
    internal bool IsWeakLinkToAny(int candIndex, ReadOnlySpan<ulong> set)
    {
        int words = wlMatrixWords;
        int rowBase = candIndex * words;
        ulong any = 0;
        for (int w = 0; w < words; w++)
        {
            any |= wlMatrix[rowBase + w] & set[w];
        }
        return any != 0;
    }

    /// <summary>Number of words in a bitmatrix row, for callers sizing their own bitsets.</summary>
    internal int WlMatrixWords => wlMatrixWords;

    /// <summary>
    /// Cell forcing by ANDing the rows of the cell's remaining candidates. Surviving bits are the
    /// eliminations; ascending bit order is ascending candidate index, which is the order the CSR
    /// scan produces, so the two paths apply the same clears in the same sequence.
    /// </summary>
    private LogicResult CellForcingForCellMatrix(int cellIndex, uint candMask)
    {
        int words = wlMatrixWords;
        Span<ulong> acc = stackalloc ulong[words];
        bool first = true;
        uint remaining = candMask;
        int candBase = cellIndex * MAX_VALUE - 1;

        while (remaining != 0)
        {
            int v = MinValue(remaining);
            remaining &= ~ValueMask(v);
            int rowBase = (candBase + v) * words;

            ulong any = 0;
            if (first)
            {
                for (int w = 0; w < words; w++)
                {
                    any |= acc[w] = wlMatrix[rowBase + w];
                }
                first = false;
            }
            else
            {
                for (int w = 0; w < words; w++)
                {
                    any |= acc[w] &= wlMatrix[rowBase + w];
                }
            }

            if (any == 0)
            {
                if (CellForcingStatsEnabled)
                {
                    CellForcingStats.PopsNothingFired++;
                }
                return LogicResult.None;
            }
        }

        // Apply, grouped by target cell exactly as the CSR path does.
        int pendingCell = -1;
        uint pendingMask = 0;
        bool changed = false;
        for (int w = 0; w < words; w++)
        {
            ulong word = acc[w];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                word &= word - 1;
                int target = (w << 6) + bit;
                int targetCell = target / MAX_VALUE;
                if (targetCell != pendingCell)
                {
                    if (pendingMask != 0 && (board[pendingCell] & pendingMask) != 0)
                    {
                        changed = true;
                        if (!ClearMaskFromCell(pendingCell, pendingMask))
                        {
                            if (CellForcingStatsEnabled)
                            {
                                CellForcingStats.PopsChanged++;
                            }
                            return LogicResult.Invalid;
                        }
                    }
                    pendingCell = targetCell;
                    pendingMask = 0;
                }
                pendingMask |= ValueMask(target - targetCell * MAX_VALUE + 1);
            }
        }

        if (pendingMask != 0 && (board[pendingCell] & pendingMask) != 0)
        {
            changed = true;
            if (!ClearMaskFromCell(pendingCell, pendingMask))
            {
                if (CellForcingStatsEnabled)
                {
                    CellForcingStats.PopsChanged++;
                }
                return LogicResult.Invalid;
            }
        }

        if (CellForcingStatsEnabled)
        {
            if (changed)
            {
                CellForcingStats.PopsChanged++;
            }
            else
            {
                CellForcingStats.PopsFiredNoChange++;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }
}
