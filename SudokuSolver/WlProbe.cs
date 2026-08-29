namespace SudokuSolver;

/// THROWAWAY PROBE -- delete. Counts IsWeakLink traffic under SUDOKU_WL_PROBE=1.
internal static class WlProbe
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("SUDOKU_WL_PROBE") == "1";
    public static long Calls;
    public static long Elements;
    public static long Steps;
    public static long Bilocal;
    public static long Pairs;
    public static long Triples;
    public static long LogicTriples;
    public static long Skyscraper;
    public static long Thermo;
    private static bool hooked;

    public static bool Bump(ref long counter)
    {
        counter++;
        return true;
    }

    public static void Hook()
    {
        if (!Enabled || hooked)
        {
            return;
        }
        hooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("=== IsWeakLink probe ===");
            Console.Error.WriteLine($"calls        {Calls,18:N0}");
            Console.Error.WriteLine($"list elems   {Elements,18:N0}   mean list {(Calls == 0 ? 0 : (double)Elements / Calls),8:F1}");
            Console.Error.WriteLine($"search steps {Steps,18:N0}   mean steps {(Calls == 0 ? 0 : (double)Steps / Calls),8:F1}");
            Console.Error.WriteLine($"  from FindBestBilocal {Bilocal,14:N0}");
            Console.Error.WriteLine($"  from FastFindPairs   {Pairs,14:N0}");
            Console.Error.WriteLine($"  FastFindTriples entries {Triples,11:N0}");
            Console.Error.WriteLine($"  SolverLogic triples     {LogicTriples,11:N0}");
            Console.Error.WriteLine($"  Skyscraper support      {Skyscraper,11:N0}");
            Console.Error.WriteLine($"  SlowThermometer         {Thermo,11:N0}");
            Console.Error.Flush();
        };
    }
}
