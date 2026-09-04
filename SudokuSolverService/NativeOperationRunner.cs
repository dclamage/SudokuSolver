using SudokuSolver;
using SudokuSolver.Logical;
using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService.Protocol;
using System.Security.Cryptography;
using System.Text;

namespace SudokuSolverService;

/// <summary>Projects and executes transport-neutral native solver operations.</summary>
public sealed class NativeOperationRunner
{
    private const int SupportedProtocolVersion = 1;
    private readonly bool _singleThreaded;
    private readonly Func<Solver, CancellationToken, bool> _findSolution;
    private readonly object _logicalSessionLock = new();
    private readonly Dictionary<LogicalSessionKey, LogicalSession> _logicalSessions = [];

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
                case "trueCandidates":
                    NativeProjectionResult trueCandidatesProjection = NativePuzzleProjector.Project(
                        request.Puzzle,
                        projectionId);
                    RunTrueCandidates(
                        request,
                        verifiedHash,
                        trueCandidatesProjection,
                        sendResponse,
                        cancellationToken);
                    break;
                case "logical.create":
                    RunLogicalCreate(request, verifiedHash, projectionId, sendResponse, cancellationToken);
                    break;
                case "logical.apply":
                    RunLogicalApply(request, verifiedHash, projectionId, sendResponse, cancellationToken);
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

    private void RunTrueCandidates(
        SolverRequest request,
        string verifiedHash,
        NativeProjectionResult projection,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        TrueCandidatesOptionsDto options = request.TrueCandidatesOptions!;
        if (options.Display is not ("possibility" or "solutionFrequency" or "logicComparison"))
        {
            throw new ArgumentException(
                $"True Candidates display {options.Display} is invalid.",
                nameof(request));
        }
        if (options.SolutionCountCap is < 1 or > 1024)
        {
            throw new ArgumentException(
                "True Candidates solutionCountCap must be between 1 and 1024.",
                nameof(request));
        }

        NativeSolverProjection selectedProjection = request.Puzzle.SolverProjections.Single(
            candidate => string.Equals(
                candidate.Id,
                options.ProjectionId,
                StringComparison.Ordinal));
        int[]? logicalCandidateMasks = null;
        if (options.Display == "logicComparison")
        {
            Solver logicalSolver = projection.Solver.Clone(willRunNonSinglesLogic: true);
            LogicResult logicalResult = logicalSolver.ConsolidateBoard(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (logicalResult == LogicResult.Invalid)
            {
                sendResponse(Error(request, verifiedHash, "contradiction", "No solutions found."));
                return;
            }
            logicalCandidateMasks = logicalSolver.FlatBoard
                .Select(mask => unchecked((int)(mask & ~SolverUtility.valueSetMask)))
                .ToArray();
        }

        long[] counts = projection.Solver.TrueCandidates(
            multiThread: !_singleThreaded,
            progressEvent: progress => sendResponse(CreateResponse(
                request,
                verifiedHash,
                "progress",
                trueCandidates: MapTrueCandidates(
                    projection,
                    selectedProjection,
                    progress,
                    options.SolutionCountCap,
                    logicalCandidateMasks))),
            numSolutionsCap: options.SolutionCountCap,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!counts.Any(count => count > 0))
        {
            sendResponse(Error(request, verifiedHash, "contradiction", "No solutions found."));
            return;
        }
        sendResponse(Result(
            request,
            verifiedHash,
            trueCandidates: MapTrueCandidates(
                projection,
                selectedProjection,
                counts,
                options.SolutionCountCap,
                logicalCandidateMasks)));
    }

    private static TrueCandidatesResultDto MapTrueCandidates(
        NativeProjectionResult projection,
        NativeSolverProjection selectedProjection,
        IReadOnlyList<long> counts,
        long solutionCountCap,
        int[]? logicalCandidateMasks)
    {
        int expectedCount = projection.CellIdByIndex.Count * selectedProjection.ValueIdsBySolverValue.Count;
        if (counts.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"True Candidates returned {counts.Count} counts for {expectedCount} projected candidates.");
        }
        if (logicalCandidateMasks is not null &&
            logicalCandidateMasks.Length != projection.CellIdByIndex.Count)
        {
            throw new InvalidOperationException(
                "True Candidates returned logical masks with an invalid projected shape.");
        }

        long[] solutionCounts = counts
            .Select(count => Math.Clamp(count, 0, solutionCountCap))
            .ToArray();
        return new TrueCandidatesResultDto
        {
            CellIds = projection.CellIdByIndex.ToArray(),
            ValueIdsBySolverValue = selectedProjection.ValueIdsBySolverValue.ToArray(),
            SolutionCounts = solutionCounts,
            LogicalCandidateMasks = logicalCandidateMasks,
            SolutionCountCap = solutionCountCap,
        };
    }

