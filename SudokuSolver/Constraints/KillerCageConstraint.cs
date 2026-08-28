using System;
using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Killer Cage", ConsoleName = "killer")]
public class KillerCageConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int sum;
    private SumTerm sumTerm;

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

        sumTerm ??= sudokuSolver.SumConstraints.RegisterFixedSum(this, cells, [sum]);
        return sumTerm.InitCandidates(sudokuSolver);
    }

    /// <summary>Measured: 179 ns / 12.7% fire.</summary>

    public override int BruteForcePropagationCost => 1411;


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
