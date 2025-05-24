using SudokuSolver.Constraints.Helpers;

namespace SudokuTests;

[TestClass]
public class ArrowConstraintHelpersTests
{
    private Solver CreateSolver(int size = 3, int maxValue = 3) // Small 3x3 grid for simplicity
    {
        var solver = new Solver(size, size, maxValue);
        // For these helper tests, often we don't need complex constraints,
        // but FinalizeConstraints is good practice for a consistent solver state.
        solver.FinalizeConstraints();
        return solver;
    }

    [TestMethod]
    public void CalculatePillValueFromValues_SingleDigit()
    {
        var values = new List<int> { 5 };
        Assert.AreEqual(5, ArrowConstraintHelpers.CalculatePillValueFromValues(values));
    }

    [TestMethod]
    public void CalculatePillValueFromValues_MultiDigit()
    {
        var values = new List<int> { 1, 2, 3 }; // e.g., for a 12x12 grid where cells can hold >9
        Assert.AreEqual(123, ArrowConstraintHelpers.CalculatePillValueFromValues(values));
    }

    [TestMethod]
    public void CalculatePillValueFromValues_Empty_ReturnsZero()
    {
        var values = new List<int>();
        Assert.AreEqual(0, ArrowConstraintHelpers.CalculatePillValueFromValues(values));
    }

    [TestMethod]
    public void CalculatePillValueFromValues_LeadingZerosInComponents_AreConcatenated()
    {
        // This test assumes components are passed as they would be from cells.
        // E.g. if MAX_VALUE allows "01", "02".
        // Our current cell values are 1-MAX_VALUE, so a cell can't be "01" if it means 1.
        // If a cell is 1, it's just 1. So {1, 2} -> 12.
        // This test is more about string concatenation behavior.
        var values = new List<int> { 0, 1, 2 }; //  If 0 was a valid digit: "0"+"1"+"2" = "012" = 12
        Assert.AreEqual(12, ArrowConstraintHelpers.CalculatePillValueFromValues(values));
    }


