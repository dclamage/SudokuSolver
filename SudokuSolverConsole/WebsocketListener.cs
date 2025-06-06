﻿using SudokuSolver;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WatsonWebsocket;

namespace SudokuSolverConsole;

#pragma warning disable IDE1006 // Naming Styles
internal class Message
{
    public int nonce { get; set; }
    public string command { get; set; }
    public string dataType { get; set; }
    public string data { get; set; }
};

// If adding a new BaseResponse, be sure to also add it to:
// - WebsocketsJsonContext
// - SendMessage
internal class BaseResponse(int nonce, string type)
{
    public int nonce { get; set; } = nonce;
    public string type { get; set; } = type;
}

internal class CanceledResponse(int nonce) : BaseResponse(nonce, "canceled")
{
}

internal class InvalidResponse(int nonce) : BaseResponse(nonce, "invalid")
{
    public string message { get; set; }
}

internal class TrueCandidatesResponse(int nonce) : BaseResponse(nonce, "truecandidates")
{
    public long[] solutionsPerCandidate { get; set; }
}

internal class SolvedResponse(int nonce) : BaseResponse(nonce, "solved")
{
    public int[] solution { get; set; }
}

internal class CountResponse(int nonce) : BaseResponse(nonce, "count")
{
    public long count { get; set; }
    public bool inProgress { get; set; }
}

internal class EstimateResponse(int nonce) : BaseResponse(nonce, "estimate")
{
    public double estimate { get; set; }
    public double stderr { get; set; }
    public long iterations { get; set; }
    public double ci95_lower { get; set; }
    public double ci95_upper { get; set; }
    public double relErrPercent { get; set; }
}

internal class ResponseCacheItem
{
    public Solver request { get; set; }
    public BaseResponse response { get; set; }
}

internal class LogicalCell
{
    public int value { get; set; }
    public int[] candidates { get; set; }
}

internal class LogicalResponse(int nonce) : BaseResponse(nonce, "logical")
{
    public LogicalCell[] cells { get; set; }
    public string message { get; set; }
    public bool isValid { get; set; }
}
#pragma warning restore IDE1006 // Naming Styles

[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(CanceledResponse))]
[JsonSerializable(typeof(InvalidResponse))]
[JsonSerializable(typeof(TrueCandidatesResponse))]
[JsonSerializable(typeof(SolvedResponse))]
[JsonSerializable(typeof(CountResponse))]
[JsonSerializable(typeof(LogicalResponse))]
[JsonSerializable(typeof(EstimateResponse))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
)]
internal partial class WebsocketsJsonContext : JsonSerializerContext
{
}

