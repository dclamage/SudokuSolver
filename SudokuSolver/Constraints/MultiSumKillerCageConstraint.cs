using System;
using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Multisum Killer Cage", ConsoleName = "mskiller")]
public class MultiSumKillerCageConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly List<int> sums;
    private SumTerm sumTerm;

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

        sumTerm ??= sudokuSolver.SumConstraints.RegisterFixedSum(this, cells, sums);
        return sumTerm.InitCandidates(sudokuSolver);
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        return sumTerm?.EnforceComplete(sudokuSolver, i * WIDTH + j) ?? true;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumTerm != null ? InitLinksByRunningLogic(sudokuSolver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumTerm?.CellsMustContain(sudokuSolver, value);
    public override bool MustContainValue(Solver sudokuSolver, int value) => sumTerm?.MustContainValue(sudokuSolver, value) ?? false;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return sumTerm?.StepLogic(sudokuSolver, logicalStepDescription, isBruteForcing) ?? LogicResult.None;
    }

    public override List<(int, int)> Group => cells;
}
