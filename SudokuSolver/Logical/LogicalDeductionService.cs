#nullable enable

namespace SudokuSolver.Logical;

/// <summary>Discovers and applies validated structured logical deductions.</summary>
public static class LogicalDeductionService
{
    /// <summary>Finds all deductions supported by the structured logical API.</summary>
    /// <param name="position">The position to inspect.</param>
    /// <returns>Available deductions in deterministic technique and board order.</returns>
    public static IReadOnlyList<LogicalDeduction> FindAvailable(LogicalPosition position)
        => NakedSingleFinder.FindAvailable(position);

    /// <summary>Validates and atomically applies one still-available deduction.</summary>
    /// <param name="position">The owned position to mutate.</param>
    /// <param name="deduction">The exact previously discovered deduction.</param>
    /// <returns>The resulting stable position hash and board.</returns>
    /// <exception cref="InvalidOperationException">The deduction is stale, forged, or no longer available.</exception>
    public static LogicalApplyResult Apply(LogicalPosition position, LogicalDeduction deduction)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(deduction);
        string currentHash = position.PositionHash;
        if (!string.Equals(currentHash, deduction.PreconditionHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The logical deduction position is stale.");
        }

        LogicalDeduction? available = FindAvailable(position).SingleOrDefault(candidate =>
            string.Equals(candidate.Id, deduction.Id, StringComparison.Ordinal));
        if (available is null || !HasSameContent(available, deduction))
        {
            throw new InvalidOperationException("The logical deduction is forged or no longer available.");
        }

        foreach (DeductionPlacement placement in available.Delta.Placements)
        {
            if (!position.Solver.SetValue(
                position.GetCellIndex(placement.CellId),
                position.GetSolverValue(placement.ValueId)))
            {
                throw new InvalidOperationException("The logical deduction could not be applied.");
            }
        }

        return new LogicalApplyResult
        {
            PositionHash = position.PositionHash,
            Cells = position.GetCells(),
        };
    }

    internal static string ComputeDeductionId(
        string techniqueId,
        string preconditionHash,
        DeductionDelta delta)
        => LogicalPosition.ComputeStableHash(writer =>
        {
            writer.Write("logical-deduction-v1");
            writer.Write(techniqueId);
            writer.Write(preconditionHash);
            DeductionPlacement[] placements = delta.Placements
                .OrderBy(placement => placement.CellId, StringComparer.Ordinal)
                .ThenBy(placement => placement.ValueId, StringComparer.Ordinal)
                .ToArray();
            writer.Write(placements.Length);
            foreach (DeductionPlacement placement in placements)
            {
                writer.Write(placement.CellId);
                writer.Write(placement.ValueId);
            }
            DeductionElimination[] eliminations = delta.Eliminations
                .OrderBy(elimination => elimination.CellId, StringComparer.Ordinal)
                .ThenBy(elimination => elimination.ValueId, StringComparer.Ordinal)
                .ToArray();
            writer.Write(eliminations.Length);
            foreach (DeductionElimination elimination in eliminations)
            {
                writer.Write(elimination.CellId);
                writer.Write(elimination.ValueId);
            }
        });

    private static bool HasSameContent(LogicalDeduction left, LogicalDeduction right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.TechniqueId, right.TechniqueId, StringComparison.Ordinal)
            && string.Equals(left.OwningConstraintId, right.OwningConstraintId, StringComparison.Ordinal)
            && string.Equals(left.PreconditionHash, right.PreconditionHash, StringComparison.Ordinal)
            && left.Premises.SequenceEqual(right.Premises)
            && left.Delta.Placements.SequenceEqual(right.Delta.Placements)
            && left.Delta.Eliminations.SequenceEqual(right.Delta.Eliminations)
            && FramesEqual(left.Frames, right.Frames);

    private static bool FramesEqual(
        IReadOnlyList<WalkthroughFrame> left,
        IReadOnlyList<WalkthroughFrame> right)
        => left.Count == right.Count
            && left.Zip(right).All(pair =>
                pair.First.Focus.SequenceEqual(pair.Second.Focus)
                && pair.First.Dim.SequenceEqual(pair.Second.Dim)
                && pair.First.Highlight.SequenceEqual(pair.Second.Highlight)
                && string.Equals(
                    pair.First.Explanation.Key,
                    pair.Second.Explanation.Key,
                    StringComparison.Ordinal)
                && pair.First.Explanation.Arguments.SequenceEqual(pair.Second.Explanation.Arguments));
}
