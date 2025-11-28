using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SudokuSolver;

namespace SudokuSolver.Wasm;

#nullable enable

public partial class Program
{
    private static readonly Dictionary<int, CancellationTokenSource> cancellationTokenMap = new();
    private static readonly Dictionary<byte[], BaseResponse> trueCandidatesResponseCache = new(new ByteArrayComparer());
    private static readonly List<ResponseCacheItem> lastTrueCandidatesResponses = new();
    private static readonly object serverLock = new();

    private static readonly ConcurrentQueue<string> messageQueue = new();
    private static readonly AutoResetEvent queueEvent = new(false);

    public static void Main()
    {
        Console.WriteLine("SudokuSolver Wasm Initialized");

        // Start background thread to process incoming messages so message handling
        // happens off the JS event callback thread and can use blocking or long-running
        // operations without blocking the caller.
        // Start the message loop on a background Task. WebAssembly environments
        // may not support managed threads in all configurations, so using a
        // Task provides compatibility while keeping the loop off the caller.
        _ = Task.Run(MessageLoop);
    }

    // Use a custom JS callback instead of `postMessage` so we don't mix with
    // page-level message events. `main.js` sets `globalThis.wasmReceiveMessage`.
    [JSImport("globalThis.wasmReceiveMessage")]
    private static partial void PostMessage(string message);

    // Enqueue incoming messages so they are processed on the dedicated message thread.
    [JSExport]
    public static Task HandleMessage(string jsonInput)
    {
        if (string.IsNullOrEmpty(jsonInput))
        {
            return Task.CompletedTask;
        }

        messageQueue.Enqueue(jsonInput);
        queueEvent.Set();
        return Task.CompletedTask;
    }

    private static void MessageLoop()
    {
        while (true)
        {
            queueEvent.WaitOne();
            while (messageQueue.TryDequeue(out string? json))
            {
                // Process each message on its own task so cancel messages can be processed immediately
                _ = Task.Run(() =>
                {
                    try
                    {
                        ProcessEnqueuedMessage(json!);
                    }
                    catch (OperationCanceledException)
                    {
                        // ignored; cancellation will have been signalled via SendMessage in ProcessEnqueuedMessage
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Unhandled exception in MessageLoop: {ex}");
                    }
                });
            }
        }
    }

