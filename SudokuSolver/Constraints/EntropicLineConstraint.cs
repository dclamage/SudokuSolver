using System.Runtime.Intrinsics;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Entropic Line", ConsoleName = "entrol")]
public class EntropicLineConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    
    public EntropicLineConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Entropic Line constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
    }

    public EntropicLineConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells) : base(sudokuSolver, cells.CellNames(""))
    {
        this.cells = cells.ToList();
    }

    public override string SpecificName => $"Entropic Line {CellName(cells[0])} - {CellName(cells[^1])}";

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Handled entirely by weak links
        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (!isInitializing)
        {
            return LogicResult.None;
        }

        // Calculate the three groups
        int smallGroupSize = MAX_VALUE / 3;
        int largeGroupSize = (MAX_VALUE + 2) / 3;
        int[] groupSizes = MAX_VALUE % 3 == 1 ? new int[3] { smallGroupSize, largeGroupSize, smallGroupSize } : new int[3] { largeGroupSize, smallGroupSize, largeGroupSize };
        int currentValue = 1;
        List<List<int>> groups = groupSizes.Select(groupSize =>
        {
            List<int> group = new();
            for (int i = 0; i < groupSize; i++)
            {
                group.Add(currentValue++);
            }
            return group;
        }).ToList();

        Dictionary<int, int> valueToGroupIndex = new();
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            foreach (int value in group)
            {
                valueToGroupIndex[value] = groupIndex;
            }
        }

        // Create the weak links
        for (int i0 = 0; i0 < cells.Count; i0++)
        {
            var cell0 = cells[i0];
            int candidateBase0 = CandidateIndex(cell0, 1);
            for (int i1 = i0 + 1; i1 < cells.Count; i1++)
            {
                var cell1 = cells[i1];
                int candidateBase1 = CandidateIndex(cell1, 1);
                int cellDist = (i1 - i0) % groups.Count;

                for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                {
                    int candidate0 = candidateBase0 + v0 - 1;
                    int groupIndex0 = valueToGroupIndex[v0];

                    for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                    {
                        int candidate1 = candidateBase1 + v1 - 1;
                        int groupIndex1 = valueToGroupIndex[v1];

                        if (cellDist == 0 && groupIndex0 != groupIndex1 || cellDist != 0 && groupIndex0 == groupIndex1)
                        {
                            solver.AddWeakLink(candidate0, candidate1);
                        }
                    }
                }
            }
        }

        return LogicResult.None;
    }

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        if (cells.Count < 3)
        {
            return base.SplitToPrimitives(sudokuSolver);
        }

        List<EntropicLineConstraint> constraints = new(cells.Count - 2);
        for (int i = 0; i < cells.Count - 2; i++)
        {
            List<(int, int)> cellsTriple = new() { cells[i], cells[i + 1], cells[i + 2] };
            // Ensure that the lines have consistent order
            if (cellsTriple[0].CompareTo(cellsTriple[2]) > 0)
            {
                cellsTriple.Reverse();
            }
            constraints.Add(new(sudokuSolver, cellsTriple));
        }
        return constraints;
    }

}