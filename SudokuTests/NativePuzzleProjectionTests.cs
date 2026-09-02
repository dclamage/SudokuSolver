using SudokuSolver.PuzzleFormats.Native;
using System.Text.Json;
using System.Text.Json.Nodes;
using static SudokuSolver.SolverUtility;

namespace SudokuTests;

/// <summary>
/// Verifies projection of shared native packages into the current square Latin solver.
/// </summary>
[TestClass]
public class NativePuzzleProjectionTests
{
    /// <summary>
    /// Projects the explicitly ordered main grid and reports an omitted auxiliary cell as visual-only.
    /// </summary>
    [TestMethod]
    public void ProjectsExplicitLatinGridAndReportsAuxiliaryCellAsVisualOnly()
    {
        string json = File.ReadAllText(FixturePath("classic-with-auxiliary.json"));
        NativePuzzlePackage package = NativePuzzlePackage.Parse(json);
        NativeProjectionResult result = NativePuzzleProjector.Project(package, "main-latin-square");

        Assert.AreEqual(81, result.Solver.NUM_CELLS);
        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["r1c1"].Status);
        Assert.AreEqual(EntityCapability.VisualOnly, result.Capabilities.Entities["aux-1"].Status);
        Assert.AreEqual("Not included in projection main-latin-square.", result.Capabilities.Entities["aux-1"].Reason);
    }

    /// <summary>
    /// Uses the projection's explicit stable-ID order when applying native givens.
    /// </summary>
    [TestMethod]
    public void AppliesGivensThroughStableCellAndValueMappings()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["givens"]!["r1c1"] = "5";

        NativeProjectionResult result = NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square");

        Assert.AreEqual(0, result.CellIndexById["r1c1"]);
        Assert.AreEqual("r1c1", result.CellIdByIndex[0]);
        Assert.IsTrue(IsValueSet(result.Solver.FlatBoard[0]));
        Assert.AreEqual(5, GetValue(result.Solver.FlatBoard[0]));
    }

    /// <summary>
    /// Rejects duplicate stable cell IDs instead of silently projecting one cell twice.
    /// </summary>
    [TestMethod]
    public void RejectsDuplicateProjectedCells()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["solverProjections"]![0]!["cellIdsByRow"]![0]![1] = "r1c1";

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square"));

        StringAssert.Contains(exception.Message, "duplicate cell r1c1");
    }

    /// <summary>
    /// Rejects a projection whose grid does not have equal width and height.
    /// </summary>
    [TestMethod]
    public void RejectsNonSquareProjection()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["solverProjections"]![0]!["cellIdsByRow"]!.AsArray().RemoveAt(8);

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square"));

        StringAssert.Contains(exception.Message, "must be square");
    }

    /// <summary>
    /// Rejects a domain order whose numeric meanings do not match current solver values.
    /// </summary>
    [TestMethod]
    public void RejectsMismatchedNumericInterpretations()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["domains"]!["digits-1-9"]!["values"]![0]!["numericValue"] = 9;

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square"));

        StringAssert.Contains(exception.Message, "must have numeric interpretation 1");
    }

    /// <summary>
    /// Matches the shared TypeScript semantic-hash goldens for both native fixtures.
    /// </summary>
    [TestMethod]
    public void SemanticHashesMatchSharedGoldenValues()
    {
        using JsonDocument goldens = JsonDocument.Parse(File.ReadAllText(FixturePath("semantic-hashes.json")));

        Assert.AreEqual(
            goldens.RootElement.GetProperty("classic-with-auxiliary").GetString(),
            NativeSemanticHasher.Compute(ReadPackage("classic-with-auxiliary.json")));
        Assert.AreEqual(
            goldens.RootElement.GetProperty("four-by-four-killer").GetString(),
            NativeSemanticHasher.Compute(ReadPackage("four-by-four-killer.json")));
    }

    /// <summary>
    /// Matches JavaScript JSON string escaping for valid Unicode semantic text.
    /// </summary>
    [TestMethod]
    public void SemanticHashMatchesSharedUnicodeGolden()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["extensions"]!["example:unicode"] = new JsonObject
        {
            ["impact"] = "semantic",
            ["data"] = new JsonObject
            {
                ["emoji"] = "😀",
                ["lineSeparator"] = "before" + char.ConvertFromUtf32(0x2028) + "after",
            },
        };
        using JsonDocument goldens = JsonDocument.Parse(File.ReadAllText(FixturePath("semantic-hashes.json")));

        Assert.AreEqual(
            goldens.RootElement.GetProperty("classic-with-unicode-semantic-extension").GetString(),
            NativeSemanticHasher.Compute(NativePuzzlePackage.Parse(root.ToJsonString())));
    }

    /// <summary>
    /// Excludes presentation geometry and metadata from document-semantic identity.
    /// </summary>
    [TestMethod]
    public void SemanticHashExcludesGeometryAndMetadata()
    {
        NativePuzzlePackage original = ReadPackage("classic-with-auxiliary.json");
        JsonNode changedRoot = ReadFixtureNode("classic-with-auxiliary.json");
        changedRoot["metadata"]!["title"] = "A cosmetic rename";
        changedRoot["cells"]!["aux-1"]!["shape"]!["x"] = 24.5;
        NativePuzzlePackage changed = NativePuzzlePackage.Parse(changedRoot.ToJsonString());

        Assert.AreEqual(NativeSemanticHasher.Compute(original), NativeSemanticHasher.Compute(changed));
    }

    /// <summary>
    /// Includes a native given in document-semantic identity.
    /// </summary>
    [TestMethod]
    public void SemanticHashIncludesProjectedGivens()
    {
        NativePuzzlePackage original = ReadPackage("classic-with-auxiliary.json");
        JsonNode changedRoot = ReadFixtureNode("classic-with-auxiliary.json");
        changedRoot["givens"]!["r1c1"] = "5";
        NativePuzzlePackage changed = NativePuzzlePackage.Parse(changedRoot.ToJsonString());

        Assert.AreNotEqual(NativeSemanticHasher.Compute(original), NativeSemanticHasher.Compute(changed));
    }

    /// <summary>
    /// Includes the complete release payload whenever a constraint references a release.
    /// </summary>
    [TestMethod]
    public void SemanticHashIncludesWholeReferencedReleasePayload()
    {
        JsonNode originalRoot = ReadFixtureNode("classic-with-auxiliary.json");
        originalRoot["constraints"] = JsonNode.Parse("""
            [{"id":"custom-1","typeId":"example.custom","definitionReleaseId":"release-1","bindings":{},"parameters":{}}]
            """);
        originalRoot["release"] = JsonNode.Parse("""
            {"releases":{"release-1":{"artifactHash":"sha256:artifact"}},"compilerVersion":1}
            """);
        JsonNode changedRoot = originalRoot.DeepClone();
        changedRoot["release"]!["compilerVersion"] = 2;

        Assert.AreNotEqual(
            NativeSemanticHasher.Compute(NativePuzzlePackage.Parse(originalRoot.ToJsonString())),
            NativeSemanticHasher.Compute(NativePuzzlePackage.Parse(changedRoot.ToJsonString())));
    }

    /// <summary>
    /// Preserves typed killer arithmetic while reporting that the current projector does not enforce it.
    /// </summary>
    [TestMethod]
    public void PreservesKillerSumAndReportsPartialVerification()
    {
        NativePuzzlePackage package = ReadPackage("four-by-four-killer.json");
        NativeConstraintInstance killer = package.Constraints.Single();
        NativeProjectionResult result = NativePuzzleProjector.Project(package, "main-latin-square");

        Assert.AreEqual(3, killer.Parameters["sum"].GetInt32());
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["killer-1"].Status);
        Assert.AreEqual(
            "Constraint type builtin.killer is preserved but not enforced by projection main-latin-square.",
            result.Capabilities.Entities["killer-1"].Reason);
    }

    /// <summary>
    /// Retains unknown members and namespaced extension data across the native parse boundary.
    /// </summary>
    [TestMethod]
    public void ParseRetainsForwardCompatibleJsonSections()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["futureSection"] = JsonNode.Parse("{\"enabled\":true}");
        root["extensions"]!["example:semantic"] = JsonNode.Parse(
            "{\"impact\":\"semantic\",\"data\":{\"weight\":7}}");

        NativePuzzlePackage package = NativePuzzlePackage.Parse(root.ToJsonString());

        Assert.IsTrue(package.ExtensionData.ContainsKey("futureSection"));
        Assert.AreEqual(7, package.Extensions["example:semantic"].Data.GetProperty("weight").GetInt32());
    }

    /// <summary>
    /// Rejects a missing required cell-input boolean rather than silently inventing false.
    /// </summary>
    [TestMethod]
    public void ParseRejectsMissingRequiredBoolean()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["cells"]!["r1c1"]!["input"]!.AsObject().Remove("acceptsCandidates");

        JsonException exception = Assert.ThrowsExactly<JsonException>(
            () => NativePuzzlePackage.Parse(root.ToJsonString()));

        StringAssert.Contains(exception.Message, "acceptsCandidates");
    }

    /// <summary>
    /// Rejects cell-to-domain references that cannot be resolved anywhere in the package.
    /// </summary>
    [TestMethod]
    public void ParseRejectsCellWithMissingDomain()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["cells"]!["aux-1"]!["domainId"] = "missing";

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => NativePuzzlePackage.Parse(root.ToJsonString()));

        StringAssert.Contains(exception.Message, "cell aux-1 references missing domain missing");
    }

    /// <summary>
    /// Rejects an invalid given on an unprojected cell rather than silently skipping it.
    /// </summary>
    [TestMethod]
    public void ParseRejectsInvalidUnprojectedGiven()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["givens"]!["aux-1"] = "missing";

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => NativePuzzlePackage.Parse(root.ToJsonString()));

        StringAssert.Contains(exception.Message, "given aux-1 references missing value missing");
    }

    /// <summary>
    /// Revalidates a mutable package before projection so post-parse invalid data cannot be skipped.
    /// </summary>
    [TestMethod]
    public void ProjectRejectsInvalidUnprojectedGivenAddedAfterParse()
    {
        NativePuzzlePackage package = ReadPackage("classic-with-auxiliary.json");
        package.Givens["aux-1"] = "missing";

        Assert.ThrowsExactly<ArgumentException>(
            () => NativePuzzleProjector.Project(package, "main-latin-square"));
    }

    /// <summary>
    /// Revalidates a mutable package before hashing so invalid references cannot gain an identity.
    /// </summary>
    [TestMethod]
    public void SemanticHasherRejectsInvalidPackageMutation()
    {
        NativePuzzlePackage package = ReadPackage("classic-with-auxiliary.json");
        package.Givens["missing"] = "1";

        Assert.ThrowsExactly<ArgumentException>(() => NativeSemanticHasher.Compute(package));
    }

    /// <summary>
    /// Reports the representation status of every semantic entity kind used by a package.
    /// </summary>
    [TestMethod]
    public void CapabilityReportCoversEverySemanticEntityKind()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["adjacency"]!["adjacency-1"] = JsonNode.Parse(
            "{\"id\":\"adjacency-1\",\"kind\":\"orthogonal\",\"fromCellId\":\"r1c1\",\"toCellId\":\"r1c2\"}");
        root["points"]!["point-1"] = JsonNode.Parse("{\"id\":\"point-1\",\"x\":0,\"y\":0}");
        root["points"]!["point-2"] = JsonNode.Parse("{\"id\":\"point-2\",\"x\":1,\"y\":0}");
        root["edges"]!["edge-1"] = JsonNode.Parse(
            "{\"id\":\"edge-1\",\"fromPointId\":\"point-1\",\"toPointId\":\"point-2\"}");
        root["paths"]!["path-1"] = JsonNode.Parse(
            "{\"id\":\"path-1\",\"pointIds\":[\"point-1\",\"point-2\"],\"closed\":false}");
        root["constraints"] = JsonNode.Parse("""
            [{"id":"custom-1","typeId":"example.custom","definitionReleaseId":"release-1","bindings":{"edge":[{"kind":"edge","id":"edge-1"}],"path":[{"kind":"path","id":"path-1"}]},"parameters":{}}]
            """);
        root["release"] = JsonNode.Parse(
            "{\"releases\":{\"release-1\":{\"artifactHash\":\"sha256:artifact\"}}}");
        root["extensions"]!["example:semantic"] = JsonNode.Parse(
            "{\"impact\":\"semantic\",\"data\":{\"enabled\":true}}");

        NativeProjectionResult result = NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square");

        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["digits-1-9"].Status);
        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["main"].Status);
        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["row-1"].Status);
        Assert.AreEqual(EntityCapability.FullyVerified, result.Capabilities.Entities["main-latin-square"].Status);
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["adjacency-1"].Status);
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["edge-1"].Status);
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["path-1"].Status);
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["example:semantic"].Status);
        Assert.AreEqual(EntityCapability.PartiallyVerified, result.Capabilities.Entities["release-1"].Status);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Capabilities.Entities["release-1"].Reason));
    }

    /// <summary>
    /// Identifies a referenced definition as invalid when no release payload accompanies it.
    /// </summary>
    [TestMethod]
    public void CapabilityReportMarksMissingReleaseDefinitionInvalid()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["constraints"] = JsonNode.Parse("""
            [{"id":"custom-1","typeId":"example.custom","definitionReleaseId":"release-1","bindings":{},"parameters":{}}]
            """);
        NativeProjectionResult result = NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square");

        Assert.AreEqual(EntityCapability.InvalidDefinition, result.Capabilities.Entities["custom-1"].Status);
        Assert.AreEqual(EntityCapability.InvalidDefinition, result.Capabilities.Entities["release-1"].Status);
        Assert.AreEqual(
            "Constraint custom-1 references definition release release-1, but no release payload is present.",
            result.Capabilities.Entities["custom-1"].Reason);
    }

    /// <summary>
    /// Identifies a definition as invalid when the release payload does not contain its stable ID.
    /// </summary>
    [TestMethod]
    public void CapabilityReportMarksUnknownReleaseDefinitionInvalid()
    {
        JsonNode root = ReadFixtureNode("classic-with-auxiliary.json");
        root["constraints"] = JsonNode.Parse("""
            [{"id":"custom-1","typeId":"example.custom","definitionReleaseId":"release-1","bindings":{},"parameters":{}}]
            """);
        root["release"] = JsonNode.Parse("{\"releases\":{}}");
        NativeProjectionResult result = NativePuzzleProjector.Project(
            NativePuzzlePackage.Parse(root.ToJsonString()),
            "main-latin-square");

        Assert.AreEqual(EntityCapability.InvalidDefinition, result.Capabilities.Entities["custom-1"].Status);
        Assert.AreEqual(EntityCapability.InvalidDefinition, result.Capabilities.Entities["release-1"].Status);
        Assert.AreEqual(
            "Constraint custom-1 references missing definition release release-1.",
            result.Capabilities.Entities["custom-1"].Reason);
    }

    /// <summary>
    /// Preserves typed killer arithmetic through serialization and a second native parse.
    /// </summary>
    [TestMethod]
    public void KillerSumSurvivesSerializeAndReparseRoundTrip()
    {
        NativePuzzlePackage original = ReadPackage("four-by-four-killer.json");

        NativePuzzlePackage reparsed = NativePuzzlePackage.Parse(original.ToJson());

        Assert.AreEqual(3, reparsed.Constraints.Single().Parameters["sum"].GetInt32());
    }

    private static NativePuzzlePackage ReadPackage(string fileName)
        => NativePuzzlePackage.Parse(File.ReadAllText(FixturePath(fileName)));

    private static JsonNode ReadFixtureNode(string fileName)
        => JsonNode.Parse(File.ReadAllText(FixturePath(fileName)))!;

    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "test-fixtures", "native", fileName);
}