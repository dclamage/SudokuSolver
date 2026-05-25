// Port of handlers.js — only the handler classes needed for 9x9 sudoku + Arrow constraints.
// JS note: this.cells = new Uint8Array(cells || []) → int[] in C#.
// JS note: grid is int[] (ushort in JS Uint16Array, but int is cleaner in C#).
using System.Numerics;

namespace SudokuSolverISS2;

// ---- Base class (port of SudokuConstraintHandler) ----

abstract class Handler
{
    public int[]   cells;    // cells this handler fires on (ordinaryHandlerMap)
    public bool    essential = true;
    public string  idStr;

    private static int _defaultId = 0;

    protected Handler(int[]? cells = null)
    {
        this.cells = cells ?? [];
        this.idStr = GetType().Name + "-" + (_defaultId++).ToString();
    }

    // Return false if grid is invalid, true otherwise.
    public virtual bool EnforceConsistency(int[] grid, HandlerAccumulator acc) => true;

    // Cells that must be mutually exclusive (used to build CellExclusions).
    public virtual int[] ExclusionCells() => [];

    // Called once before solving, with the initial grid (all candidates = allValues).
    // Return false if the constraint is statically unsatisfiable.
    public virtual bool Initialize(int[] gridCells, CellExclusions cellExclusions) => true;

    // Called after all handlers have been initialized.
    public virtual void PostInitialize(int[] gridState) { }

    // Priority for initial cell selection seeding.
    public virtual int Priority() => cells.Length;

    // Candidate finders for house-bivalue search (used by CandidateSelector).
    public virtual CandidateFinder[] CandidateFinders(int[] grid) => [];
}

// ---- BoxInfo (port of BoxInfo) ----
// Purely passes box region data to the optimizer.

sealed class BoxInfo : Handler
{
    public readonly int[][] BoxRegions;

    public BoxInfo(int[][] boxRegions) : base()
    {
        BoxRegions = boxRegions;
    }
}

// ---- True (port of True) ----

sealed class TrueHandler : Handler
{
    public TrueHandler(int[]? cells = null) : base(cells) { }
}

// ---- False (port of False) ----

sealed class FalseHandler : Handler
{
    public FalseHandler(int[]? cells = null) : base(cells) { }
    public override bool Initialize(int[] gridCells, CellExclusions ce) => false;
    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc) => false;
}

// ---- AllDifferent (port of AllDifferent) ----
// In ISS, AllDifferent with PROPAGATE_WITH_EXCLUSION_CELLS has cells=[].
// Its exclusionCells() returns the actual house cells.
// Enforcement is done by UniqueValueExclusion (singleton handler) + PerfectAllDifferent.

sealed class AllDifferent : Handler
{
    private readonly int[] _exclusionCells;

    public AllDifferent(int[] exclusionCells) : base([])   // cells = [] in ISS
    {
        _exclusionCells = (int[])exclusionCells.Clone();
        Array.Sort(_exclusionCells);
        // idStr must be deterministic and unique per cell set.
        idStr = "AllDifferent|" + string.Join(",", _exclusionCells);
    }

    public override int[] ExclusionCells() => _exclusionCells;

    // AllDifferent with PROPAGATE_WITH_EXCLUSION_CELLS: enforcement is via
    // UniqueValueExclusion (singleton handler); this method is never called.
    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc) => true;
}

// ---- PerfectAllDifferent (port of PerfectAllDifferent) ----
// Added by optimizer via addNonEssential. Does hidden-single detection.
// cells = actual house cells (unlike AllDifferent which has cells=[]).

sealed class PerfectAllDifferent : Handler
{
    private int _valueMask;

    public PerfectAllDifferent(int[] cells, int valueMask = 0) : base((int[])cells.Clone())
    {
        _valueMask = valueMask;
        Array.Sort(this.cells);
        idStr = "PerfectAllDifferent|" + string.Join(",", this.cells);
    }

    public override int[] ExclusionCells() => cells;

