#nullable enable

namespace SudokuSolver.Logical;

/// <summary>Describes a stable logical technique independently of presentation text.</summary>
/// <param name="Id">The stable technique identifier.</param>
/// <param name="NameKey">The localization key for the technique name.</param>
public sealed record TechniqueDescriptor(string Id, string NameKey);
