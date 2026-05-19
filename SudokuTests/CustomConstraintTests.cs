using System.Text.Json;
using LZStringCSharp;
using SudokuSolver.PuzzleFormats;

namespace SudokuTests;

[TestClass]
public class CustomConstraintTests
{
    // Compiled by: node scripts/compile-constraint.mjs scripts/examples/strictly-increasing.mjs
    private const string StrictlyIncreasingNFA = "TgZf8SNFZ4n_EaKzxP8NFZ4n8IrPE_BWeJ8DPE8B4nARMAkAAA";

    // Compiled by: node scripts/compile-constraint.mjs scripts/examples/sum-to-ten.mjs
    private const string SumToTenTable = "AAEAAIAAAABAAAAAIAAAABAAAAAIAAAABAAAAAIAAAABAAAA";

    // Hand-crafted: 1 state, start+accept, all 9 symbols loop back to state 0.
    // Bytes: [0x42, 0x2F, 0xFC, 0x00]
    private const string AcceptAllNFA = "Qi_8AA";

    // Compiled from the provided running-sum-to-10 NFA script.
    private const string AddsToTenNFA = "TgRf8jRWeJoAf80VniaF_orPE0J_VniaEfs8TQh94mhB8TQgeaEB0IBE";

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static int CI(int row, int col) => row * 9 + col;

    private static uint Candidates(Solver solver, int cellIndex) =>
        solver.FlatBoard[cellIndex] & ~valueSetMask;

    private static Solver MakeBlankSolver()
    {
        var solver = new Solver(9, 9, 9);
        solver.SetRegions(DefaultRegions(9));
        return solver;
    }

    private static byte[] GetComparableData(FPuzzlesBoard board)
    {
        string fpuzzlesJson = JsonSerializer.Serialize(board);
        string fpuzzlesBase64 = LZString.CompressToBase64(fpuzzlesJson);
        Solver solver = SolverFactory.CreateFromFPuzzles(fpuzzlesBase64);
        Assert.IsTrue(solver.customInfo.TryGetValue("ComparableData", out object comparableDataObj));
        Assert.IsInstanceOfType(comparableDataObj, typeof(byte[]));
        return (byte[])comparableDataObj;
    }

    private static FPuzzlesBoard MakeBlankBoard()
    {
        return new FPuzzlesBoard
        {
            size = 9,
            title = "cache-key-test",
            author = "test",
            ruleset = string.Empty,
            grid = Enumerable.Range(0, 9)
                .Select(_ => Enumerable.Range(0, 9)
                    .Select(_ => new FPuzzlesGridEntry())
                    .ToArray())
                .ToArray(),
        };
    }

    // Creates a solver with an NFA constraint applied, already finalized.
    private static (Solver solver, NFAConstraint nfa) MakeNFASolver(int[] cells, string nfa, string name = null)
    {
        var solver = MakeBlankSolver();
        var constraint = new NFAConstraint(solver, cells, nfa, name);
        solver.AddConstraint(constraint);
        bool valid = solver.FinalizeConstraints();
        Assert.IsTrue(valid, "FinalizeConstraints unexpectedly returned false");
        return (solver, constraint);
    }

    // Creates a solver with a BinaryLookupConstraint applied, already finalized.
    private static (Solver solver, BinaryLookupConstraint binary) MakeBinarySolver(
        int ci0, int ci1, string tableBase64, string name = null)
    {
        var solver = MakeBlankSolver();
        uint[] tableAB = BinaryLookupConstraint.DecodeTable(tableBase64);
        uint[] tableBA = BinaryLookupConstraint.BuildReverseTable(tableAB, 9);
        var constraint = new BinaryLookupConstraint(solver, ci0, ci1, tableAB, tableBA, name);
        solver.AddConstraint(constraint);
        bool valid = solver.FinalizeConstraints();
        Assert.IsTrue(valid, "FinalizeConstraints unexpectedly returned false");
        return (solver, constraint);
    }

    // Reads all set candidates for a cell as a sorted list of values.
    private static List<int> CandidateList(Solver solver, int cellIndex)
    {
        uint mask = Candidates(solver, cellIndex);
        var list = new List<int>();
        for (int v = 1; v <= 9; v++)
            if ((mask & ValueMask(v)) != 0)
                list.Add(v);
        return list;
    }