    public override bool Initialize(int[] gridCells, CellExclusions ce)
    {
        // Compute allValues for these cells.
        int all = 0;
        foreach (int c in cells) all |= gridCells[c];
        _valueMask = all;
        return true;
    }

    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc)
    {
        int numCells = cells.Length;
        int allValues = 0, atLeastTwo = 0, fixedValues = 0;
        for (int i = 0; i < numCells; i++)
        {
            int v = grid[cells[i]];
            atLeastTwo |= allValues & v;
            allValues  |= v;
            // (!(v & (v-1))) * v  — JS trick for: if singleton, v; else 0
            if ((v & (v - 1)) == 0) fixedValues |= v;
        }

        if (allValues != _valueMask) return false;
        if (fixedValues == _valueMask) return true;

        int hiddenSingles = allValues & ~atLeastTwo & ~fixedValues;
        if (hiddenSingles != 0)
        {
            if (!HandlerUtil.ExposeHiddenSingles(grid, cells, hiddenSingles)) return false;
        }

        return true;
    }

    public override CandidateFinder[] CandidateFinders(int[] grid)
        => [new HouseCandidateFinder(cells)];
}

// ---- UniqueValueExclusion (port of UniqueValueExclusion) ----
// SINGLETON_HANDLER — fired only via addForFixedCell's singleton path.
// Removes the fixed cell's value from all cells in exclusionCells.

sealed class UniqueValueExclusion : Handler
{
    public static readonly bool IsSingletonHandler = true;

    private readonly int _cell;
    private int[]? _exclusionCells;  // set during Initialize

    public UniqueValueExclusion(int cell) : base([cell])
    {
        _cell = cell;
        idStr = "UniqueValueExclusion-" + cell;
    }

    public override bool Initialize(int[] gridCells, CellExclusions ce)
    {
        _exclusionCells = ce.GetArray(_cell);
        return true;
    }

    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc)
    {
        int[] exclusionCells = _exclusionCells!;
        int numExclusions = exclusionCells.Length;
        int value = grid[_cell];

        for (int i = 0; i < numExclusions; i++)
        {
            int ec = exclusionCells[i];
            if ((grid[ec] & value) != 0)
            {
                if ((grid[ec] ^= value) == 0) return false;
                acc.AddForCell(ec);
            }
        }
        return true;
    }

    public override int Priority() => 0;
}

// ---- SameValuesIgnoreCount (port of SameValuesIgnoreCount) ----
// Added as AUX by optimizer (_addGridHouseIntersections = locked candidates).
// Enforces: union(cells0) == union(cells1).

sealed class SameValuesIgnoreCount : Handler
{
    private readonly int[] _cells0;
    private readonly int[] _cells1;

    public SameValuesIgnoreCount(int[] cells0, int[] cells1)
        : base(cells0.Concat(cells1).Distinct().OrderBy(x => x).ToArray())
    {
        _cells0 = cells0;
        _cells1 = cells1;
        Array.Sort(_cells0);
        Array.Sort(_cells1);
        idStr = "SameValuesIgnoreCount|" + string.Join(",", _cells0) + "|" + string.Join(",", _cells1);
    }

    public override int Priority() => 0;

    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc)
    {
        int values0 = 0, values1 = 0;
        foreach (int c in _cells0) values0 |= grid[c];
        foreach (int c in _cells1) values1 |= grid[c];

        int intersection = values0 & values1;

        if (LookupTables.CountOnes(intersection) < _cells0.Length) return false;

        if (values0 != intersection)
        {
            foreach (int c in _cells0)
            {
                int masked = grid[c] & intersection;
                if (masked == grid[c]) continue;
                if (masked == 0) return false;
                grid[c] = masked;
                acc.AddForCell(c);
            }
        }

        if (values1 != intersection)
        {
            foreach (int c in _cells1)
            {
                int masked = grid[c] & intersection;
                if (masked == grid[c]) continue;
                if (masked == 0) return false;
                grid[c] = masked;
                acc.AddForCell(c);
            }
        }

        return true;
    }
}

// ---- GivenCandidates (port of GivenCandidates) ----
// Used by optimizer to fix a cell to a specific value.
// valueMap: cell → value (1-indexed digit).

sealed class GivenCandidates : Handler
{
    private readonly Dictionary<int, int> _valueMap;  // cell → digit value (1-indexed)

