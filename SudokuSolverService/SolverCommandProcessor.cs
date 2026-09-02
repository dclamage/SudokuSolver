using SudokuSolver;
using SudokuSolverService.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace SudokuSolverService;

/// <summary>Controls how a legacy host handles request kinds it historically did not support.</summary>
public enum LegacyInvalidRequestBehavior
{
    /// <summary>Emit a legacy <c>invalid</c> response.</summary>
    RespondWithInvalid,

    /// <summary>Ignore the unsupported request without emitting a response.</summary>
    Ignore,
}

/// <summary>Identifies an internally observable legacy cache reuse path for regression tests.</summary>
internal enum LegacyCacheEvent
{
    /// <summary>An exact comparable-data cache entry supplied the response.</summary>
    ExactHit,

    /// <summary>A cached solver response was inherited by a more constrained request.</summary>
    InheritedMatch,
}

/// <summary>Compares byte arrays by their contents for legacy cache keys.</summary>
internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    /// <inheritdoc/>
    public bool Equals(byte[]? x, byte[]? y)
        => ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

    /// <inheritdoc/>
    public int GetHashCode([DisallowNull] byte[] data)
    {
        unchecked
        {
            const int p = 16777619;
            int hash = (int)2166136261;

            for (int i = 0; i < data.Length; i++)
            {
                hash = (hash ^ data[i]) * p;
            }

            hash += hash << 13;
            hash ^= hash >> 7;
            hash += hash << 3;
            hash ^= hash >> 17;
            hash += hash << 5;
            return hash;
        }
    }
}

/// <summary>Associates a legacy true-candidates result with the solver state that produced it.</summary>
internal sealed class ResponseCacheItem
{
    private readonly object inheritanceLock = new();

    /// <summary>Gets or sets the solver request whose result was cached.</summary>
    public required Solver request { get; set; }
    /// <summary>Gets or sets the cached legacy response.</summary>
    public required BaseResponse response { get; set; }

    /// <summary>Checks inheritance while isolating the cached solver's lazily initialized state.</summary>
    /// <param name="candidate">The request solver that may inherit this cached solver.</param>
    /// <returns>Whether <paramref name="candidate"/> inherits this cached solver.</returns>
    internal bool IsInheritedBy(Solver candidate)
    {
        lock (inheritanceLock)
        {
            return candidate.IsInheritOf(request);
        }
    }
}

