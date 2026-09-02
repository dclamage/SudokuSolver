using System.Text.Json.Serialization;

namespace SudokuSolverWasm;

#pragma warning disable IDE1006 // The browser benchmark contract uses lower-camel wire names.

/// <summary>Contains runtime facts that make a browser benchmark self-describing.</summary>
internal sealed class RuntimeInfo
{
    /// <summary>Gets or sets whether the runtime was built with thread support.</summary>
    public bool threadsEnabled { get; set; }

    /// <summary>Gets or sets the runtime-visible processor count.</summary>
    public int processorCount { get; set; }

    /// <summary>Gets or sets the managed runtime version.</summary>
    public string runtimeVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the runtime operating-system description.</summary>
    public string osDescription { get; set; } = string.Empty;
}

#pragma warning restore IDE1006

/// <summary>Provides AOT-safe JSON metadata for WASM-host-only payloads.</summary>
[JsonSerializable(typeof(RuntimeInfo))]
internal partial class WasmJsonContext : JsonSerializerContext;
