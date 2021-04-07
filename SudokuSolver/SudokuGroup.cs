using System.Collections.Generic;

namespace SudokuSolver
{
    public record SudokuGroup(string Name, List<(int, int)> Cells)
    {
        public override string ToString() => Name;
    }
}
