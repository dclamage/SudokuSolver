namespace SudokuSolverService.Protocol;

#pragma warning disable IDE1006 // Legacy wire names are intentionally lower case.

/// <summary>Contains one legacy f-puzzles websocket request.</summary>
public sealed class Message
{
    /// <summary>Gets or sets the legacy request nonce.</summary>
    public int nonce { get; set; }
    /// <summary>Gets or sets the legacy command name.</summary>
    public string command { get; set; } = string.Empty;
    /// <summary>Gets or sets the legacy puzzle data type.</summary>
    public string dataType { get; set; } = string.Empty;
    /// <summary>Gets or sets the legacy puzzle payload.</summary>
    public string data { get; set; } = string.Empty;
}

/// <summary>Provides nonce and type fields common to legacy responses.</summary>
public class BaseResponse
{
    /// <summary>Initializes a legacy response.</summary>
    /// <param name="nonce">The request nonce.</param>
    /// <param name="type">The legacy response type.</param>
    public BaseResponse(int nonce, string type)
    {
        this.nonce = nonce;
        this.type = type;
    }

    /// <summary>Gets or sets the legacy request nonce.</summary>
    public int nonce { get; set; }
    /// <summary>Gets or sets the legacy response type.</summary>
    public string type { get; set; }
}

/// <summary>Represents legacy cancellation.</summary>
public sealed class CanceledResponse(int nonce) : BaseResponse(nonce, "canceled");

/// <summary>Represents a legacy invalid request or puzzle.</summary>
public sealed class InvalidResponse(int nonce) : BaseResponse(nonce, "invalid")
{
    /// <summary>Gets or sets the diagnostic message.</summary>
    public string message { get; set; } = string.Empty;
}

/// <summary>Contains legacy per-candidate solution counts.</summary>
public sealed class TrueCandidatesResponse(int nonce) : BaseResponse(nonce, "truecandidates")
{
    /// <summary>Gets or sets flattened candidate counts.</summary>
    public long[] solutionsPerCandidate { get; set; } = [];
}

/// <summary>Contains a legacy solved board.</summary>
public sealed class SolvedResponse(int nonce) : BaseResponse(nonce, "solved")
{
    /// <summary>Gets or sets solved values in row-major order.</summary>
    public int[] solution { get; set; } = [];
}

/// <summary>Contains legacy count progress or a final result.</summary>
public sealed class CountResponse(int nonce) : BaseResponse(nonce, "count")
{
    /// <summary>Gets or sets the current solution count.</summary>
    public long count { get; set; }
    /// <summary>Gets or sets whether this is a progress response.</summary>
    public bool inProgress { get; set; }
}

/// <summary>Contains one legacy estimation progress response.</summary>
public sealed class EstimateResponse(int nonce) : BaseResponse(nonce, "estimate")
{
    /// <summary>Gets or sets the estimated solution count.</summary>
    public double estimate { get; set; }
    /// <summary>Gets or sets the estimate standard error.</summary>
    public double stderr { get; set; }
    /// <summary>Gets or sets the completed iteration count.</summary>
    public long iterations { get; set; }
    /// <summary>Gets or sets the lower 95-percent confidence bound.</summary>
    public double ci95_lower { get; set; }
    /// <summary>Gets or sets the upper 95-percent confidence bound.</summary>
    public double ci95_upper { get; set; }
    /// <summary>Gets or sets the relative 95-percent error percentage.</summary>
    public double relErrPercent { get; set; }
}

/// <summary>Contains one legacy logical cell state.</summary>
public sealed class LogicalCell
{
    /// <summary>Gets or sets the placed value, or zero when unset.</summary>
    public int value { get; set; }
    /// <summary>Gets or sets candidates when the cell is unset.</summary>
    public int[]? candidates { get; set; }
}

/// <summary>Contains one legacy logical operation result.</summary>
public sealed class LogicalResponse(int nonce) : BaseResponse(nonce, "logical")
{
    /// <summary>Gets or sets row-major logical cell states.</summary>
    public LogicalCell[] cells { get; set; } = [];
    /// <summary>Gets or sets the human-readable logical explanation.</summary>
    public string message { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the logical position remains valid.</summary>
    public bool isValid { get; set; }
}

#pragma warning restore IDE1006
