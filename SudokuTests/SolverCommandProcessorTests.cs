using System.Text.Json;
using System.Text.Json.Nodes;
using SudokuSolverService;
using SudokuSolverService.Protocol;
using SudokuTests.Helpers;

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
}
