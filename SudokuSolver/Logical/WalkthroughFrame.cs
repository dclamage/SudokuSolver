#nullable enable

namespace SudokuSolver.Logical;

/// <summary>References one semantic entity for a future walkthrough renderer.</summary>
/// <param name="Kind">The stable entity kind.</param>
/// <param name="Id">The stable entity identifier.</param>
public sealed record LogicalEntityReference(string Kind, string Id);

/// <summary>Supplies one typed value to a localized explanation.</summary>
/// <param name="Kind">The argument's semantic kind.</param>
/// <param name="Value">The stable identifier or literal value.</param>
public sealed record LogicalExplanationArgument(string Kind, string Value);

/// <summary>Identifies localized explanation text and its typed arguments.</summary>
public sealed record LogicalExplanation
{
    /// <summary>Gets the stable localization key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets ordered typed explanation arguments.</summary>
    public required IReadOnlyList<LogicalExplanationArgument> Arguments { get; init; }
}

/// <summary>Describes one semantic walkthrough frame without presentation coordinates or colors.</summary>
public sealed record WalkthroughFrame
{
    /// <summary>Gets entities that should remain in normal focus.</summary>
    public required IReadOnlyList<LogicalEntityReference> Focus { get; init; }

    /// <summary>Gets entities that should be semantically de-emphasized.</summary>
    public required IReadOnlyList<LogicalEntityReference> Dim { get; init; }

    /// <summary>Gets entities emphasized by this frame.</summary>
    public required IReadOnlyList<LogicalEntityReference> Highlight { get; init; }

    /// <summary>Gets the typed explanation resolved later by the UI.</summary>
    public required LogicalExplanation Explanation { get; init; }
}
