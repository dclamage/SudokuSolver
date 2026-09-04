#nullable enable

namespace SudokuSolver.Logical;

/// <summary>Discovers structured naked-single deductions without mutating the position.</summary>
public static class NakedSingleFinder
{
    /// <summary>Finds every naked single in stable flat-board order.</summary>
    /// <param name="position">The logical position to observe.</param>
    /// <returns>All currently available naked-single deductions.</returns>
    public static IReadOnlyList<LogicalDeduction> FindAvailable(LogicalPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        List<LogicalDeduction> deductions = [];
        string preconditionHash = position.PositionHash;
        IReadOnlyList<uint> board = position.Solver.FlatBoard;
        for (int cellIndex = 0; cellIndex < board.Count; cellIndex++)
        {
            uint mask = board[cellIndex];
            if (SolverUtility.IsValueSet(mask) || SolverUtility.ValueCount(mask) != 1)
            {
                continue;
            }

            string cellId = position.GetCellId(cellIndex);
            string valueId = position.GetValueId(SolverUtility.GetValue(mask));
            DeductionDelta delta = new()
            {
                Placements = [new DeductionPlacement(cellId, valueId)],
                Eliminations = [],
            };
            deductions.Add(new LogicalDeduction
            {
                Id = LogicalDeductionService.ComputeDeductionId(
                    "basic.naked-single",
                    preconditionHash,
                    delta),
                TechniqueId = "basic.naked-single",
                OwningConstraintId = "builtin:latin-square",
                PreconditionHash = preconditionHash,
                Premises = [],
                Delta = delta,
                Frames =
                [
                    new WalkthroughFrame
                    {
                        Focus = [new LogicalEntityReference("cell", cellId)],
                        Dim = [],
                        Highlight =
                        [
                            new LogicalEntityReference("cell", cellId),
                            new LogicalEntityReference("value", valueId),
                        ],
                        Explanation = new LogicalExplanation
                        {
                            Key = "logical.nakedSingle.place",
                            Arguments =
                            [
                                new LogicalExplanationArgument("cell", cellId),
                                new LogicalExplanationArgument("value", valueId),
                            ],
                        },
                    },
                ],
            });
        }
        return deductions;
    }
}
