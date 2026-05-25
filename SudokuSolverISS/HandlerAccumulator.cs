namespace SudokuSolverISS;

/// <summary>
/// Port of ISS's HandlerAccumulator.
/// Intrusive linked list over handler indices:
///   _ll[i] == -2  → not in list
///   _ll[i] == -1  → tail
///   _ll[i] >= 0   → next index
/// Push-to-front for singleton (fixed-cell exclusion) handlers.
/// Enqueue-to-back for aux/ordinary handlers.
/// Aux handlers (e.g. LockedCandidates) mirror ISS: fired only from addForFixedCell,
/// not from addForCell. This matches ISS's HandlerAccumulator.addAux behavior.
/// </summary>
sealed class HandlerAccumulator
{
    private readonly IHandler[] _handlers;
    private readonly short[]    _ll;           // linked-list next pointers

    // Per-cell handler lists.
    private readonly int[][] _singleton;   // one handler per cell (fixed-value exclusion)
    private readonly int[][] _aux;         // aux handlers: fired only when cell becomes fixed
    private readonly int[][] _ordinary;    // constraint handlers watching each cell

    private int _head = -1;
    private int _tail = -1;
    private int _active = -1;   // index of handler currently running (don't re-enqueue)

    // When true, AddForCell/AddForFixedCell are no-ops.
    // Used during ISS _initRun phase: handlers run once but cannot schedule more work.
    public bool SuppressCascade;

    public HandlerAccumulator(
        IHandler[]   handlers,
        int[][]      singletonMap,   // singletonMap[cell] = {handlerIndex}
        int[][]      auxMap,         // auxMap[cell]       = {handlerIndex, ...}  (fixed-cell only)
        int[][]      ordinaryMap)    // ordinaryMap[cell]  = {handlerIndex, ...}
    {
        _handlers  = handlers;
        _ll        = new short[handlers.Length];
        _ll.AsSpan().Fill(-2);
        _singleton = singletonMap;
        _aux       = auxMap;
        _ordinary  = ordinaryMap;
    }

    public void Reset()
    {
        // Clear the linked list without scanning the whole array.
        int cur = _head;
        while (cur >= 0) { int nxt = _ll[cur]; _ll[cur] = -2; cur = nxt; }
        _head          = -1;
        _tail          = -1;
        _active        = -1;
        SuppressCascade = false;
    }

    // Called for each cell that just became fixed (i.e., singleton).
    // Mirrors ISS addForFixedCell: singleton pushed to front, aux then ordinary to back.
    public void AddForFixedCell(int cell)
    {
        if (SuppressCascade) return;
        foreach (int idx in _singleton[cell]) PushFront(idx);
        foreach (int idx in _aux[cell])       Enqueue(idx);
        foreach (int idx in _ordinary[cell])  Enqueue(idx);
    }

    // Called when a cell's candidates were narrowed (not necessarily fixed).
    // Mirrors ISS addForCell: ordinary handlers to back only, skip active.
    public void AddForCell(int cell)
    {
        if (SuppressCascade) return;
        foreach (int idx in _ordinary[cell])
            if (idx != _active) Enqueue(idx);
    }

    public bool IsEmpty() => _head < 0;

    public IHandler TakeNext()
    {
        int idx  = _head;
        _head    = _ll[idx];
        _ll[idx] = -2;
        _active  = idx;
        return _handlers[idx];
    }

    private void PushFront(int idx)
    {
        if (_ll[idx] != -2) return;          // already in list
        _ll[idx] = (short)_head;
        if (_head < 0) _tail = idx;
        _head = idx;
    }

    private void Enqueue(int idx)
    {
        if (_ll[idx] != -2) return;          // already in list
        _ll[idx] = -1;
        if (_head < 0) _head = idx;
        else           _ll[_tail] = (short)idx;
        _tail = idx;
    }
}
