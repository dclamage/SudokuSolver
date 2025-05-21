namespace SudokuSolver;

public sealed record SudokuGroup(GroupType GroupType, string Name, List<int> Cells, Constraint FromConstraint) : IComparable<SudokuGroup>
{
    public override string ToString() => Name;

    public bool MustContain(Solver solver, int val)
    {
        if (Cells.Count == solver.MAX_VALUE)
        {
            return true;
        }

        var mustContain = FromConstraint?.CellsMustContain(solver, val);
        return mustContain != null && mustContain.Count > 0;
    }

    public List<(int, int)> CellsMustContain(Solver solver, int val)
    {
        var flatBoard = solver.FlatBoard;
        if (Cells.Count == solver.MAX_VALUE)
        {
            return Cells
                .Where(cellIndex => HasValue(flatBoard[cellIndex], val))
                .Select(solver.CellIndexToCoord)
                .ToList();
        }
        if (FromConstraint != null)
        {
            return FromConstraint.CellsMustContain(solver, val);
        }
        return null;
    }

    int IComparable<SudokuGroup>.CompareTo(SudokuGroup other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (GroupType != other.GroupType)
        {
            return (int)GroupType - (int)other.GroupType;
        }

        if (Name != other.Name)
        {
            return Name.CompareTo(other.Name);
        }

        if (Cells.Count != other.Cells.Count)
        {
            return Cells.Count - other.Cells.Count;
        }

        for (int i = 0; i < Cells.Count; i++)
        {
            int compare = Cells[i].CompareTo(other.Cells[i]);
            if (compare != 0)
            {
                return compare;
            }
        }

        return 0;
    }
}
