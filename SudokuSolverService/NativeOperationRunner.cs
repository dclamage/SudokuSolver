using SudokuSolver;
using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService.Protocol;

namespace SudokuSolverService;

/// <summary>Projects and executes transport-neutral native solver operations.</summary>
public sealed class NativeOperationRunner
{
    private const int SupportedProtocolVersion = 1;
    private readonly bool _singleThreaded;
    private readonly Func<Solver, CancellationToken, bool> _findSolution;

    /// <summary>Initializes a native operation runner.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    public NativeOperationRunner(bool singleThreaded)
    {
        _singleThreaded = singleThreaded;
        _findSolution = (solver, cancellationToken) => solver.FindSolution(
            multiThread: !singleThreaded,
            isRandom: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>Initializes a runner with an injectable solve boundary for deterministic cancellation tests.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="findSolution">The solver search function.</param>
    internal NativeOperationRunner(
        bool singleThreaded,
        Func<Solver, CancellationToken, bool> findSolution)
    {
        _singleThreaded = singleThreaded;
        _findSolution = findSolution ?? throw new ArgumentNullException(nameof(findSolution));
    }

    /// <summary>Executes one native operation and emits progress followed by one terminal response.</summary>
    /// <param name="request">The typed native request.</param>
    /// <param name="sendResponse">The response sink owned by the transport host.</param>
    /// <param name="cancellationToken">The host-owned cancellation token.</param>
    public void Run(
        SolverRequest request,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sendResponse);

        string verifiedHash;
        try
        {
            verifiedHash = NativeSemanticHasher.Compute(request.Puzzle);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            sendResponse(Error(request, string.Empty, "invalidPackage", exception.Message));
            return;
        }

        if (request.ProtocolVersion != SupportedProtocolVersion)
        {
            sendResponse(Error(
                request,
                verifiedHash,
                "unsupportedVersion",
                $"Unsupported native solver protocol version {request.ProtocolVersion}."));
            return;
        }
        if (!string.Equals(request.SemanticHash, verifiedHash, StringComparison.Ordinal))
        {
            sendResponse(Error(
                request,
                verifiedHash,
                "semanticHashMismatch",
                "The client semantic hash does not match the authoritative server hash."));
            return;
        }

        try
        {
            string projectionId = GetProjectionId(request);
            EnsureSupportedProjection(request.Puzzle, projectionId);
            switch (request.Operation)
            {
                case "validate":
                    NativeProjectionValidationResult validation = NativePuzzleProjector.ValidateProjection(
                        request.Puzzle,
                        projectionId);
                    sendResponse(Result(
                        request,
                        verifiedHash,
                        capability: MapCapability(
                            validation.Capabilities,
                            projectionId,
                            validation.Contradiction)));
                    break;
                case "solve":
                    NativeProjectionResult solveProjection = NativePuzzleProjector.Project(request.Puzzle, projectionId);
                    RunSolve(request, verifiedHash, solveProjection, sendResponse, cancellationToken);
                    break;
                case "count":
                    NativeProjectionResult countProjection = NativePuzzleProjector.Project(request.Puzzle, projectionId);
                    RunCount(request, verifiedHash, countProjection, sendResponse, cancellationToken);
                    break;
                default:
                    sendResponse(Error(
                        request,
                        verifiedHash,
                        "invalidPackage",
                        $"Unknown native solver operation {request.Operation}."));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            sendResponse(CreateResponse(request, verifiedHash, "canceled"));
        }
        catch (UnsupportedProjectionException exception)
        {
            sendResponse(Error(request, verifiedHash, "unsupportedProjection", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            sendResponse(Error(request, verifiedHash, "contradiction", exception.Message));
        }
        catch (ArgumentException exception)
        {
            sendResponse(Error(request, verifiedHash, "invalidPackage", exception.Message));
        }
        catch (Exception exception)
        {
            sendResponse(Error(request, verifiedHash, "internalError", exception.Message));
        }
    }

    private void RunSolve(
        SolverRequest request,
        string verifiedHash,
        NativeProjectionResult projection,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        bool foundSolution = _findSolution(projection.Solver, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!foundSolution)
        {
            sendResponse(Error(request, verifiedHash, "contradiction", "No solutions found."));
            return;
        }

        NativeSolverProjection selectedProjection = request.Puzzle.SolverProjections.Single(
            candidate => string.Equals(
                candidate.Id,
                request.SolveOptions!.ProjectionId,
                StringComparison.Ordinal));
        Dictionary<string, string> valuesByCellId = new(StringComparer.Ordinal);
        for (int index = 0; index < projection.CellIdByIndex.Count; index++)
        {
            int solverValue = SolverUtility.GetValue(projection.Solver.FlatBoard[index]);
            valuesByCellId[projection.CellIdByIndex[index]] =
                selectedProjection.ValueIdsBySolverValue[solverValue - 1];
        }
        sendResponse(Result(
            request,
            verifiedHash,
            solve: new SolveResultDto { ValuesByCellId = valuesByCellId }));
    }

    private void RunCount(
        SolverRequest request,
        string verifiedHash,
        NativeProjectionResult projection,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        CountOptionsDto options = request.CountOptions!;
        if (options.MaxSolutions < 1)
        {
            sendResponse(Error(
                request,
                verifiedHash,
                "invalidPackage",
                "Count maxSolutions must be at least 1."));
            return;
        }

        sendResponse(CreateResponse(
            request,
            verifiedHash,
            "progress",
            count: new CountResultDto
            {
                SolutionCount = 0,
                MaxSolutions = options.MaxSolutions,
                IsClamped = false,
            }));
        long count = projection.Solver.CountSolutions(
            options.MaxSolutions,
            multiThread: !_singleThreaded,
            cancellationToken: cancellationToken,
            progressEvent: progress => sendResponse(CreateResponse(
                request,
                verifiedHash,
                "progress",
                count: new CountResultDto
                {
                    SolutionCount = Math.Min(progress, options.MaxSolutions),
                    MaxSolutions = options.MaxSolutions,
                    IsClamped = progress >= options.MaxSolutions,
                })));
        cancellationToken.ThrowIfCancellationRequested();
        sendResponse(Result(
            request,
            verifiedHash,
            count: new CountResultDto
            {
                SolutionCount = Math.Min(count, options.MaxSolutions),
                MaxSolutions = options.MaxSolutions,
                IsClamped = count >= options.MaxSolutions,
            }));
    }

    private static string GetProjectionId(SolverRequest request)
        => request.Operation switch
        {
            "validate" => request.ValidateOptions?.ProjectionId
                ?? throw new ArgumentException("Native validateOptions are required.", nameof(request)),
            "solve" => request.SolveOptions?.ProjectionId
                ?? throw new ArgumentException("Native solveOptions are required.", nameof(request)),
            "count" => request.CountOptions?.ProjectionId
                ?? throw new ArgumentException("Native countOptions are required.", nameof(request)),
            _ => request.ValidateOptions?.ProjectionId
                ?? request.SolveOptions?.ProjectionId
                ?? request.CountOptions?.ProjectionId
                ?? throw new ArgumentException("Native operation options are required.", nameof(request)),
        };

    private static void EnsureSupportedProjection(NativePuzzlePackage package, string projectionId)
    {
        NativeSolverProjection? projection = package.SolverProjections.SingleOrDefault(
            candidate => string.Equals(candidate.Id, projectionId, StringComparison.Ordinal));
        if (projection is null)
        {
            throw new UnsupportedProjectionException(
                $"Native puzzle does not contain projection {projectionId}.");
        }
        if (!string.Equals(projection.Kind, "latin-square", StringComparison.Ordinal))
        {
            throw new UnsupportedProjectionException(
                $"Projection {projectionId} has unsupported kind {projection.Kind}.");
        }
    }

    private static CapabilityResultDto MapCapability(
        CapabilityReport capabilities,
        string projectionId,
        bool contradiction)
    {
        Dictionary<string, CapabilityEntityDto> entities = new(StringComparer.Ordinal);
        foreach ((string key, EntityCapabilityResult entity) in capabilities.Entities)
        {
            entities[key] = new CapabilityEntityDto
            {
                EntityKind = entity.EntityKind,
                EntityId = entity.EntityId,
                Status = entity.Status switch
                {
                    EntityCapability.InvalidDefinition => "invalidDefinition",
                    EntityCapability.FullyVerified => "fullyVerified",
                    EntityCapability.PartiallyVerified => "partiallyVerified",
                    EntityCapability.VisualOnly => "visualOnly",
                    _ => throw new InvalidOperationException($"Unknown entity capability {entity.Status}."),
                },
                Reason = entity.Reason,
            };
        }
        return new CapabilityResultDto
        {
            ProjectionId = projectionId,
            Contradiction = contradiction,
            Entities = entities,
        };
    }

    private static SolverResponse Result(
        SolverRequest request,
        string verifiedHash,
        CapabilityResultDto? capability = null,
        SolveResultDto? solve = null,
        CountResultDto? count = null)
        => CreateResponse(request, verifiedHash, "result", capability, solve, count);

    private static SolverResponse Error(
        SolverRequest request,
        string verifiedHash,
        string code,
        string message)
        => CreateResponse(
            request,
            verifiedHash,
            "error",
            error: new SolverErrorDto { Code = code, Message = message });

    private static SolverResponse CreateResponse(
        SolverRequest request,
        string verifiedHash,
        string kind,
        CapabilityResultDto? capability = null,
        SolveResultDto? solve = null,
        CountResultDto? count = null,
        SolverErrorDto? error = null)
        => new()
        {
            Kind = kind,
            ProtocolVersion = SupportedProtocolVersion,
            RequestId = request.RequestId,
            DocumentRevision = request.DocumentRevision,
            SemanticRevision = request.SemanticRevision,
            SemanticHash = verifiedHash,
            ContextId = request.ContextId,
            Operation = request.Operation,
            Capability = capability,
            Solve = solve,
            Count = count,
            Error = error,
        };

    private sealed class UnsupportedProjectionException(string message) : Exception(message);
}