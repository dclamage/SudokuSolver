namespace SudokuTests;

/// <summary>
/// Milestone 2: pure static-scoring math and budgeted selection for derived sum discovery.
/// </summary>
[TestClass]
public class DerivedSumScoringTests
{
    private static ulong M(params int[] sums)
    {
        ulong mask = 0;
        foreach (int s in sums)
        {
            mask |= 1UL << s;
        }
        return mask;
    }

    private static DerivedSumCandidate Cand(double gain, double score, params int[] cells)
        => new() { Gain = gain, Score = score, WatchedCells = cells };

    [TestMethod]
    public void DifferenceGain_NoRestriction_IsZero()
    {
        // Both sides {5,6,7}, d=0: every P is matched by an equal N and vice versa -> nothing removed.
        double gain = DerivedSumScoring.ScoreDifferenceGain(M(5, 6, 7), M(5, 6, 7), 0, out bool contradiction);
        Assert.IsFalse(contradiction);
        Assert.AreEqual(0.0, gain, 1e-9);
    }

    [TestMethod]
    public void DifferenceGain_Contradiction_IsFlaggedAndZero()
    {
        // P must equal N (d=0) but P={5}, N={1}: no joint solution.
        double gain = DerivedSumScoring.ScoreDifferenceGain(M(5), M(1), 0, out bool contradiction);
        Assert.IsTrue(contradiction);
        Assert.AreEqual(0.0, gain, 1e-9);
    }

    [TestMethod]
    public void DifferenceGain_EdgeRestriction_MatchesHandComputation()
    {
        // P={3,4,5,6,7}, N={1,2,3}, d=4 => P is pinned to N+4 = {5,6,7}; N unchanged.
        // P side: infoGain=log2(5/3)=0.73697, edgeCut=((5-3)+(7-7))/5=0.4 -> +0.8; total 1.53697.
        double gain = DerivedSumScoring.ScoreDifferenceGain(M(3, 4, 5, 6, 7), M(1, 2, 3), 4, out bool contradiction);
        Assert.IsFalse(contradiction);
        Assert.AreEqual(1.53697, gain, 1e-4);
        Assert.IsTrue(gain >= DerivedSumScoring.MinGain);
    }

    [TestMethod]
    public void FixedSumGain_Restriction_MatchesHandComputation()
    {
        // term={5,6,7,8}, allowed={7}: infoGain=log2(4/1)=2, edgeCut=((7-5)+(8-7))/4=0.75 -> +1.5; total 3.5.
        double gain = DerivedSumScoring.ScoreFixedSumGain(M(5, 6, 7, 8), M(7), out bool contradiction);
        Assert.IsFalse(contradiction);
        Assert.AreEqual(3.5, gain, 1e-9);
    }

    [TestMethod]
    public void FixedSumGain_NoRestrictionAndContradiction()
    {
        Assert.AreEqual(0.0, DerivedSumScoring.ScoreFixedSumGain(M(7), M(7), out bool none), 1e-9);
        Assert.IsFalse(none);

        Assert.AreEqual(0.0, DerivedSumScoring.ScoreFixedSumGain(M(5, 6), M(8), out bool contradiction), 1e-9);
        Assert.IsTrue(contradiction);
    }

    [TestMethod]
    public void Select_RespectsMaxConstraintCountAndOrdersByScore()
    {
        // 9 candidates on distinct cells, scores 1..9. Only 8 admitted -> the score-1 one is dropped.
        var candidates = new List<DerivedSumCandidate>();
        for (int i = 1; i <= 9; i++)
        {
            candidates.Add(Cand(gain: 2.0, score: i, cells: i));
        }

        var selected = DerivedSumScoring.SelectWithinBudget(candidates);
        Assert.AreEqual(DerivedSumScoring.MaxDerivedConstraints, selected.Count);
        CollectionAssert.DoesNotContain(selected, candidates[0]); // lowest score excluded
    }

    [TestMethod]
    public void Select_ExcludesBelowMinGain()
    {
        var selected = DerivedSumScoring.SelectWithinBudget(
        [
            Cand(gain: DerivedSumScoring.MinGain - 0.1, score: 100, cells: 0),
            Cand(gain: 2.0, score: 1, cells: 1),
        ]);
        Assert.AreEqual(1, selected.Count);
        Assert.AreEqual(1, selected[0].WatchedCells[0]);
    }

    [TestMethod]
    public void Select_RespectsPerCellWatcherCap()
    {
        // Three candidates all watching only cell 0; cap is 2 per cell.
        var selected = DerivedSumScoring.SelectWithinBudget(
        [
            Cand(2.0, 3.0, 0),
            Cand(2.0, 2.0, 0),
            Cand(2.0, 1.0, 0),
        ]);
        Assert.AreEqual(DerivedSumScoring.MaxDerivedWatchersPerCell, selected.Count);
    }

    [TestMethod]
    public void Select_RespectsIncidenceBudget()
    {
        // Five candidates, 10 distinct cells each (50 total) vs a 48 incidence budget -> 4 admitted.
        var candidates = new List<DerivedSumCandidate>();
        for (int i = 0; i < 5; i++)
        {
            candidates.Add(Cand(gain: 2.0, score: 5 - i, cells: [.. Enumerable.Range(i * 10, 10)]));
        }

        var selected = DerivedSumScoring.SelectWithinBudget(candidates);
        Assert.AreEqual(4, selected.Count);
    }
}
