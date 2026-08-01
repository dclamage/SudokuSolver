using System.Numerics;
using SudokuSolver.Constraints;

namespace SudokuSolver;

// Setup-time discovery of derived (combined) sum constraints. Runs once per brute-force solve on
// the root clone, after base propagation. See the design in the feature branch: candidate
// generation + static scoring + strict overhead budget, committed via CommitDerivedConstraints.
public partial class Solver
{
    // Skip discovery entirely on easy puzzles: below this residual entropy the search is cheap
    // enough that any preparation is net overhead. Settable so tests can force discovery on
    // small, exactly-countable puzzles.
    internal static double DerivedSumMinResidualEntropy = 10.0;

    /// <summary>
    /// Discovers and commits high-value derived sum constraints for this (root brute-force) solver.
    /// Milestone 1: the plumbing only — candidate generation is not implemented yet, so this is a
    /// no-op in production and exists to prove the commit path is inert until a template turns on.
    /// </summary>
    // Number of derived constraints committed by the most recent PrepareDerivedConstraints call.
    // Test observability only.
    internal static int LastDerivedCommitCount;

    internal void PrepareDerivedConstraints()
    {
        LastDerivedCommitCount = 0;

        if (DerivedSumsDisabled())
        {
            return;
        }

        if (ResidualEntropy() <= DerivedSumMinResidualEntropy)
        {
            return;
        }

        List<Constraint> derived = GenerateDerivedConstraints();
        LastDerivedCommitCount = derived.Count;
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

    private List<Constraint> GenerateDerivedConstraints()
    {
        var candidates = new List<DerivedSumCandidate>();
        CollectCombinedLittleKillerCandidates(candidates);

        if (candidates.Count == 0)
        {
            return [];
        }

        List<DerivedSumCandidate> selected = DerivedSumScoring.SelectWithinBudget(candidates);
        var result = new List<Constraint>(selected.Count);
        foreach (DerivedSumCandidate candidate in selected)
        {
            result.Add(candidate.Build());
        }
        return result;
    }

    // A harvested little-killer sum piece: its diagonal cells, clue total, diagonal family, and offset.
    private readonly struct LittleKillerPiece(IReadOnlyList<(int, int)> cells, int sum, bool antiDiagonal, int key, HashSet<int> indices)
    {
        internal readonly IReadOnlyList<(int, int)> Cells = cells;
        internal readonly int Sum = sum;
        internal readonly bool AntiDiagonal = antiDiagonal;
        internal readonly int Key = key;
        internal readonly HashSet<int> Indices = indices;
    }

    /// <summary>
    /// Combines pairs of parallel, adjacent, disjoint little killers into a single fixed sum over their
    /// union. This is only useful when cells of the two diagonals see each other (so the joint sum +
    /// uniqueness prunes beyond the two independent sums); otherwise it merely restates them.
    /// </summary>
    private void CollectCombinedLittleKillerCandidates(List<DerivedSumCandidate> candidates)
    {
        var pieces = new List<LittleKillerPiece>();
        foreach (SumTerm term in sumConstraints.Terms)
        {
            if (term.Source is not LittleKillerConstraint littleKiller)
            {
                continue;
            }

            IReadOnlyList<(int, int)> cells = term.Cells;
            if (cells.Count == 0)
            {
                continue;
            }

            // Anti-diagonals (i+j constant) for UpRight/DownLeft; main diagonals (i-j) for UpLeft/DownRight.
            bool antiDiagonal = littleKiller.direction is LittleKillerConstraint.Direction.UpRight or LittleKillerConstraint.Direction.DownLeft;
            int key = antiDiagonal ? cells[0].Item1 + cells[0].Item2 : cells[0].Item1 - cells[0].Item2;

            var indices = new HashSet<int>();
            foreach ((int, int) cell in cells)
            {
                indices.Add(CellIndex(cell));
            }

            pieces.Add(new LittleKillerPiece(cells, littleKiller.sum, antiDiagonal, key, indices));
        }

        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                LittleKillerPiece pieceA = pieces[a];
                LittleKillerPiece pieceB = pieces[b];

                if (pieceA.AntiDiagonal != pieceB.AntiDiagonal || Math.Abs(pieceA.Key - pieceB.Key) != 1)
                {
                    continue;
                }
                if (pieceA.Indices.Overlaps(pieceB.Indices))
                {
                    continue;
                }

                int combinedSum = pieceA.Sum + pieceB.Sum;
                int unionCount = pieceA.Cells.Count + pieceB.Cells.Count;
                // Enforcement handles any size via the list path; cap only to bound recurring cost.
                if (unionCount > 2 * MAX_VALUE)
                {
                    continue;
                }
                if (!CrossSourceSeen(pieceA.Indices, pieceB.Indices))
                {
                    continue;
                }

                var unionCells = new List<(int, int)>(pieceA.Cells);
                unionCells.AddRange(pieceB.Cells);

                DerivedSumConstraint constraint = DerivedSumConstraint.CreateFixedSum(this, unionCells, [combinedSum]);

                // Small terms use the exact 64-bit sum mask; larger ones fall back to range scoring
                // (and range-based enforcement via SumCellsHelper's list path).
                double gain;
                bool contradiction;
                double cost;
                if (unionCount * MAX_VALUE <= 63 && combinedSum <= 63)
                {
                    ulong mask = constraint.FixedTermScoringMask(this);
                    gain = DerivedSumScoring.ScoreFixedSumGain(mask, 1UL << combinedSum, out contradiction);
                    cost = DerivedSumScoring.Cost(unionCount, groupCount: 1, popcountP: BitOperations.PopCount(mask), popcountN: 0);
                }
                else
                {
                    (int min, int max) = constraint.FixedTermScoringRange(this);
                    gain = DerivedSumScoring.ScoreFixedSumRangeGain(min, max, combinedSum, out contradiction);
                    // Non-mask terms enforce via the allocating list path, so weight their cost higher.
                    cost = DerivedSumScoring.Cost(unionCount, groupCount: 2, popcountP: Math.Min(max - min + 1, 60), popcountN: 0);
                }
                if (contradiction || gain <= 0.0)
                {
                    continue;
                }

                int[] watched = [.. pieceA.Indices, .. pieceB.Indices];
                candidates.Add(new DerivedSumCandidate
                {
                    Gain = gain,
                    Score = gain / cost,
                    WatchedCells = watched,
                    Build = () => constraint,
                });
            }
        }
    }

    private bool CrossSourceSeen(HashSet<int> a, HashSet<int> b)
    {
        foreach (int cellA in a)
        {
            foreach (int cellB in b)
            {
                if (IsSeen(cellA, cellB))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
