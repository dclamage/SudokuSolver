using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Extra Region", ConsoleName = "extraregion")]
    public class ExtraRegionConstraint : Constraint
    {
        public readonly List<(int, int)> cells;

        public ExtraRegionConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 1)
            {
                throw new ArgumentException($"Extra region expects 1 cell group, got {cellGroups.Count} groups.");
            }
            cells = cellGroups[0];
        }

        public override string SpecificName => $"Extra Region at {cells[0]}";

        public override LogicResult InitCandidates(Solver sudokuSolver) => LogicResult.None;

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val) => true;

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing) => LogicResult.None;

        public override List<(int, int)> Group => cells;
    }
}