internal class WebsocketListener : IDisposable
{
    private WatsonWsServer server;
    private readonly object serverLock = new();
    private readonly Dictionary<string, CancellationTokenSource> cancellationTokenMap = [];
    private readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());
    private readonly List<ResponseCacheItem> lastTrueCandidatesResponses = [];
    private List<string> additionalConstraints;
    private bool verboseLogs = false;
    private bool singleThreaded = false;

    public async Task Listen(string host, int port, IEnumerable<string> additionalConstraints = null, bool verboseLogs = false, bool singleThreaded = false)
    {
        if (server != null)
        {
            throw new InvalidOperationException("Server already listening!");
        }

        this.additionalConstraints = additionalConstraints?.ToList();
        this.verboseLogs = verboseLogs;
        this.singleThreaded = singleThreaded;

        server = new(host, port, false);
        server.ClientConnected += (_, args) => ClientConnected(args);
        server.ClientDisconnected += (_, args) => ClientDisconnected(args);
        server.MessageReceived += (_, args) => MessageReceived(args);
        await server.StartAsync();
        Console.WriteLine($"Accepting connections from {host}:{port}");
    }

    private void ClientConnected(ClientConnectedEventArgs args)
    {
        Console.WriteLine("Client connected: " + args.IpPort);
    }

    private void ClientDisconnected(ClientDisconnectedEventArgs args)
    {
        Console.WriteLine("Client disconnected: " + args.IpPort);
        if (cancellationTokenMap.TryGetValue(args.IpPort, out CancellationTokenSource cancellationToken))
        {
            cancellationToken.Cancel();
            _ = cancellationTokenMap.Remove(args.IpPort);
        }
    }

    private void MessageReceived(MessageReceivedEventArgs args)
    {
        string messageString = Encoding.UTF8.GetString(args.Data);
        Message message = JsonSerializer.Deserialize(messageString, WebsocketsJsonContext.Default.Message);

        if (cancellationTokenMap.TryGetValue(args.IpPort, out CancellationTokenSource cancellationTokenSource))
        {
            cancellationTokenSource.Cancel();
        }

        if (message.command == "cancel")
        {
            SendMessage(args.IpPort, new CanceledResponse(message.nonce));
            return;
        }

        if (message.dataType == "fpuzzles")
        {
            cancellationTokenSource = cancellationTokenMap[args.IpPort] = new();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            string ipPort = args.IpPort;
            _ = Task.Run(() =>
            {
                try
                {
                    bool onlyGivens = false;
                    switch (message.command)
                    {
                        case "truecandidates":
                        case "solve":
                        case "check":
                        case "count":
                            onlyGivens = true;
                            break;
                    }

                    Solver solver = SolverFactory.CreateFromFPuzzles(message.data, additionalConstraints, onlyGivens: onlyGivens);
                    if (message.command == "truecandidates")
                    {
                        if (solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj) && comparableDataObj is byte[] comparableData)
                        {
                            lock (serverLock)
                            {
                                if (trueCandidatesResponseCache.TryGetValue(comparableData, out BaseResponse response))
                                {
                                    response.nonce = message.nonce;
                                    SendMessage(ipPort, response);
                                    return;
                                }
                            }
                        }
                    }

                    solver.customInfo["fpuzzlesdata"] = message.data;
                    switch (message.command)
                    {
                        case "truecandidates":
                            SendTrueCandidates(ipPort, message.nonce, solver, cancellationToken);
                            break;
                        case "solve":
                            SendSolve(ipPort, message.nonce, solver, cancellationToken);
                            break;
                        case "check":
                            SendCount(ipPort, message.nonce, solver, 2, cancellationToken);
                            break;
                        case "count":
                            SendCount(ipPort, message.nonce, solver, 0, cancellationToken);
                            break;
                        case "estimate":
                            SendEstimate(ipPort, message.nonce, solver, cancellationToken);
                            break;
                        case "solvepath":
                            SendSolvePath(ipPort, message.nonce, solver, cancellationToken);
                            break;
                        case "step":
                            SendStep(ipPort, message.nonce, solver, cancellationToken);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    SendMessage(ipPort, new CanceledResponse(message.nonce));
                }
                catch (Exception e)
                {
                    if (verboseLogs)
                    {
                        Console.WriteLine(e);
                    }
                    SendMessage(ipPort, new InvalidResponse(message.nonce) { message = e.Message });
                }
            }, cancellationToken);
        }
    }

    private bool GetBooleanOption(Solver solver, string option)
    {
        return solver.customInfo.TryGetValue(option, out object obj) && obj is bool value && value;
    }

    private void SendMessage(string ipPort, BaseResponse response)
    {
        string json = response switch
        {
            CanceledResponse canceledResponse => JsonSerializer.Serialize(canceledResponse, WebsocketsJsonContext.Default.CanceledResponse),
            InvalidResponse invalidResponse => JsonSerializer.Serialize(invalidResponse, WebsocketsJsonContext.Default.InvalidResponse),
            TrueCandidatesResponse trueCandidatesResponse => JsonSerializer.Serialize(trueCandidatesResponse, WebsocketsJsonContext.Default.TrueCandidatesResponse),
            SolvedResponse solvedResponse => JsonSerializer.Serialize(solvedResponse, WebsocketsJsonContext.Default.SolvedResponse),
            CountResponse countResponse => JsonSerializer.Serialize(countResponse, WebsocketsJsonContext.Default.CountResponse),
            LogicalResponse logicalResponse => JsonSerializer.Serialize(logicalResponse, WebsocketsJsonContext.Default.LogicalResponse),
            EstimateResponse estimateResponse => JsonSerializer.Serialize(estimateResponse, WebsocketsJsonContext.Default.EstimateResponse),
            _ => throw new NotImplementedException($"Unknown response type: {response.type}"),
        };
        lock (serverLock)
        {
            _ = server.SendAsync(ipPort, json);
        }
    }

    private void SendTrueCandidatesMessage(string ipPort, BaseResponse response, Solver request, CancellationToken cancellationToken, byte[] trueCandidatesKey = null)
    {
        lock (serverLock)
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

            SendMessage(ipPort, response);
        }
    }

    /// <summary>
    /// Update solver to keep only the candidates that are present in the given response.
    /// Send an "invalid" response if the puzzle has no more solutions after this operation.
    /// </summary>
    /// <param name="ipPort">IP port to send the response message</param>
    /// <param name="nonce">Nonce of the response message</param>
    /// <param name="solver">Solver to update</param>
    /// <param name="response">Response of a previous "true candidates" request</param>
    /// <param name="keepCandidateCondition">A callback that takes the number of solutions for a candidate and returns whether the candidate should be kept in the solver</param>
    /// <returns>Is the puzzle still valid?</returns>
    private bool KeepCandidatesOfResponse(string ipPort, int nonce, Solver solver, BaseResponse response, Predicate<long> keepCandidateCondition)
    {
        if (response is InvalidResponse invalidResponse)
        {
            SendMessage(ipPort, new InvalidResponse(nonce) { message = invalidResponse.message });
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
                        SendMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." });
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void SendTrueCandidates(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
    {
        // Accepts an integer option for number of solutions to cap (was previously 'truecandidatescolored' boolean)
        long numSolutionsCap = 1;
        if (solver.customInfo.TryGetValue("truecandidatesnumsolutions", out object numSolutionsObj) && numSolutionsObj is long n)
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
        lock (serverLock)
        {
            // Only clone the list in the lock, filter it outside the lock
            matchingCacheItems = new(lastTrueCandidatesResponses);
        }
        matchingCacheItems = matchingCacheItems.FindAll(item => request.IsInheritOf(item.request));

        Solver logicalSolver = null;
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
                if (!KeepCandidatesOfResponse(ipPort, nonce, logicalSolver, item.response, numSolutions => numSolutions != 0))
                {
                    return;
                }
            }

            if (logicalSolver.ConsolidateBoard(cancellationToken: cancellationToken) == LogicResult.Invalid)
            {
                SendTrueCandidatesMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." }, request, cancellationToken);
                return;
            }
        }

        foreach (ResponseCacheItem item in matchingCacheItems)
        {
            // Remove candidates that already proved (by logic or by brute force) to have no solutions
            if (!KeepCandidatesOfResponse(ipPort, nonce, solver, item.response, numSolutions => numSolutions > 0))
            {
                return;
            }
        }

        long[] numSolutions = solver.TrueCandidates(
                multiThread: !singleThreaded,
                numSolutionsCap: numSolutionsCap,
                cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (numSolutions == null || numSolutions.All(candidate => candidate == 0))
        {
            SendTrueCandidatesMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." }, request, cancellationToken);
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

        IReadOnlyList<uint> logicalFlat = logicalSolver?.FlatBoard;
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
        lock (serverLock)
        {
            if (solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj) && comparableDataObj is byte[] comparableData)
            {
                SendTrueCandidatesMessage(ipPort, response, request, cancellationToken, comparableData);
            }
            else
            {
                SendTrueCandidatesMessage(ipPort, response, request, cancellationToken);
            }
        }
    }

    private void SendSolve(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
    {
        if (!solver.FindSolution(multiThread: !singleThreaded, isRandom: true, cancellationToken: cancellationToken))
        {
            SendMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." });
        }
        else
        {
            SendMessage(ipPort, new SolvedResponse(nonce)
            {
                solution = solver.FlatBoard.Select(SolverUtility.GetValue).ToArray()
            });
        }
    }

    private void SendCount(string ipPort, int nonce, Solver solver, long maxSolutions, CancellationToken cancellationToken)
    {
        long numSolutions = solver.CountSolutions(maxSolutions, multiThread: !singleThreaded, cancellationToken: cancellationToken, progressEvent: (count) =>
        {
            SendMessage(ipPort, new CountResponse(nonce) { count = count, inProgress = true });
        });
        if (!cancellationToken.IsCancellationRequested)
        {
            SendMessage(ipPort, new CountResponse(nonce) { count = numSolutions, inProgress = false });
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

    private void SendSolvePath(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
    {
        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.ConsolidateBoard(logicalStepDescs, cancellationToken);
        SendLogicResponse(ipPort, nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private void SendStep(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
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
                        SendLogicResponse(ipPort, nonce, solver, LogicResult.Changed, sb);
                        return;
                    }
                }
            }
        }

        List<LogicalStepDesc> logicalStepDescs = [];
        LogicResult logicResult = solver.StepLogic(logicalStepDescs, cancellationToken);
        SendLogicResponse(ipPort, nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private void SendLogicResponse(string ipPort, int nonce, Solver solver, LogicResult logicResult, StringBuilder description)
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
        SendMessage(ipPort, new LogicalResponse(nonce)
        {
            cells = cells,
            message = description.ToString().TrimStart(),
            isValid = logicResult != LogicResult.Invalid
        });
    }

    private void SendEstimate(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
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
                    SendMessage(ipPort, new EstimateResponse(nonce)
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

    public void Dispose()
    {
        ((IDisposable)server).Dispose();
    }
}
