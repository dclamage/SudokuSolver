#nullable enable

using SudokuSolver;
using SudokuSolver.Logical;

namespace SudokuTests;

/// <summary>Verifies the pure structured logical-deduction domain.</summary>
[TestClass]
public sealed class LogicalDeductionServiceTests
{
    /// <summary>Proves discovery observes the current board without changing it.</summary>
    [TestMethod]
    public void FindAvailableDoesNotMutateTheBoard()
    {
        LogicalPosition position = CreatePosition();
        Solver solver = position.Solver;
        string before = solver.CandidateString;

        IReadOnlyList<LogicalDeduction> deductions = LogicalDeductionService.FindAvailable(position);

        Assert.AreEqual(before, solver.CandidateString);
        Assert.AreEqual("basic.naked-single", deductions.Single().TechniqueId);
    }

    /// <summary>Proves a deduction cannot be applied after its position has changed.</summary>
    [TestMethod]
    public void ApplyRejectsAChangedPosition()
    {
        LogicalPosition position = CreatePosition();
        LogicalDeduction deduction = LogicalDeductionService.FindAvailable(position).Single();
        DeductionPlacement placement = deduction.Delta.Placements.Single();
        Assert.IsTrue(position.Solver.SetValue(0, 0, position.GetSolverValue(placement.ValueId)));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => LogicalDeductionService.Apply(position, deduction));
    }

    /// <summary>Locks the first technique to stable semantic references rather than presentation data.</summary>
    [TestMethod]
    public void NakedSingleContainsStableDeltaAndSemanticFrame()
    {
        LogicalDeduction deduction = LogicalDeductionService.FindAvailable(CreatePosition()).Single();

        Assert.AreEqual("basic.naked-single", deduction.TechniqueId);
        Assert.AreEqual("builtin:latin-square", deduction.OwningConstraintId);
        Assert.IsEmpty(deduction.Premises);
        DeductionPlacement placement = deduction.Delta.Placements.Single();
        Assert.AreEqual("r1c1", placement.CellId);
        Assert.AreEqual("3", placement.ValueId);
        Assert.IsEmpty(deduction.Delta.Eliminations);
        WalkthroughFrame frame = deduction.Frames.Single();
        Assert.AreEqual("logical.nakedSingle.place", frame.Explanation.Key);
        CollectionAssert.AreEqual(
            new[] { new LogicalEntityReference("cell", "r1c1"), new LogicalEntityReference("value", "3") },
            frame.Highlight.ToArray());
        Assert.IsTrue(frame.Explanation.Arguments.Any(argument =>
            argument.Kind == "cell" && argument.Value == "r1c1"));
        Assert.IsTrue(frame.Explanation.Arguments.Any(argument =>
            argument.Kind == "value" && argument.Value == "3"));
    }

    /// <summary>Proves deduction and position identities are deterministic but content-sensitive.</summary>
    [TestMethod]
    public void HashesAndDeductionIdsAreDeterministicAndContentSensitive()
    {
        LogicalPosition first = CreatePosition();
        LogicalPosition equivalent = CreatePosition();
        LogicalDeduction firstDeduction = LogicalDeductionService.FindAvailable(first).Single();
        LogicalDeduction equivalentDeduction = LogicalDeductionService.FindAvailable(equivalent).Single();

        Assert.AreEqual(first.PositionHash, equivalent.PositionHash);
        Assert.AreEqual(firstDeduction.Id, equivalentDeduction.Id);

        LogicalPosition otherSource = CreatePosition("sha256:other-source");
        LogicalDeduction otherSourceDeduction = LogicalDeductionService.FindAvailable(otherSource).Single();
        Assert.AreNotEqual(first.PositionHash, otherSource.PositionHash);
        Assert.AreNotEqual(firstDeduction.Id, otherSourceDeduction.Id);

        LogicalPosition otherCell = CreatePositionWithMissingSolutionIndex(1);
        LogicalDeduction otherCellDeduction = LogicalDeductionService.FindAvailable(otherCell).Single();
        Assert.AreNotEqual(firstDeduction.Id, otherCellDeduction.Id);
    }

    /// <summary>Proves every available naked single is returned in stable board order.</summary>
    [TestMethod]
    public void FindAvailableReturnsEveryNakedSingleInCellOrder()
    {
        LogicalPosition position = CreatePositionWithMissingSolutionIndices(0, 1);

        LogicalDeduction[] deductions = LogicalDeductionService.FindAvailable(position).ToArray();

        Assert.HasCount(2, deductions);
        CollectionAssert.AreEqual(
            new[] { "r1c1", "r1c2" },
            deductions.Select(deduction => deduction.Delta.Placements.Single().CellId).ToArray());
    }

    /// <summary>Proves a complete position has no available deduction.</summary>
    [TestMethod]
    public void SolvedPositionHasNoAvailableDeduction()
    {
        Solver solver = SolverFactory.CreateFromGivens(Puzzles.uniqueClassics[0].Item2);
        LogicalPosition position = CreatePosition(solver, "sha256:solved");

        Assert.IsEmpty(LogicalDeductionService.FindAvailable(position));
    }

    /// <summary>Proves an altered identifier cannot authorize the otherwise valid delta.</summary>
    [TestMethod]
    public void ApplyRejectsForgedDeductionWithoutMutation()
    {
        LogicalPosition position = CreatePosition();
        LogicalDeduction deduction = LogicalDeductionService.FindAvailable(position).Single();
        string before = position.Solver.CandidateString;
        LogicalDeduction forged = deduction with { Id = "sha256:forged" };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => LogicalDeductionService.Apply(position, forged));
        Assert.AreEqual(before, position.Solver.CandidateString);
    }

    /// <summary>Proves apply returns a stable-ID board and a new precondition hash.</summary>
    [TestMethod]
    public void ApplyReturnsNextStableBoard()
    {
        LogicalPosition position = CreatePosition();
        LogicalDeduction deduction = LogicalDeductionService.FindAvailable(position).Single();

        LogicalApplyResult result = LogicalDeductionService.Apply(position, deduction);

        Assert.AreNotEqual(deduction.PreconditionHash, result.PositionHash);
        LogicalCellState first = result.Cells.Single(cell => cell.CellId == "r1c1");
        Assert.AreEqual("3", first.ValueId);
        Assert.IsEmpty(first.CandidateValueIds);
    }

    /// <summary>Rejects ambiguous or incomplete stable mappings at the domain boundary.</summary>
    [TestMethod]
    public void PositionRejectsInvalidStableMappings()
    {
        Solver solver = SolverFactory.CreateFromGivens(Puzzles.uniqueClassics[0].Item2);
        string[] cellIds = CellIds();
        cellIds[1] = cellIds[0];

        Assert.ThrowsExactly<ArgumentException>(
            () => new LogicalPosition(solver, cellIds, ValueIds(), "sha256:test"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LogicalPosition(solver, CellIds()[..80], ValueIds(), "sha256:test"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LogicalPosition(solver, CellIds(), new[] { "1", "1", "3", "4", "5", "6", "7", "8", "9" }, "sha256:test"));
    }

    /// <summary>Proves callers cannot alter stable mappings after position construction.</summary>
    [TestMethod]
    public void PositionMappingsAreReadOnlySnapshots()
    {
        LogicalPosition position = CreatePosition();
        IList<string> cellIds = (IList<string>)position.CellIds;
        IList<string> valueIds = (IList<string>)position.ValueIdsBySolverValue;

        Assert.ThrowsExactly<NotSupportedException>(() => cellIds[0] = "changed-cell");
        Assert.ThrowsExactly<NotSupportedException>(() => valueIds[0] = "changed-value");
        Assert.AreEqual("r1c1", position.GetCellId(0));
        Assert.AreEqual("1", position.GetValueId(1));
    }

    /// <summary>Proves an owned clone can advance without changing its source position.</summary>
    [TestMethod]
    public void CloneIsolatesLogicalBoardState()
    {
        LogicalPosition source = CreatePosition();
        LogicalPosition clone = source.Clone();
        string sourceBefore = source.Solver.CandidateString;

        _ = LogicalDeductionService.Apply(clone, LogicalDeductionService.FindAvailable(clone).Single());

        Assert.AreEqual(sourceBefore, source.Solver.CandidateString);
        Assert.AreNotEqual(source.PositionHash, clone.PositionHash);
    }

    private static LogicalPosition CreatePosition(string semanticHash = "sha256:logical-test")
        => CreatePositionWithMissingSolutionIndices(semanticHash, 0);

    private static LogicalPosition CreatePositionWithMissingSolutionIndex(int index)
        => CreatePositionWithMissingSolutionIndices("sha256:logical-test", index);

    private static LogicalPosition CreatePositionWithMissingSolutionIndices(params int[] indices)
        => CreatePositionWithMissingSolutionIndices("sha256:logical-test", indices);

    private static LogicalPosition CreatePositionWithMissingSolutionIndices(string semanticHash, params int[] indices)
    {
        char[] givens = Puzzles.uniqueClassics[0].Item2.ToCharArray();
        foreach (int index in indices)
        {
            givens[index] = '.';
        }
        return CreatePosition(SolverFactory.CreateFromGivens(givens), semanticHash);
    }

    private static LogicalPosition CreatePosition(Solver solver, string semanticHash)
        => new(solver, CellIds(), ValueIds(), semanticHash);

    private static string[] CellIds()
        => Enumerable.Range(1, 9)
            .SelectMany(row => Enumerable.Range(1, 9).Select(column => $"r{row}c{column}"))
            .ToArray();

    private static string[] ValueIds()
        => Enumerable.Range(1, 9).Select(value => value.ToString()).ToArray();
}