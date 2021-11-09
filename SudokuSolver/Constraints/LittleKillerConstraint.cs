namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Little Killer", ConsoleName = "lk")]
public class LittleKillerConstraint : Constraint
{
    public enum Direction
    {
        UpRight,
        UpLeft,
        DownRight,
        DownLeft,
    }

    public readonly (int, int) outerCell;
    public readonly Direction direction;
    public readonly int sum;
    private readonly (int, int) cellStart;
    private readonly HashSet<(int, int)> cells;
    private readonly List<(int, int)> cellsList;
    private SumCellsHelper sumCells = null;

    private static readonly Regex optionsRegex = new(@"(\d+);[rR](\d+)[cC](\d+);([UD][LR])");

    public LittleKillerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"Little Killer options \"{options}\" invalid. Expecting: \"sum;rXcY;UL|UR|DL|DR\"");
        }

        sum = int.Parse(match.Groups[1].Value);

        outerCell = cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

        direction = Direction.UpRight;
        switch (match.Groups[4].Value)
        {
            case "UR":
                direction = Direction.UpRight;
                break;
            case "UL":
                direction = Direction.UpLeft;
                break;
            case "DR":
                direction = Direction.DownRight;
                break;
            case "DL":
                direction = Direction.DownLeft;
                break;
        }

        // F-Puzzles starts off the grid, so allow one step to enter the grid if necessary
        if (cellStart.Item1 < 0 || cellStart.Item1 >= HEIGHT || cellStart.Item2 < 0 || cellStart.Item2 >= WIDTH)
        {
            cellStart = NextCell(cellStart);
        }
        else
        {
            outerCell = PrevCell(cellStart);
        }

        // If the cell start is still invalid, then this is an error.
        if (cellStart.Item1 < 0 || cellStart.Item1 >= HEIGHT || cellStart.Item2 < 0 || cellStart.Item2 >= WIDTH)
        {
            throw new ArgumentException($"Little Killer options \"{options}\" invalid. Starting cell is invalid.");
        }

        cells = new HashSet<(int, int)>();
        (int, int) cell = cellStart;
        while (cell.Item1 >= 0 && cell.Item1 < HEIGHT && cell.Item2 >= 0 && cell.Item2 < WIDTH)
        {
            cells.Add(cell);
            cell = NextCell(cell);
        }
        cellsList = new(cells);
    }

    private (int, int) NextCell((int, int) cell)
    {
        switch (direction)
        {
            case Direction.UpRight:
                cell = (cell.Item1 - 1, cell.Item2 + 1);
                break;
            case Direction.UpLeft:
                cell = (cell.Item1 - 1, cell.Item2 - 1);
                break;
            case Direction.DownRight:
                cell = (cell.Item1 + 1, cell.Item2 + 1);
                break;
            case Direction.DownLeft:
                cell = (cell.Item1 + 1, cell.Item2 - 1);
                break;
        }
        return cell;
    }

    private (int, int) PrevCell((int, int) cell)
    {
        switch (direction)
        {
            case Direction.UpRight:
                cell = (cell.Item1 + 1, cell.Item2 - 1);
                break;
            case Direction.UpLeft:
                cell = (cell.Item1 + 1, cell.Item2 + 1);
                break;
            case Direction.DownRight:
                cell = (cell.Item1 - 1, cell.Item2 - 1);
                break;
            case Direction.DownLeft:
                cell = (cell.Item1 - 1, cell.Item2 + 1);
                break;
        }
        return cell;
    }

    public override string SpecificName => $"Little Killer at {CellName(cellStart)}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cellsList.Count == 0 || sum <= 0)
        {
            return LogicResult.None;
        }

        sumCells = new SumCellsHelper(sudokuSolver, cellsList);
        return sumCells.Init(sudokuSolver, sum.ToEnumerable());
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (sumCells == null || !cells.Contains((i, j)))
        {
            return true;
        }

        var board = sudokuSolver.Board;

        int actualSum = 0;
        foreach (var cell in cells)
        {
            uint mask = board[cell.Item1, cell.Item2];
            if (!IsValueSet(mask))
            {
                return true;
            }
            actualSum += GetValue(mask);
        }
        return sum == actualSum;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return sumCells?.StepLogic(sudokuSolver, sum.ToEnumerable(), logicalStepDescription) ?? LogicResult.None;
    }
}
