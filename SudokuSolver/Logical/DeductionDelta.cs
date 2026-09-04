#nullable enable

namespace SudokuSolver.Logical;

/// <summary>Places one stable value in one stable cell.</summary>
/// <param name="CellId">The stable cell identifier.</param>
/// <param name="ValueId">The stable value identifier.</param>
public sealed record DeductionPlacement(string CellId, string ValueId);

/// <summary>Eliminates one stable value from one stable cell.</summary>
/// <param name="CellId">The stable cell identifier.</param>
/// <param name="ValueId">The stable value identifier.</param>
public sealed record DeductionElimination(string CellId, string ValueId);

/// <summary>Contains the complete semantic mutation represented by a deduction.</summary>
public sealed record DeductionDelta
{
    /// <summary>Gets values placed by the deduction.</summary>
    public required IReadOnlyList<DeductionPlacement> Placements { get; init; }

    /// <summary>Gets candidates eliminated by the deduction.</summary>
    public required IReadOnlyList<DeductionElimination> Eliminations { get; init; }
}

/// <summary>Describes the stable value and candidates currently visible in one cell.</summary>
public sealed record LogicalCellState
{
    /// <summary>Gets the stable cell identifier.</summary>
    public required string CellId { get; init; }

    /// <summary>Gets the placed stable value identifier, or <see langword="null"/> when unset.</summary>
    public string? ValueId { get; init; }

    /// <summary>Gets candidate stable value identifiers when the cell is unset.</summary>
    public required IReadOnlyList<string> CandidateValueIds { get; init; }
}

/// <summary>Contains the position produced by a validated logical apply.</summary>
public sealed record LogicalApplyResult
{
    /// <summary>Gets the deterministic hash of the resulting position.</summary>
    public required string PositionHash { get; init; }

    /// <summary>Gets the resulting board in stable projected cell order.</summary>
    public required IReadOnlyList<LogicalCellState> Cells { get; init; }
}
