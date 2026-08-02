namespace SudokuSolverWasm;

internal static class Program
{
    // Never invoked: the host loads this assembly as a library and drives it through the
    // [JSExport] surface in SolverInterop / BenchInterop. Running Main would shut the runtime
    // down as soon as it returned. An entry point exists only because the WASM SDK wants an Exe.
    private static void Main()
    {
    }
}
