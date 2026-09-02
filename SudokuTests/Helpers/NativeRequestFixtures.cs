using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService.Protocol;
using SudokuSolver;

#nullable enable

namespace SudokuTests.Helpers;

/// <summary>Creates correlated native solver requests from the shared cross-language fixture.</summary>
internal static class NativeRequestFixtures
{
    private static readonly Lazy<NativePuzzlePackage> PackageValue = new(ReadPackage);

    /// <summary>Gets the authoritative semantic hash of the shared native fixture.</summary>
    internal static string SemanticHash => NativeSemanticHasher.Compute(PackageValue.Value);

    /// <summary>Gets an existing known-good legacy f-puzzles-compatible classic puzzle.</summary>
    internal static string LegacyPuzzle
    {
        get
        {
            Solver solver = new(9, 9, 9);
            solver.SetRegions(SolverUtility.DefaultRegions(9));
            _ = solver.FinalizeConstraints();
            string givens = Puzzles.uniqueClassics[0].Item1;
            bool[,] givenCells = new bool[9, 9];
            for (int index = 0; index < givens.Length; index++)
            {
                if (givens[index] is >= '1' and <= '9')
                {
                    _ = solver.SetValue(index, givens[index] - '0');
                    givenCells[index / 9, index % 9] = true;
                }
            }
            solver.customInfo["Givens"] = givenCells;
            return SolverFactory.ToFPuzzlesURL(solver, justBase64: true);
        }
    }

    /// <summary>Gets the unique solution for <see cref="LegacyPuzzle"/>.</summary>
    internal static string LegacySolution => Puzzles.uniqueClassics[0].Item2;

    /// <summary>Creates a native validation request.</summary>
    /// <param name="requestId">The request correlation identifier.</param>
    /// <param name="documentRevision">The document revision to echo.</param>
    /// <param name="semanticRevision">The semantic revision to echo.</param>
    /// <param name="contextId">The candidate context identifier to echo.</param>
    /// <param name="semanticHash">An optional client hash override.</param>
    /// <param name="protocolVersion">The protocol version to send.</param>
    /// <param name="projectionId">The native projection to select.</param>
    /// <returns>The serialized native request.</returns>
    internal static string Validate(
        string requestId,
        long documentRevision,
        long semanticRevision,
        string contextId,
        string? semanticHash = null,
        int protocolVersion = 1,
        string projectionId = "main-latin-square")
        => Serialize(new SolverRequest
        {
            ProtocolVersion = protocolVersion,
            RequestId = requestId,
            DocumentRevision = documentRevision,
            SemanticRevision = semanticRevision,
            SemanticHash = semanticHash ?? SemanticHash,
            ContextId = contextId,
            Operation = "validate",
            Puzzle = PackageValue.Value,
            ValidateOptions = new ValidateOptionsDto { ProjectionId = projectionId },
        });

    /// <summary>Creates a native solve request for the shared fixture.</summary>
    /// <returns>The serialized native request.</returns>
    internal static string Solve()
        => Serialize(new SolverRequest
        {
            ProtocolVersion = 1,
            RequestId = "solve-1",
            DocumentRevision = 1,
            SemanticRevision = 1,
            SemanticHash = SemanticHash,
            ContextId = "playtest",
            Operation = "solve",
            Puzzle = PackageValue.Value,
            SolveOptions = new SolveOptionsDto { ProjectionId = "main-latin-square" },
        });

    /// <summary>Creates a native bounded-count request for the shared fixture.</summary>
    /// <param name="maxSolutions">The inclusive solution-count cap.</param>
    /// <returns>The serialized native request.</returns>
    internal static string Count(long maxSolutions)
        => Serialize(new SolverRequest
        {
            ProtocolVersion = 1,
            RequestId = "count-1",
            DocumentRevision = 1,
            SemanticRevision = 1,
            SemanticHash = SemanticHash,
            ContextId = "true-candidates",
            Operation = "count",
            Puzzle = PackageValue.Value,
            CountOptions = new CountOptionsDto
            {
                ProjectionId = "main-latin-square",
                MaxSolutions = maxSolutions,
            },
        });

    private static string Serialize(SolverRequest request)
        => System.Text.Json.JsonSerializer.Serialize(request, ProtocolJsonContext.Default.SolverRequest);

    private static NativePuzzlePackage ReadPackage()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "test-fixtures",
            "native",
            "classic-with-auxiliary.json");
        return NativePuzzlePackage.Parse(File.ReadAllText(path));
    }
}
