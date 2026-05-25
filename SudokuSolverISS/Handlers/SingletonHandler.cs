namespace SudokuSolverISS.Handlers;

/// <summary>
/// Fired when a cell becomes fixed.  Removes its value from all peers.
/// Mirrors ISS's per-cell singleton handler (pushed to front of queue).
/// </summary>
sealed class SingletonHandler(int cell, int[] peers) : IHandler
{
    public bool EnforceConsistency(uint[] grid, HandlerAccumulator acc)
    {
        uint v = grid[cell];
        if (!G.IsSingleton(v)) return true;

        foreach (int peer in peers)
        {
            uint pv = grid[peer];
            if ((pv & v) == 0) continue;   // peer doesn't have this value
            uint after = pv & ~v;
            if (after == 0) return false;  // contradiction
            if (after != pv)
            {
                grid[peer] = after;
                acc.AddForCell(peer);
            }
        }
        return true;
    }
}
