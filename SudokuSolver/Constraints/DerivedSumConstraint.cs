namespace SudokuSolver.Constraints;

/// <summary>
/// A synthetic, setup-time constraint that drives a single derived sum relation discovered by
/// <see cref="Solver.PrepareDerivedConstraints"/> (e.g. a combined killer/little-killer sum, or an
/// innie/outie difference). It has no user-facing parse form and no attribute, so it is never
/// created by name. It is a logical consequence of the base rules, so it needs no enforcement
/// pass (<see cref="NeedsEnforceConstraint"/> is false) and rides the brute-force propagation
/// queue instead. It exposes no logical-step descriptions in this first version.
/// </summary>
internal sealed class DerivedSumConstraint : Constraint
{
    private readonly int[] _watchedCells;
    private readonly string _specificName;

    // Exactly one of these drives propagation; set by the factory after construction so the
    // owned SumTerm(s) can reference this constraint as their source.
    // (A SumEqualityRelation path will be added with arrow-pair discovery.)
    private SumTerm _fixedTerm;
    private SumDifferenceRelation _difference;

    private DerivedSumConstraint(Solver solver, IReadOnlyList<int> watchedCells, string specificName)
        : base(solver, "")
    {
        _watchedCells = [.. watchedCells];
        _specificName = specificName;
    }

    /// <summary>
    /// Builds a derived constraint enforcing that a fixed set of cells sums to one of the given totals.
    /// Used for combined fixed sums (e.g. merged little killers).
    /// </summary>
    internal static DerivedSumConstraint CreateFixedSum(Solver solver, IReadOnlyList<(int, int)> cells, IReadOnlyList<int> allowedSums)
    {
        var constraint = new DerivedSumConstraint(solver, ToCellIndices(solver, cells), $"Derived sum {string.Join("/", allowedSums)}");
        constraint._fixedTerm = new SumTerm(constraint, solver, cells, [.. allowedSums]);
        constraint._fixedTerm.RefreshHelper(solver);
        return constraint;
    }

    /// <summary>
    /// Builds a derived constraint enforcing sum(positiveCells) - sum(negativeCells) = targetDiff.
    /// Used for innie/outie style derivations.
    /// </summary>
    internal static DerivedSumConstraint CreateDifference(Solver solver, IReadOnlyList<(int, int)> positiveCells, IReadOnlyList<(int, int)> negativeCells, int targetDiff)
    {
        var watched = new List<int>(ToCellIndices(solver, positiveCells));
        watched.AddRange(ToCellIndices(solver, negativeCells));
        var constraint = new DerivedSumConstraint(solver, watched, $"Derived difference {targetDiff}");

        var positive = new SumTerm(constraint, solver, positiveCells, []);
        var negative = new SumTerm(constraint, solver, negativeCells, []);
        positive.RefreshHelper(solver);
        negative.RefreshHelper(solver);
        constraint._difference = new SumDifferenceRelation(constraint, positive, negative, targetDiff);
        return constraint;
    }

    public override string SpecificName => _specificName;

    public override bool NeedsEnforceConstraint => false;

    public override IReadOnlyList<int> CellIndicesForPropagationQueue => _watchedCells;

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        if (_fixedTerm != null)
        {
            return _fixedTerm.StepLogic(sudokuSolver, null, isBruteForcing);
        }
        return _difference.StepLogic(sudokuSolver, null, isBruteForcing);
    }

    private static int[] ToCellIndices(Solver solver, IReadOnlyList<(int, int)> cells)
    {
        int[] indices = new int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            indices[i] = solver.CellIndex(cells[i]);
        }
        return indices;
    }
}
