using System;
using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Multisum Killer Cage", ConsoleName = "mskiller")]
public class MultiSumKillerCageConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly List<int> sums;
    private SumCellsHelper sumCells;

    private static readonly Regex optionsRegex = new(@"([\d,]+);(.*)");

    public MultiSumKillerCageConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        var match = optionsRegex.Match(options);
        if (match.Success)
        {
            sums = match.Groups[1].Value.Split(',').Select(s => int.Parse(s.Trim())).ToList();
            options = match.Groups[2].Value;
        }
        else
        {
            // No sum provided
            sums = new();
        }

        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Multisum Killer cage expects 1 cell group, got {cellGroups.Count} groups.");
        }
        cells = cellGroups[0];
    }

    public override string SpecificName => sums.Count != 0 ? $"Multisum Killer Cage {String.Join(',', sums)} at {CellName(cells[0])}" : $"Muiltisum Killer Cage at {CellName(cells[0])}";

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells.Count == MAX_VALUE || sums.Count == 0)
        {
            return LogicResult.None;
        }

        sumCells = new(sudokuSolver, cells);
        return sumCells.Init(sudokuSolver, sums);
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Determine if the sum is now complete
        if (sumCells != null && cells.Contains((i, j)) && cells.All(cell => sudokuSolver.IsValueSet(cell.Item1, cell.Item2)))
        {
            return sums.Contains(cells.Select(cell => sudokuSolver.GetValue(cell)).Sum());
        }
        return true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumCells != null ? InitLinksByRunningLogic(sudokuSolver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumCells != null ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return sumCells?.StepLogic(sudokuSolver, sums, logicalStepDescription, isBruteForcing) ?? LogicResult.None;
    }

    public override List<(int, int)> Group => cells;
}
