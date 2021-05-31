using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WatsonWebsocket;
using SudokuSolver;
using System.IO;
using System.Threading;
using System.Collections;

namespace SudokuSolverConsole
{
    class WebsocketListener : IDisposable
    {
        private WatsonWsServer server;
        private readonly object serverLock = new();
        private readonly Dictionary<string, CancellationTokenSource> cancellationTokenMap = new();
        private readonly Dictionary<byte[], string> trueCandidatesResponseCache = new(new ByteArrayComparer());
        private readonly Dictionary<byte[], string> trueCandidatesColoredResponseCache = new(new ByteArrayComparer());

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
            string message = Encoding.UTF8.GetString(args.Data);
            string[] split = message.Split(':');
            if (split.Length != 4)
            {
                return;
            }

            string platform = split[0];
            string nonce = split[1];
            string command = split[2];
            string data = split[3];
            if (platform == "fpuzzles")
            {
                if (cancellationTokenMap.TryGetValue(args.IpPort, out var cancellationTokenSource))
                {
                    cancellationTokenSource.Cancel();
                }
                
                if (command == "cancel")
                {
                    server.SendAsync(args.IpPort, $"{nonce}:canceled");
                    return;
                }

                cancellationTokenSource = cancellationTokenMap[args.IpPort] = new();
                var cancellationToken = cancellationTokenSource.Token;

                string ipPort = args.IpPort;
                Task.Run(() =>
                {
                    try
                    {
                        bool onlyGivens = false;
                        switch (command)
                        {
                            case "truecandidates":
                            case "truecandidatescolored":
                            case "solve":
                            case "check":
                            case "count":
                                onlyGivens = true;
                                break;
                        }

                        Solver solver = SolverFactory.CreateFromFPuzzles(data, onlyGivens: onlyGivens);
                        if (command == "truecandidates" || command == "truecandidatescolored")
                        {
                            if (solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj) && comparableDataObj is byte[] comparableData)
                            {
                                var cache = command == "truecandidates" ? trueCandidatesResponseCache : trueCandidatesColoredResponseCache;
                                lock (serverLock)
                                {
                                    if (cache.TryGetValue(comparableData, out string response))
                                    {
                                        server.SendAsync(args.IpPort, $"{nonce}:{response}");
                                        return;
                                    }
                                }
                            }
                        }

                        solver.customInfo["fpuzzlesdata"] = data;
                        switch (command)
                        {
                            case "truecandidates":
                                SendTrueCandidates(ipPort, nonce, false, solver, cancellationToken);
                                break;
                            case "truecandidatescolored":
                                SendTrueCandidates(ipPort, nonce, true, solver, cancellationToken);
                                break;
                            case "solve":
                                SendSolve(ipPort, nonce, solver, cancellationToken);
                                break;
                            case "check":
                                SendCount(ipPort, nonce, solver, 2, cancellationToken);
                                break;
                            case "count":
                                SendCount(ipPort, nonce, solver, 0, cancellationToken);
                                break;
                            case "solvepath":
                                SendSolvePath(ipPort, nonce, solver);
                                break;
                            case "simplepath":
                                solver.DisableContradictions = true;
                                SendSolvePath(ipPort, nonce, solver);
                                break;
                            case "step":
                                SendStep(ipPort, nonce, solver);
                                break;
                            case "simplestep":
                                solver.DisableContradictions = true;
                                SendStep(ipPort, nonce, solver);
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Do nothing, no response expected
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Exception processing message: {e.Message}");
                        lock (serverLock)
                        {
                            server.SendAsync(ipPort, nonce + ":Invalid");
                        }
                    }
                }, cancellationToken);
            }
        }

        void SendTrueCandidates(string ipPort, string nonce, bool colored, Solver solver, CancellationToken cancellationToken)
        {
            int[] numSolutions = colored ? new int[solver.HEIGHT * solver.WIDTH * solver.MAX_VALUE] : null;
            if (!solver.FillRealCandidates(multiThread: true, numSolutions: numSolutions, cancellationToken: cancellationToken))
            {
                lock (serverLock)
                {
                    server.SendAsync(ipPort, nonce + ":Invalid", CancellationToken.None);
                }
            }
            else
            {
                string candidateString = solver.CandidateString;
                string payload;
                if (colored)
                {
                    StringBuilder numSolutionsString = new(numSolutions.Length);
                    for (int i = 0; i < numSolutions.Length; i++)
                    {
                        int curNumSolutions = Math.Min(8, numSolutions[i]);
                        numSolutionsString.Append(curNumSolutions);
                    }
                    payload = candidateString + numSolutionsString.ToString();
                }
                else
                {
                    payload = candidateString;
                }
                string fpuzzles = $"{nonce}:{payload}";
                lock (serverLock)
                {
                    if (solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj) && comparableDataObj is byte[] comparableData)
                    {
                        var cache = !colored ? trueCandidatesResponseCache : trueCandidatesColoredResponseCache;
                        cache[comparableData] = payload;
                    }

                    server.SendAsync(ipPort, fpuzzles, CancellationToken.None);
                }
            }
        }

        void SendSolve(string ipPort, string nonce, Solver solver, CancellationToken cancellationToken)
        {
            if (!solver.FindSolution(multiThread: true, isRandom: true, cancellationToken: cancellationToken))
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    lock (serverLock)
                    {
                        server.SendAsync(ipPort, nonce + ":Invalid", CancellationToken.None);
                    }
                }
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                string givenString = solver.GivenString;
                string fpuzzles = $"{nonce}:{givenString}";
                lock (serverLock)
                {
                    server.SendAsync(ipPort, fpuzzles, CancellationToken.None);
                }
            }
        }

        void SendCount(string ipPort, string nonce, Solver solver, ulong maxSolutions, CancellationToken cancellationToken)
        {
            ulong numSolutions = solver.CountSolutions(maxSolutions, true, cancellationToken: cancellationToken, progressEvent: (count) =>
            {
                server.SendAsync(ipPort, $"{nonce}:progress:{count}", CancellationToken.None);
            });
            if (!cancellationToken.IsCancellationRequested)
            {
                server.SendAsync(ipPort, $"{nonce}:final:{numSolutions}", CancellationToken.None);
            }
        }

        void SendSolvePath(string ipPort, string nonce, Solver solver)
        {
            StringBuilder stepsDescription = new();
            var logicResult = solver.ConsolidateBoard(stepsDescription);
            SendLogicResponse(ipPort, nonce, solver, logicResult, stepsDescription);
        }

        void SendStep(string ipPort, string nonce, Solver solver)
        {
            StringBuilder stepDescription = new();
            var logicResult = solver.StepLogic(stepDescription, true);
            SendLogicResponse(ipPort, nonce, solver, logicResult, stepDescription);
        }

        void SendLogicResponse(string ipPort, string nonce, Solver solver, LogicResult logicResult, StringBuilder description)
        {
            if (!description.ToString().EndsWith(Environment.NewLine))
            {
                description.AppendLine();
            }

            StringBuilder finalMessage = new();
            finalMessage.Append(nonce).Append(':');

            if (logicResult == LogicResult.Invalid)
            {
                description.AppendLine("Board is invalid!");
            }
            else if (logicResult == LogicResult.None)
            {
                description.AppendLine("No logical steps found.");
            }
            finalMessage.Append(solver.DistinguishedCandidateString).Append(':');
            finalMessage.Append(description);

            lock (serverLock)
            {
                server.SendAsync(ipPort, finalMessage.ToString(), CancellationToken.None);
            }
        }

        public void Dispose()
        {
            ((IDisposable)server).Dispose();
        }
    }
}
