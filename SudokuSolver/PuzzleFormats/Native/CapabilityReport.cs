namespace SudokuSolver.PuzzleFormats.Native;

#nullable enable

/// <summary>Describes how completely an entity is supported by the active solver projection.</summary>
public enum EntityCapability
{
    /// <summary>The projected solver enforces the entity's supported semantics.</summary>
    FullyVerified,

    /// <summary>The entity is preserved, but some or all of its semantics are not enforced.</summary>
    PartiallyVerified,

    /// <summary>The entity remains editable and renderable but does not participate in this projection.</summary>
    VisualOnly,
}

/// <summary>Reports one native entity's solver capability.</summary>
/// <param name="Status">The level of solver support.</param>
/// <param name="Reason">A precise explanation when support is not complete.</param>
public sealed record EntityCapabilityResult(EntityCapability Status, string? Reason = null);

/// <summary>Contains capability results keyed by stable native entity identifier.</summary>
public sealed class CapabilityReport
{
    /// <summary>Initializes a capability report.</summary>
    /// <param name="entities">Capability results keyed by stable native entity identifier.</param>
    public CapabilityReport(IReadOnlyDictionary<string, EntityCapabilityResult> entities)
    {
        Entities = entities;
    }

    /// <summary>Gets capability results keyed by stable native entity identifier.</summary>
    public IReadOnlyDictionary<string, EntityCapabilityResult> Entities { get; }
}