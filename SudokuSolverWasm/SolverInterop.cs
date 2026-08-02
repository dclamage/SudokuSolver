using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace SudokuSolverWasm;

/// <summary>
/// The JS-facing surface of the WASM solver. It speaks the same JSON protocol as the native
/// websocket server, so a client can be pointed at either one.
///
/// Threading:
/// - Single-threaded builds run <see cref="HandleMessage"/> inline. The calling worker is blocked
///   for the duration, so mid-solve cancellation is not possible; the host terminates the worker.
/// - Multi-threaded builds (<c>WasmEnableThreads=true</c>) run the work on a pool thread via
///   <see cref="HandleMessageAsync"/>, leaving the worker's message loop free to deliver
///   <see cref="Cancel"/>.
/// </summary>
public static partial class SolverInterop
{
    private static SolverCommandProcessor processor;
    private static CancellationTokenSource cancellationTokenSource = new();

    // JS interop is only legal on the thread that loaded the runtime. In multi-threaded builds the
    // solve runs on a pool thread, so responses have to be marshalled back to the interop thread.
    private static SynchronizationContext interopContext;
    private static int interopThreadId;

    /// <summary>Delivers one protocol response (JSON) back to the host.</summary>
    [JSImport("sendResponse", "solver")]
    internal static partial void SendResponse(string json);

    // Threads-enabled builds run managed code on a deputy thread, and the main JS thread may only
    // call Task-returning exports there ("Cannot call synchronous C# methods"). These async
    // wrappers are the portable entry points; the sync ones remain for single-threaded hosts that
    // call from inside a worker.
    [JSExport]
    public static Task InitializeAsync(bool singleThreaded)
    {
        Initialize(singleThreaded);
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task<string> GetRuntimeInfoAsync() => Task.FromResult(GetRuntimeInfo());

    [JSExport]
    public static void Initialize(bool singleThreaded)
    {
        interopContext = SynchronizationContext.Current;
        interopThreadId = Environment.CurrentManagedThreadId;
        processor = new SolverCommandProcessor(DispatchResponse, singleThreaded);
    }

    /// <summary>
    /// Sends a response from whichever thread produced it. Progress callbacks fire mid-solve, so
    /// the same-thread case must stay a direct call to keep streaming responsive.
    /// </summary>
    private static void DispatchResponse(string json)
    {
        if (interopContext == null || Environment.CurrentManagedThreadId == interopThreadId)
        {
            SendResponse(json);
        }
        else
        {
            interopContext.Post(static state => SendResponse((string)state), json);
        }
    }

    /// <summary>Runs a message to completion on the calling thread.</summary>
    [JSExport]
    public static void HandleMessage(string messageJson)
    {
        CancellationTokenSource cts = cancellationTokenSource = new();
        processor.Handle(messageJson, cts.Token);
    }

    /// <summary>
    /// Runs a message on a pool thread. Only meaningful in multi-threaded builds; in
    /// single-threaded builds the continuation still occupies the one available thread.
    /// </summary>
    [JSExport]
    public static Task HandleMessageAsync(string messageJson)
    {
        CancellationTokenSource cts = cancellationTokenSource = new();
        return Task.Run(() => processor.Handle(messageJson, cts.Token), cts.Token);
    }

    [JSExport]
    public static void Cancel()
    {
        cancellationTokenSource.Cancel();
    }

    /// <summary>Runtime facts the page displays so a benchmark run is self-describing.</summary>
    [JSExport]
    public static string GetRuntimeInfo()
    {
        bool threads =
#if WASM_THREADS
            true;
#else
            false;
#endif
        return JsonSerializer.Serialize(new RuntimeInfo
        {
            threadsEnabled = threads,
            processorCount = Environment.ProcessorCount,
            runtimeVersion = Environment.Version.ToString(),
            osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        }, WasmJsonContext.Default.RuntimeInfo);
    }
}
