using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Disjoint Groups", ConsoleName = "djg")]
    public class DisjointConstraintGroup : IConstraintGroup
    {
        public DisjointConstraintGroup(Solver sudokuSolver, string _)
        {
        }

        public void AddConstraints(Solver solver)
        {
            for (int i = 0; i < solver.Regions.Length; i++)
            {
                solver.AddConstraint(new DisjointGroupConstraint(solver, i));
            }
        }
    }

    [Constraint(DisplayName = "Disjoint Group", ConsoleName = "disjointoffset")]
    public class DisjointGroupConstraint : Constraint
    {
        private readonly int groupIndex = 0;
        private List<(int, int)> group = null;

        public DisjointGroupConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            groupIndex = int.Parse(options) - 1;
        }

        public DisjointGroupConstraint(Solver sudokuSolver, int groupIndex) : base(sudokuSolver)
        {
            this.groupIndex = groupIndex;
        }

        public override string SpecificName => $"Disjoint Group {groupIndex + 1}";

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            group = new(WIDTH);

            var regions = sudokuSolver.Regions;
            int[] numSeen = new int[WIDTH];
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    int region = regions[i, j];
                    if (numSeen[region]++ == groupIndex)
                    {
                        group.Add((i, j));
                    }
                }
            }
            return LogicResult.None;
        }

        public override List<(int, int)> Group => group;
    }
}
