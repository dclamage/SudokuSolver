#nullable enable

namespace SudokuSolver.Logical;

/// <summary>Describes one premise used to justify a deduction.</summary>
/// <param name="Kind">The stable premise kind.</param>
/// <param name="CellId">The optional stable cell identifier.</param>
/// <param name="ValueId">The optional stable value identifier.</param>
public sealed record LogicalPremise(string Kind, string? CellId, string? ValueId);

/// <summary>Contains a deterministic, structured logical deduction.</summary>
public sealed record LogicalDeduction
{
    /// <summary>Gets the deterministic content identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the stable logical technique identifier.</summary>
    public required string TechniqueId { get; init; }

    /// <summary>Gets the stable constraint or rule identifier that owns the deduction.</summary>
    public required string OwningConstraintId { get; init; }

    /// <summary>Gets the exact position hash on which the deduction was discovered.</summary>
    public required string PreconditionHash { get; init; }

    /// <summary>Gets typed premises used by the deduction.</summary>
    public required IReadOnlyList<LogicalPremise> Premises { get; init; }

    /// <summary>Gets the complete semantic board delta.</summary>
    public required DeductionDelta Delta { get; init; }

    /// <summary>Gets ordered semantic walkthrough frames.</summary>
    public required IReadOnlyList<WalkthroughFrame> Frames { get; init; }
}
