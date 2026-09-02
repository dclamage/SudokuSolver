#nullable enable

namespace SudokuSolverConsole;

/// <summary>Owns the active cancellation source for each websocket client.</summary>
internal sealed class ClientCancellationRegistry : IDisposable
{
    private readonly Dictionary<Guid, CancellationTokenSource> sources = [];
    private readonly object syncRoot = new();
    private bool disposed;

    /// <summary>Atomically replaces and cancels a client's previous operation.</summary>
    /// <param name="clientId">The websocket client identifier.</param>
    /// <param name="cancellationToken">The new operation token, or a canceled token after disposal.</param>
    /// <returns>Whether a new operation token was registered.</returns>
    internal bool TryReplace(Guid clientId, out CancellationToken cancellationToken)
    {
        CancellationTokenSource? previous;
        lock (syncRoot)
        {
            if (disposed)
            {
                cancellationToken = new(canceled: true);
                return false;
            }

            _ = sources.Remove(clientId, out previous);
            CancellationTokenSource current = new();
            cancellationToken = current.Token;
            sources.Add(clientId, current);
        }

        CancelAndDispose(previous);
        return true;
    }

    /// <summary>Atomically removes and cancels a client's active operation, if any.</summary>
    /// <param name="clientId">The websocket client identifier.</param>
    internal void CancelAndRemove(Guid clientId)
    {
        CancellationTokenSource? source;
        lock (syncRoot)
        {
            _ = sources.Remove(clientId, out source);
        }
        CancelAndDispose(source);
    }

    /// <summary>Cancels every registered operation and rejects subsequent registrations.</summary>
    public void Dispose()
    {
        CancellationTokenSource[] snapshot;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            snapshot = [.. sources.Values];
            sources.Clear();
        }

        foreach (CancellationTokenSource source in snapshot)
        {
            CancelAndDispose(source);
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
    }
}