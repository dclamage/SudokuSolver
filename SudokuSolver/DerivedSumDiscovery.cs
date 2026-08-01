namespace SudokuSolver;

// Setup-time discovery of derived (combined) sum constraints. Runs once per brute-force solve on
// the root clone, after base propagation. See the design in the feature branch: candidate
// generation + static scoring + strict overhead budget, committed via CommitDerivedConstraints.
public partial class Solver
{
    // Skip discovery entirely on easy puzzles: below this residual entropy the search is cheap
    // enough that any preparation is net overhead.
    private const double DerivedSumMinResidualEntropy = 10.0;

    /// <summary>
    /// Discovers and commits high-value derived sum constraints for this (root brute-force) solver.
    /// Milestone 1: the plumbing only — candidate generation is not implemented yet, so this is a
    /// no-op in production and exists to prove the commit path is inert until a template turns on.
    /// </summary>
    internal void PrepareDerivedConstraints()
    {
        if (DerivedSumsDisabled())
        {
            return;
        }

        if (ResidualEntropy() <= DerivedSumMinResidualEntropy)
        {
            return;
        }

        List<Constraint> derived = GenerateDerivedConstraints();
        CommitDerivedConstraints(derived);
    }

    private static bool DerivedSumsDisabled()
    {
        string value = Environment.GetEnvironmentVariable("SUDOKU_DISABLE_DERIVED_SUMS");
        return value != null &&
            (value == "1" ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Residual search entropy: the sum over unset cells of log2(candidateCount). A cheap proxy
    /// for how much brute-force work remains, used to gate discovery cost.
    /// </summary>
    private double ResidualEntropy()
    {
        double entropy = 0.0;
        for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
        {
            uint mask = board[cellIndex];
            if (IsValueSet(mask))
            {
                continue;
            }

            int count = ValueCount(mask & ALL_VALUES_MASK);
            if (count > 1)
            {
                entropy += Math.Log2(count);
            }
        }
        return entropy;
    }

    // Milestone 1: no templates yet. Milestones 3+ populate this (generalized innie/outie,
    // parallel little-killer combinations, then arrow-pair equalities).
    private List<Constraint> GenerateDerivedConstraints() => [];
}