/// <summary>
/// Processes native and legacy solver commands independently of their transport host.
/// </summary>
public sealed class SolverCommandProcessor
{
    private readonly bool singleThreaded;
    private readonly IReadOnlyList<string>? legacyAdditionalConstraints;
    private readonly LegacyInvalidRequestBehavior legacyInvalidRequestBehavior;
    private readonly Action<LegacyCacheEvent>? legacyCacheObserver;
    private readonly object legacyCacheLock = new();
    private readonly NativeOperationRunner nativeRunner;
    private readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());
    private readonly List<ResponseCacheItem> lastTrueCandidatesResponses = [];

    /// <summary>Initializes a processor that uses the repository's standard legacy constraints.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    public SolverCommandProcessor(bool singleThreaded)
        : this(singleThreaded, null, LegacyInvalidRequestBehavior.RespondWithInvalid)
    {
    }

    /// <summary>Initializes a processor with console-supplied legacy additional constraints.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="legacyAdditionalConstraints">Optional additional legacy constraint declarations.</param>
    public SolverCommandProcessor(
        bool singleThreaded,
        IEnumerable<string>? legacyAdditionalConstraints)
        : this(singleThreaded, legacyAdditionalConstraints, LegacyInvalidRequestBehavior.RespondWithInvalid)
    {
    }

    /// <summary>Initializes a processor with explicit legacy host compatibility behavior.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="legacyAdditionalConstraints">Optional additional legacy constraint declarations.</param>
    /// <param name="legacyInvalidRequestBehavior">How unsupported legacy requests are handled.</param>
    public SolverCommandProcessor(
        bool singleThreaded,
        IEnumerable<string>? legacyAdditionalConstraints,
        LegacyInvalidRequestBehavior legacyInvalidRequestBehavior)
        : this(singleThreaded, legacyAdditionalConstraints, legacyInvalidRequestBehavior, null)
    {
    }

    /// <summary>Initializes a processor with an internal cache observer for deterministic regression tests.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="legacyAdditionalConstraints">Optional additional legacy constraint declarations.</param>
    /// <param name="legacyInvalidRequestBehavior">How unsupported legacy requests are handled.</param>
    /// <param name="legacyCacheObserver">An optional observer for cache reuse paths.</param>
    internal SolverCommandProcessor(
        bool singleThreaded,
        IEnumerable<string>? legacyAdditionalConstraints,
        LegacyInvalidRequestBehavior legacyInvalidRequestBehavior,
        Action<LegacyCacheEvent>? legacyCacheObserver)
    {
        this.singleThreaded = singleThreaded;
        this.legacyAdditionalConstraints = legacyAdditionalConstraints?.ToArray();
        this.legacyInvalidRequestBehavior = legacyInvalidRequestBehavior;
        this.legacyCacheObserver = legacyCacheObserver;
        nativeRunner = new NativeOperationRunner(singleThreaded);
    }

    /// <summary>
    /// Runs one protocol message to completion, emitting zero or more responses through the sink.
    /// Long-running commands ("count", "estimate") emit progress responses as they go.
    /// </summary>
    /// <param name="messageJson">The native or legacy request JSON.</param>
    /// <param name="sendJson">The transport-owned serialized response sink.</param>
    /// <param name="cancellationToken">The transport-owned cancellation token.</param>
    public void Handle(
        string messageJson,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageJson);
        ArgumentNullException.ThrowIfNull(sendJson);

        using JsonDocument document = JsonDocument.Parse(messageJson);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("protocolVersion", out _))
        {
            HandleNative(messageJson, sendJson, cancellationToken);
            return;
        }

        HandleLegacy(messageJson, sendJson, cancellationToken);
    }

    private void HandleNative(
        string messageJson,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        SolverRequestHeader header;
        try
        {
            header = JsonSerializer.Deserialize(
                messageJson,
                ProtocolJsonContext.Default.SolverRequestHeader)
                ?? throw new JsonException("Native solver request header was empty.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            SendNativeEnvelopeError(messageJson, sendJson, "invalidPackage", exception.Message);
            return;
        }

        if (header.ProtocolVersion != 1)
        {
            SolverResponse unsupported = new()
            {
                Kind = "error",
                ProtocolVersion = 1,
                RequestId = header.RequestId,
                DocumentRevision = header.DocumentRevision,
                SemanticRevision = header.SemanticRevision,
                SemanticHash = string.Empty,
                ContextId = header.ContextId,
                Operation = header.Operation,
                Error = new SolverErrorDto
                {
                    Code = "unsupportedVersion",
                    Message = $"Unsupported native solver protocol version {header.ProtocolVersion}.",
                },
            };
            sendJson(JsonSerializer.Serialize(unsupported, ProtocolJsonContext.Default.SolverResponse));
            return;
        }

        SolverRequest request;
        try
        {
            request = JsonSerializer.Deserialize(
                messageJson,
                ProtocolJsonContext.Default.SolverRequest)
                ?? throw new JsonException("Native solver request was empty.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            SendNativeEnvelopeError(messageJson, sendJson, "invalidPackage", exception.Message);
            return;
        }
        catch (Exception exception)
        {
            SendNativeEnvelopeError(messageJson, sendJson, "internalError", exception.Message);
            return;
        }
        nativeRunner.Run(
            request,
            response => sendJson(JsonSerializer.Serialize(
                response,
                ProtocolJsonContext.Default.SolverResponse)),
            cancellationToken);
    }

    private static void SendNativeEnvelopeError(
        string messageJson,
        Action<string> sendJson,
        string code,
        string message)
    {
        using JsonDocument document = JsonDocument.Parse(messageJson);
        JsonElement root = document.RootElement;
        SolverResponse response = new()
        {
            Kind = "error",
            ProtocolVersion = 1,
            RequestId = ReadString(root, "requestId"),
            DocumentRevision = ReadInt64(root, "documentRevision"),
            SemanticRevision = ReadInt64(root, "semanticRevision"),
            SemanticHash = string.Empty,
            ContextId = ReadString(root, "contextId"),
            Operation = ReadString(root, "operation"),
            Error = new SolverErrorDto { Code = code, Message = message },
        };
        sendJson(JsonSerializer.Serialize(response, ProtocolJsonContext.Default.SolverResponse));
    }

    private static string ReadString(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;

    private static long ReadInt64(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt64(out long value)
                ? value
                : 0;

    private void HandleLegacy(
        string messageJson,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        Message message = JsonSerializer.Deserialize(messageJson, ProtocolJsonContext.Default.Message)
            ?? throw new JsonException("Legacy solver request was empty.");

        if (message.command == "cancel")
        {
            Send(sendJson, new CanceledResponse(message.nonce));
            return;
        }

        if (message.dataType != "fpuzzles")
        {
            if (legacyInvalidRequestBehavior == LegacyInvalidRequestBehavior.RespondWithInvalid)
            {
                Send(sendJson, new InvalidResponse(message.nonce) { message = $"Unsupported dataType: {message.dataType}" });
            }
            return;
        }

        try
        {
            bool onlyGivens = message.command switch
            {
                "truecandidates" or "solve" or "check" or "count" => true,
                _ => false,
            };

            Solver solver = SolverFactory.CreateFromFPuzzles(
                message.data,
                legacyAdditionalConstraints,
                onlyGivens: onlyGivens);

            if (message.command == "truecandidates"
                && solver.customInfo.TryGetValue("ComparableData", out object? comparableDataObj)
                && comparableDataObj is byte[] cachedKey
                && TryGetCachedResponse(cachedKey, message.nonce, out BaseResponse? cached))
            {
                legacyCacheObserver?.Invoke(LegacyCacheEvent.ExactHit);
                Send(sendJson, cached!);
                return;
            }

            solver.customInfo["fpuzzlesdata"] = message.data;
            switch (message.command)
            {
                case "truecandidates":
                    SendTrueCandidates(message.nonce, solver, sendJson, cancellationToken);
                    break;
                case "solve":
                    SendSolve(message.nonce, solver, sendJson, cancellationToken);
                    break;
                case "check":
                    SendCount(message.nonce, solver, 2, sendJson, cancellationToken);
                    break;
                case "count":
                    SendCount(message.nonce, solver, 0, sendJson, cancellationToken);
                    break;
                case "estimate":
                    SendEstimate(message.nonce, solver, sendJson, cancellationToken);
                    break;
                case "solvepath":
                    SendSolvePath(message.nonce, solver, sendJson, cancellationToken);
                    break;
                case "step":
                    SendStep(message.nonce, solver, sendJson, cancellationToken);
                    break;
                default:
                    if (legacyInvalidRequestBehavior == LegacyInvalidRequestBehavior.RespondWithInvalid)
                    {
                        Send(sendJson, new InvalidResponse(message.nonce) { message = $"Unknown command: {message.command}" });
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Send(sendJson, new CanceledResponse(message.nonce));
        }
        catch (Exception e)
        {
            Send(sendJson, new InvalidResponse(message.nonce) { message = e.Message });
        }
    }

    private static bool GetBooleanOption(Solver solver, string option)
    {
        return solver.customInfo.TryGetValue(option, out object? obj) && obj is bool value && value;
    }

    private static void Send(Action<string> sendJson, BaseResponse response)
    {
        string json = response switch
        {
            CanceledResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.CanceledResponse),
            InvalidResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.InvalidResponse),
            TrueCandidatesResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.TrueCandidatesResponse),
            SolvedResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.SolvedResponse),
            CountResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.CountResponse),
            LogicalResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.LogicalResponse),
            EstimateResponse r => JsonSerializer.Serialize(r, ProtocolJsonContext.Default.EstimateResponse),
            _ => throw new NotImplementedException($"Unknown response type: {response.type}"),
        };
        sendJson(json);
    }

    private bool TryGetCachedResponse(byte[] key, int nonce, out BaseResponse? response)
    {
        lock (legacyCacheLock)
        {
            if (!trueCandidatesResponseCache.TryGetValue(key, out BaseResponse? cached))
            {
                response = null;
                return false;
            }

            response = cached switch
            {
                TrueCandidatesResponse trueCandidates => new TrueCandidatesResponse(nonce)
                {
                    solutionsPerCandidate = trueCandidates.solutionsPerCandidate,
                },
                InvalidResponse invalid => new InvalidResponse(nonce) { message = invalid.message },
                _ => throw new InvalidOperationException($"Unsupported cached response type {cached.type}."),
            };
            return true;
        }
    }

    private void SendTrueCandidatesMessage(
        BaseResponse response,
        Solver request,
        Action<string> sendJson,
        CancellationToken cancellationToken,
        byte[]? trueCandidatesKey = null)
    {
        lock (legacyCacheLock)
        {
            if (trueCandidatesKey != null)
            {
                trueCandidatesResponseCache[trueCandidatesKey] = response;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                lastTrueCandidatesResponses.Add(new() { request = request, response = response });
                // Keep only last N responses to minimize search time and memory usage
                if (lastTrueCandidatesResponses.Count > 1000)
                {
                    lastTrueCandidatesResponses.RemoveAt(0);
                }
            }
        }

        Send(sendJson, response);
    }

    /// <summary>
    /// Update solver to keep only the candidates that are present in the given response.
    /// Send an "invalid" response if the puzzle has no more solutions after this operation.
    /// </summary>
    /// <returns>Is the puzzle still valid?</returns>
    private static bool KeepCandidatesOfResponse(
        int nonce,
        Solver solver,
        BaseResponse response,
        Predicate<long> keepCandidateCondition,
        Action<string> sendJson)
    {
        if (response is InvalidResponse invalidResponse)
        {
            Send(sendJson, new InvalidResponse(nonce) { message = invalidResponse.message });
            return false;
        }

        if (response is TrueCandidatesResponse successResponse)
        {
            for (int i = 0; i < solver.HEIGHT; i++)
            {
                for (int j = 0; j < solver.WIDTH; j++)
                {
                    uint mask = 0;
                    for (int value = 1; value <= solver.MAX_VALUE; value++)
                    {
                        long candidateNumSolutions = successResponse.solutionsPerCandidate[(i * solver.WIDTH + j) * solver.MAX_VALUE + (value - 1)];
                        if (keepCandidateCondition(candidateNumSolutions))
                        {
                            mask |= SolverUtility.ValueMask(value);
                        }
                    }
                    if (solver.KeepMask(i, j, mask) == LogicResult.Invalid)
                    {
                        Send(sendJson, new InvalidResponse(nonce) { message = "No solutions found." });
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void SendTrueCandidates(
        int nonce,
        Solver solver,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        long numSolutionsCap = 1;
        if (solver.customInfo.TryGetValue("truecandidatesnumsolutions", out object? numSolutionsObj)
            && numSolutionsObj is long n)
        {
            numSolutionsCap = n;
        }
        else if (GetBooleanOption(solver, "truecandidatescolored"))
        {
            numSolutionsCap = 8; // fallback for legacy boolean
        }
        bool logical = GetBooleanOption(solver, "truecandidateslogical");

        // Save the state of the solver with the initial grid (before applying any logic to the puzzle)
        Solver request = solver.Clone(willRunNonSinglesLogic: false);

        List<ResponseCacheItem> matchingCacheItems;
        lock (legacyCacheLock)
        {
            matchingCacheItems = new(lastTrueCandidatesResponses);
        }
        matchingCacheItems = matchingCacheItems.FindAll(item =>
        {
            bool inherited = item.IsInheritedBy(request);
            if (inherited)
            {
                legacyCacheObserver?.Invoke(LegacyCacheEvent.InheritedMatch);
            }
            return inherited;
        });

        Solver? logicalSolver = null;
        if (logical)
        {
            logicalSolver = solver.Clone(willRunNonSinglesLogic: true);

            foreach (ResponseCacheItem item in matchingCacheItems)
            {
                // Use only results of logical solves
                if (!GetBooleanOption(item.request, "truecandidateslogical"))
                {
                    continue;
                }

                // Ignore results if the previous input allowed more logic types
                if ((request.DisabledLogicFlags & ~item.request.DisabledLogicFlags) != 0)
                {
                    continue;
                }

                // Remove candidates that already logically proved to have no solutions
                if (!KeepCandidatesOfResponse(nonce, logicalSolver, item.response, numSolutions => numSolutions != 0, sendJson))
                {
                    return;
                }
            }

            if (logicalSolver.ConsolidateBoard(cancellationToken: cancellationToken) == LogicResult.Invalid)
            {
                SendTrueCandidatesMessage(new InvalidResponse(nonce) { message = "No solutions found." }, request, sendJson, cancellationToken);
                return;
            }
        }

        foreach (ResponseCacheItem item in matchingCacheItems)
        {
            // Remove candidates that already proved (by logic or by brute force) to have no solutions
            if (!KeepCandidatesOfResponse(nonce, solver, item.response, numSolutions => numSolutions > 0, sendJson))
            {
                return;
            }
        }

        long[]? numSolutions = solver.TrueCandidates(
                multiThread: !singleThreaded,
                numSolutionsCap: numSolutionsCap,
                cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (numSolutions == null || numSolutions.All(candidate => candidate == 0))
        {
            SendTrueCandidatesMessage(new InvalidResponse(nonce) { message = "No solutions found." }, request, sendJson, cancellationToken);
            return;
        }

        for (int i = 0; i < numSolutions.Length; i++)
        {
            numSolutions[i] = Math.Min(numSolutions[i], numSolutionsCap);
        }

        int maxValue = solver.MAX_VALUE;
        uint[] realFlatBoard = new uint[solver.NUM_CELLS];
        for (int cellIndex = 0; cellIndex < solver.NUM_CELLS; cellIndex++)
        {
            uint mask = 0;
            for (int valIndex = 0; valIndex < maxValue; valIndex++)
            {
                if (numSolutions[cellIndex * maxValue + valIndex] > 0)
                {
                    mask |= SolverUtility.ValueMask(valIndex + 1);
                }
            }
            realFlatBoard[cellIndex] = mask;
        }

        IReadOnlyList<uint>? logicalFlat = logicalSolver?.FlatBoard;
        for (int i = 0; i < realFlatBoard.Length; i++)
        {
            uint realMask = realFlatBoard[i];
            uint logicalMask = logicalFlat != null ? logicalFlat[i] : realMask;
            for (int v = 0; v < maxValue; v++)
            {
                int solutionIndex = i * maxValue + v;
                uint valueMask = SolverUtility.ValueMask(v + 1);
                bool haveValueReal = (realMask & valueMask) != 0;
                bool haveLogicalReal = (logicalMask & valueMask) != 0;
                if (!haveValueReal && haveLogicalReal)
                {
                    numSolutions[solutionIndex] = -1;
                }
                else if (haveValueReal && numSolutionsCap == 1)
                {
                    numSolutions[solutionIndex] = 1;
                }
            }
        }

        TrueCandidatesResponse response = new(nonce) { solutionsPerCandidate = numSolutions };
        if (solver.customInfo.TryGetValue("ComparableData", out object? comparableDataObj)
            && comparableDataObj is byte[] comparableData)
        {
            SendTrueCandidatesMessage(response, request, sendJson, cancellationToken, comparableData);
        }
        else
        {
            SendTrueCandidatesMessage(response, request, sendJson, cancellationToken);
        }
    }

    private void SendSolve(
        int nonce,
        Solver solver,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        if (!solver.FindSolution(multiThread: !singleThreaded, isRandom: true, cancellationToken: cancellationToken))
        {
            Send(sendJson, new InvalidResponse(nonce) { message = "No solutions found." });
        }
        else
        {
            Send(sendJson, new SolvedResponse(nonce)
            {
                solution = solver.FlatBoard.Select(SolverUtility.GetValue).ToArray()
            });
        }
    }

    private void SendCount(
        int nonce,
        Solver solver,
        long maxSolutions,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        long numSolutions = solver.CountSolutions(maxSolutions, multiThread: !singleThreaded, cancellationToken: cancellationToken, progressEvent: (count) =>
        {
            Send(sendJson, new CountResponse(nonce) { count = count, inProgress = true });
        });
        if (!cancellationToken.IsCancellationRequested)
        {
            Send(sendJson, new CountResponse(nonce) { count = numSolutions, inProgress = false });
        }
    }

    private static StringBuilder StepsDescription(List<LogicalStepDesc> logicalStepDescs)
    {
        StringBuilder sb = new();
        foreach (LogicalStepDesc step in logicalStepDescs)
        {
            _ = sb.AppendLine(step.ToString());
        }
        return sb;
    }

    private static void SendSolvePath(
        int nonce,
        Solver solver,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.ConsolidateBoard(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs), sendJson);
    }

    private static void SendStep(
        int nonce,
        Solver solver,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        if (solver.customInfo["OriginalCenterMarks"] is uint[,] originalCenterMarks)
        {
            BoardView board = solver.Board;
            for (int i = 0; i < solver.HEIGHT; i++)
            {
                for (int j = 0; j < solver.WIDTH; j++)
                {
                    uint origMask = originalCenterMarks[i, j] & ~SolverUtility.valueSetMask;
                    uint newMask = board[i, j] & ~SolverUtility.valueSetMask;
                    if (origMask != newMask)
                    {
                        StringBuilder sb = new();
                        _ = sb.Append("Initial candidates.");
                        SendLogicResponse(nonce, solver, LogicResult.Changed, sb, sendJson);
                        return;
                    }
                }
            }
        }

        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.StepLogic(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs), sendJson);
    }

    private static void SendLogicResponse(
        int nonce,
        Solver solver,
        LogicResult logicResult,
        StringBuilder description,
        Action<string> sendJson)
    {
        if (!description.ToString().EndsWith(Environment.NewLine))
        {
            _ = description.AppendLine();
        }

        if (logicResult == LogicResult.Invalid)
        {
            _ = description.AppendLine("Board is invalid!");
        }
        else if (logicResult == LogicResult.None)
        {
            _ = description.AppendLine("No logical steps found.");
        }

        IReadOnlyList<uint> flatBoard = solver.FlatBoard;
        LogicalCell[] cells = new LogicalCell[flatBoard.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            uint mask = flatBoard[i];
            if (SolverUtility.IsValueSet(mask))
            {
                cells[i] = new() { value = SolverUtility.GetValue(mask) };
            }
            else
            {
                List<int> candidates = [];
                for (int v = 1; v <= solver.MAX_VALUE; v++)
                {
                    uint valueMask = SolverUtility.ValueMask(v);
                    if ((mask & valueMask) != 0)
                    {
                        candidates.Add(v);
                    }
                }
                cells[i] = new() { value = 0, candidates = candidates.ToArray() };
            }
        }
        Send(sendJson, new LogicalResponse(nonce)
        {
            cells = cells,
            message = description.ToString().TrimStart(),
            isValid = logicResult != LogicResult.Invalid
        });
    }

    private void SendEstimate(
        int nonce,
        Solver solver,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
        const double z95 = 1.96;
        solver.EstimateSolutions(
            numIterations: 0, // 0 = go forever (until cancel)
            progressEvent: (progressData) =>
            {
                double estimate = progressData.estimate;
                double stderr = progressData.stderr;
                long iterations = progressData.iterations;
                double lower = estimate - z95 * stderr;
                double upper = estimate + z95 * stderr;
                double relErrPercent = estimate != 0 ? 100.0 * (z95 * stderr) / estimate : 0.0;
                if (!cancellationToken.IsCancellationRequested)
                {
                    Send(sendJson, new EstimateResponse(nonce)
                    {
                        estimate = estimate,
                        stderr = stderr,
                        iterations = iterations,
                        ci95_lower = lower,
                        ci95_upper = upper,
                        relErrPercent = relErrPercent
                    });
                }
            },
            multiThread: !singleThreaded,
            cancellationToken: cancellationToken);
    }
}