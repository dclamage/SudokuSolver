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

namespace SudokuSolverConsole
{
    class WebsocketListener : IDisposable
    {
        private WatsonWsServer server;
        private readonly object serverLock = new();
        private Dictionary<string, CancellationTokenSource> cancellationTokenMap = new();

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
            if (message.StartsWith("fpuzzles:"))
            {
                message = message.Substring("fpuzzles:".Length);

                string nonce = "";
                int nonceColon = message.IndexOf(':');
                if (nonceColon != -1)
                {
                    nonce = message.Substring(0, nonceColon);
                    message = message.Substring(nonceColon + 1);

                    if (cancellationTokenMap.TryGetValue(args.IpPort, out var cancellationToken))
                    {
                        cancellationToken.Cancel();
                    }
                    cancellationToken = cancellationTokenMap[args.IpPort] = new();

                    string ipPort = args.IpPort;
                    Task.Run(() =>
                    {
                        try
                        {
                            Solver solver = SolverFactory.CreateFromFPuzzles(message, onlyGivens: true);
                            SendTrueCandidates(ipPort, nonce, solver);
                        }
                        catch (Exception)
                        {
                            server.SendAsync(ipPort, nonce + ":Invalid");
                        }
                    }, cancellationToken.Token);
                }
            }
        }

        void SendTrueCandidates(string ipPort, string nonce, Solver solver)
        {
            Solver solverClone = solver.Clone();
            if (!solver.FillRealCandidates(multiThread: true))
            {
                server.SendAsync(ipPort, nonce + ":Invalid");
            }
            else
            {
                lock (serverLock)
                {
                    string fpuzzles = $"{nonce}:{solver.CandidateString}";
                    server.SendAsync(ipPort, fpuzzles);
                }
            }
        }

        public void Dispose()
        {
            ((IDisposable)server).Dispose();
        }
    }
}
