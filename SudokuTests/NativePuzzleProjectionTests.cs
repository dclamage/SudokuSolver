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

    private static NativePuzzlePackage ReadPackage(string fileName)
        => NativePuzzlePackage.Parse(File.ReadAllText(FixturePath(fileName)));

    private static JsonNode ReadFixtureNode(string fileName)
        => JsonNode.Parse(File.ReadAllText(FixturePath(fileName)))!;

    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "test-fixtures", "native", fileName);
}