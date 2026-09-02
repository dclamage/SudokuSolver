namespace SudokuSolverService;

/// <summary>Creates solver processors with the compatibility behavior required by each transport host.</summary>
public static class SolverCommandProcessorFactory
{
    /// <summary>Creates a console-host processor that ignores unsupported legacy requests.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="legacyAdditionalConstraints">Optional console-supplied legacy constraints.</param>
    /// <returns>A processor configured for the websocket console host.</returns>
    public static SolverCommandProcessor CreateConsole(
        bool singleThreaded,
        IEnumerable<string>? legacyAdditionalConstraints = null)
        => new(
            singleThreaded,
            legacyAdditionalConstraints,
            LegacyInvalidRequestBehavior.Ignore);

    /// <summary>Creates a WASM-host processor that reports unsupported legacy requests as invalid.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <returns>A processor configured for the WASM host.</returns>
    public static SolverCommandProcessor CreateWasm(bool singleThreaded)
        => new(singleThreaded);
}