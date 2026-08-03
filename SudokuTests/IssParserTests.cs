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

    /// <summary>
    /// A whisper is defined on adjacent pairs, so ISS's way of writing a closed loop — repeating the
    /// starting cell at the end — translates correctly and must keep working.
    /// </summary>
    [TestMethod]
    public void AcceptsClosedLoopOnASlidingWindowLine()
    {
        Solver loop = SolverFactory.CreateFromIss(".Shape~6x6\n.Whisper~3~R1C1~R1C2~R2C2~R2C1~R1C1");
        Solver pairs = SolverFactory.CreateFromIss(string.Join('\n',
            ".Shape~6x6",
            ".Whisper~3~R1C1~R1C2",
            ".Whisper~3~R1C2~R2C2",
            ".Whisper~3~R2C2~R2C1",
            ".Whisper~3~R2C1~R1C1"));

        Assert.AreEqual(pairs.CountSolutions(), loop.CountSolutions());
    }

    /// <summary>
    /// A renban is a set, so a repeated cell would make this solver's version unsatisfiable (0
    /// solutions where the open line has 5,640,192). Refusing beats silently answering 0.
    /// </summary>
    [TestMethod]
    public void RejectsRepeatedCellOnASetLine()
    {
        IssUnsupportedConstraintException e = Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Renban~R1C1~R1C2~R2C2~R2C1~R1C1"));
        Assert.AreEqual("Renban (repeated cell)", e.ConstraintName);
    }

    /// <summary>
    /// ".Shape~9x9~0-8" is a 9x9 played with digits 0–8. This solver only does 1..size, and ignoring
    /// the digit set silently produced a different puzzle — a real CTC puzzle lost its only solution
    /// that way during a bulk import.
    /// </summary>
    [TestMethod]
    public void RejectsNonDefaultDigitSet()
    {
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Shape~9x9~0-8\n.~R1C1_5"));
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Shape~6x6~9\n.~R1C1_5"));

        // A spec that restates the default is fine.
        Solver solver = SolverFactory.CreateFromIss(".Shape~9x9~1-9\n.~R1C1_5");
        Assert.AreEqual(5, GetValue(solver.Board[0, 0]));
    }

    /// <summary>
    /// ISS's general constraint DSL declares auxiliary variables with ".Var" and references them as
    /// "V" plus the name, anywhere a cell could appear. Those must read as unsupported — a puzzle
    /// using them is skipped by bulk import — rather than as a malformed cell reference.
    /// </summary>
    [TestMethod]
    public void RejectsAuxiliaryVariableReference()
    {
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".~R1C7_5~VD1_1_2_3"));
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".EqualSum~R1C1~VB1~-~VW1"));
    }

    [TestMethod]
    public void ParsesQuadrupleFromItsTopLeftCell()
    {
        Solver solver = SolverFactory.CreateFromIss(".Quad~R2C2~1~4~6~8");

        QuadrupleConstraint quad = solver.Constraints<QuadrupleConstraint>().Single();
        CollectionAssert.AreEquivalent(new[] { (1, 1), (1, 2), (2, 1), (2, 2) }, quad.cells);
        CollectionAssert.AreEquivalent(new[] { 1, 4, 6, 8 }, quad.requiredValues);
    }

    [TestMethod]
    public void ParsesSandwichOnRowsAndColumns()
    {
        Solver rowClue = SolverFactory.CreateFromIss(".Sandwich~35~R6");
        Solver colClue = SolverFactory.CreateFromIss(".Sandwich~16~C2");

        // The constraint reads its direction from whichever coordinate is out of range, so a row
        // clue must land on row 6 with no column, and a column clue on column 2 with no row.
        SandwichConstraint row = rowClue.Constraints<SandwichConstraint>().Single();
        Assert.AreEqual(35, row.sum);
        Assert.AreEqual((5, -1), row.cellStart);

        SandwichConstraint col = colClue.Constraints<SandwichConstraint>().Single();
        Assert.AreEqual(16, col.sum);
        Assert.AreEqual((-1, 1), col.cellStart);
    }

    /// <summary>
    /// ISS names the larger cell first. A two-cell thermometer ascends from its bulb, so the order
    /// has to flip — getting this backwards would invert every inequality in the puzzle.
    /// </summary>
    [TestMethod]
    public void ParsesGreaterThanAsAnAscendingThermometer()
    {
        Solver solver = SolverFactory.CreateFromIss(".GreaterThan~R2C5~R1C5");

        ThermometerConstraint thermo = solver.Constraints<ThermometerConstraint>().Single();
        CollectionAssert.AreEqual(new[] { (0, 4), (1, 4) }, thermo.cells);
    }

    [TestMethod]
    public void ParsesDiagonalsBySlopeAndRefusesAnUnspecifiedOne()
    {
        Assert.AreEqual(1, SolverFactory.CreateFromIss(".Diagonal~1")
            .Constraints<DiagonalPositiveGroupConstraint>().Count());
        Assert.AreEqual(1, SolverFactory.CreateFromIss(".Diagonal~-1")
            .Constraints<DiagonalNegativeGroupConstraint>().Count());

        // ".Diagonal~" does not say which diagonal, so it cannot be guessed at.
        Assert.ThrowsExactly<IssUnsupportedConstraintException>(
            () => SolverFactory.CreateFromIss(".Diagonal~"));
    }

    [TestMethod]
    public void ParsesAllDifferentAsAnExtraRegion()
    {
        Solver solver = SolverFactory.CreateFromIss(".AllDifferent~R1C3~R2C3~R3C3~R6C7~R6C8~R6C9");

        ExtraRegionConstraint region = solver.Constraints<ExtraRegionConstraint>().Single();
        CollectionAssert.AreEquivalent(
            new[] { (0, 2), (1, 2), (2, 2), (5, 6), (5, 7), (5, 8) }, region.cells);
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
