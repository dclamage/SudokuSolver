﻿using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SudokuSolver;
using WatsonWebsocket;

namespace SudokuSolverConsole;

class Message
{
    public int nonce { get; set; }
    public string command { get; set; }
    public string dataType { get; set; }
    public string data { get; set; }
};

// If adding a new BaseResponse, be sure to also add it to:
// - WebsocketsJsonContext
// - SendMessage
class BaseResponse
{
    public BaseResponse(int nonce, string type)
    {
        this.nonce = nonce;
        this.type = type;
    }

    public int nonce { get; set; }
    public string type { get; set; }
}

class CanceledResponse : BaseResponse
{
    public CanceledResponse(int nonce) : base(nonce, "canceled") { }
}

class InvalidResponse : BaseResponse
{
    public InvalidResponse(int nonce) : base(nonce, "invalid") { }
    public string message { get; set; }
}

class TrueCandidatesResponse : BaseResponse
{
    public TrueCandidatesResponse(int nonce) : base(nonce, "truecandidates") { }
    public int[] solutionsPerCandidate { get; set; }
}

class SolvedResponse : BaseResponse
{
    public SolvedResponse(int nonce) : base(nonce, "solved") { }
    public int[] solution { get; set; }
}

class CountResponse : BaseResponse
{
    public CountResponse(int nonce) : base(nonce, "count") { }
    public ulong count { get; set; }
    public bool inProgress { get; set; }
}

class LogicalCell
{
    public int value { get; set; }
    public int[] candidates { get; set; }
}

class LogicalResponse : BaseResponse
{
    public LogicalResponse(int nonce) : base(nonce, "logical") { }
    public LogicalCell[] cells { get; set; }
    public string message { get; set; }
    public bool isValid { get; set; }
}

[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(CanceledResponse))]
[JsonSerializable(typeof(InvalidResponse))]
[JsonSerializable(typeof(TrueCandidatesResponse))]
[JsonSerializable(typeof(SolvedResponse))]
[JsonSerializable(typeof(CountResponse))]
[JsonSerializable(typeof(LogicalResponse))]
partial class WebsocketsJsonContext : JsonSerializerContext
{
}