    private void RunLogicalCreate(
        SolverRequest request,
        string verifiedHash,
        string projectionId,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        LogicalCreateOptionsDto options = request.LogicalCreateOptions!;
        ValidateRequiredText(options.ProjectionId, "Logical create projectionId");
        if (options.AppliedDeductionIds is null
            || options.AppliedDeductionIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Logical create appliedDeductionIds must contain valid IDs.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        NativeProjectionResult projection = NativePuzzleProjector.Project(request.Puzzle, projectionId);
        NativeSolverProjection selectedProjection = GetSelectedProjection(request.Puzzle, projectionId);
        LogicalPosition position = new(
            projection.Solver,
            projection.CellIdByIndex,
            selectedProjection.ValueIdsBySolverValue,
            verifiedHash);
        List<string> history = [];
        foreach (string deductionId in options.AppliedDeductionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogicalDeduction? deduction = LogicalDeductionService.FindAvailable(position).SingleOrDefault(candidate =>
                string.Equals(candidate.Id, deductionId, StringComparison.Ordinal));
            if (deduction is null)
            {
                sendResponse(Error(
                    request,
                    verifiedHash,
                    "staleContext",
                    $"Logical replay deduction {deductionId} is not available."));
                return;
            }
            _ = LogicalDeductionService.Apply(position, deduction);
            history.Add(deductionId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogicalSessionKey key = GetLogicalSessionKey(request, verifiedHash);
        LogicalSession session = new(
            CreateLogicalSessionId(key, projectionId),
            projectionId,
            position,
            history.ToArray());
        lock (_logicalSessionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logicalSessions[key] = session;
        }
        sendResponse(Result(request, verifiedHash, logical: MapLogicalSession(session)));
    }

    private void RunLogicalApply(
        SolverRequest request,
        string verifiedHash,
        string projectionId,
        Action<SolverResponse> sendResponse,
        CancellationToken cancellationToken)
    {
        LogicalApplyOptionsDto options = request.LogicalApplyOptions!;
        ValidateRequiredText(options.ProjectionId, "Logical apply projectionId");
        ValidateRequiredText(options.SessionId, "Logical apply sessionId");
        ValidateRequiredText(options.PositionHash, "Logical apply positionHash");
        ValidateRequiredText(options.DeductionId, "Logical apply deductionId");
        cancellationToken.ThrowIfCancellationRequested();

        LogicalResultDto? result = null;
        LogicalSessionKey key = GetLogicalSessionKey(request, verifiedHash);
        lock (_logicalSessionLock)
        {
            if (!_logicalSessions.TryGetValue(key, out LogicalSession? current)
                || !string.Equals(current.SessionId, options.SessionId, StringComparison.Ordinal)
                || !string.Equals(current.ProjectionId, projectionId, StringComparison.Ordinal)
                || !string.Equals(current.Position.PositionHash, options.PositionHash, StringComparison.Ordinal))
            {
                sendResponse(Error(request, verifiedHash, "staleContext", "The logical session context is stale."));
                return;
            }

            LogicalDeduction? deduction = LogicalDeductionService.FindAvailable(current.Position)
                .SingleOrDefault(candidate => string.Equals(candidate.Id, options.DeductionId, StringComparison.Ordinal));
            if (deduction is null)
            {
                sendResponse(Error(
                    request,
                    verifiedHash,
                    "staleContext",
                    "The logical deduction is forged, stale, or no longer available."));
                return;
            }

            LogicalPosition nextPosition = current.Position.Clone();
            try
            {
                _ = LogicalDeductionService.Apply(nextPosition, deduction);
            }
            catch (InvalidOperationException)
            {
                sendResponse(Error(
                    request,
                    verifiedHash,
                    "staleContext",
                    "The logical deduction is forged, stale, or no longer available."));
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            LogicalSession next = current with
            {
                Position = nextPosition,
                HistoryDeductionIds = [.. current.HistoryDeductionIds, deduction.Id],
            };
            _logicalSessions[key] = next;
            result = MapLogicalSession(next);
        }
        sendResponse(Result(request, verifiedHash, logical: result));
    }

    private static LogicalResultDto MapLogicalSession(LogicalSession session)
        => new()
        {
            SessionId = session.SessionId,
            PositionHash = session.Position.PositionHash,
            Cells = session.Position.GetCells().Select(MapLogicalCell).ToArray(),
            AvailableDeductions = LogicalDeductionService.FindAvailable(session.Position)
                .Select(MapLogicalDeduction)
                .ToArray(),
            HistoryDeductionIds = session.HistoryDeductionIds.ToArray(),
        };

    private static LogicalCellStateDto MapLogicalCell(LogicalCellState cell)
        => new()
        {
            CellId = cell.CellId,
            ValueId = cell.ValueId,
            CandidateValueIds = cell.CandidateValueIds.ToArray(),
        };

    private static LogicalDeductionDto MapLogicalDeduction(LogicalDeduction deduction)
        => new()
        {
            Id = deduction.Id,
            TechniqueId = deduction.TechniqueId,
            OwningConstraintId = deduction.OwningConstraintId,
            PreconditionHash = deduction.PreconditionHash,
            Premises = deduction.Premises.Select(premise => new LogicalPremiseDto
            {
                Kind = premise.Kind,
                CellId = premise.CellId,
                ValueId = premise.ValueId,
            }).ToArray(),
            Delta = new LogicalDeltaDto
            {
                Placements = deduction.Delta.Placements.Select(placement => new LogicalPlacementDto
                {
                    CellId = placement.CellId,
                    ValueId = placement.ValueId,
                }).ToArray(),
                Eliminations = deduction.Delta.Eliminations.Select(elimination => new LogicalEliminationDto
                {
                    CellId = elimination.CellId,
                    ValueId = elimination.ValueId,
                }).ToArray(),
            },
            Frames = deduction.Frames.Select(frame => new LogicalWalkthroughFrameDto
            {
                Focus = frame.Focus.Select(MapLogicalReference).ToArray(),
                Dim = frame.Dim.Select(MapLogicalReference).ToArray(),
                Highlight = frame.Highlight.Select(MapLogicalReference).ToArray(),
                Explanation = new LogicalExplanationDto
                {
                    Key = frame.Explanation.Key,
                    Arguments = frame.Explanation.Arguments.Select(argument => new LogicalExplanationArgumentDto
                    {
                        Kind = argument.Kind,
                        Value = argument.Value,
                    }).ToArray(),
                },
            }).ToArray(),
        };

    private static LogicalEntityReferenceDto MapLogicalReference(LogicalEntityReference reference)
        => new() { Kind = reference.Kind, Id = reference.Id };

    private static NativeSolverProjection GetSelectedProjection(
        NativePuzzlePackage package,
        string projectionId)
        => package.SolverProjections.Single(projection =>
            string.Equals(projection.Id, projectionId, StringComparison.Ordinal));

    private static LogicalSessionKey GetLogicalSessionKey(SolverRequest request, string verifiedHash)
        => new(request.ContextId, request.SemanticRevision, verifiedHash);

    private static string CreateLogicalSessionId(LogicalSessionKey key, string projectionId)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write("logical-session-v1");
            writer.Write(key.ContextId);
            writer.Write(key.SemanticRevision);
            writer.Write(key.SemanticHash);
            writer.Write(projectionId);
        }
        return $"sha256:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private static void ValidateRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }
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
            "trueCandidates" => request.TrueCandidatesOptions?.ProjectionId
                ?? throw new ArgumentException("Native trueCandidatesOptions are required.", nameof(request)),
            "logical.create" => request.LogicalCreateOptions?.ProjectionId
                ?? throw new ArgumentException("Native logicalCreateOptions are required.", nameof(request)),
            "logical.apply" => request.LogicalApplyOptions?.ProjectionId
                ?? throw new ArgumentException("Native logicalApplyOptions are required.", nameof(request)),
            _ => request.ValidateOptions?.ProjectionId
                ?? request.SolveOptions?.ProjectionId
                ?? request.CountOptions?.ProjectionId
                ?? request.TrueCandidatesOptions?.ProjectionId
                ?? request.LogicalCreateOptions?.ProjectionId
                ?? request.LogicalApplyOptions?.ProjectionId
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
        CountResultDto? count = null,
        TrueCandidatesResultDto? trueCandidates = null,
        LogicalResultDto? logical = null)
        => CreateResponse(request, verifiedHash, "result", capability, solve, count, trueCandidates, logical);

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
        TrueCandidatesResultDto? trueCandidates = null,
        LogicalResultDto? logical = null,
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
            TrueCandidates = trueCandidates,
            Logical = logical,
            Error = error,
        };

    private readonly record struct LogicalSessionKey(
        string ContextId,
        long SemanticRevision,
        string SemanticHash);

    private sealed record LogicalSession(
        string SessionId,
        string ProjectionId,
        LogicalPosition Position,
        string[] HistoryDeductionIds);

    private sealed class UnsupportedProjectionException(string message) : Exception(message);
}