namespace SudokuSolver.Constraints;

// A constraint that enforces an allowed-pairs relationship between two cells.
// The wire format (from f-puzzles JSON) is a base64url-encoded little-endian uint32 array
// where tableAB[v1-1] = bitmask of allowed values for cell1 when cell0 = v1.
[Constraint(DisplayName = "Binary Lookup Constraint", ConsoleName = null)]
public sealed class BinaryLookupConstraint : Constraint
{
    private readonly int cellIndex0;
    private readonly int cellIndex1;
    private readonly uint[] tableAB; // tableAB[v-1] = allowed values in cell1 when cell0 = v
    private readonly uint[] tableBA; // tableBA[v-1] = allowed values in cell0 when cell1 = v
    private readonly string hashKey;
    private readonly string constraintName;

    public BinaryLookupConstraint(Solver solver, int cellIndex0, int cellIndex1, uint[] tableAB, uint[] tableBA, string name = null)
        : base(solver, $"{cellIndex0},{cellIndex1}")
    {
        this.cellIndex0 = cellIndex0;
        this.cellIndex1 = cellIndex1;
        this.tableAB = tableAB;
        this.tableBA = tableBA;
        this.hashKey = $"{cellIndex0},{cellIndex1}:{string.Join(',', tableAB)}";
        this.constraintName = name ?? "Binary Lookup Constraint";
    }

    public override string SpecificName => constraintName;
    public override bool NeedsEnforceConstraint => true;

    public override string GetHash(Solver solver) => hashKey;

    public override LogicResult InitCandidates(Solver solver) => RunFilter(solver);

    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        int cellIndex = i * WIDTH + j;
        if (cellIndex != cellIndex0 && cellIndex != cellIndex1) return true;
        return RunFilter(solver) != LogicResult.Invalid;
    }

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return RunFilter(solver);
    }

    private LogicResult RunFilter(Solver solver)
    {
        IReadOnlyList<uint> board = solver.FlatBoard;
        uint mask0 = board[cellIndex0] & ~valueSetMask;
        uint mask1 = board[cellIndex1] & ~valueSetMask;

        // Compute which values of cell1 are reachable from any current candidate in cell0
        uint supported1 = 0;
        uint m = mask0;
        while (m != 0)
        {
            int v = MinValue(m);
            m &= ~ValueMask(v);
            if (v <= tableAB.Length)
                supported1 |= tableAB[v - 1];
        }
        supported1 &= mask1;

        // Compute which values of cell0 are reachable from any current candidate in cell1
        uint supported0 = 0;
        m = mask1;
        while (m != 0)
        {
            int v = MinValue(m);
            m &= ~ValueMask(v);
            if (v <= tableBA.Length)
                supported0 |= tableBA[v - 1];
        }
        supported0 &= mask0;

        if (supported0 == 0 || supported1 == 0)
            return LogicResult.Invalid;

        bool changed = false;

        if (supported0 != mask0)
        {
            uint toRemove = mask0 & ~supported0;
            while (toRemove != 0)
            {
                int val = MinValue(toRemove);
                toRemove &= ~ValueMask(val);
                if (!solver.ClearValue(cellIndex0, val))
                    return LogicResult.Invalid;
            }
            changed = true;
        }

        if (supported1 != mask1)
        {
            uint toRemove = mask1 & ~supported1;
            while (toRemove != 0)
            {
                int val = MinValue(toRemove);
                toRemove &= ~ValueMask(val);
                if (!solver.ClearValue(cellIndex1, val))
                    return LogicResult.Invalid;
            }
            changed = true;
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    // Decodes a base64url-encoded little-endian uint32 array.
    public static uint[] DecodeTable(string base64url)
    {
        string b64 = base64url.Replace('-', '+').Replace('_', '/');
        int pad = (4 - b64.Length % 4) % 4;
        if (pad > 0) b64 += new string('=', pad);
        byte[] bytes = Convert.FromBase64String(b64);
        int count = bytes.Length / 4;
        uint[] table = new uint[count];
        for (int i = 0; i < count; i++)
            table[i] = BitConverter.ToUInt32(bytes, i * 4);
        return table;
    }

    // Builds the reverse lookup: tableBA[v2-1] = bitmask of values v1 s.t. tableAB[v1-1] contains v2.
    public static uint[] BuildReverseTable(uint[] tableAB, int maxValue)
    {
        uint[] tableBA = new uint[maxValue];
        for (int v1 = 1; v1 <= Math.Min(tableAB.Length, maxValue); v1++)
        {
            uint allowed = tableAB[v1 - 1];
            for (int v2 = 1; v2 <= maxValue; v2++)
            {
                if ((allowed & ValueMask(v2)) != 0)
                    tableBA[v2 - 1] |= ValueMask(v1);
            }
        }
        return tableBA;
    }
}
