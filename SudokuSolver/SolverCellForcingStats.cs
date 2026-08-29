namespace SudokuSolver;

/// <summary>
/// Instrumentation for in-search cell forcing, enabled by <c>SUDOKU_CF_STATS=1</c>: a census of pops
/// by outcome, and per-row scan/fire/elimination counts folded into a histogram over the two static
/// properties a build-time filter could actually use.
/// </summary>
/// <remarks>
/// The question this exists to answer is not "how many pops do nothing" — <c>docs/cell-forcing-worklist.md</c>
/// § "Where the waste is" answered that, and building against it produced a filter worth -1.3%
/// because it caught the cheap no-ops. The question is <em>where the scan cost goes</em>, so the unit
/// here is the row scan, and every count is reported against it.
///
/// The two static properties:
/// <list type="bullet">
///   <item><b>size</b> = <c>popcount(S)</c>. A row can only fire for a candidate set no larger than
///   its mask, so size bounds how early in a cell's life it can ever contribute.</item>
///   <item><b>distance</b> = <c>popcount(cand(A) &amp; ~S)</c> on the board the table was compiled
///   on: how many candidates the source cell must still lose before the row can fire at all.
///   Distance 0 means it fires immediately, so root cell forcing has already applied it and every
///   later scan of it is pure cost.</item>
/// </list>
///
/// Counters are plain (non-interlocked) adds on the hot path, so a stats run must be
/// single-threaded to be exact. They are gated on a <c>static readonly bool</c>, which the JIT folds
/// away in a normal build.
/// </remarks>
internal static class CellForcingStats
{
    /// <summary>Per-table row counters, kept alive for the process so they can be folded at exit.</summary>
    internal sealed class Block
    {
        public required uint[] Masks;
        public required int[] Distance;
        public required int MaxValue;
        public required long[] Scans;
        public required long[] Fires;
        public required long[] Elims;
    }

    // Pop census. Every pop lands in exactly one of these.
    public static long PopsValueSet;
    public static long PopsTooFewCandidates;
    public static long PopsOverCap;
    public static long PopsNothingFired;
    public static long PopsFiredNoChange;
    public static long PopsChanged;

    // Enqueue census, for the write-path filter.
    public static long EnqueueOffered;
    public static long EnqueueValueSet;
    public static long EnqueueFiltered;
    public static long EnqueuePushed;

    private static readonly List<Block> blocks = [];
    private static bool exitHooked;

    /// <summary>Registers a freshly compiled table's counters. Called once per table build.</summary>
    public static Block Register(uint[] masks, int[] distance, int maxValue)
    {
        Block block = new()
        {
            Masks = masks,
            Distance = distance,
            MaxValue = maxValue,
            Scans = new long[masks.Length],
            Fires = new long[masks.Length],
            Elims = new long[masks.Length],
        };

        lock (blocks)
        {
            blocks.Add(block);
            if (!exitHooked)
            {
                exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Report();
            }
        }

        return block;
    }

