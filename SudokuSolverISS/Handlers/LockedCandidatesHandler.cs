namespace SudokuSolverISS.Handlers;

/// <summary>
/// Port of ISS SameValuesIgnoreCount (added by _addGridHouseIntersections).
/// For a row and a box that share K cells, the 6 row cells outside the box
/// and the 6 box cells outside the row must carry the same set of candidate
/// values (locked-candidates / pointing-pairs technique).
/// Enforces: union(grid[cells0]) == union(grid[cells1]).
/// Any value present in only one part is removed from that part's cells
/// because it must be placed in the shared intersection cells.
/// Mirrors ISS SameValues.enforceConsistency (intersectionSize check + cell narrowing).
/// </summary>
sealed class LockedCandidatesHandler(int[] cells0, int[] cells1) : IHandler
{
    public bool EnforceConsistency(uint[] grid, HandlerAccumulator acc)
    {
        uint values0 = 0, values1 = 0;
        foreach (int c in cells0) values0 |= grid[c];
        foreach (int c in cells1) values1 |= grid[c];

        uint intersection = values0 & values1;

        if (G.Count(intersection) < cells0.Length) return false;

        if (values0 != intersection)
        {
            foreach (int c in cells0)
            {
                uint masked = grid[c] & intersection;
                if (masked == grid[c]) continue;
                if (masked == 0) return false;
                grid[c] = masked;
                acc.AddForCell(c);
            }
        }

        if (values1 != intersection)
        {
            foreach (int c in cells1)
            {
                uint masked = grid[c] & intersection;
                if (masked == grid[c]) continue;
                if (masked == 0) return false;
                grid[c] = masked;
                acc.AddForCell(c);
            }
        }

        return true;
    }
}
