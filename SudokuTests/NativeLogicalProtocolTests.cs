#nullable enable

using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService;
using SudokuSolverService.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SudokuTests;

/// <summary>Verifies revision-safe native structured logical sessions.</summary>
[TestClass]
public sealed class NativeLogicalProtocolTests
{
    /// <summary>Proves create returns a stable mapped board and structured naked-single deduction.</summary>
    [TestMethod]
    public void CreateReturnsCurrentBoardDeductionAndSemanticFrame()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);

        SolverResponse response = Run(processor, CreateRequest("logical.create", "create-1"));

        Assert.AreEqual("result", response.Kind);
        Assert.AreEqual("logical.create", response.Operation);
        Assert.IsNotNull(response.Logical);
        LogicalResultDto logical = response.Logical;
        Assert.IsEmpty(logical.HistoryDeductionIds);
        LogicalCellStateDto first = logical.Cells.Single(cell => cell.CellId == "r1c1");
        Assert.IsNull(first.ValueId);
        CollectionAssert.AreEqual(new[] { "3" }, first.CandidateValueIds);
        LogicalDeductionDto deduction = logical.AvailableDeductions.Single();
        Assert.AreEqual("basic.naked-single", deduction.TechniqueId);
        Assert.AreEqual("r1c1", deduction.Delta.Placements.Single().CellId);
        Assert.AreEqual("3", deduction.Delta.Placements.Single().ValueId);
        Assert.IsTrue(deduction.Frames.Single().Highlight.Any(reference =>
            reference.Kind == "cell" && reference.Id == "r1c1"));
    }

    /// <summary>Proves apply advances only the matching session and appends history once.</summary>
    [TestMethod]
    public void ApplyReturnsNextBoardAndAppendsHistoryOnce()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        SolverResponse created = Run(processor, CreateRequest("logical.create", "create-apply"));
        LogicalResultDto initial = created.Logical!;
        string deductionId = initial.AvailableDeductions.Single().Id;

        SolverResponse applied = Run(processor, ApplyRequest(initial, deductionId, "apply-1"));

        Assert.AreEqual("result", applied.Kind);
        Assert.AreEqual("logical.apply", applied.Operation);
        LogicalResultDto next = applied.Logical!;
        Assert.AreNotEqual(initial.PositionHash, next.PositionHash);
        Assert.AreEqual("3", next.Cells.Single(cell => cell.CellId == "r1c1").ValueId);
        CollectionAssert.AreEqual(new[] { deductionId }, next.HistoryDeductionIds);
        Assert.IsEmpty(next.AvailableDeductions);

        SolverResponse duplicate = Run(processor, ApplyRequest(initial, deductionId, "apply-duplicate"));
        AssertStale(duplicate);
    }

    /// <summary>Proves create and apply do not mutate the native package supplied by the caller.</summary>
    [TestMethod]
    public void LogicalSessionKeepsSourcePackageImmutable()
    {
        NativePuzzlePackage package = LogicalPackage();
        string before = package.ToJson();
        SolverRequest request = CreateRequestObject("logical.create", "source-create", package);
        NativeOperationRunner runner = new(singleThreaded: true);
        List<SolverResponse> responses = [];

        runner.Run(request, responses.Add, CancellationToken.None);
        LogicalResultDto created = responses.Single().Logical!;
        runner.Run(ApplyRequestObject(request, created, created.AvailableDeductions.Single().Id, "source-apply"), responses.Add, CancellationToken.None);

        Assert.AreEqual(before, package.ToJson());
    }

    /// <summary>Proves forged identifiers and stale position hashes cannot partially mutate a session.</summary>
    [TestMethod]
    public void ForgedAndStaleApplyAreRejectedWithoutMutation()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        LogicalResultDto initial = Run(processor, CreateRequest("logical.create", "create-stale")).Logical!;
        string validId = initial.AvailableDeductions.Single().Id;

        SolverResponse forged = Run(processor, ApplyRequest(initial, "sha256:forged", "apply-forged"));
        AssertStale(forged);
        SolverResponse staleHash = Run(processor, ApplyRequest(initial, validId, "apply-stale-hash", "sha256:wrong-position"));
        AssertStale(staleHash);

        SolverResponse valid = Run(processor, ApplyRequest(initial, validId, "apply-valid-after-errors"));
        Assert.AreEqual("result", valid.Kind);
        CollectionAssert.AreEqual(new[] { validId }, valid.Logical!.HistoryDeductionIds);
    }

    /// <summary>Proves exact session ownership includes session, context, and semantic revision.</summary>
    [TestMethod]
    public void ApplyRejectsWrongSessionContextAndRevision()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        LogicalResultDto initial = Run(processor, CreateRequest("logical.create", "create-owner")).Logical!;
        string deductionId = initial.AvailableDeductions.Single().Id;

        AssertStale(Run(processor, ApplyRequest(initial with { SessionId = "sha256:wrong" }, deductionId, "wrong-session")));
        AssertStale(Run(processor, ApplyRequest(initial, deductionId, "wrong-context", contextId: "another-context")));
        AssertStale(Run(processor, ApplyRequest(initial, deductionId, "wrong-revision", semanticRevision: 8)));

        SolverResponse valid = Run(processor, ApplyRequest(initial, deductionId, "valid-owner"));
        Assert.AreEqual("result", valid.Kind);
    }

    /// <summary>Proves replay after processor restart reconstructs exactly the prior logical state.</summary>
    [TestMethod]
    public void CreateWithReplayRecoversEquivalentSessionAfterRestart()
    {
        SolverCommandProcessor firstProcessor = new(singleThreaded: true);
        LogicalResultDto initial = Run(firstProcessor, CreateRequest("logical.create", "create-original")).Logical!;
        string deductionId = initial.AvailableDeductions.Single().Id;
        LogicalResultDto advanced = Run(firstProcessor, ApplyRequest(initial, deductionId, "apply-original")).Logical!;

        SolverCommandProcessor restartedProcessor = new(singleThreaded: true);
        LogicalResultDto replayed = Run(
            restartedProcessor,
            CreateRequest("logical.create", "create-replay", new[] { deductionId })).Logical!;

        Assert.AreEqual(advanced.SessionId, replayed.SessionId);
        Assert.AreEqual(advanced.PositionHash, replayed.PositionHash);
        CollectionAssert.AreEqual(advanced.HistoryDeductionIds, replayed.HistoryDeductionIds);
        CollectionAssert.AreEqual(
            advanced.Cells.Select(CellSignature).ToArray(),
            replayed.Cells.Select(CellSignature).ToArray());
        CollectionAssert.AreEqual(
            advanced.AvailableDeductions.Select(deduction => deduction.Id).ToArray(),
            replayed.AvailableDeductions.Select(deduction => deduction.Id).ToArray());
    }

    /// <summary>Proves an invalid replay does not replace the last successfully published session.</summary>
    [TestMethod]
    public void FailedReplayDoesNotReplacePublishedSession()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        LogicalResultDto initial = Run(processor, CreateRequest("logical.create", "create-good")).Logical!;
        string deductionId = initial.AvailableDeductions.Single().Id;

        SolverResponse failed = Run(processor, CreateRequest("logical.create", "create-bad-replay", new[] { "sha256:forged" }));
        AssertStale(failed);

        SolverResponse applied = Run(processor, ApplyRequest(initial, deductionId, "apply-after-bad-replay"));
        Assert.AreEqual("result", applied.Kind);
    }

    /// <summary>Proves cancellation prevents a create operation from publishing a usable session.</summary>
    [TestMethod]
    public void CanceledCreateDoesNotPublishSession()
    {
        SolverCommandProcessor processor = new(singleThreaded: true);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        SolverResponse canceled = Run(processor, CreateRequest("logical.create", "create-canceled"), cancellation.Token);
        Assert.AreEqual("canceled", canceled.Kind);

        LogicalResultDto guessed = new()
        {
            SessionId = StableExpectedSessionId(),
            PositionHash = "sha256:unknown",
            Cells = [],
            AvailableDeductions = [],
            HistoryDeductionIds = [],
        };
        AssertStale(Run(processor, ApplyRequest(guessed, "sha256:forged", "apply-after-cancel")));
    }

    /// <summary>Proves missing typed operation DTOs retain the native invalid-package envelope.</summary>
    [TestMethod]
    public void MissingLogicalOptionsReturnInvalidPackage()
    {
        JsonObject request = JsonNode.Parse(CreateRequest("logical.create", "missing-options"))!.AsObject();
        request.Remove("logicalCreateOptions");

        SolverResponse response = Run(new SolverCommandProcessor(singleThreaded: true), request.ToJsonString());

        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("invalidPackage", response.Error?.Code);
        Assert.AreEqual("missing-options", response.RequestId);
    }

    private static string CreateRequest(string operation, string requestId, string[]? appliedIds = null)
        => Serialize(CreateRequestObject(operation, requestId, LogicalPackage(), appliedIds));

    private static SolverRequest CreateRequestObject(
        string operation,
        string requestId,
        NativePuzzlePackage package,
        string[]? appliedIds = null)
        => new()
        {
            ProtocolVersion = 1,
            RequestId = requestId,
            DocumentRevision = 4,
            SemanticRevision = 7,
            SemanticHash = NativeSemanticHasher.Compute(package),
            ContextId = "logical-context",
            Operation = operation,
            Puzzle = package,
            LogicalCreateOptions = new LogicalCreateOptionsDto
            {
                ProjectionId = "main-latin-square",
                AppliedDeductionIds = appliedIds ?? [],
            },
        };

    private static string ApplyRequest(
        LogicalResultDto session,
        string deductionId,
        string requestId,
        string? positionHash = null,
        string contextId = "logical-context",
        long semanticRevision = 7)
    {
        SolverRequest create = CreateRequestObject("logical.apply", requestId, LogicalPackage());
        return Serialize(new SolverRequest
        {
            ProtocolVersion = create.ProtocolVersion,
            RequestId = create.RequestId,
            DocumentRevision = create.DocumentRevision,
            SemanticRevision = semanticRevision,
            SemanticHash = create.SemanticHash,
            ContextId = contextId,
            Operation = "logical.apply",
            Puzzle = create.Puzzle,
            LogicalApplyOptions = new LogicalApplyOptionsDto
            {
                ProjectionId = "main-latin-square",
                SessionId = session.SessionId,
                PositionHash = positionHash ?? session.PositionHash,
                DeductionId = deductionId,
            },
        });
    }

    private static SolverRequest ApplyRequestObject(
        SolverRequest create,
        LogicalResultDto session,
        string deductionId,
        string requestId)
        => new()
        {
            ProtocolVersion = create.ProtocolVersion,
            RequestId = requestId,
            DocumentRevision = create.DocumentRevision,
            SemanticRevision = create.SemanticRevision,
            SemanticHash = create.SemanticHash,
            ContextId = create.ContextId,
            Operation = "logical.apply",
            Puzzle = create.Puzzle,
            LogicalApplyOptions = new LogicalApplyOptionsDto
            {
                ProjectionId = create.LogicalCreateOptions!.ProjectionId,
                SessionId = session.SessionId,
                PositionHash = session.PositionHash,
                DeductionId = deductionId,
            },
        };

    private static SolverResponse Run(
        SolverCommandProcessor processor,
        string request,
        CancellationToken cancellationToken = default)
    {
        List<string> responses = [];
        processor.Handle(request, responses.Add, cancellationToken);
        return responses.Select(SolverResponse.Parse).Single(response => response.Kind is "result" or "error" or "canceled");
    }

    private static string Serialize(SolverRequest request)
        => JsonSerializer.Serialize(request, ProtocolJsonContext.Default.SolverRequest);

    private static NativePuzzlePackage LogicalPackage()
    {
        NativePuzzlePackage package = SudokuTests.Helpers.NativeRequestFixtures.ClassicPackage();
        package.Givens.Clear();
        string solution = Puzzles.uniqueClassics[0].Item2;
        for (int index = 1; index < solution.Length; index++)
        {
            package.Givens[$"r{index / 9 + 1}c{index % 9 + 1}"] = solution[index].ToString();
        }
        return package;
    }

    private static string CellSignature(LogicalCellStateDto cell)
        => $"{cell.CellId}:{cell.ValueId}:{string.Join(',', cell.CandidateValueIds)}";

    private static string StableExpectedSessionId()
        => "sha256:guess-does-not-matter";

    private static void AssertStale(SolverResponse response)
    {
        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("staleContext", response.Error?.Code);
        Assert.IsNull(response.Logical);
    }
}