class WebsocketListener : IDisposable
{
    private WatsonWsServer server;
    private readonly object serverLock = new();
    private readonly Dictionary<string, CancellationTokenSource> cancellationTokenMap = new();
    private readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());
    private List<string> additionalConstraints;

    public async Task Listen(string host, int port, IEnumerable<string> additionalConstraints = null)
    {
        if (server != null)
        {
            throw new InvalidOperationException("Server already listening!");
        }

        this.additionalConstraints = additionalConstraints?.ToList();

        server = new(host, port, false);
        server.ClientConnected += (_, args) => ClientConnected(args);
        server.ClientDisconnected += (_, args) => ClientDisconnected(args);
        server.MessageReceived += (_, args) => MessageReceived(args);
        await server.StartAsync();
        Console.WriteLine($"Accepting connections from {host}:{port}");
    }

    void ClientConnected(ClientConnectedEventArgs args)
    {
        Console.WriteLine("Client connected: " + args.IpPort);
    }

    void ClientDisconnected(ClientDisconnectedEventArgs args)
    {
        Console.WriteLine("Client disconnected: " + args.IpPort);
        if (cancellationTokenMap.TryGetValue(args.IpPort, out var cancellationToken))
        {
            cancellationToken.Cancel();
            cancellationTokenMap.Remove(args.IpPort);
        }
    }

    void MessageReceived(MessageReceivedEventArgs args)
    {
        string messageString = Encoding.UTF8.GetString(args.Data);
        Message message = JsonSerializer.Deserialize(messageString, WebsocketsJsonContext.Default.Message);

        if (cancellationTokenMap.TryGetValue(args.IpPort, out var cancellationTokenSource))
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
            var cancellationToken = cancellationTokenSource.Token;

            string ipPort = args.IpPort;
            Task.Run(() =>
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

                    Solver solver = SolverFactory.CreateFromFPuzzles(message.data, this.additionalConstraints, onlyGivens: onlyGivens);
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
                        case "solvepath":
                            SendSolvePath(ipPort, message.nonce, solver);
                            break;
                        case "step":
                            SendStep(ipPort, message.nonce, solver);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                        // Do nothing, no response expected
                    }
                catch (Exception e)
                {
                    SendMessage(ipPort, new InvalidResponse(message.nonce) { message = e.Message });
                }
            }, cancellationToken);
        }
    }

    bool GetBooleanOption(Solver solver, string option)
    {
        if (solver.customInfo.TryGetValue(option, out object obj) && obj is bool value)
        {
            return value;
        }
        return false;
    }

    void SendMessage(string ipPort, BaseResponse response, byte[] trueCandidatesKey = null)
    {
        string json = response switch
        {
            CanceledResponse canceledResponse => JsonSerializer.Serialize(canceledResponse, WebsocketsJsonContext.Default.CanceledResponse),
            InvalidResponse invalidResponse => JsonSerializer.Serialize(invalidResponse, WebsocketsJsonContext.Default.InvalidResponse),
            TrueCandidatesResponse trueCandidatesResponse => JsonSerializer.Serialize(trueCandidatesResponse, WebsocketsJsonContext.Default.TrueCandidatesResponse),
            SolvedResponse solvedResponse => JsonSerializer.Serialize(solvedResponse, WebsocketsJsonContext.Default.SolvedResponse),
            CountResponse countResponse => JsonSerializer.Serialize(countResponse, WebsocketsJsonContext.Default.CountResponse),
            LogicalResponse logicalResponse => JsonSerializer.Serialize(logicalResponse, WebsocketsJsonContext.Default.LogicalResponse),
            _ => throw new NotImplementedException($"Unknown response type: {response.type}"),
        };
        lock (serverLock)
        {
            if (trueCandidatesKey != null)
            {
                trueCandidatesResponseCache[trueCandidatesKey] = response;
            }
            server.SendAsync(ipPort, json);
        }
    }

    void SendTrueCandidates(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
    {
        bool colored = GetBooleanOption(solver, "truecandidatescolored");
        bool logical = GetBooleanOption(solver, "truecandidateslogical");

        Solver logicalSolver = null;
        if (logical)
        {
            logicalSolver = solver.Clone(willRunNonSinglesLogic: true);
            if (logicalSolver.ConsolidateBoard() == LogicResult.Invalid)
            {
                SendMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." });
                return;
            }
        }

        int totalCandidates = solver.HEIGHT * solver.WIDTH * solver.MAX_VALUE;
        int[] numSolutions = colored ? new int[totalCandidates] : null;
        if (!solver.FillRealCandidates(multiThread: false, numSolutions: numSolutions, cancellationToken: cancellationToken))
        {
            SendMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." });
            return;
        }

        if (numSolutions == null)
        {
            numSolutions = new int[totalCandidates];
        }

        int maxValue = solver.MAX_VALUE;
        var realFlatBoard = solver.FlatBoard;
        var logicalFlat = logicalSolver?.FlatBoard;
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
                else if (haveValueReal && !colored)
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
                SendMessage(ipPort, response, comparableData);
            }
            else
            {
                SendMessage(ipPort, response);
            }
        }
    }

    void SendSolve(string ipPort, int nonce, Solver solver, CancellationToken cancellationToken)
    {
        if (!solver.FindSolution(multiThread: true, isRandom: true, cancellationToken: cancellationToken))
        {
            SendMessage(ipPort, new InvalidResponse(nonce) { message = "No solutions found." });
        }
        else
        {
            SendMessage(ipPort, new SolvedResponse(nonce)
            {
                solution = solver.FlatBoard.Select(mask => SolverUtility.GetValue(mask)).ToArray()
            });
        }
    }

    void SendCount(string ipPort, int nonce, Solver solver, ulong maxSolutions, CancellationToken cancellationToken)
    {
        ulong numSolutions = solver.CountSolutions(maxSolutions, true, cancellationToken: cancellationToken, progressEvent: (count) =>
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
        foreach (var step in logicalStepDescs)
        {
            sb.AppendLine(step.ToString());
        }
        return sb;
    }

    void SendSolvePath(string ipPort, int nonce, Solver solver)
    {
        List<LogicalStepDesc> logicalStepDescs = new();
        var logicResult = solver.ConsolidateBoard(logicalStepDescs);
        SendLogicResponse(ipPort, nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    void SendStep(string ipPort, int nonce, Solver solver)
    {
        if (solver.customInfo["OriginalCenterMarks"] is uint[,] originalCenterMarks)
        {
            uint[,] board = solver.Board;
            for (int i = 0; i < solver.HEIGHT; i++)
            {
                for (int j = 0; j < solver.WIDTH; j++)
                {
                    uint origMask = originalCenterMarks[i, j] & ~SolverUtility.valueSetMask;
                    uint newMask = board[i, j] & ~SolverUtility.valueSetMask;
                    if (origMask != newMask)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append("Initial candidates.");
                        SendLogicResponse(ipPort, nonce, solver, LogicResult.Changed, sb);
                        return;
                    }
                }
            }
        }

        List<LogicalStepDesc> logicalStepDescs = new();
        var logicResult = solver.StepLogic(logicalStepDescs);
        SendLogicResponse(ipPort, nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    void SendLogicResponse(string ipPort, int nonce, Solver solver, LogicResult logicResult, StringBuilder description)
    {
        if (!description.ToString().EndsWith(Environment.NewLine))
        {
            description.AppendLine();
        }

        if (logicResult == LogicResult.Invalid)
        {
            description.AppendLine("Board is invalid!");
        }
        else if (logicResult == LogicResult.None)
        {
            description.AppendLine("No logical steps found.");
        }

        var flatBoard = solver.FlatBoard;
        LogicalCell[] cells = new LogicalCell[flatBoard.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            uint mask = flatBoard[i];
            if (SolverUtility.IsValueSet(mask))
            {
                cells[i] = new() { value = SolverUtility.GetValue(mask) };
            }
            else
            {
                List<int> candidates = new();
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

    public void Dispose()
    {
        ((IDisposable)server).Dispose();
    }
}
