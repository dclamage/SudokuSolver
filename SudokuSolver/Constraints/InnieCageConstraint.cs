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
    private readonly int[] cellIndices;
    private SumTerm posTerm;
    private SumTerm negTerm;
    private SumDifferenceRelation sumRelation;

    public InnieCageConstraint(Solver solver, List<(int, int)> posCells, List<(int, int)> negCells, int targetDiff)
        : base(solver, string.Empty)
    {
        this.posCells = posCells;
        this.negCells = negCells;
        this.targetDiff = targetDiff;
        cellIndices = posCells.Concat(negCells)
            .Select(cell => cell.Item1 * WIDTH + cell.Item2)
            .Distinct()
            .Order()
            .ToArray();
    }

    public override string SpecificName => negCells.Count == 0
        ? $"Innie Sum {targetDiff} at {posCells.CellNames()}"
        : $"Innie/Outie {targetDiff} at {posCells.CellNames()} - {negCells.CellNames()}";

    public override LogicResult InitCandidates(Solver solver)
    {
        if (negCells.Count == 0)
        {
            posTerm ??= solver.SumConstraints.RegisterFixedSum(this, posCells, [targetDiff]);
            return posTerm.InitCandidates(solver);
        }

        posTerm ??= solver.SumConstraints.RegisterOpenSum(this, posCells);
        negTerm ??= solver.SumConstraints.RegisterOpenSum(this, negCells);
        sumRelation ??= solver.SumConstraints.RegisterDifference(this, posTerm, negTerm, targetDiff);
        return sumRelation.InitCandidates(solver);
    }

    /// <summary>Measured: 486 ns / 6.9% fire.</summary>

    public override int BruteForcePropagationCost => 7084;


    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        int cellIndex = i * WIDTH + j;
        if (negCells.Count == 0)
        {
            return posTerm?.EnforceComplete(solver, cellIndex) ?? true;
        }
        return sumRelation?.EnforceComplete(solver, cellIndex) ?? true;
    }

    public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (posTerm == null)
        {
            var init = InitCandidates(solver);
            if (init == LogicResult.Invalid) return LogicResult.Invalid;
            if (init == LogicResult.Changed) return LogicResult.Changed;
        }

        if (negCells.Count == 0)
        {
            return posTerm?.StepLogic(solver, logicalStepDescription, isBruteForcing) ?? LogicResult.None;
        }

        return sumRelation?.StepLogic(solver, logicalStepDescription, isBruteForcing) ?? LogicResult.None;
    }

    // No group — no distinctness enforcement for derived innie/outie cells
    public override List<(int, int)> Group => null;
}
