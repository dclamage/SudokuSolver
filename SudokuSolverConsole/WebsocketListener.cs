using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WatsonWebsocket;
using SudokuSolver;
using System.Threading;

namespace SudokuSolverConsole
{
    record Message(int nonce, string command, string dataType, string data);

    record BaseResponse(int nonce, string type);

    record CanceledResponse(int nonce) : BaseResponse(nonce, "canceled");

    record InvalidResponse(int nonce, string message) : BaseResponse(nonce, "invalid");

    record TrueCandidatesResponse(int nonce, int[] solutionsPerCandidate) : BaseResponse(nonce, "truecandidates");

    record SolvedResponse(int nonce, int[] solution) : BaseResponse(nonce, "solved");

    record CountResponse(int nonce, ulong count, bool inProgress) : BaseResponse(nonce, "count");

    record LogicalCell(int value, int[] candidates);
    record LogicalResponse(int nonce, LogicalCell[] cells, string message, bool isValid) : BaseResponse(nonce, "logical");

    class WebsocketListener : IDisposable
    {
        private WatsonWsServer server;
        private readonly object serverLock = new();
        private readonly Dictionary<string, CancellationTokenSource> cancellationTokenMap = new();
        private readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());

        public async Task Listen(string host, int port)
        {
            if (server != null)
            {
                throw new InvalidOperationException("Server already listening!");
            }

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
            Message message = JsonSerializer.Deserialize<Message>(messageString);

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

                        Solver solver = SolverFactory.CreateFromFPuzzles(message.data, onlyGivens: onlyGivens);
                        if (message.command == "truecandidates")
                        {
                            if (solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj) && comparableDataObj is byte[] comparableData)
                            {
                                lock (serverLock)
                                {
                                    if (trueCandidatesResponseCache.TryGetValue(comparableData, out BaseResponse response))
                                    {
                                        SendMessage(ipPort, response with { nonce = message.nonce });
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
                        SendMessage(ipPort, new InvalidResponse(message.nonce, e.Message));
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
            string json = JsonSerializer.Serialize(response, response.GetType());
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
                logicalSolver = solver.Clone();
                if (logicalSolver.ConsolidateBoard() == LogicResult.Invalid)
                {
                    SendMessage(ipPort, new InvalidResponse(nonce, "No solutions found."));
                    return;
                }
            }

            int totalCandidates = solver.HEIGHT * solver.WIDTH * solver.MAX_VALUE;
            int[] numSolutions = colored ? new int[totalCandidates] : null;
            if (!solver.FillRealCandidates(multiThread: false, numSolutions: numSolutions, cancellationToken: cancellationToken))
            {
                SendMessage(ipPort, new InvalidResponse(nonce, "No solutions found."));
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

            TrueCandidatesResponse response = new(nonce, numSolutions);
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
                SendMessage(ipPort, new InvalidResponse(nonce, "No solutions found."));
            }
            else
            {
                SendMessage(ipPort, new SolvedResponse(nonce, solver.FlatBoard.Select(mask => SolverUtility.GetValue(mask)).ToArray()));
            }
        }

        void SendCount(string ipPort, int nonce, Solver solver, ulong maxSolutions, CancellationToken cancellationToken)
        {
            ulong numSolutions = solver.CountSolutions(maxSolutions, true, cancellationToken: cancellationToken, progressEvent: (count) =>
            {
                SendMessage(ipPort, new CountResponse(nonce, count, true));
            });
            if (!cancellationToken.IsCancellationRequested)
            {
                SendMessage(ipPort, new CountResponse(nonce, numSolutions, false));
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
            List<LogicalStepDesc> logicalStepDescs = new();

            LogicResult logicResult;
            if (solver.IsBoardValid(logicalStepDescs))
            {
                logicResult = solver.StepLogic(logicalStepDescs);
            }
            else
            {
                logicResult = LogicResult.Invalid;
            }
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
                    cells[i] = new(SolverUtility.GetValue(mask), null);
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
                    cells[i] = new(0, candidates.ToArray());
                }
            }
            SendMessage(ipPort, new LogicalResponse(nonce, cells, description.ToString().TrimStart(), logicResult != LogicResult.Invalid));
        }

        public void Dispose()
        {
            ((IDisposable)server).Dispose();
        }
    }
}
