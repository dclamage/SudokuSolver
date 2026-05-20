namespace SudokuSolver.Constraints;

// Derived sum constraint from innie/outie overlap analysis.
// Enforces: Σ(posCells) - Σ(negCells) = targetDiff.
// For pure innie (negCells empty): Σ(posCells) = targetDiff.
// Does NOT enforce digit distinctness — that's handled by existing cage/region groups.
public class InnieCageConstraint : Constraint
{
    private readonly List<(int, int)> posCells;
    private readonly List<(int, int)> negCells;
    private readonly int targetDiff;
    private SumCellsHelper posHelper;
    private SumCellsHelper negHelper;

    public InnieCageConstraint(Solver solver, List<(int, int)> posCells, List<(int, int)> negCells, int targetDiff)
        : base(solver, string.Empty)
    {
        this.posCells = posCells;
        this.negCells = negCells;
        this.targetDiff = targetDiff;
    }

    public override string SpecificName => negCells.Count == 0
        ? $"Innie Sum {targetDiff} at {posCells.CellNames()}"
        : $"Innie/Outie {targetDiff} at {posCells.CellNames()} - {negCells.CellNames()}";

    public override LogicResult InitCandidates(Solver solver)
    {
        posHelper = new SumCellsHelper(solver, posCells);

        if (negCells.Count == 0)
        {
            return posHelper.Init(solver, [targetDiff]);
        }

        negHelper = new SumCellsHelper(solver, negCells);

        var (posMin, posMax) = posHelper.SumRange(solver);
        var (negMin, negMax) = negHelper.SumRange(solver);

        if (posMin == 0 || posMax == 0 || negMin == 0 || negMax == 0)
        {
            return LogicResult.None;
        }

        // posSum = negSum + targetDiff
        int validPosMin = Math.Max(posMin, negMin + targetDiff);
        int validPosMax = Math.Min(posMax, negMax + targetDiff);
        int validNegMin = Math.Max(negMin, posMin - targetDiff);
        int validNegMax = Math.Min(negMax, posMax - targetDiff);

        if (validPosMin > validPosMax || validNegMin > validNegMax)
        {
            return LogicResult.Invalid;
        }

        bool changed = false;
        if (validPosMin > posMin || validPosMax < posMax)
        {
            var r = posHelper.Init(solver, new[] { validPosMin, validPosMax });
            if (r == LogicResult.Invalid) return LogicResult.Invalid;
            if (r == LogicResult.Changed) changed = true;
        }
        if (validNegMin > negMin || validNegMax < negMax)
        {
            var r = negHelper.Init(solver, new[] { validNegMin, validNegMax });
            if (r == LogicResult.Invalid) return LogicResult.Invalid;
            if (r == LogicResult.Changed) changed = true;
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        bool isPos = posCells.Contains((i, j));
        bool isNeg = negCells.Contains((i, j));
        if (!isPos && !isNeg) return true;

        // Verify sum once all cells are set
        foreach (var c in posCells)
            if (!solver.IsValueSet(c.Item1, c.Item2)) return true;
        foreach (var c in negCells)
            if (!solver.IsValueSet(c.Item1, c.Item2)) return true;

        int posSum = 0;
        foreach (var c in posCells) posSum += solver.GetValue(c);
        int negSum = 0;
        foreach (var c in negCells) negSum += solver.GetValue(c);
        return posSum - negSum == targetDiff;
    }

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (posHelper == null)
        {
            var init = InitCandidates(solver);
            if (init == LogicResult.Invalid) return LogicResult.Invalid;
            if (init == LogicResult.Changed) return LogicResult.Changed;
        }

        if (negHelper == null)
        {
            return posHelper?.StepLogic(solver, [targetDiff], logicalStepDescription) ?? LogicResult.None;
        }

        // Cross-restrict: posSum - negSum = targetDiff
        var posSums = posHelper.PossibleSums(solver);
        var negSums = negHelper.PossibleSums(solver);

        if (posSums == null || posSums.Count == 0 || negSums == null || negSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        var negSumSet = new HashSet<int>(negSums);
        var posSumSet = new HashSet<int>(posSums);
        var validPosSums = posSums.Where(p => negSumSet.Contains(p - targetDiff)).ToList();
        var validNegSums = negSums.Where(n => posSumSet.Contains(n + targetDiff)).ToList();

        if (validPosSums.Count == 0 || validNegSums.Count == 0)
        {
            return LogicResult.Invalid;
        }

        bool changed = false;
        var r1 = posHelper.StepLogic(solver, validPosSums, (StringBuilder)null);
        if (r1 == LogicResult.Invalid) return LogicResult.Invalid;
        if (r1 == LogicResult.Changed) changed = true;

        var r2 = negHelper.StepLogic(solver, validNegSums, (StringBuilder)null);
        if (r2 == LogicResult.Invalid) return LogicResult.Invalid;
        if (r2 == LogicResult.Changed) changed = true;

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    // No group — no distinctness enforcement for derived innie/outie cells
    public override List<(int, int)> Group => null;
}