    private static List<int> TrueCandidateList(long[] counts, int cellIndex)
    {
        var list = new List<int>();
        for (int v = 1; v <= 9; v++)
            if (counts[cellIndex * 9 + v - 1] > 0)
                list.Add(v);
        return list;
    }

    // ---------------------------------------------------------------------------
    // DecodeTable / BuildReverseTable
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void DecodeTable_SumToTen_CorrectPairs()
    {
        uint[] tableAB = BinaryLookupConstraint.DecodeTable(SumToTenTable);
        Assert.AreEqual(9, tableAB.Length);

        // Valid pairs are (v1, 10-v1) for v1 in 1..9
        for (int v1 = 1; v1 <= 9; v1++)
        {
            int v2 = 10 - v1;
            uint expected = ValueMask(v2);
            Assert.AreEqual(expected, tableAB[v1 - 1],
                $"tableAB[{v1 - 1}] should allow only {v2} (got {tableAB[v1 - 1]:X})");
        }
    }

    [TestMethod]
    public void BuildReverseTable_SumToTen_IsSymmetric()
    {
        uint[] tableAB = BinaryLookupConstraint.DecodeTable(SumToTenTable);
        uint[] tableBA = BinaryLookupConstraint.BuildReverseTable(tableAB, 9);

        // For sum-to-10, the relation is symmetric: tableBA should equal tableAB.
        for (int v = 0; v < 9; v++)
            Assert.AreEqual(tableAB[v], tableBA[v],
                $"tableBA[{v}] should equal tableAB[{v}] for a symmetric relation");
    }

    // ---------------------------------------------------------------------------
    // NFAConstraint — empty/trivial NFA
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void NFA_EmptyString_FinalizeReturnsFalse()
    {
        var solver = MakeBlankSolver();
        var constraint = new NFAConstraint(solver, [CI(0, 0), CI(0, 1)], string.Empty);
        solver.AddConstraint(constraint);
        bool valid = solver.FinalizeConstraints();
        Assert.IsFalse(valid, "An empty NFA should make the board invalid (no accepting paths)");
    }

    [TestMethod]
    public void NFA_AcceptAll_NoEliminations()
    {
        int[] cells = [CI(0, 0), CI(0, 1), CI(0, 2)];
        var (solver, _) = MakeNFASolver(cells, AcceptAllNFA);

        // Accept-all NFA should leave all 9 candidates in every constrained cell
        foreach (int ci in cells)
            Assert.AreEqual(9, CandidateList(solver, ci).Count,
                $"Cell {ci} should still have 9 candidates with accept-all NFA");
    }