    private static void Report()
    {
        const int DIM = 33;
        long[,] rowsAt = new long[DIM, DIM];
        long[,] scansAt = new long[DIM, DIM];
        long[,] firesAt = new long[DIM, DIM];
        long[,] elimsAt = new long[DIM, DIM];

        long rows = 0, scans = 0, fires = 0, elims = 0;
        long deadRows = 0, deadScans = 0;      // rows that never newly eliminated
        long muteRows = 0, muteScans = 0;      // rows that never even fired
        long coldRows = 0;                     // rows never scanned at all
        int maxValue = 0;

        lock (blocks)
        {
            foreach (Block block in blocks)
            {
                maxValue = Math.Max(maxValue, block.MaxValue);
                for (int row = 0; row < block.Masks.Length; row++)
                {
                    int size = Math.Min(DIM - 1, BitOperations.PopCount(block.Masks[row] & ~SolverUtility.valueSetMask));
                    int distance = Math.Min(DIM - 1, block.Distance[row]);
                    long rowScans = block.Scans[row];
                    long rowFires = block.Fires[row];
                    long rowElims = block.Elims[row];

                    rowsAt[size, distance]++;
                    scansAt[size, distance] += rowScans;
                    firesAt[size, distance] += rowFires;
                    elimsAt[size, distance] += rowElims;

                    rows++;
                    scans += rowScans;
                    fires += rowFires;
                    elims += rowElims;

                    if (rowScans == 0)
                    {
                        coldRows++;
                    }
                    if (rowElims == 0)
                    {
                        deadRows++;
                        deadScans += rowScans;
                    }
                    if (rowFires == 0)
                    {
                        muteRows++;
                        muteScans += rowScans;
                    }
                }
            }
        }

        if (rows == 0)
        {
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine("=== cell-forcing stats ===");

        long pops = PopsValueSet + PopsTooFewCandidates + PopsOverCap + PopsNothingFired + PopsFiredNoChange + PopsChanged;
        sb.AppendLine($"pops {pops:N0}");
        AppendShare(sb, "  value already set", PopsValueSet, pops);
        AppendShare(sb, "  under two candidates", PopsTooFewCandidates, pops);
        AppendShare(sb, "  over the candidate cap", PopsOverCap, pops);
        AppendShare(sb, "  scanned, nothing fired", PopsNothingFired, pops);
        AppendShare(sb, "  fired, target already gone", PopsFiredNoChange, pops);
        AppendShare(sb, "  ELIMINATED SOMETHING", PopsChanged, pops);

        sb.AppendLine($"enqueues offered {EnqueueOffered:N0}");
        AppendShare(sb, "  value now set", EnqueueValueSet, EnqueueOffered);
        AppendShare(sb, "  rejected by filter", EnqueueFiltered, EnqueueOffered);
        AppendShare(sb, "  pushed", EnqueuePushed, EnqueueOffered);

        sb.AppendLine($"rows {rows:N0} over {blocks.Count:N0} table(s)   row scans {scans:N0}   fires {fires:N0}   novel elims {elims:N0}");
        if (scans > 0)
        {
            sb.AppendLine($"  scans per novel elim {(elims == 0 ? double.PositiveInfinity : (double)scans / elims):N1}"
                + $"   fires per novel elim {(elims == 0 ? double.PositiveInfinity : (double)fires / elims):N1}");
        }

        // The filter's ceiling: what fraction of the scan cost sits in rows that never paid off.
        sb.AppendLine("cost concentration (the ceiling for any per-row filter):");
        AppendPair(sb, "  rows that never newly eliminated", deadRows, rows, deadScans, scans);
        AppendPair(sb, "  rows that never even fired", muteRows, rows, muteScans, scans);
        AppendShare(sb, "  rows never scanned at all", coldRows, rows);

        // Marginals first: they are what a filter would threshold on.
        sb.AppendLine("by size = popcount(S):");
        AppendMarginal(sb, maxValue, rowsAt, scansAt, firesAt, elimsAt, bySize: true);
        sb.AppendLine("by distance = candidates the source cell must still lose:");
        AppendMarginal(sb, maxValue, rowsAt, scansAt, firesAt, elimsAt, bySize: false);

        sb.AppendLine("scan share by (size, distance), % of all row scans; '.' is zero:");
        sb.Append("  size\\dist ");
        for (int d = 0; d <= maxValue; d++)
        {
            sb.Append($"{d,7}");
        }
        sb.AppendLine();
        for (int size = 0; size <= maxValue; size++)
        {
            bool any = false;
            for (int d = 0; d <= maxValue; d++)
            {
                any |= scansAt[size, d] != 0 || rowsAt[size, d] != 0;
            }
            if (!any)
            {
                continue;
            }
            sb.Append($"  {size,9} ");
            for (int d = 0; d <= maxValue; d++)
            {
                long cell = scansAt[size, d];
                sb.Append(cell == 0 ? $"{".",7}" : $"{100.0 * cell / scans,7:F2}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("novel elims by (size, distance), % of all novel elims:");
        for (int size = 0; size <= maxValue; size++)
        {
            bool any = false;
            for (int d = 0; d <= maxValue; d++)
            {
                any |= elimsAt[size, d] != 0;
            }
            if (!any)
            {
                continue;
            }
            sb.Append($"  {size,9} ");
            for (int d = 0; d <= maxValue; d++)
            {
                long cell = elimsAt[size, d];
                sb.Append(cell == 0 ? $"{".",7}" : $"{100.0 * cell / Math.Max(1, elims),7:F2}");
            }
            sb.AppendLine();
        }

        Console.Error.Write(sb.ToString());
        Console.Error.Flush();
    }

    private static void AppendShare(StringBuilder sb, string label, long value, long total)
    {
        sb.AppendLine($"{label,-32} {value,16:N0}   {(total == 0 ? 0 : 100.0 * value / total),6:F1}%");
    }

    private static void AppendPair(StringBuilder sb, string label, long count, long countTotal, long cost, long costTotal)
    {
        sb.AppendLine($"{label,-36} {count,12:N0} rows ({(countTotal == 0 ? 0 : 100.0 * count / countTotal),5:F1}% of rows)"
            + $"   {cost,16:N0} scans ({(costTotal == 0 ? 0 : 100.0 * cost / costTotal),5:F1}% of scan cost)");
    }

    private static void AppendMarginal(StringBuilder sb, int maxValue, long[,] rowsAt, long[,] scansAt, long[,] firesAt, long[,] elimsAt, bool bySize)
    {
        long scanTotal = 0, elimTotal = 0;
        for (int i = 0; i <= maxValue; i++)
        {
            for (int j = 0; j <= maxValue; j++)
            {
                scanTotal += scansAt[i, j];
                elimTotal += elimsAt[i, j];
            }
        }

        sb.AppendLine("      bucket        rows       row scans    % scans        fires    novel elims   % elims   scans/elim");
        for (int b = 0; b <= maxValue; b++)
        {
            long r = 0, s = 0, f = 0, e = 0;
            for (int other = 0; other <= maxValue; other++)
            {
                int i = bySize ? b : other;
                int j = bySize ? other : b;
                r += rowsAt[i, j];
                s += scansAt[i, j];
                f += firesAt[i, j];
                e += elimsAt[i, j];
            }
            if (r == 0 && s == 0)
            {
                continue;
            }
            string perElim = e == 0 ? "-" : $"{(double)s / e:N1}";
            sb.AppendLine($"      {b,6} {r,11:N0} {s,15:N0} {(scanTotal == 0 ? 0 : 100.0 * s / scanTotal),9:F2} {f,13:N0} {e,14:N0} {(elimTotal == 0 ? 0 : 100.0 * e / elimTotal),9:F2} {perElim,12}");
        }
    }
}
