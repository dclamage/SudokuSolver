namespace SudokuTests;

/// <summary>
/// Covers <see cref="QuadrupleConstraint"/>, with the emphasis on quadruples that ask for the same
/// digit more than once. Those are rare but real -- <c>.Quad~R3C3~1~9~1~4</c> appears in the ISS
/// corpus -- and they are exactly what a "distinct required values" bitmask silently gets wrong, so
/// they are what pins down the multiset bookkeeping shared by <c>EnforceConstraint</c> and
/// <c>StepLogic</c>.
/// </summary>
/// <remarks>
/// The repeated-digit cases all use a quadruple straddling the box boundary at
/// r3c3/r3c4/r4c3/r4c4, and ask for <b>two 1s and nothing else</b>. Both details are load-bearing:
/// <list type="bullet">
/// <item>The straddle is what makes a repeat possible at all. In an aligned 2x2 all four cells share
/// a box, so no digit can appear twice; only r3c3/r4c4 and r3c4/r4c3 see neither each other's row,
/// column nor box.</item>
/// <item>Asking for fewer digits than there are cells keeps <c>InitCandidates</c> from restricting
/// the four cells to the required values. When it does restrict them, ordinary row/column/box logic
/// solves these positions on its own and the test stops testing the quadruple at all.</item>
/// </list>
/// <para>
/// Checked by mutation: making a placed digit satisfy every required copy of itself, rather than one,
/// fails <see cref="RepeatedValue_SecondCopyIsStillRequiredAfterTheFirstIsPlaced"/>. Collapsing the
/// outstanding *count* to a distinct-value count is by contrast close to an equivalent mutation --
/// the availability mask and the hidden-single pass catch it downstream on every position tried here
/// -- so treat the count as defensive rather than as something these tests pin down.
/// </para>
/// </remarks>
[TestClass]
public class QuadrupleConstraintTests
{
    // Two 1s required across a box-straddling 2x2.
    private const string TwoOnes = "quad:1;1;r3c3r3c4r4c3r4c4";

    private static Solver Blank(string constraint) => SolverFactory.CreateBlank(9, [constraint]);

    [TestMethod]
    public void RepeatedValue_SecondCopyIsStillRequiredAfterTheFirstIsPlaced()
    {
        Solver solver = Blank(TwoOnes);

        // r3c3 = 1 takes the first copy, and its own weak links rule 1 out of r3c4 (same row) and
        // r4c3 (same column), leaving r4c4 as the only home for the second copy.
        // r4c6 = 1 then shares row 4 and box 5 with r4c4, which takes that last home away.
        bool ok = solver.SetValue(2, 2, 1) && solver.SetValue(3, 5, 1);

        Assert.IsTrue(!ok || solver.ConsolidateBoard() == LogicResult.Invalid,
            "One 1 is placed but a second is still required, and no cell can hold it");
    }

    [TestMethod]
    public void RepeatedValue_NeedsAsManyCellsAsCopies()
    {
        Solver solver = Blank(TwoOnes);

        // Rule 1 out of three of the four cells without touching r3c3, by placing a 1 in each of the
        // other three boxes the quadruple reaches into: box 2 covers r3c4, box 4 covers r4c3, box 5
        // covers r4c4.
        Assert.IsTrue(solver.SetValue(0, 4, 1), "r1c5 = 1");
        Assert.IsTrue(solver.SetValue(4, 0, 1), "r5c1 = 1");
        Assert.IsTrue(solver.SetValue(5, 5, 1), "r6c6 = 1");

        Assert.AreEqual(LogicResult.Invalid, solver.ConsolidateBoard(),
            "Only r3c3 can still be 1, which is one cell for two required copies");
    }

    [TestMethod]
    public void RepeatedValue_BothCopiesPlacedIsAccepted()
    {
        Solver solver = Blank(TwoOnes);

        // r3c3 and r4c4 share no row, column or box, so both may be 1.
        Assert.IsTrue(solver.SetValue(2, 2, 1), "r3c3 = 1");
        Assert.IsTrue(solver.SetValue(3, 3, 1), "r4c4 = 1");

        Assert.AreNotEqual(LogicResult.Invalid, solver.ConsolidateBoard(),
            "Two 1s in the quadruple satisfy a quadruple asking for two 1s");
    }

    [TestMethod]
    public void PlacedDigitThatWasNeverRequiredSatisfiesNothing()
    {
        // Only 1 and 2 are required, of four cells, so the other two digits are free -- but filling
        // every cell with something else must still be rejected.
        Solver solver = Blank("quad:1;2;r1c1r1c2r2c1r2c2");

        bool ok = solver.SetValue(0, 0, 3)
            && solver.SetValue(0, 1, 4)
            && solver.SetValue(1, 0, 5)
            && solver.SetValue(1, 1, 6);

        Assert.IsTrue(!ok || solver.ConsolidateBoard() == LogicResult.Invalid,
            "A quadruple requiring 1 and 2 must reject a board containing neither");
    }

    [TestMethod]
    public void FourDistinctValuesRestrictTheFourCellsToThoseValues()
    {
        Solver solver = Blank("quad:1;4;6;8;r2c2r2c3r3c2r3c3");

        uint expected = ValuesMask(1, 4, 6, 8);
        foreach (var (i, j) in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
        {
            Assert.AreEqual(expected, solver.Board[i, j] & ~valueSetMask,
                $"r{i + 1}c{j + 1} should be restricted to 1/4/6/8");
        }
    }

    [TestMethod]
    public void RequiredValueWithNowhereToGoIsRejected()
    {
        Solver solver = Blank("quad:9;r3c3r3c4r4c3r4c4");

        // Place a 9 in each of the four boxes the quadruple reaches into, so no cell of it can be 9.
        bool ok = solver.SetValue(0, 0, 9)
            && solver.SetValue(0, 4, 9)
            && solver.SetValue(4, 1, 9)
            && solver.SetValue(5, 5, 9);

        Assert.IsTrue(!ok || solver.ConsolidateBoard() == LogicResult.Invalid,
            "A quadruple whose required 9 has no remaining home must be invalid");
    }
}
