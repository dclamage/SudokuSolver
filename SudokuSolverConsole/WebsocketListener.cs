using SudokuSolverService;
using System.Text;
using System.Text.Json;
using WatsonWebsocket;

namespace SudokuSolverConsole;

/// <summary>Owns websocket clients and cancellation while delegating solver operations.</summary>
internal sealed class WebsocketListener : IDisposable
{
    private readonly ClientCancellationRegistry cancellations = new();
    private readonly object serverLock = new();
    private WatsonWsServer server;
    private SolverCommandProcessor processor;
    private bool verboseLogs;

    /// <summary>Starts listening for legacy or native solver protocol messages.</summary>
    /// <param name="host">The network host to bind.</param>
    /// <param name="port">The network port to bind.</param>
    /// <param name="additionalConstraints">Optional legacy additional constraint declarations.</param>
    /// <param name="verboseLogs">Whether unexpected host failures should be logged.</param>
    /// <param name="singleThreaded">Whether solver algorithms must avoid internal parallelism.</param>
    /// <returns>A task that completes when the websocket server has started.</returns>
    internal async Task Listen(
        string host,
        int port,
        IEnumerable<string> additionalConstraints = null,
        bool verboseLogs = false,
        bool singleThreaded = false)
    {
        if (server != null)
        {
            throw new InvalidOperationException("Server already listening!");
        }

        this.verboseLogs = verboseLogs;
        processor = SolverCommandProcessorFactory.CreateConsole(singleThreaded, additionalConstraints);
        server = new(host, port, false);
        server.ClientConnected += (_, args) => ClientConnected(args);
        server.ClientDisconnected += (_, args) => ClientDisconnected(args);
        server.MessageReceived += (_, args) => MessageReceived(args);
        await server.StartAsync();
        Console.WriteLine($"Accepting connections from {host}:{port}");
    }

    private static void ClientConnected(ConnectionEventArgs args)
    {
        Console.WriteLine("Client connected: " + args.Client.IpPort);
    }

    private void ClientDisconnected(DisconnectionEventArgs args)
    {
        Console.WriteLine("Client disconnected: " + args.Client.IpPort);
        cancellations.CancelAndRemove(args.Client.Guid);
    }

    private void MessageReceived(MessageReceivedEventArgs args)
    {
        string messageJson = Encoding.UTF8.GetString(args.Data);
        Guid clientGuid = args.Client.Guid;

        if (IsLegacyCancel(messageJson))
        {
            cancellations.CancelAndRemove(clientGuid);
            processor.Handle(messageJson, json => SendMessage(clientGuid, json), CancellationToken.None);
            return;
        }

        if (!cancellations.TryReplace(clientGuid, out CancellationToken current))
        {
            return;
        }
        _ = Task.Run(
            () =>
            {
                try
                {
                    processor.Handle(messageJson, json => SendMessage(clientGuid, json), current);
                }
                catch (Exception exception)
                {
                    if (verboseLogs)
                    {
                        Console.WriteLine(exception);
                    }
                }
            },
            current);
    }

    private static bool IsLegacyCancel(string messageJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("command", out JsonElement command)
                && command.ValueKind == JsonValueKind.String
                && string.Equals(command.GetString(), "cancel", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void SendMessage(Guid clientGuid, string json)
    {
        lock (serverLock)
        {
            _ = server.SendAsync(clientGuid, json);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        cancellations.Dispose();
        ((IDisposable)server).Dispose();
    }
}