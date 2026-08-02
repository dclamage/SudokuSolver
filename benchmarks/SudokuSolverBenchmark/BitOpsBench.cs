#nullable enable
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using SudokuSolver;

namespace SudokuSolverBenchmark;

/// <summary>
/// Answers one question: does Mono's WASM AOT lower <see cref="BitOperations"/> to the native
/// WebAssembly bit instructions (<c>i32.popcnt</c>, <c>i32.clz</c>, <c>i32.ctz</c>), or fall back
/// to software?
///
/// The test is comparative rather than absolute. Each intrinsic is timed against a hand-written
/// software equivalent, in the same host. If the intrinsic is real, it should clearly beat the
/// software version — as it does natively. If the two run at about the same speed, the "intrinsic"
/// is software too, and the solver's hottest primitives are paying for it on every call.
///
/// Shared by the native harness and the WASM build so both hosts execute identical code.
/// </summary>
internal static class BitOpsBench
{
    /// <summary>Result for one operation: nanoseconds per call for each implementation.</summary>
    internal sealed class BitOpsResult
    {
        public string Name { get; set; } = "";
        public double IntrinsicNs { get; set; }
        public double SoftwareNs { get; set; }
        /// <summary>Software / intrinsic. Well above 1 means the intrinsic is doing real work.</summary>
        public double Speedup => IntrinsicNs > 0 ? SoftwareNs / IntrinsicNs : double.NaN;
        /// <summary>Kept only to stop the optimizer discarding the measured loops.</summary>
        public long Checksum { get; set; }
    }

    private const int MaskCount = 1 << 16;
    private const int Repeats = 256;

    private static readonly int[] DeBruijnPositions =
    [
        0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
        31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
    ];

    private static uint[] BuildMasks()
    {
        // Values shaped like real candidate masks: 9 significant bits, never zero, plus the
        // occasional wider value. A fixed seed keeps runs comparable across hosts.
        var random = new Random(12345);
        var masks = new uint[MaskCount];
        for (int i = 0; i < masks.Length; i++)
        {
            uint mask = (uint)random.Next(1, 1 << 9);
            if ((i & 15) == 0)
            {
                mask |= (uint)random.Next(1, 1 << 16) << 9;
            }
            masks[i] = mask;
        }
        return masks;
    }

    private static uint SoftwarePopCount(uint x)
    {
        x -= (x >> 1) & 0x55555555u;
        x = (x & 0x33333333u) + ((x >> 2) & 0x33333333u);
        x = (x + (x >> 4)) & 0x0F0F0F0Fu;
        return (x * 0x01010101u) >> 24;
    }

    private static int SoftwareTrailingZeroCount(uint x)
        => x == 0 ? 32 : DeBruijnPositions[((x & (uint)-(int)x) * 0x077CB531u) >> 27];

    private static int SoftwareLog2(uint x)
    {
        int result = 0;
        while ((x >>= 1) != 0)
        {
            result++;
        }
        return result;
    }

    private static int SoftwareLeadingZeroCount(uint x)
    {
        if (x == 0)
        {
            return 32;
        }
        int count = 0;
        while ((x & 0x80000000u) == 0)
        {
            x <<= 1;
            count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int NoInlineTrailingZeroCount(uint x) => BitOperations.TrailingZeroCount(x);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int NoInlineLeadingZeroCount(uint x) => BitOperations.LeadingZeroCount(x);

    private static double TimeNs(Func<uint[], long> body, uint[] masks, out long checksum)
    {
        body(masks); // warm up

        var stopwatch = Stopwatch.StartNew();
        checksum = body(masks);
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / ((double)MaskCount * Repeats);
    }

    public static List<BitOpsResult> Run()
    {
        uint[] masks = BuildMasks();
        var results = new List<BitOpsResult>();

        void Measure(string name, Func<uint[], long> intrinsic, Func<uint[], long> software)
        {
            double intrinsicNs = TimeNs(intrinsic, masks, out long checksumA);
            double softwareNs = TimeNs(software, masks, out long checksumB);
            results.Add(new BitOpsResult
            {
                Name = name,
                IntrinsicNs = intrinsicNs,
                SoftwareNs = softwareNs,
                Checksum = checksumA ^ checksumB,
            });
        }

        Measure("PopCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += BitOperations.PopCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwarePopCount(m[i]); return s; });

        Measure("TrailingZeroCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += BitOperations.TrailingZeroCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwareTrailingZeroCount(m[i]); return s; });

        Measure("Log2",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += BitOperations.Log2(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwareLog2(m[i]); return s; });

        Measure("LeadingZeroCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += BitOperations.LeadingZeroCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwareLeadingZeroCount(m[i]); return s; });

        Measure("NoInline.TrailingZeroCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += NoInlineTrailingZeroCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwareTrailingZeroCount(m[i]); return s; });

        Measure("NoInline.LeadingZeroCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += NoInlineLeadingZeroCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwareLeadingZeroCount(m[i]); return s; });

        // The solver's own wrappers, to confirm the helpers inherit whatever the primitives get.
        Measure("SolverUtility.ValueCount",
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SolverUtility.ValueCount(m[i]); return s; },
            m => { long s = 0; for (int r = 0; r < Repeats; r++) for (int i = 0; i < m.Length; i++) s += SoftwarePopCount(m[i] & ~SolverUtility.valueSetMask); return s; });

        return results;
    }

    public static string Format(List<BitOpsResult> results)
    {
        var lines = new List<string>
        {
            $"{"operation",-26}{"intrinsic ns",14}{"software ns",14}{"software/intrinsic",20}",
            new string('-', 74),
        };
        foreach (BitOpsResult r in results)
        {
            lines.Add($"{r.Name,-26}{r.IntrinsicNs,14:0.000}{r.SoftwareNs,14:0.000}{r.Speedup,19:0.00}x");
        }
        return string.Join('\n', lines);
    }
}
