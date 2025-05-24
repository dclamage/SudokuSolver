namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Clone", ConsoleName = "clone")]
public class CloneConstraint : Constraint
{
    public readonly List<((int, int), (int, int))> cellPairs = new();
    private readonly Dictionary<(int, int), List<(int, int)>> cellToClones = new();

    public CloneConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count == 0)
        {
            throw new ArgumentException($"Clone constraint expects at least 1 cell group.");
        }

        foreach (var group in cellGroups)
        {
            if (group.Count != 2)
            {
                throw new ArgumentException($"Clone cell groups should have exactly 2 cells ({group.Count} in group).");
            }
            var cell0 = group[0];
            var cell1 = group[1];
            if (cell0 == cell1)
            {
                throw new ArgumentException($"Clone cells need to be distinct ({CellName(cell0)}).");
            }

            cellPairs.Add((cell0, cell1));
            cellToClones.AddToList(cell0, cell1);
            cellToClones.AddToList(cell1, cell0);
        }
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        return LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (!isInitializing)
        {
            return LogicResult.None;
        }

        foreach (var (cell0, cell1) in cellPairs)
        {
            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                int candIndex0 = CandidateIndex(cell0, v0);
                int candIndex1 = CandidateIndex(cell1, v0);
                sudokuSolver.AddCloneLink(candIndex0, candIndex1);
            }
        }

        return LogicResult.None;
    }
}
