using System;
using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Killer Cage", ConsoleName = "killer")]
public class KillerCageConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int sum;
    private SumCellsHelper sumCells;

    private static readonly Regex optionsRegex = new(@"(\d+);(.*)");

    public KillerCageConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var match = optionsRegex.Match(options);
        if (match.Success)
        {
            sum = int.Parse(match.Groups[1].Value);
            options = match.Groups[2].Value;
        }
        else
        {
            // No sum provided
            sum = 0;
        }

        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Killer cage expects 1 cell group, got {cellGroups.Count} groups.");
        }
        cells = cellGroups[0];
    }

    public KillerCageConstraint(Solver sudokuSolver, IEnumerable<(int, int)> cells, int sum = 0)
        : base(sudokuSolver, (sum == 0 ? "" : sum + ";") + cells.CellNames(""))
    {
        this.sum = sum;
        this.cells = cells.ToList();
    }

    public override string SpecificName => sum > 0 ? $"Killer Cage {sum} at {CellName(cells[0])}" : $"Killer Cage at {CellName(cells[0])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == MAX_VALUE || sum <= 0)
        {
            return LogicResult.None;
        }

        sumCells = new(sudokuSolver, cells);
        return sumCells.Init(sudokuSolver, sum.ToEnumerable());
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Determine if the sum is now complete
        if (sumCells != null && cells.Contains((i, j)) && cells.All(cell => sudokuSolver.IsValueSet(cell.Item1, cell.Item2)))
        {
            return cells.Select(cell => sudokuSolver.GetValue(cell)).Sum() == sum;
        }
        return true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumCells != null ? InitLinksByRunningLogic(sudokuSolver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumCells != null ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return sumCells?.StepLogic(sudokuSolver, sum.ToEnumerable(), logicalStepDescription) ?? LogicResult.None;
    }

    public override List<(int, int)> Group => cells;

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        // A killer cage with a sum clue is a union of two constraints:
        List<KillerCageConstraint> constraints = new()
        {
            // 1. Digits uniqueness inside the region (represented by a clueless cage with the same cells)
            new(sudokuSolver, cells)
        };

        if (sum != 0)
        {
            // 2. Sum of the cells, represented by the same object
            constraints.Add(new(sudokuSolver, cells, sum));
        }

        return constraints;
    }
}
