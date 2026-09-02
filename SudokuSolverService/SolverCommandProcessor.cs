using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using SudokuSolver;
using SudokuSolverService.Protocol;

namespace SudokuSolverService;

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

internal sealed class ResponseCacheItem
{
    /// <summary>Gets or sets the solver request whose result was cached.</summary>
    public required Solver request { get; set; }
    /// <summary>Gets or sets the cached legacy response.</summary>
    public required BaseResponse response { get; set; }
}

/// <summary>
/// Processes native and legacy solver commands independently of their transport host.
/// </summary>
public sealed class SolverCommandProcessor
{
    private readonly bool singleThreaded;
    private readonly IReadOnlyList<string>? legacyAdditionalConstraints;
    private readonly object legacyExecutionLock = new();
    private readonly NativeOperationRunner nativeRunner;
    private readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());
    private readonly List<ResponseCacheItem> lastTrueCandidatesResponses = [];
    private Action<string>? sendJson;

    /// <summary>Initializes a processor that uses the repository's standard legacy constraints.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    public SolverCommandProcessor(bool singleThreaded)
        : this(singleThreaded, null)
    {
    }

    /// <summary>Initializes a processor with console-supplied legacy additional constraints.</summary>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <param name="legacyAdditionalConstraints">Optional additional legacy constraint declarations.</param>
    public SolverCommandProcessor(
        bool singleThreaded,
        IEnumerable<string>? legacyAdditionalConstraints)
    {
        this.singleThreaded = singleThreaded;
        this.legacyAdditionalConstraints = legacyAdditionalConstraints?.ToArray();
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

        lock (legacyExecutionLock)
        {
            this.sendJson = sendJson;
            try
            {
                HandleLegacy(messageJson, cancellationToken);
            }
            finally
            {
                this.sendJson = null;
            }
        }
    }

    private void HandleNative(
        string messageJson,
        Action<string> sendJson,
        CancellationToken cancellationToken)
    {
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

    private void HandleLegacy(string messageJson, CancellationToken cancellationToken)
    {
        Message message = JsonSerializer.Deserialize(messageJson, ProtocolJsonContext.Default.Message)
            ?? throw new JsonException("Legacy solver request was empty.");

        if (message.command == "cancel")
        {
            Send(new CanceledResponse(message.nonce));
            return;
        }

        if (message.dataType != "fpuzzles")
        {
            Send(new InvalidResponse(message.nonce) { message = $"Unsupported dataType: {message.dataType}" });
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
                && trueCandidatesResponseCache.TryGetValue(cachedKey, out BaseResponse? cached))
            {
                cached.nonce = message.nonce;
                Send(cached);
                return;
            }

            solver.customInfo["fpuzzlesdata"] = message.data;
            switch (message.command)
            {
                case "truecandidates":
                    SendTrueCandidates(message.nonce, solver, cancellationToken);
                    break;
                case "solve":
                    SendSolve(message.nonce, solver, cancellationToken);
                    break;
                case "check":
                    SendCount(message.nonce, solver, 2, cancellationToken);
                    break;
                case "count":
                    SendCount(message.nonce, solver, 0, cancellationToken);
                    break;
                case "estimate":
                    SendEstimate(message.nonce, solver, cancellationToken);
                    break;
                case "solvepath":
                    SendSolvePath(message.nonce, solver, cancellationToken);
                    break;
                case "step":
                    SendStep(message.nonce, solver, cancellationToken);
                    break;
                default:
                    Send(new InvalidResponse(message.nonce) { message = $"Unknown command: {message.command}" });
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Send(new CanceledResponse(message.nonce));
        }
        catch (Exception e)
        {
            Send(new InvalidResponse(message.nonce) { message = e.Message });
        }
    }

    private static bool GetBooleanOption(Solver solver, string option)
    {
        return solver.customInfo.TryGetValue(option, out object? obj) && obj is bool value && value;
    }

    private void Send(BaseResponse response)
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
        sendJson!(json);
    }

    private void SendTrueCandidatesMessage(
        BaseResponse response,
        Solver request,
        CancellationToken cancellationToken,
        byte[]? trueCandidatesKey = null)
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

        Send(response);
    }

    /// <summary>
    /// Update solver to keep only the candidates that are present in the given response.
    /// Send an "invalid" response if the puzzle has no more solutions after this operation.
    /// </summary>
    /// <returns>Is the puzzle still valid?</returns>
    private bool KeepCandidatesOfResponse(int nonce, Solver solver, BaseResponse response, Predicate<long> keepCandidateCondition)
    {
        if (response is InvalidResponse invalidResponse)
        {
            Send(new InvalidResponse(nonce) { message = invalidResponse.message });
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
                        Send(new InvalidResponse(nonce) { message = "No solutions found." });
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void SendTrueCandidates(int nonce, Solver solver, CancellationToken cancellationToken)
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

        List<ResponseCacheItem> matchingCacheItems = new(lastTrueCandidatesResponses);
        matchingCacheItems = matchingCacheItems.FindAll(item => request.IsInheritOf(item.request));

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
                if (!KeepCandidatesOfResponse(nonce, logicalSolver, item.response, numSolutions => numSolutions != 0))
                {
                    return;
                }
            }

            if (logicalSolver.ConsolidateBoard(cancellationToken: cancellationToken) == LogicResult.Invalid)
            {
                SendTrueCandidatesMessage(new InvalidResponse(nonce) { message = "No solutions found." }, request, cancellationToken);
                return;
            }
        }

        foreach (ResponseCacheItem item in matchingCacheItems)
        {
            // Remove candidates that already proved (by logic or by brute force) to have no solutions
            if (!KeepCandidatesOfResponse(nonce, solver, item.response, numSolutions => numSolutions > 0))
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
            SendTrueCandidatesMessage(new InvalidResponse(nonce) { message = "No solutions found." }, request, cancellationToken);
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
            SendTrueCandidatesMessage(response, request, cancellationToken, comparableData);
        }
        else
        {
            SendTrueCandidatesMessage(response, request, cancellationToken);
        }
    }

    private void SendSolve(int nonce, Solver solver, CancellationToken cancellationToken)
    {
        if (!solver.FindSolution(multiThread: !singleThreaded, isRandom: true, cancellationToken: cancellationToken))
        {
            Send(new InvalidResponse(nonce) { message = "No solutions found." });
        }
        else
        {
            Send(new SolvedResponse(nonce)
            {
                solution = solver.FlatBoard.Select(SolverUtility.GetValue).ToArray()
            });
        }
    }

    private void SendCount(int nonce, Solver solver, long maxSolutions, CancellationToken cancellationToken)
    {
        long numSolutions = solver.CountSolutions(maxSolutions, multiThread: !singleThreaded, cancellationToken: cancellationToken, progressEvent: (count) =>
        {
            Send(new CountResponse(nonce) { count = count, inProgress = true });
        });
        if (!cancellationToken.IsCancellationRequested)
        {
            Send(new CountResponse(nonce) { count = numSolutions, inProgress = false });
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

    private void SendSolvePath(int nonce, Solver solver, CancellationToken cancellationToken)
    {
        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.ConsolidateBoard(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private void SendStep(int nonce, Solver solver, CancellationToken cancellationToken)
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
                        SendLogicResponse(nonce, solver, LogicResult.Changed, sb);
                        return;
                    }
                }
            }
        }

        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.StepLogic(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private void SendLogicResponse(int nonce, Solver solver, LogicResult logicResult, StringBuilder description)
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
        Send(new LogicalResponse(nonce)
        {
            cells = cells,
            message = description.ToString().TrimStart(),
            isValid = logicResult != LogicResult.Invalid
        });
    }

    private void SendEstimate(int nonce, Solver solver, CancellationToken cancellationToken)
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
                    Send(new EstimateResponse(nonce)
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
