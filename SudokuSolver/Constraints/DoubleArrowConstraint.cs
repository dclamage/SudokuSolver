using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Double Arrow", ConsoleName = "doublearrow")]
    public class DoubleArrowConstraint : EqualSumsConstraint
    {
        public readonly List<(int, int)> lineCells;

        public override string SpecificName => $"Double Arrow from {CellName(lineCells[0])} - {CellName(lineCells[^1])}";

        public DoubleArrowConstraint(Solver solver, string options) : base(solver, options)
        {
            var groups = ParseCells(options);
            if (groups.Count != 1)
            {
                throw new ArgumentException($"Double Arrow constraint expects 1 cell group, got {cellGroups.Count}.");
            }

            lineCells = groups[0];
        }

        protected override List<List<(int, int)>> GetCellGroups(Solver solver)
        {
            if (lineCells.Count < 3)
            {
                return null;
            }

            List<(int, int)> circleCells = new() { lineCells[0], lineCells[^1] };
            List<(int, int)> betweenCells = lineCells.Skip(1).Take(lineCells.Count - 2).ToList();
            return new() { circleCells, betweenCells };
        }
    }
}