    // The original HandleMessage logic moved here; it runs on the background thread.
    private static void ProcessEnqueuedMessage(string jsonInput)
    {
        try
        {
            Message? message = JsonSerializer.Deserialize(jsonInput, WasmJsonContext.Default.Message);
            if (message == null)
            {
                Console.WriteLine("Invalid JSON input");
                return;
            }

            if (message.command == "cancel")
            {
                lock (serverLock)
                {
                    if (cancellationTokenMap.TryGetValue(message.nonce, out CancellationTokenSource? cts))
                    {
                        cts.Cancel();
                    }
                }
                SendMessage(new CanceledResponse(message.nonce));
                return;
            }

            if (message.dataType == "fpuzzles")
            {
                CancellationTokenSource cancellationTokenSource;
                lock (serverLock)
                {
                    // Cancel any existing operation with the same nonce (though typically nonce is unique per request)
                    if (cancellationTokenMap.TryGetValue(message.nonce, out CancellationTokenSource? existingCts))
                    {
                        existingCts.Cancel();
                        cancellationTokenMap.Remove(message.nonce);
                    }

                    cancellationTokenSource = new();
                    cancellationTokenMap[message.nonce] = cancellationTokenSource;
                }
                CancellationToken cancellationToken = cancellationTokenSource.Token;

                try
                {
                    ProcessMessage(message, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SendMessage(new CanceledResponse(message.nonce));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    SendMessage(new InvalidResponse(message.nonce) { message = e.Message });
                }
                finally
                {
                    lock (serverLock)
                    {
                        if (cancellationTokenMap.ContainsKey(message.nonce))
                        {
                            cancellationTokenMap.Remove(message.nonce);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unhandled exception: {ex.Message}");
        }
    }

    private static void ProcessMessage(Message message, CancellationToken cancellationToken)
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

        // Assuming no additional constraints passed via message for now, or we could parse them if needed
        Solver solver = SolverFactory.CreateFromFPuzzles(message.data!, null, onlyGivens: onlyGivens);
        bool multiThread = message.multithread;

        if (message.command == "truecandidates")
        {
            if (solver.customInfo.TryGetValue("ComparableData", out object? comparableDataObj) && comparableDataObj is byte[] comparableData)
            {
                lock (serverLock)
                {
                    if (trueCandidatesResponseCache.TryGetValue(comparableData, out BaseResponse? response))
                    {
                        response.nonce = message.nonce;
                        SendMessage(response);
                        return;
                    }
                }
            }
        }

        solver.customInfo["fpuzzlesdata"] = message.data!;
        switch (message.command)
        {
            case "truecandidates":
                SendTrueCandidates(message.nonce, solver, multiThread, cancellationToken);
                break;
            case "solve":
                SendSolve(message.nonce, solver, multiThread, cancellationToken);
                break;
            case "check":
                SendCount(message.nonce, solver, 2, multiThread, cancellationToken);
                break;
            case "count":
                SendCount(message.nonce, solver, 0, multiThread, cancellationToken);
                break;
            case "estimate":
                SendEstimate(message.nonce, solver, multiThread, cancellationToken);
                break;
            case "solvepath":
                SendSolvePath(message.nonce, solver, cancellationToken);
                break;
            case "step":
                SendStep(message.nonce, solver, cancellationToken);
                break;
        }
    }

    private static void SendMessage(BaseResponse response)
    {
        string json = response switch
        {
            CanceledResponse canceledResponse => JsonSerializer.Serialize(canceledResponse, WasmJsonContext.Default.CanceledResponse),
            InvalidResponse invalidResponse => JsonSerializer.Serialize(invalidResponse, WasmJsonContext.Default.InvalidResponse),
            TrueCandidatesResponse trueCandidatesResponse => JsonSerializer.Serialize(trueCandidatesResponse, WasmJsonContext.Default.TrueCandidatesResponse),
            SolvedResponse solvedResponse => JsonSerializer.Serialize(solvedResponse, WasmJsonContext.Default.SolvedResponse),
            CountResponse countResponse => JsonSerializer.Serialize(countResponse, WasmJsonContext.Default.CountResponse),
            LogicalResponse logicalResponse => JsonSerializer.Serialize(logicalResponse, WasmJsonContext.Default.LogicalResponse),
            EstimateResponse estimateResponse => JsonSerializer.Serialize(estimateResponse, WasmJsonContext.Default.EstimateResponse),
            _ => throw new NotImplementedException($"Unknown response type: {response.type}"),
        };

        PostMessage(json);
    }

    private static bool GetBooleanOption(Solver solver, string option)
    {
        return solver.customInfo.TryGetValue(option, out object? obj) && obj is bool value && value;
    }

    private static void SendTrueCandidatesMessage(BaseResponse response, Solver request, CancellationToken cancellationToken, byte[]? trueCandidatesKey = null)
    {
        lock (serverLock)
        {
            if (trueCandidatesKey != null)
            {
                trueCandidatesResponseCache[trueCandidatesKey] = response;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                lastTrueCandidatesResponses.Add(new ResponseCacheItem { request = request, response = response });
                // Keep only last N responses to minimize search time and memory usage
                if (lastTrueCandidatesResponses.Count > 1000)
                {
                    lastTrueCandidatesResponses.RemoveAt(0);
                }
            }

            SendMessage(response);
        }
    }

    private static bool KeepCandidatesOfResponse(int nonce, Solver solver, BaseResponse response, Predicate<long> keepCandidateCondition)
    {
        if (response is InvalidResponse invalidResponse)
        {
            SendMessage(new InvalidResponse(nonce) { message = invalidResponse.message });
            return false;
        }

        if (response is TrueCandidatesResponse successResponse && successResponse.solutionsPerCandidate != null)
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
                        SendMessage(new InvalidResponse(nonce) { message = "No solutions found." });
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void SendTrueCandidates(int nonce, Solver solver, bool multiThread, CancellationToken cancellationToken)
    {
        long numSolutionsCap = 1;
        if (solver.customInfo.TryGetValue("truecandidatesnumsolutions", out object? numSolutionsObj) && numSolutionsObj is long n)
        {
            numSolutionsCap = n;
        }
        else if (GetBooleanOption(solver, "truecandidatescolored"))
        {
            numSolutionsCap = 8;
        }
        bool logical = GetBooleanOption(solver, "truecandidateslogical");

        Solver request = solver.Clone(willRunNonSinglesLogic: false);

        List<ResponseCacheItem> matchingCacheItems;
        lock (serverLock)
        {
            matchingCacheItems = new(lastTrueCandidatesResponses);
        }
        matchingCacheItems = matchingCacheItems.FindAll(item => request.IsInheritOf(item.request));

        Solver? logicalSolver = null;
        if (logical)
        {
            logicalSolver = solver.Clone(willRunNonSinglesLogic: true);

            foreach (ResponseCacheItem item in matchingCacheItems)
            {
                if (!GetBooleanOption(item.request, "truecandidateslogical"))
                {
                    continue;
                }

                if ((request.DisabledLogicFlags & ~item.request.DisabledLogicFlags) != 0)
                {
                    continue;
                }

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
            if (!KeepCandidatesOfResponse(nonce, solver, item.response, numSolutions => numSolutions > 0))
            {
                return;
            }
        }

        long[]? numSolutions = solver.TrueCandidates(
                multiThread: multiThread,
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
        lock (serverLock)
        {
            if (solver.customInfo.TryGetValue("ComparableData", out object? comparableDataObj) && comparableDataObj is byte[] comparableData)
            {
                SendTrueCandidatesMessage(response, request, cancellationToken, comparableData);
            }
            else
            {
                SendTrueCandidatesMessage(response, request, cancellationToken);
            }
        }
    }

    private static void SendSolve(int nonce, Solver solver, bool multiThread, CancellationToken cancellationToken)
    {
        if (!solver.FindSolution(multiThread: multiThread, isRandom: true, cancellationToken: cancellationToken))
        {
            SendMessage(new InvalidResponse(nonce) { message = "No solutions found." });
        }
        else
        {
            SendMessage(new SolvedResponse(nonce)
            {
                solution = solver.FlatBoard.Select(SolverUtility.GetValue).ToArray()
            });
        }
    }

    private static void SendCount(int nonce, Solver solver, long maxSolutions, bool multiThread, CancellationToken cancellationToken)
    {
        long numSolutions = solver.CountSolutions(maxSolutions, multiThread: multiThread, cancellationToken: cancellationToken, progressEvent: (count) =>
        {
            SendMessage(new CountResponse(nonce) { count = count, inProgress = true });
        });
        if (!cancellationToken.IsCancellationRequested)
        {
            SendMessage(new CountResponse(nonce) { count = numSolutions, inProgress = false });
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

    private static void SendSolvePath(int nonce, Solver solver, CancellationToken cancellationToken)
    {
        List<LogicalStepDesc> logicalStepDescs = new();
        LogicResult logicResult = solver.ConsolidateBoard(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private static void SendStep(int nonce, Solver solver, CancellationToken cancellationToken)
    {
        if (solver.customInfo.TryGetValue("OriginalCenterMarks", out object? obj) && obj is uint[,] originalCenterMarks)
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

        List<LogicalStepDesc> logicalStepDescs = new();
        LogicResult logicResult = solver.StepLogic(logicalStepDescs, cancellationToken);
        SendLogicResponse(nonce, solver, logicResult, StepsDescription(logicalStepDescs));
    }

    private static void SendLogicResponse(int nonce, Solver solver, LogicResult logicResult, StringBuilder description)
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
        SendMessage(new LogicalResponse(nonce)
        {
            cells = cells,
            message = description.ToString().TrimStart(),
            isValid = logicResult != LogicResult.Invalid
        });
    }

    private static void SendEstimate(int nonce, Solver solver, bool multiThread, CancellationToken cancellationToken)
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
                    SendMessage(new EstimateResponse(nonce)
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
            multiThread: multiThread,
            cancellationToken: cancellationToken);
    }

    internal class ResponseCacheItem
    {
        public required Solver request { get; set; }
        public required BaseResponse response { get; set; }
    }

    internal class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? left, byte[]? right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(byte[] key)
        {
            if (key == null)
            {
                return 0;
            }
            int hash = 17;
            foreach (byte b in key)
            {
                hash = hash * 31 + b;
            }
            return hash;
        }
    }
}
