#nullable enable

using SudokuSolverConsole;
using System.Collections.Concurrent;

namespace SudokuTests;

/// <summary>Verifies websocket-host cancellation ownership independently of the network server.</summary>
[TestClass]
public sealed class WebsocketHostTests
{
    /// <summary>Verifies replacing and removing one client's operation cancels the previous token.</summary>
    [TestMethod]
    public void ClientCancellationRegistryAtomicallyReplacesAndRemoves()
    {
        using ClientCancellationRegistry registry = new();
        Guid clientId = Guid.NewGuid();

        Assert.IsTrue(registry.TryReplace(clientId, out CancellationToken first));
        Assert.IsTrue(registry.TryReplace(clientId, out CancellationToken second));
        Assert.IsTrue(first.IsCancellationRequested);
        Assert.IsFalse(second.IsCancellationRequested);

        registry.CancelAndRemove(clientId);

        Assert.IsTrue(second.IsCancellationRequested);
    }

    /// <summary>Verifies concurrent replacement, removal, and disposal cancel every issued client token.</summary>
    [TestMethod]
    public async Task ClientCancellationRegistryCoordinatesConcurrentDisposal()
    {
        ClientCancellationRegistry registry = new();
        ConcurrentQueue<CancellationToken> issuedTokens = new();
        using ManualResetEventSlim start = new();
        Guid clientId = Guid.NewGuid();
        Assert.IsTrue(registry.TryReplace(clientId, out CancellationToken initial));
        issuedTokens.Enqueue(initial);
        Task[] operations = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
        {
            start.Wait();
            if (index % 4 == 0)
            {
                registry.CancelAndRemove(clientId);
            }
            else if (registry.TryReplace(clientId, out CancellationToken token))
            {
                issuedTokens.Enqueue(token);
            }
        })).Append(Task.Run(() =>
        {
            start.Wait();
            registry.Dispose();
        })).ToArray();

        start.Set();
        await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(issuedTokens.All(token => token.IsCancellationRequested));
        Assert.IsFalse(registry.TryReplace(Guid.NewGuid(), out CancellationToken rejected));
        Assert.IsTrue(rejected.IsCancellationRequested);
        registry.Dispose();
    }
}