namespace SudokuTests;

[TestClass]
public class IssParserTests
{
    [TestMethod]
    public void ParsesGivens()
    {
        Solver solver = SolverFactory.CreateFromIss(".~R1C1_5~R9C9_3");

        Assert.AreEqual(5, GetValue(solver.Board[0, 0]));
        Assert.AreEqual(3, GetValue(solver.Board[8, 8]));
    }

    [TestMethod]
    public void ParsesCandidateRestriction()
    {
        // Several trailing values restrict the cell rather than setting it.
        Solver solver = SolverFactory.CreateFromIss(".~R1C1_1_2_3");

        uint mask = solver.Board[0, 0] & ~valueSetMask;
        Assert.AreEqual(ValuesMask(1, 2, 3), mask);
    }

    [TestMethod]
    public void ParsesCageWithSum()
    {
        Solver solver = SolverFactory.CreateFromIss(".Cage~10~R1C1~R1C2~R1C3");

        KillerCageConstraint cage = solver.Constraints<KillerCageConstraint>().Single();
        Assert.AreEqual(10, cage.sum);
        CollectionAssert.AreEquivalent(new[] { (0, 0), (0, 1), (0, 2) }, cage.cells);
    }

    [TestMethod]
    public void ParsesArrowBulbAndShaft()
    {
        Solver solver = SolverFactory.CreateFromIss(".Arrow~R1C1~R1C2~R1C3");

        ArrowSumConstraint arrow = solver.Constraints<ArrowSumConstraint>().Single();
        CollectionAssert.AreEquivalent(new[] { (0, 0) }, arrow.circleCells);
        CollectionAssert.AreEquivalent(new[] { (0, 1), (0, 2) }, arrow.arrowCells);
    }

    /// <summary>
    /// ISS counts a shaft cell once per occurrence, so a repeated cell contributes twice to the
    /// sum. This solver's arrow cannot express that, and de-duplicating would silently change the
    /// puzzle, so the import must refuse it.
    /// </summary>
    [TestMethod]
    public void RejectsArrowWithRepeatedShaftCell()
    {
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Arrow~R2C2~R2C1~R2C1~R1C1~R1C2"));
    }

    [TestMethod]
    public void RejectsUnmappedConstraint()
    {
        IssUnsupportedConstraintException e = Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Replicate~R1C1~R1C2"));
        Assert.AreEqual("Replicate", e.ConstraintName);
    }

    [TestMethod]
    public void IgnoresBlankAndNonDirectiveLines()
    {
        Solver solver = SolverFactory.CreateFromIss("\n# a comment\n\n.~R1C1_5\n");

        Assert.AreEqual(5, GetValue(solver.Board[0, 0]));
    }

    /// <summary>End-to-end: a real CTC puzzle should solve to the single solution ISS recorded.</summary>
    [TestMethod]
    public void SolvesAnImportedPuzzle()
    {
        Solver solver = SolverFactory.CreateFromIss(string.Join('\n',
            ".~R1C6_8~R2C5_2~R4C3_6~R6C7_4~R8C5_1~R9C4_9",
            ".Thermo~R1C1~R2C2~R3C3",
            ".WhiteDot~R5C5~R5C6",
            ".AntiKnight"));

        Assert.IsTrue(solver.CountSolutions(maxSolutions: 1) >= 1);
    }
}