    public GivenCandidates(Dictionary<int, int> valueMap) : base()
    {
        _valueMap = valueMap;
    }

    public override bool Initialize(int[] gridCells, CellExclusions ce)
    {
        foreach (var (cell, digit) in _valueMap)
        {
            int bit = LookupTables.FromValue(digit);
            if ((gridCells[cell] & bit) == 0) return false;
            gridCells[cell] &= bit;
        }
        return true;
    }
}

// ---- BinaryConstraint (port of BinaryConstraint) ----
// Two-cell constraint using lookup tables.
// _tables[0][maskA] = allowed values for B given A has candidates maskA.
// _tables[1][maskB] = allowed values for A given B has candidates maskB.

sealed class BinaryConstraint : Handler
{
    private readonly int[] _tableAtoB;   // [512] given A mask → allowed B mask
    private readonly int[] _tableBtoA;   // [512] given B mask → allowed A mask

    // Build from a predicate: fn(a, b) = true if digit pair (a,b) is allowed (1-indexed).
    public BinaryConstraint(int cell0, int cell1, Func<int, int, bool> fn,
                            bool mutuallyExclusive = false)
        : base([cell0, cell1])
    {
        _tableAtoB = new int[LookupTables.COMBINATIONS];
        _tableBtoA = new int[LookupTables.COMBINATIONS];

        // Base cases: single-bit masks.
        for (int a = 1; a <= LookupTables.NUM_VALUES; a++)
        {
            for (int b = 1; b <= LookupTables.NUM_VALUES; b++)
            {
                bool allowed = fn(a, b) && (!mutuallyExclusive || a != b);
                if (allowed)
                {
                    _tableAtoB[1 << (a - 1)] |= 1 << (b - 1);
                    _tableBtoA[1 << (b - 1)] |= 1 << (a - 1);
                }
            }
        }

        // Fill multi-bit masks by ORing single-bit entries.
        for (int i = 1; i < LookupTables.COMBINATIONS; i++)
        {
            _tableAtoB[i] = _tableAtoB[i & (i - 1)] | _tableAtoB[i & -i];
            _tableBtoA[i] = _tableBtoA[i & (i - 1)] | _tableBtoA[i & -i];
        }

        idStr = "BinaryConstraint-" + cell0 + "-" + cell1 + "-"
                + string.Join("", Enumerable.Range(1, 9).SelectMany(a =>
                    Enumerable.Range(1, 9).Select(b => fn(a, b) ? "1" : "0")));
    }

    public override bool Initialize(int[] gridCells, CellExclusions ce)
        => _tableAtoB[LookupTables.ALL_VALUES] != 0;

    public override bool EnforceConsistency(int[] grid, HandlerAccumulator acc)
    {
        int v0 = grid[cells[0]];
        int v1 = grid[cells[1]];

        int v0New = v0 & _tableBtoA[v1];
        int v1New = v1 & _tableAtoB[v0];

        grid[cells[0]] = v0New;
        grid[cells[1]] = v1New;

        if (v0New == 0 || v1New == 0) return false;
        if (v0 != v0New) acc.AddForCell(cells[0]);
        if (v1 != v1New) acc.AddForCell(cells[1]);

        return true;
    }
}

// ---- HandlerUtil (port of HandlerUtil static methods we need) ----

static class HandlerUtil
{
    // Port of HandlerUtil.exposeHiddenSingles.
    // Sets each cell to its unique hidden-single value if it has one.
    // Returns false if any cell contains multiple hidden singles (contradiction).
    public static bool ExposeHiddenSingles(int[] grid, int[] cells, int hiddenSingles)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            int cell  = cells[i];
            int value = grid[cell] & hiddenSingles;
            if (value != 0)
            {
                if ((value & (value - 1)) != 0) return false;  // multiple hidden singles
                grid[cell] = value;
            }
        }
        return true;
    }

    // Port of HandlerUtil.cellsAllValues: OR of all candidate masks.
    public static int CellsAllValues(int[] grid, int[] cells)
    {
        int all = 0;
        foreach (int c in cells) all |= grid[c];
        return all;
    }
}
