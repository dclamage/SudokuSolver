#nullable enable

using SudokuSolver.PuzzleFormats.Native;
using SudokuSolverService;
using SudokuSolverService.Protocol;
using SudokuTests.Helpers;

namespace SudokuTests;

/// <summary>Verifies the stable native true-candidates protocol.</summary>
[TestClass]
public sealed class NativeTrueCandidatesProtocolTests
{
    /// <summary>Verifies each display mode returns its requested count cap and mask shape.</summary>
    [TestMethod]
    [DataRow("possibility", 1L, false)]
    [DataRow("solutionFrequency", 8L, false)]
    [DataRow("logicComparison", 1L, true)]
    public void TrueCandidateModesReturnCountsAndOptionalLogicalMasks(
        string display,
        long cap,
        bool expectLogicalMasks)
    {
        SolverResponse response = RunTrueCandidates(display, cap);
        TrueCandidatesResultDto result = response.TrueCandidates!;

        Assert.AreEqual(cap, result.SolutionCountCap);
        Assert.HasCount(81 * 9, result.SolutionCounts);
        Assert.IsTrue(result.SolutionCounts.All(count => count >= 0));
        Assert.AreEqual(expectLogicalMasks, result.LogicalCandidateMasks is not null);
        if (expectLogicalMasks)
        {
            Assert.HasCount(81, result.LogicalCandidateMasks!);
        }
    }

    /// <summary>Verifies native identifiers stay in projection order and auxiliary cells are omitted.</summary>
    [TestMethod]
    public void TrueCandidatesReturnStableProjectedIdsWithoutAuxiliaryCells()
    {
        TrueCandidatesResultDto result = RunTrueCandidates("possibility", 1).TrueCandidates!;

        CollectionAssert.AreEqual(
            Enumerable.Range(1, 9)
                .SelectMany(row => Enumerable.Range(1, 9).Select(column => $"r{row}c{column}"))
                .ToArray(),
            result.CellIds);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 9).Select(value => value.ToString()).ToArray(),
            result.ValueIdsBySolverValue);
        CollectionAssert.DoesNotContain(result.CellIds, "aux-1");
    }

    /// <summary>Verifies unsupported display values are rejected at the native boundary.</summary>
    [TestMethod]
    public void TrueCandidatesRejectInvalidDisplay()
    {
        SolverResponse response = RunRaw("futureDisplay", 1);

        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("invalidPackage", response.Error?.Code);
    }

    /// <summary>Verifies the count cap is restricted to the supported positive range.</summary>
    [TestMethod]
    [DataRow(0L)]
    [DataRow(1025L)]
    public void TrueCandidatesRejectOutOfRangeCap(long cap)
    {
        SolverResponse response = RunRaw("possibility", cap);

        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("invalidPackage", response.Error?.Code);
    }

    /// <summary>Verifies comparison masks include logical deductions, not just projected givens.</summary>
    [TestMethod]
    public void LogicComparisonReturnsCandidatesAfterLogicalConsolidation()
    {
        SolverResponse response = RunRaw(NativeRequestFixtures.TrueCandidatesForClassic());
        Assert.AreEqual("result", response.Kind);
        Assert.IsNull(response.Error);
        TrueCandidatesResultDto result = response.TrueCandidates!;
        int[] projectedMasks = NativePuzzleProjector
            .Project(NativeRequestFixtures.ClassicPackage(), "main-latin-square")
            .Solver.FlatBoard
            .Select(mask => unchecked((int)(mask & ~SolverUtility.valueSetMask)))
            .ToArray();

        Assert.IsTrue(
            projectedMasks.Zip(
                result.LogicalCandidateMasks!,
                (projected, logical) =>
                    logical != projected && (logical & projected) == logical)
                .Any(eliminated => eliminated),
            "Comparison masks must contain a logical elimination beyond the projected givens.");
    }

    /// <summary>Verifies every display reports an unsatisfiable but locally consistent puzzle as contradictory.</summary>
    [TestMethod]
    [DataRow("possibility")]
    [DataRow("solutionFrequency")]
    [DataRow("logicComparison")]
    public void TrueCandidatesRejectUnsatisfiablePuzzle(string display)
    {
        SolverResponse response = RunRaw(
            NativeRequestFixtures.TrueCandidatesForUnsatisfiableClassic(display));

        Assert.AreEqual("error", response.Kind);
        Assert.AreEqual("contradiction", response.Error?.Code);
        StringAssert.Contains(response.Error?.Message, "No solutions found");
        Assert.IsNull(response.TrueCandidates);
    }

    private static SolverResponse RunTrueCandidates(string display, long cap)
    {
        SolverResponse response = RunRaw(display, cap);
        Assert.AreEqual("result", response.Kind);
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.TrueCandidates);
        return response;
    }

    private static SolverResponse RunRaw(string display, long cap)
        => RunRaw(NativeRequestFixtures.TrueCandidates(display, cap, contextId: "true-candidates"));

    private static SolverResponse RunRaw(string request)
    {
        List<string> responses = [];
        new SolverCommandProcessor(singleThreaded: true).Handle(
            request,
            responses.Add,
            CancellationToken.None);

        SolverResponse[] parsed = responses.Select(SolverResponse.Parse).ToArray();
        Assert.IsFalse(parsed.Any(response => response.Kind == "error") &&
            parsed.Any(response => response.Kind == "result"));
        return parsed.Single(response => response.Kind is "result" or "error");
    }
}
