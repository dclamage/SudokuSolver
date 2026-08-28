using System.Collections.Generic;

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
    private readonly int[] cellIndices;
    private SumTerm sumTerm = null;

    private static readonly Regex optionsRegex = new(@"(\d+);[rR](\d+)[cC](\d+);([UD][LR])");

    public LittleKillerConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
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
        cellIndices = cellsList.Select(cell => cell.Item1 * WIDTH + cell.Item2).Order().ToArray();
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

    public override string SpecificName => $"Little Killer {sum} at {CellName(cellStart)}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cellsList.Count == 0 || sum <= 0)
        {
            return LogicResult.None;
        }

        sumTerm ??= sudokuSolver.SumConstraints.RegisterFixedSum(this, cellsList, [sum]);
        return sumTerm.InitCandidates(sudokuSolver);
    }

    /// <summary>Measured: 971 ns / 25.9% fire, corpus.json.</summary>

    public override int BruteForcePropagationCost => 3753;


    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return sumTerm?.EnforceComplete(sudokuSolver, i * WIDTH + j) ?? true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumTerm != null ? InitLinksByRunningLogic(sudokuSolver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumTerm != null ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return sumTerm?.StepLogic(sudokuSolver, logicalStepDescription, isBruteForcing) ?? LogicResult.None;
    }
}
