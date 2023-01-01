using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Knightmare", ConsoleName = "knightmare")]
    public class KnightmareConstraint : Constraint
    {
        private readonly List<int> disallowedSums = null;

        public KnightmareConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            disallowedSums = options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (disallowedSums.Count == 0)
            {
                disallowedSums = new List<int>() { 5, 15 };
            }
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

        public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription)
        {
            List<(int, int)> disallowedValuePairs = new();
            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                {
                    if (disallowedSums.Contains(v0 + v1))
                    {
                        disallowedValuePairs.Add((v0, v1));
                    }
                }
            }
            if (disallowedValuePairs.Count == 0)
            {
                return LogicResult.None;
            }

            for (int i0 = 0; i0 < HEIGHT; i0++)
            {
                for (int j0 = 0; j0 < WIDTH; j0++)
                {
                    var cell0 = (i0, j0);
                    for (int i = 0; i < 8; i++)
                    {
                        int sign0 = (i & 1) == 0 ? -1 : 1;
                        int sign1 = (i & 2) == 0 ? -1 : 1;
                        var cell1Offset = (i & 4) == 0 ? (1 * sign0, 2 * sign1) : (2 * sign0, 1 * sign1);
                        var cell1 = (i0 + cell1Offset.Item1, j0 + cell1Offset.Item2);
                        if (cell1.Item1 >= 0 && cell1.Item1 < HEIGHT && cell1.Item2 >= 0 && cell1.Item2 < WIDTH)
                        {
                            foreach (var (v0, v1) in disallowedValuePairs)
                            {
                                int cell0CandIndex = CandidateIndex(cell0, v0);
                                int cell1CandIndex = CandidateIndex(cell1, v1);
                                sudokuSolver.AddWeakLink(cell0CandIndex, cell1CandIndex);
                            }
                        }
                    }
                }
            }
            return LogicResult.None;
        }
    }
}
