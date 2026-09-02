#nullable enable

using SudokuSolver;
using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService;
using SudokuSolverService.Protocol;
using SudokuTests.Helpers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SudokuTests;

/// <summary>Verifies the transport-neutral native and legacy command protocols.</summary>
[TestClass]
public sealed class SolverCommandProcessorTests
{
    /// <summary>Verifies that native validation responses retain every stale-response correlation field.</summary>
    [TestMethod]
    public void NativeValidateEchoesRevisionHashAndContext()
    {
        string request = NativeRequestFixtures.Validate(
            "request-1",
            documentRevision: 7,
            semanticRevision: 4,
            contextId: "true-candidates");
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request,
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("result", response.Kind);
        Assert.AreEqual("request-1", response.RequestId);
        Assert.AreEqual(7, response.DocumentRevision);
        Assert.AreEqual(4, response.SemanticRevision);
        Assert.AreEqual(NativeRequestFixtures.SemanticHash, response.SemanticHash);
        Assert.AreEqual("true-candidates", response.ContextId);
        Assert.AreEqual("validate", response.Operation);
        Assert.IsNotNull(response.Capability);
        Assert.IsFalse(response.Capability.Contradiction);
    }

    /// <summary>Verifies that the legacy solve wire result remains JSON-compatible after extraction.</summary>
    [TestMethod]
    public void LegacySolveStillReturnsNonceBasedSolvedResponse()
    {
        string request = JsonSerializer.Serialize(new
        {
            nonce = 41,
            command = "solve",
            dataType = "fpuzzles",
            data = NativeRequestFixtures.LegacyPuzzle,
        });
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request,
            responses.Add,
            CancellationToken.None);

