namespace SudokuSolverISS.Handlers;

/// <summary>
/// Hidden-single detection for a house (row / column / box).
/// Exact port of ISS PerfectAllDifferent.enforceConsistency + exposeHiddenSingles.
/// </summary>
sealed class AllDifferentHandler(int[] cells) : IHandler
{
    public bool EnforceConsistency(uint[] grid, HandlerAccumulator acc)
    {
        uint allValues = 0, atLeastTwo = 0, fixedValues = 0;
        foreach (int c in cells)
        {
            uint v = grid[c];
            if (v == 0) return false;
            atLeastTwo  |= allValues & v;
            allValues   |= v;
            if (G.IsSingleton(v)) fixedValues |= v;
        }

        if (allValues != G.ALL_VALUES) return false;
        if (fixedValues == G.ALL_VALUES) return true;

        // Values that appear in exactly one non-fixed cell = hidden singles.
        uint hiddenSingles = allValues & ~atLeastTwo & ~fixedValues;
        if (hiddenSingles == 0) return true;

        // exposeHiddenSingles: fix each cell to its hidden-single value.
        // If a cell contains >1 hidden-single value, that's a contradiction.
        foreach (int c in cells)
        {
            uint h = grid[c] & hiddenSingles;
            if (h == 0) continue;
            if ((h & (h - 1)) != 0) return false;   // multiple hidden singles in same cell
            grid[c] = h;
        }
        return true;
    }
}