    [TestMethod]
    public void NFA_AddsToTen_TwoCells_NoEliminations()
    {
        int[] cells = [CI(0, 0), CI(0, 1)];
        var (solver, _) = MakeNFASolver(cells, AddsToTenNFA, "Adds to Ten");

        CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToList(), CandidateList(solver, cells[0]),
            "The first cell should keep all digits 1..9 for a two-cell sum-to-10 NFA.");
        CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToList(), CandidateList(solver, cells[1]),
            "The second cell should keep all digits 1..9 for a two-cell sum-to-10 NFA.");
    }

    [TestMethod]
    public void FPuzzlesNFA_AddsToTen_TwoCells_NoEliminations()
    {
        FPuzzlesBoard board = MakeBlankBoard();
        board.nfaConstraints =
        [
            new FPuzzlesNFAConstraintEntry
            {
                cells = ["R1C1", "R1C2"],
                nfa = AddsToTenNFA,
                name = "Adds to Ten"
            }
        ];

        string fpuzzlesJson = JsonSerializer.Serialize(board);
        string fpuzzlesBase64 = LZString.CompressToBase64(fpuzzlesJson);
        Solver solver = SolverFactory.CreateFromFPuzzles(fpuzzlesBase64);

        CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToList(), CandidateList(solver, CI(0, 0)),
            "FPuzzles import should keep all digits 1..9 in R1C1 for the two-cell sum-to-10 NFA.");
        CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToList(), CandidateList(solver, CI(0, 1)),
            "FPuzzles import should keep all digits 1..9 in R1C2 for the two-cell sum-to-10 NFA.");
    }

    [TestMethod]
    public void FPuzzlesNFA_AddsToTen_TwoCells_TrueCandidatesAreComprehensive()
    {
        FPuzzlesBoard board = MakeBlankBoard();
        board.nfaConstraints =
        [
            new FPuzzlesNFAConstraintEntry
            {
                cells = ["R1C1", "R1C2"],
                nfa = AddsToTenNFA,
                name = "Adds to Ten"
            }
        ];

        string fpuzzlesJson = JsonSerializer.Serialize(board);
        string fpuzzlesBase64 = LZString.CompressToBase64(fpuzzlesJson);
        Solver solver = SolverFactory.CreateFromFPuzzles(fpuzzlesBase64);
        long[] counts = solver.TrueCandidates(multiThread: true, numSolutionsCap: 1);
        var expected = new List<int> { 1, 2, 3, 4, 6, 7, 8, 9 };

        CollectionAssert.AreEqual(expected, TrueCandidateList(counts, CI(0, 0)),
            "R1C1 should see every sum-to-10 value except 5, which would force a duplicate 5 in the row.");
        CollectionAssert.AreEqual(expected, TrueCandidateList(counts, CI(0, 1)),
            "R1C2 should see every sum-to-10 value except 5, which would force a duplicate 5 in the row.");
    }

    [TestMethod]
    public void NFAComparableData_DifferentCells_ChangesCacheKey()
    {
        FPuzzlesBoard board1 = MakeBlankBoard();
        board1.nfaConstraints =
        [
            new FPuzzlesNFAConstraintEntry
            {
                cells = ["R1C1", "R1C2"],
                nfa = StrictlyIncreasingNFA,
                name = "strict"
            }
        ];

        FPuzzlesBoard board2 = MakeBlankBoard();
        board2.nfaConstraints =
        [
            new FPuzzlesNFAConstraintEntry
            {
                cells = ["R2C1", "R2C2"],
                nfa = StrictlyIncreasingNFA,
                name = "strict"
            }
        ];

        byte[] comparableData1 = GetComparableData(board1);
        byte[] comparableData2 = GetComparableData(board2);

        Assert.IsFalse(comparableData1.SequenceEqual(comparableData2),
            "ComparableData should include NFA constraint placement so true-candidates cache keys do not collide.");
    }

    [TestMethod]
    public void NFAIsInheritOf_DifferentCells_IsFalse()
    {
        var (solver1, _) = MakeNFASolver([CI(0, 0), CI(0, 1)], StrictlyIncreasingNFA);
        var (solver2, _) = MakeNFASolver([CI(1, 0), CI(1, 1)], StrictlyIncreasingNFA);

        Assert.IsFalse(solver2.IsInheritOf(solver1),
            "IsInheritOf should distinguish NFA constraints by constrained cells to avoid websocket true-candidates cache reuse.");
    }

    // ---------------------------------------------------------------------------
    // NFAConstraint — strictly-increasing (2 cells)
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void NFA_StrictlyIncreasing_TwoCells_EliminatesLowAndHigh()
    {
        // cell0 < cell1 strictly.
        // cell0 can't be 9 (nothing greater); cell1 can't be 1 (nothing smaller).
        int[] cells = [CI(0, 0), CI(0, 1)];
        var (solver, _) = MakeNFASolver(cells, StrictlyIncreasingNFA);

        var c0 = CandidateList(solver, cells[0]);
        var c1 = CandidateList(solver, cells[1]);

        CollectionAssert.DoesNotContain(c0, 9, "cell0 can't be 9 (no valid partner above)");
        CollectionAssert.DoesNotContain(c1, 1, "cell1 can't be 1 (no valid partner below)");

        // 1..8 remain for cell0, 2..9 remain for cell1
        Assert.AreEqual(8, c0.Count, "cell0 should have values 1..8");
        Assert.AreEqual(8, c1.Count, "cell1 should have values 2..9");
    }

    [TestMethod]
    public void NFA_StrictlyIncreasing_ThreeCells_CorrectRanges()
    {
        // cell0 < cell1 < cell2. Min spread is 1,2,3 and max is 7,8,9.
        int[] cells = [CI(0, 0), CI(0, 1), CI(0, 2)];
        var (solver, _) = MakeNFASolver(cells, StrictlyIncreasingNFA);

        var c0 = CandidateList(solver, cells[0]);
        var c1 = CandidateList(solver, cells[1]);
        var c2 = CandidateList(solver, cells[2]);

        // cell0 ∈ {1..7}, cell1 ∈ {2..8}, cell2 ∈ {3..9}
        CollectionAssert.AreEqual(Enumerable.Range(1, 7).ToList(), c0, "cell0 range wrong");
        CollectionAssert.AreEqual(Enumerable.Range(2, 7).ToList(), c1, "cell1 range wrong");
        CollectionAssert.AreEqual(Enumerable.Range(3, 7).ToList(), c2, "cell2 range wrong");
    }

    [TestMethod]
    public void NFA_StrictlyIncreasing_GivenFirstCell_ConstrainsSecond()
    {
        // If cell0 is fixed to 5, cell1 must be in {6,7,8,9}.
        int ci0 = CI(1, 0);
        int ci1 = CI(1, 1);
        var (solver, _) = MakeNFASolver([ci0, ci1], StrictlyIncreasingNFA);

        // Set cell0 = 5
        bool setOk = solver.SetValue(ci0 / 9, ci0 % 9, 5);
        Assert.IsTrue(setOk);

        // Trigger constraint propagation
        var result = solver.ConsolidateBoard();
        Assert.AreNotEqual(LogicResult.Invalid, result);

        var c1 = CandidateList(solver, ci1);
        Assert.IsTrue(c1.All(v => v > 5),
            $"After setting cell0=5, cell1 must be > 5; got [{string.Join(",", c1)}]");
    }

    [TestMethod]
    public void NFA_StrictlyIncreasing_ImpossibleSequence_IsInvalid()
    {
        // If we set cell0=9 and cell1=1, the NFA must reject the board.
        var solver = MakeBlankSolver();
        var constraint = new NFAConstraint(solver, [CI(0, 0), CI(0, 1)], StrictlyIncreasingNFA);
        solver.AddConstraint(constraint);
        solver.FinalizeConstraints();

        // Force cell0=9 (sets value, eliminates 1..8)
        bool ok0 = solver.SetValue(0, 0, 9);
        // Force cell1=1
        bool ok1 = solver.SetValue(0, 1, 1);

        // Either a SetValue fails immediately, or ConsolidateBoard must return Invalid.
        bool isInvalid = !ok0 || !ok1 || solver.ConsolidateBoard() == LogicResult.Invalid;
        Assert.IsTrue(isInvalid, "Board with cell0=9, cell1=1 violating strictly-increasing should be invalid");
    }

    // ---------------------------------------------------------------------------
    // BinaryLookupConstraint — sum-to-10
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Binary_SumToTen_BlankBoard_NoEliminations()
    {
        int ci0 = CI(0, 0);
        int ci1 = CI(0, 1);
        var (solver, _) = MakeBinarySolver(ci0, ci1, SumToTenTable);

        // Every v from 1..9 has exactly one valid partner (10-v), so no value is unsupported.
        Assert.AreEqual(9, CandidateList(solver, ci0).Count, "cell0 should keep all 9 candidates");
        Assert.AreEqual(9, CandidateList(solver, ci1).Count, "cell1 should keep all 9 candidates");
    }

    [TestMethod]
    public void BinaryLookupComparableData_DifferentCells_ChangesCacheKey()
    {
        FPuzzlesBoard board1 = MakeBlankBoard();
        board1.binaryLookupConstraints =
        [
            new FPuzzlesBinaryLookupEntry
            {
                cells = ["R1C1", "R1C2"],
                table = SumToTenTable,
                name = "sum-to-ten"
            }
        ];

        FPuzzlesBoard board2 = MakeBlankBoard();
        board2.binaryLookupConstraints =
        [
            new FPuzzlesBinaryLookupEntry
            {
                cells = ["R2C1", "R2C2"],
                table = SumToTenTable,
                name = "sum-to-ten"
            }
        ];

        byte[] comparableData1 = GetComparableData(board1);
        byte[] comparableData2 = GetComparableData(board2);

        Assert.IsFalse(comparableData1.SequenceEqual(comparableData2),
            "ComparableData should include BinaryLookup constraint placement so true-candidates cache keys do not collide.");
    }

    [TestMethod]
    public void BinaryLookupIsInheritOf_DifferentTables_IsFalse()
    {
        var (solver1, _) = MakeBinarySolver(CI(0, 0), CI(0, 1), SumToTenTable);

        uint[] identityTable = Enumerable.Range(1, 9)
            .Select(v => ValueMask(v))
            .ToArray();
        uint[] identityReverse = BinaryLookupConstraint.BuildReverseTable(identityTable, 9);
        var solver2 = MakeBlankSolver();
        solver2.AddConstraint(new BinaryLookupConstraint(solver2, CI(0, 0), CI(0, 1), identityTable, identityReverse));
        Assert.IsTrue(solver2.FinalizeConstraints(), "FinalizeConstraints unexpectedly returned false");

        Assert.IsFalse(solver2.IsInheritOf(solver1),
            "IsInheritOf should distinguish BinaryLookup constraints by lookup table to avoid websocket true-candidates cache reuse.");
    }

    [TestMethod]
    public void Binary_SumToTen_GivenFirstCell_FixesSecond()
    {
        // If cell0 = 3, then cell1 must be 7 (3+7=10).
        int ci0 = CI(0, 0);
        int ci1 = CI(0, 1);
        var (solver, _) = MakeBinarySolver(ci0, ci1, SumToTenTable);

        bool ok = solver.SetValue(0, 0, 3);
        Assert.IsTrue(ok);

        var result = solver.ConsolidateBoard();
        Assert.AreNotEqual(LogicResult.Invalid, result);

        var c1 = CandidateList(solver, ci1);
        CollectionAssert.AreEqual(new List<int> { 7 }, c1,
            $"After cell0=3, cell1 must be 7; got [{string.Join(",", c1)}]");
    }

    [TestMethod]
    public void Binary_SumToTen_GivenSecondCell_FixesFirst()
    {
        // Symmetric: if cell1 = 6, then cell0 must be 4 (4+6=10).
        int ci0 = CI(0, 0);
        int ci1 = CI(0, 1);
        var (solver, _) = MakeBinarySolver(ci0, ci1, SumToTenTable);

        bool ok = solver.SetValue(0, 1, 6);
        Assert.IsTrue(ok);

        var result = solver.ConsolidateBoard();
        Assert.AreNotEqual(LogicResult.Invalid, result);

        var c0 = CandidateList(solver, ci0);
        CollectionAssert.AreEqual(new List<int> { 4 }, c0,
            $"After cell1=6, cell0 must be 4; got [{string.Join(",", c0)}]");
    }

    [TestMethod]
    public void Binary_SumToTen_NoValidPair_IsInvalid()
    {
        // Manually build a table where no pair is allowed (all zeros).
        var solver = MakeBlankSolver();
        uint[] emptyTable = new uint[9]; // all zeros
        uint[] emptyBA = new uint[9];
        var constraint = new BinaryLookupConstraint(solver, CI(0, 0), CI(0, 1), emptyTable, emptyBA);
        solver.AddConstraint(constraint);

        bool valid = solver.FinalizeConstraints();
        Assert.IsFalse(valid, "A table with no allowed pairs should make the board invalid");
    }

    [TestMethod]
    public void Binary_SumToTen_RestrictedCell_NarrowsOther()
    {
        // If cell0's candidates are restricted to {1,2,3} before finalization, then
        // cell1 must be in {7,8,9} after propagation.
        var solver = MakeBlankSolver();
        uint[] tableAB = BinaryLookupConstraint.DecodeTable(SumToTenTable);
        uint[] tableBA = BinaryLookupConstraint.BuildReverseTable(tableAB, 9);
        var constraint = new BinaryLookupConstraint(solver, CI(0, 0), CI(0, 1), tableAB, tableBA);
        solver.AddConstraint(constraint);

        // Restrict cell0 to {1,2,3} before finalizing
        int ci0 = CI(0, 0);
        for (int v = 4; v <= 9; v++)
            solver.ClearValue(ci0, v);

        bool valid = solver.FinalizeConstraints();
        Assert.IsTrue(valid);

        var c1 = CandidateList(solver, CI(0, 1));
        CollectionAssert.AreEqual(new List<int> { 7, 8, 9 }, c1,
            $"With cell0 in {{1,2,3}}, cell1 should be in {{7,8,9}}; got [{string.Join(",", c1)}]");
    }
}