        int[] solution = NativeRequestFixtures.LegacySolution.Select(character => character - '0').ToArray();
        JsonNode expected = JsonSerializer.SerializeToNode(new
        {
            nonce = 41,
            type = "solved",
            solution,
        })!;
        JsonNode actual = JsonNode.Parse(responses.Single())!;
        Assert.IsTrue(JsonNode.DeepEquals(expected, actual), $"Expected {expected}; actual {actual}.");
    }

    /// <summary>Verifies that an untrusted client semantic hash is rejected and not echoed as verified.</summary>
    [TestMethod]
    public void IncorrectSemanticHashReturnsMismatchError()
    {
        string request = NativeRequestFixtures.Validate(
            "request-hash",
            documentRevision: 1,
            semanticRevision: 1,
            contextId: "playtest",
            semanticHash: "sha256:not-the-server-hash");
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request,
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("semanticHashMismatch", response.Error?.Code);
        Assert.AreEqual(NativeRequestFixtures.SemanticHash, response.SemanticHash);
        Assert.AreNotEqual("sha256:not-the-server-hash", response.SemanticHash);
    }

    /// <summary>Verifies that native solve results use stable cell identifiers instead of coordinates.</summary>
    [TestMethod]
    public void NativeSolveReturnsValuesByStableCellId()
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Solve(),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("result", response.Kind);
        Assert.HasCount(81, response.Solve!.ValuesByCellId);
        StringAssert.Matches(response.Solve.ValuesByCellId["r1c1"], new("^[1-9]$"));
        Assert.IsFalse(response.Solve.ValuesByCellId.ContainsKey("aux-1"));
    }

    /// <summary>Verifies count progress and a final result clamped to the requested bound.</summary>
    [TestMethod]
    public void NativeCountEmitsProgressAndClampedFinalResult()
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Count(maxSolutions: 1),
            responses.Add,
            CancellationToken.None);

        SolverResponse[] parsed = responses.Select(SolverResponse.Parse).ToArray();
        Assert.AreEqual("progress", parsed.First().Kind);
        Assert.AreEqual(0, parsed.First().Count?.SolutionCount);
        Assert.AreEqual("result", parsed.Last().Kind);
        Assert.AreEqual(1, parsed.Last().Count?.SolutionCount);
        Assert.IsTrue(parsed.Last().Count?.IsClamped);
    }

    /// <summary>Verifies that a nonpositive count bound is rejected before solver work begins.</summary>
    [TestMethod]
    public void NativeCountRequiresPositiveMaximum()
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Count(maxSolutions: 0),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("invalidPackage", response.Error?.Code);
    }

    /// <summary>Verifies that protocol negotiation fails with a typed unsupported-version outcome.</summary>
    [TestMethod]
    public void UnsupportedNativeProtocolVersionReturnsTypedError()
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Validate(
                "request-version",
                documentRevision: 1,
                semanticRevision: 1,
                contextId: "playtest",
                protocolVersion: 2),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("unsupportedVersion", response.Error?.Code);
    }

    /// <summary>Verifies that an unknown projection returns the dedicated typed outcome.</summary>
    [TestMethod]
    public void UnknownNativeProjectionReturnsTypedError()
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Validate(
                "request-projection",
                documentRevision: 1,
                semanticRevision: 1,
                contextId: "playtest",
                projectionId: "missing-projection"),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("unsupportedProjection", response.Error?.Code);
    }

    /// <summary>Verifies malformed native packages return a typed error without trusting their hash.</summary>
    [TestMethod]
    public void MalformedNativePackageReturnsInvalidPackageWithoutEchoingClientHash()
    {
        JsonObject request = JsonNode.Parse(NativeRequestFixtures.Validate(
            "request-invalid",
            documentRevision: 3,
            semanticRevision: 2,
            contextId: "playtest"))!.AsObject();
        request["puzzle"]!.AsObject().Remove("cells");
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request.ToJsonString(),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("invalidPackage", response.Error?.Code);
        Assert.AreEqual(string.Empty, response.SemanticHash);
        Assert.AreEqual("request-invalid", response.RequestId);
    }

    /// <summary>Verifies validation reports contradictory givens alongside structural capabilities.</summary>
    [TestMethod]
    public void NativeValidateReportsContradictionWithCapability()
    {
        JsonObject request = JsonNode.Parse(NativeRequestFixtures.Validate(
            "request-contradiction",
            documentRevision: 2,
            semanticRevision: 2,
            contextId: "playtest"))!.AsObject();
        JsonObject puzzle = request["puzzle"]!.AsObject();
        JsonObject givens = puzzle["givens"]!.AsObject();
        givens["r1c1"] = "1";
        givens["r1c2"] = "1";
        NativePuzzlePackage parsed = NativePuzzlePackage.Parse(puzzle.ToJsonString());
        request["semanticHash"] = NativeSemanticHasher.Compute(parsed);
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request.ToJsonString(),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("result", response.Kind);
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Capability);
        Assert.IsTrue(response.Capability.Contradiction);
        Assert.AreEqual(
            "fullyVerified",
            response.Capability.Entities["solverProjection:main-latin-square"].Status);
    }

    /// <summary>Verifies a canceled native solve is never misclassified as a contradiction.</summary>
    [TestMethod]
    public void NativeSolveCancellationReturnsCanceled()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            NativeRequestFixtures.Solve(),
            responses.Add,
            cancellation.Token);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("canceled", response.Kind);
        Assert.IsNull(response.Error);
    }

    /// <summary>Verifies cancellation observed as search returns wins over the no-solution classification.</summary>
    [TestMethod]
    public void NativeSolveCancellationAfterSearchReturnsCanceled()
    {
        SolverRequest request = JsonSerializer.Deserialize(
            NativeRequestFixtures.Solve(),
            ProtocolJsonContext.Default.SolverRequest)!;
        using CancellationTokenSource cancellation = new();
        List<SolverResponse> responses = [];
        NativeOperationRunner runner = new(
            singleThreaded: true,
            findSolution: (_, token) =>
            {
                cancellation.Cancel();
                Assert.IsTrue(token.IsCancellationRequested);
                return false;
            });

        runner.Run(request, responses.Add, cancellation.Token);

        SolverResponse response = responses.Single();
        Assert.AreEqual("canceled", response.Kind);
        Assert.IsNull(response.Error);
    }

    /// <summary>Verifies future-version negotiation precedes parsing of a v1-specific package body.</summary>
    [TestMethod]
    public void FutureProtocolVersionWinsOverMalformedV1Body()
    {
        JsonObject request = new()
        {
            ["protocolVersion"] = 2,
            ["requestId"] = "request-future",
            ["documentRevision"] = 8,
            ["semanticRevision"] = 5,
            ["semanticHash"] = "sha256:unverified-future-hash",
            ["contextId"] = "playtest",
            ["operation"] = "future.operation",
            ["puzzle"] = "future-package-shape",
            ["futureOptions"] = new JsonObject { ["mode"] = "future" },
        };
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            request.ToJsonString(),
            responses.Add,
            CancellationToken.None);

        SolverResponse response = SolverResponse.Parse(responses.Single());
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("unsupportedVersion", response.Error?.Code);
        Assert.AreEqual("request-future", response.RequestId);
        Assert.AreEqual(string.Empty, response.SemanticHash);
    }

    /// <summary>Verifies one blocked estimate sink cannot block or capture another legacy request.</summary>
    [TestMethod]
    public async Task ConcurrentLegacyEstimateKeepsSinksIsolatedAndAllowsCancellation()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        using CancellationTokenSource estimateCancellation = new();
        using ManualResetEventSlim estimateSinkEntered = new();
        using ManualResetEventSlim releaseEstimateSink = new();
        ConcurrentQueue<JsonNode> estimateResponses = new();
        ConcurrentQueue<JsonNode> solveResponses = new();

        Task estimateTask = Task.Run(() => processor.Handle(
            LegacyRequest(301, "estimate"),
            json =>
            {
                estimateResponses.Enqueue(JsonNode.Parse(json)!);
                estimateSinkEntered.Set();
                releaseEstimateSink.Wait();
            },
            estimateCancellation.Token));
        Assert.IsTrue(
            estimateSinkEntered.Wait(TimeSpan.FromSeconds(10)),
            "The real estimate operation did not reach its response sink.");

        Task solveTask = Task.Run(() => processor.Handle(
            LegacyRequest(302, "solve"),
            json => solveResponses.Enqueue(JsonNode.Parse(json)!),
            CancellationToken.None));

        try
        {
            Task first = await Task.WhenAny(solveTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(
                solveTask,
                first,
                "The solve request was blocked by another request's estimate sink.");
            await solveTask;
            Assert.HasCount(1, solveResponses);
            Assert.IsTrue(solveResponses.All(response => (int)response["nonce"]! == 302));
        }
        finally
        {
            estimateCancellation.Cancel();
            releaseEstimateSink.Set();
            await estimateTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.IsTrue(estimateResponses.All(response => (int)response["nonce"]! == 301));
    }

    /// <summary>Verifies the shared legacy cache returns equivalent parsed JSON with the current nonce.</summary>
    [TestMethod]
    public void LegacyTrueCandidatesCacheHitUsesCurrentNonce()
    {
        ConcurrentQueue<LegacyCacheEvent> cacheEvents = new();
        SolverCommandProcessor processor = new(
            singleThreaded: true,
            legacyAdditionalConstraints: null,
            LegacyInvalidRequestBehavior.RespondWithInvalid,
            cacheEvents.Enqueue);
        List<string> firstResponses = [];
        List<string> secondResponses = [];

        processor.Handle(LegacyRequest(401, "truecandidates"), firstResponses.Add, CancellationToken.None);
        processor.Handle(LegacyRequest(402, "truecandidates"), secondResponses.Add, CancellationToken.None);

        JsonObject first = JsonNode.Parse(firstResponses.Single())!.AsObject();
        JsonObject second = JsonNode.Parse(secondResponses.Single())!.AsObject();
        Assert.AreEqual("truecandidates", (string?)first["type"]);
        Assert.AreEqual(401, (int)first["nonce"]!);
        Assert.AreEqual(402, (int)second["nonce"]!);
        first["nonce"] = 402;
        Assert.IsTrue(JsonNode.DeepEquals(first, second));
        Assert.AreEqual(1, cacheEvents.Count(cacheEvent => cacheEvent == LegacyCacheEvent.ExactHit));
    }

    /// <summary>Verifies concurrent inherited requests safely reuse the same real cached solver state.</summary>
    [TestMethod]
    public async Task ConcurrentInheritedTrueCandidatesReuseSharedCachedSolverSafely()
    {
        ConcurrentQueue<LegacyCacheEvent> cacheEvents = new();
        SolverCommandProcessor processor = new(
            singleThreaded: true,
            legacyAdditionalConstraints: null,
            LegacyInvalidRequestBehavior.RespondWithInvalid,
            cacheEvents.Enqueue);
        List<string> seedResponses = [];
        processor.Handle(LegacyRequest(450, "truecandidates"), seedResponses.Add, CancellationToken.None);
        Assert.AreEqual("truecandidates", (string?)JsonNode.Parse(seedResponses.Single())!["type"]);

        string[] inheritedPuzzles = Enumerable.Range(0, 8)
            .Select(LegacyPuzzleWithAdditionalGiven)
            .ToArray();
        using ManualResetEventSlim start = new();
        ConcurrentQueue<JsonObject> responses = new();
        Task[] tasks = inheritedPuzzles.Select((puzzle, index) => Task.Run(() =>
        {
            start.Wait();
            processor.Handle(
                LegacyRequest(451 + index, "truecandidates", puzzle),
                json => responses.Enqueue(JsonNode.Parse(json)!.AsObject()),
                CancellationToken.None);
        })).ToArray();

        start.Set();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.HasCount(inheritedPuzzles.Length, responses);
        Assert.IsTrue(responses.All(response => (string?)response["type"] == "truecandidates"));
        Assert.IsTrue(
            cacheEvents.Count(cacheEvent => cacheEvent == LegacyCacheEvent.InheritedMatch)
                >= inheritedPuzzles.Length,
            "Every derived request should inherit the seeded cached solver response.");
    }

    /// <summary>Verifies check and count keep progress-before-final legacy response sequencing.</summary>
    [TestMethod]
    [DataRow("check", 2L)]
    [DataRow("count", 0L)]
    public void LegacyCountCommandsEndWithOneFinalResponse(string command, long maximum)
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            LegacyRequest(410, command),
            responses.Add,
            CancellationToken.None);

        JsonObject[] parsed = responses.Select(json => JsonNode.Parse(json)!.AsObject()).ToArray();
        Assert.IsTrue(parsed.All(response => (string?)response["type"] == "count"));
        Assert.IsTrue(parsed.Take(parsed.Length - 1).All(response => (bool)response["inProgress"]!));
        Assert.IsFalse((bool)parsed.Last()["inProgress"]!);
        Assert.AreEqual(1L, (long)parsed.Last()["count"]!);
        Assert.IsTrue(maximum is 0 or 2);
    }

    /// <summary>Verifies cancellation stops the unbounded legacy estimate after a parsed progress response.</summary>
    [TestMethod]
    public async Task LegacyEstimateCancellationStopsAfterProgress()
    {
        using CancellationTokenSource cancellation = new();
        ConcurrentQueue<JsonObject> responses = new();

        Task task = Task.Run(() => new SolverCommandProcessor(singleThreaded: true).Handle(
            LegacyRequest(420, "estimate"),
            json =>
            {
                responses.Enqueue(JsonNode.Parse(json)!.AsObject());
                cancellation.Cancel();
            },
            cancellation.Token));

        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotEmpty(responses);
        Assert.IsTrue(responses.All(response => (string?)response["type"] == "estimate"));
        Assert.IsTrue(responses.All(response => (int)response["nonce"]! == 420));
    }

    /// <summary>Verifies both legacy logical commands retain their parsed response contract.</summary>
    [TestMethod]
    [DataRow("step")]
    [DataRow("solvepath")]
    public void LegacyLogicalCommandsReturnLogicalBoard(string command)
    {
        List<string> responses = [];

        new SolverCommandProcessor(singleThreaded: true).Handle(
            LegacyRequest(430, command),
            responses.Add,
            CancellationToken.None);

        JsonObject response = JsonNode.Parse(responses.Single())!.AsObject();
        Assert.AreEqual("logical", (string?)response["type"]);
        Assert.AreEqual(430, (int)response["nonce"]!);
        Assert.HasCount(81, response["cells"]!.AsArray());
        Assert.IsNotNull(response["isValid"]);
        Assert.IsFalse(string.IsNullOrWhiteSpace((string?)response["message"]));
    }

    /// <summary>Verifies console-supplied additional constraints remain active at the shared boundary.</summary>
    [TestMethod]
    public void LegacyAdditionalConstraintsAreApplied()
    {
        List<string> responses = [];

        new SolverCommandProcessor(
            singleThreaded: true,
            legacyAdditionalConstraints: ["knightmare"]).Handle(
                LegacyRequest(435, "solve"),
                responses.Add,
                CancellationToken.None);

        JsonObject response = JsonNode.Parse(responses.Single())!.AsObject();
        Assert.AreEqual(435, (int)response["nonce"]!);
        Assert.AreEqual("invalid", (string?)response["type"]);
    }

    /// <summary>Verifies console and WASM legacy invalid-request behavior remains an explicit host choice.</summary>
    [TestMethod]
    public void LegacyInvalidRequestBehaviorMatchesEachHost()
    {
        List<string> wasmResponses = [];
        List<string> consoleResponses = [];
        SolverCommandProcessor wasmProcessor = new(
            true,
            legacyAdditionalConstraints: null,
            LegacyInvalidRequestBehavior.RespondWithInvalid);
        SolverCommandProcessor consoleProcessor = new(
            true,
            legacyAdditionalConstraints: null,
            LegacyInvalidRequestBehavior.Ignore);

        foreach (string request in new[]
        {
            LegacyRequest(440, "solve", dataType: "future-format"),
            LegacyRequest(441, "future-command"),
        })
        {
            wasmProcessor.Handle(request, wasmResponses.Add, CancellationToken.None);
            consoleProcessor.Handle(request, consoleResponses.Add, CancellationToken.None);
        }

        Assert.HasCount(2, wasmResponses);
        Assert.IsTrue(wasmResponses.All(json => (string?)JsonNode.Parse(json)!["type"] == "invalid"));
        Assert.IsEmpty(consoleResponses);
    }

    /// <summary>Verifies the factories consumed by the concrete hosts preserve their invalid-command behavior.</summary>
    [TestMethod]
    public void HostFactoriesPreserveConsoleAndWasmLegacyBehavior()
    {
        List<string> consoleResponses = [];
        List<string> wasmResponses = [];
        string request = LegacyRequest(460, "future-command");

        SolverCommandProcessorFactory.CreateConsole(singleThreaded: true).Handle(
            request,
            consoleResponses.Add,
            CancellationToken.None);
        SolverCommandProcessorFactory.CreateWasm(singleThreaded: true).Handle(
            request,
            wasmResponses.Add,
            CancellationToken.None);

        Assert.IsEmpty(consoleResponses);
        Assert.HasCount(1, wasmResponses);
        Assert.AreEqual("invalid", (string?)JsonNode.Parse(wasmResponses.Single())!["type"]);
    }

    private static string LegacyPuzzleWithAdditionalGiven(int ordinal)
    {
        Solver solver = SolverFactory.CreateFromFPuzzles(
            NativeRequestFixtures.LegacyPuzzle,
            onlyGivens: true);
        bool[,] givens = (bool[,])solver.customInfo["Givens"];
        int cellIndex = Enumerable.Range(0, solver.NUM_CELLS)
            .Where(index => !givens[index / solver.WIDTH, index % solver.WIDTH])
            .ElementAt(ordinal);
        Assert.IsTrue(solver.SetValue(cellIndex, NativeRequestFixtures.LegacySolution[cellIndex] - '0'));
        givens[cellIndex / solver.WIDTH, cellIndex % solver.WIDTH] = true;
        return SolverFactory.ToFPuzzlesURL(solver, justBase64: true);
    }

    private static string LegacyRequest(
        int nonce,
        string command,
        string? data = null,
        string dataType = "fpuzzles")
        => JsonSerializer.Serialize(new
        {
            nonce,
            command,
            dataType,
            data = data ?? NativeRequestFixtures.LegacyPuzzle,
        });
}