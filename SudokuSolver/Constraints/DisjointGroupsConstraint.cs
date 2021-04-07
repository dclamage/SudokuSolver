using System;
using System.Collections.Generic;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Disjoint Groups", ConsoleName = "djg", FPuzzlesName = "disjointgroups")]
    public class DisjointConstraintGroup : IConstraintGroup
    {
        public DisjointConstraintGroup(string _)
        {
        }

        public void AddConstraints(Solver solver)
        {
            for (int i = 0; i < BOX_CELL_COUNT; i++)
            {
                solver.AddConstraint(new DisjointGroupConstraint(i));
            }
        }
    }

    [Constraint(DisplayName = "Disjoint Group", ConsoleName = "disjointoffset")]
    public class DisjointGroupConstraint : Constraint
    {
        public int GroupIndex { get; set; }

        public DisjointGroupConstraint(string options)
        {
            GroupIndex = int.Parse(options) - 1;
        }

        public DisjointGroupConstraint(int groupIndex)
        {
            GroupIndex = groupIndex;
        }

        public override string SpecificName => $"Disjoint Group {GroupIndex + 1}";

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;

        public override List<(int, int)> Group
        {
            get
            {
                if (_group != null)
                {
                    return _group;
                }

                int groupi = GroupIndex / BOX_WIDTH;
                int groupj = GroupIndex % BOX_WIDTH;

                _group = new(9);
                for (int boxi = 0; boxi < NUM_BOXES_HEIGHT; boxi++)
                {
                    int celli = boxi * BOX_HEIGHT + groupi;
                    for (int boxj = 0; boxj < NUM_BOXES_WIDTH; boxj++)
                    {
                        int cellj = boxj * BOX_WIDTH + groupj;
                        _group.Add((celli, cellj));
                    }
                }
                return _group;
            }
        }
        private List<(int, int)> _group = null;
    }
}
