using System.Threading;

namespace SudokuSolver;

public partial class Solver
{
    /// <summary>
    /// Result from the MakeUnique operation.
    /// </summary>
    public class MakeUniqueResult
    {
        public bool Success { get; init; }
        public string Message { get; init; }
        public int GivensPlaced { get; init; }
    }

    /// <summary>
    /// Makes the puzzle unique by placing minimal givens heuristically.
    /// Uses true candidates with a solution cap to find the best candidate to set.
    /// </summary>
    /// <param name="solutionCap">The solution cap per candidate for true candidates computation.</param>
    /// <param name="estimationIterations">Number of iterations for Monte-Carlo estimation when all candidates have >= solutionCap solutions.</param>
    /// <param name="multiThread">Whether to use multiple threads.</param>
    /// <param name="progressEvent">Event called with progress messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and number of givens placed.</returns>
    public MakeUniqueResult MakeUnique(
        long solutionCap = 1000,
        long estimationIterations = 100,
        bool multiThread = false,
        Action<string> progressEvent = null,
        CancellationToken cancellationToken = default)
    {
        if (seenMap == null)
        {
            throw new InvalidOperationException("Must call FinalizeConstraints() first (even if there are no constraints)");
        }

        int givensPlaced = 0;
        const double z95 = 1.96; // 95% confidence interval multiplier

        while (!cancellationToken.IsCancellationRequested)
        {
            // Case 1: Check if already unique (exactly one solution)
            progressEvent?.Invoke($"Checking solution count (givens placed so far: {givensPlaced})...");
            long solutionCount = CountSolutions(maxSolutions: 2, multiThread: multiThread, cancellationToken: cancellationToken);

            if (solutionCount == 0)
            {
                return new MakeUniqueResult
                {
                    Success = false,
                    Message = "Puzzle has no solutions.",
                    GivensPlaced = givensPlaced
                };
            }

            if (solutionCount == 1)
            {
                return new MakeUniqueResult
                {
                    Success = true,
                    Message = $"Puzzle is now unique! Placed {givensPlaced} given(s).",
                    GivensPlaced = givensPlaced
                };
            }

            // Compute true candidates with solution cap
            progressEvent?.Invoke($"Computing true candidates with solution cap {solutionCap}...");
            long[] trueCandidateCounts = TrueCandidates(
                multiThread: multiThread,
                numSolutionsCap: solutionCap,
                cancellationToken: cancellationToken);

            // Find candidates to consider (unset cells only)
            List<(int cellIndex, int value, long count)> candidates = new();

            for (int cellIndex = 0; cellIndex < NUM_CELLS; cellIndex++)
            {
                uint cellMask = board[cellIndex];
                if (IsValueSet(cellMask))
                {
                    continue; // Skip already-set cells
                }

                for (int value = 1; value <= MAX_VALUE; value++)
                {
                    if (!HasValue(cellMask, value))
                    {
                        continue; // Candidate not available
                    }

                    int candidateIndex = cellIndex * MAX_VALUE + value - 1;
                    long count = trueCandidateCounts[candidateIndex];

                    if (count > 0)
                    {
                        candidates.Add((cellIndex, value, count));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return new MakeUniqueResult
                {
                    Success = false,
                    Message = "No valid candidates found.",
                    GivensPlaced = givensPlaced
                };
            }

            // Case 2: Is there a candidate with exactly 1 solution?
            var uniqueCandidates = candidates.Where(c => c.count == 1).ToList();
            if (uniqueCandidates.Count > 0)
            {
                // Pick one at random
                var chosen = uniqueCandidates[RandomNext(0, uniqueCandidates.Count)];
                var (i, j) = CellIndexToCoord(chosen.cellIndex);
                progressEvent?.Invoke($"Found candidate with 1 solution: {CellName(i, j)}={chosen.value}. Setting it.");

                if (!SetValue(chosen.cellIndex, chosen.value))
                {
                    return new MakeUniqueResult
                    {
                        Success = false,
                        Message = $"Failed to set value {chosen.value} at {CellName(i, j)}.",
                        GivensPlaced = givensPlaced
                    };
                }

                givensPlaced++;
                continue; // Re-check if now unique
            }

            // Case 3: Is there at least one candidate with < solutionCap solutions?
            var belowCapCandidates = candidates.Where(c => c.count < solutionCap).ToList();
            if (belowCapCandidates.Count > 0)
            {
                // Find the minimum count
                long minCount = belowCapCandidates.Min(c => c.count);
                var tiedCandidates = belowCapCandidates.Where(c => c.count == minCount).ToList();

                // Pick one at random from tied candidates
                var chosen = tiedCandidates[RandomNext(0, tiedCandidates.Count)];
                var (i, j) = CellIndexToCoord(chosen.cellIndex);
                progressEvent?.Invoke($"Found candidate with {chosen.count} solutions (below cap): {CellName(i, j)}={chosen.value}. Setting it.");

                if (!SetValue(chosen.cellIndex, chosen.value))
                {
                    return new MakeUniqueResult
                    {
                        Success = false,
                        Message = $"Failed to set value {chosen.value} at {CellName(i, j)}.",
                        GivensPlaced = givensPlaced
                    };
                }

                givensPlaced++;
                continue;
            }

            // Case 4: All candidates have >= solutionCap solutions
            // Use Monte-Carlo estimation in one pass to find the best candidate
            progressEvent?.Invoke($"All candidates have >= {solutionCap} solutions. Using Monte-Carlo estimation ({estimationIterations} iterations)...");

            // Run single-pass estimation for all candidates
            var (estimatesArray, stdErrsArray) = EstimateTrueCandidates(
                numIterations: estimationIterations,
                multiThread: multiThread,
                cancellationToken: cancellationToken);

            // Build list of candidates with their estimates
            List<(int cellIndex, int value, double estimate, double stderr)> estimates = new();
            foreach (var (cellIndex, value, _) in candidates)
            {
                int candidateIndex = cellIndex * MAX_VALUE + value - 1;
                double estimate = estimatesArray[candidateIndex];
                double stderr = stdErrsArray[candidateIndex];

                if (estimate > 0) // Only consider candidates with positive estimates
                {
                    estimates.Add((cellIndex, value, estimate, stderr));
                }
            }

            if (estimates.Count == 0)
            {
                return new MakeUniqueResult
                {
                    Success = false,
                    Message = "No valid candidates after estimation.",
                    GivensPlaced = givensPlaced
                };
            }

            // Log top candidates
            var topCandidates = estimates.OrderBy(e => e.estimate).Take(5).ToList();
            foreach (var (cellIndex, value, estimate, stderr) in topCandidates)
            {
                var (i, j) = CellIndexToCoord(cellIndex);
                progressEvent?.Invoke($"  {CellName(i, j)}={value}: {estimate:E3} ± {z95 * stderr:E3}");
            }

            // Find candidates quasi-tied for minimum (using 95% CI overlap)
            double minEstimate = estimates.Min(e => e.estimate);
            var bestCandidate = estimates.First(e => e.estimate == minEstimate);
            double bestLowerBound = bestCandidate.estimate - z95 * bestCandidate.stderr;
            double bestUpperBound = bestCandidate.estimate + z95 * bestCandidate.stderr;

            var quasiTiedCandidates = estimates
                .Where(e =>
                {
                    double lowerBound = e.estimate - z95 * e.stderr;
                    // Consider quasi-tied if lower bound is below the best's upper bound
                    return lowerBound <= bestUpperBound;
                })
                .ToList();

            if (quasiTiedCandidates.Count == 0)
            {
                quasiTiedCandidates = new List<(int cellIndex, int value, double estimate, double stderr)> { bestCandidate };
            }

            // Pick one at random from quasi-tied candidates
            var chosenEst = quasiTiedCandidates[RandomNext(0, quasiTiedCandidates.Count)];
            var (ci, cj) = CellIndexToCoord(chosenEst.cellIndex);
            progressEvent?.Invoke($"Chose candidate via estimation: {CellName(ci, cj)}={chosenEst.value} (estimate: {chosenEst.estimate:E3}, {quasiTiedCandidates.Count} quasi-tied). Setting it.");

            if (!SetValue(chosenEst.cellIndex, chosenEst.value))
            {
                return new MakeUniqueResult
                {
                    Success = false,
                    Message = $"Failed to set value {chosenEst.value} at {CellName(ci, cj)}.",
                    GivensPlaced = givensPlaced
                };
            }

            givensPlaced++;
        }

        return new MakeUniqueResult
        {
            Success = false,
            Message = "Operation was cancelled.",
            GivensPlaced = givensPlaced
        };
    }
}
