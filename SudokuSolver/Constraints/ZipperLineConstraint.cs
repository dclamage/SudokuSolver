using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Zipper Line", ConsoleName = "zipper")]
    public class ZipperLineConstraint : EqualSumsConstraint
    {
        public readonly List<(int, int)> lineCells;

        public override string SpecificName => $"Zipper Line from {CellName(lineCells[0])} - {CellName(lineCells[^1])}";

        public ZipperLineConstraint(Solver solver, string options) : base(solver, options)
        {
            var groups = ParseCells(options);
            if (groups.Count != 1)
            {
                throw new ArgumentException($"Zipper Line constraint expects 1 cell group, got {groups.Count}.");
            }

            lineCells = groups[0];
        }

        protected override List<List<(int, int)>> GetCellGroups(Solver solver)
        {
            if (lineCells.Count <= 1)
            {
                return null;
            }

            List<List<(int, int)>> cellGroups = new();
            for (int i = 0; i < (lineCells.Count + 1) / 2; i++)
            {
                int index0 = i;
                int index1 = lineCells.Count - 1 - i;
                List<(int, int)> cells = [lineCells[index0]];
                if (index0 != index1)
                {
                    cells.Add(lineCells[index1]);
                }
                cellGroups.Add(cells);
            }

            return cellGroups;
        }
    }
}