    [TestMethod]
    public void PossiblePillArrangements_Sum12_2Cells_Max9()
    {
        var solver = CreateSolver(9, 9); // Standard 9x9
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(12, 2, 9, solver).ToList();

        // Expected: {1,2} (cell1=1, cell2=2), {2,X} no, {3,X} no ...
        // String "12".
        //   sFirst="1", firstVal=1. sRemaining="2", remainingVal=2. Recurse(2, 1 cell) -> yields {2}. Combine: {1,2}
        //   sFirst="12", firstVal=12. firstVal > maxValue (9). Break.
        Assert.AreEqual(1, arrangements.Count);
        CollectionAssert.AreEqual(new int[] { 1, 2 }, arrangements[0]);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum123_3Cells_Max9()
    {
        var solver = CreateSolver(9, 9);
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(123, 3, 9, solver).ToList();
        // "123" -> {1,2,3}
        Assert.AreEqual(1, arrangements.Count);
        CollectionAssert.AreEqual(new int[] { 1, 2, 3 }, arrangements[0]);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum3_2Cells_Max9()
    {
        var solver = CreateSolver(9, 9);
        // Sum "3", 2 cells. No valid arrangement (e.g., {0,3} no, {1,-ve} no)
        // because 1-based digits.
        // sFirst="3", firstVal=3. sRemaining="", remainingVal=0. Recurse(0, 1 cell) -> yields nothing (sum 0 not possible for 1 cell 1-9).
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(3, 2, 9, solver).ToList();
        Assert.AreEqual(0, arrangements.Count);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum21_2Cells_Max9()
    {
        var solver = CreateSolver(9, 9);
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(21, 2, 9, solver).ToList();
        // "21"
        //  sFirst="2", firstVal=2. sRemaining="1", remVal=1. Recurse(1,1 cell) -> {1}. Combine: {2,1}
        Assert.AreEqual(1, arrangements.Count);
        CollectionAssert.AreEqual(new int[] { 2, 1 }, arrangements[0]);
    }


    [TestMethod]
    public void PossiblePillArrangements_Sum0_2Cells_Max9_YieldsNothing()
    {
        var solver = CreateSolver(9, 9);
        // Sum 0, but cells are 1-9. Impossible.
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(0, 2, 9, solver).ToList();
        Assert.AreEqual(0, arrangements.Count);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum1_1Cell_Max9()
    {
        var solver = CreateSolver(9, 9);
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(1, 1, 9, solver).ToList();
        Assert.AreEqual(1, arrangements.Count);
        CollectionAssert.AreEqual(new int[] { 1 }, arrangements[0]);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum10_1Cell_Max9_YieldsNothing()
    {
        var solver = CreateSolver(9, 9);
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(10, 1, 9, solver).ToList();
        Assert.AreEqual(0, arrangements.Count);
    }

    [TestMethod]
    public void PossiblePillArrangements_Sum10_2Cells_Max15_Grid15()
    {
        // For a grid where a cell can be "10"
        var solver = CreateSolver(15, 15); // MAX_VALUE is 15
        var arrangements = ArrowConstraintHelpers.PossiblePillArrangements(10, 2, 15, solver).ToList();

        // "10" -> {1,0} if 0 allowed. Not here.
        // So if it yields {1,0} (string-wise "1"+"0"), this is valid.
        // sFirst="1", firstVal=1. sRemaining="0", remVal=0. Recurse(0,1 cell) -> nothing.
        // Expected: only interpretations like {"1","0"} where 0 is part of number, not a cell val.
        // Our current interpretation of PossiblePillArrangements creates int[] of cell values.
        // So, {1,0} would mean cell1=1, cell2=0. Since 0 not allowed, this combo is invalid.
        // This test highlights a subtle point: does {1,0} from "10" mean first cell is 1, second cell is 0?
        // Yes, that's what the code does. So this should be empty.
        Assert.AreEqual(0, arrangements.Count, "Expected no arrangement for sum 10 with 2 cells (1-15) if 0 is not a cell value.");

        // If we want to test for sum "10" being formed by cell1=1, cell2=0, then 0 needs to be allowed.
        // But sum 10 from two cells 1-15 could be e.g. 1+9 (not a pill), 2+8 etc.
        // Pill arrangements:
        // Cell1=1, Cell2 must form value from string "0". Not possible if 0 isn't allowed as a value.
        // Let's test for sum e.g. 110. Cells: C1, C2. MaxValue 15.
        // "110"
        //  sFirst="1", firstVal=1. sRemaining="10", remVal=10. Recurse(10, 1 cell) -> {10}. Combine: {1,10}
        //  sFirst="11", firstVal=11. sRemaining="0", remVal=0. Recurse(0, 1 cell) -> {}.
        arrangements = ArrowConstraintHelpers.PossiblePillArrangements(110, 2, 15, solver).ToList();
        Assert.AreEqual(1, arrangements.Count);
        CollectionAssert.AreEqual(new int[] { 1, 10 }, arrangements[0]);
    }

    [TestMethod]
    public void GeneratePillTotalsFromCandidates_Simple()
    {
        var solver = CreateSolver(3, 3); // 3x3, MAX_VALUE=3
        var pillCells = new List<(int, int)> { (0, 0), (0, 1) };
        // R1C1 candidates: {1, 2}
        // R1C2 candidates: {3}
        solver.SetMask(0, 0, new int[] { 1, 2 });
        solver.SetMask(0, 1, new int[] { 3 });

        var totals = ArrowConstraintHelpers.GeneratePillTotalsFromCandidates(solver, pillCells);
        // Expected: 13, 23
        Assert.IsTrue(totals.Contains(13));
        Assert.IsTrue(totals.Contains(23));
        Assert.AreEqual(2, totals.Count);
    }

    [TestMethod]
    public void RestrictPillCandidatesBySumSet_Basic()
    {
        var solver = CreateSolver(3, 3); // 3x3, MAX_VALUE=3
        var pillCells = new List<(int, int)> { (0, 0), (0, 1) };
        // R1C1: {1,2,3}, R1C2: {1,2,3}
        // Allowed arrow sums: {12, 21}
        var allowedSums = new HashSet<int> { 12, 21 }; // "12" -> {1,2}, "21" -> {2,1}

        var result = ArrowConstraintHelpers.RestrictPillCandidatesBySumSet(solver, pillCells, allowedSums, null);
        Assert.AreEqual(LogicResult.Changed, result);
        // R1C1 should now be {1,2}
        // R1C2 should now be {1,2}
        uint r1c1Mask = solver.Board[0, 0];
        uint r1c2Mask = solver.Board[0, 1];

        Assert.IsFalse(HasValue(r1c1Mask, 3));
        Assert.IsTrue(HasValue(r1c1Mask, 1));
        Assert.IsTrue(HasValue(r1c1Mask, 2));

        Assert.IsFalse(HasValue(r1c2Mask, 3));
        Assert.IsTrue(HasValue(r1c2Mask, 1));
        Assert.IsTrue(HasValue(r1c2Mask, 2));
    }
}
