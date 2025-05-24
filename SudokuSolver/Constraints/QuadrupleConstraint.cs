using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Quadruple", ConsoleName = "quad")]
public class QuadrupleConstraint : Constraint
{
    public readonly List<(int, int)> cells = null;
    private readonly HashSet<(int, int)> cellsLookup;
    public readonly List<int> requiredValues = new();
    private readonly uint requiredMask;
    private List<(int, int)> groupCells = null;

    public override string SpecificName => $"Quadruple at {CellName(cells[0])}";

    public override List<(int, int)> Group => groupCells;

    public QuadrupleConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        foreach (var group in options.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(group, out int value))
            {
                requiredValues.Add(value);
                requiredMask |= ValueMask(value);
            }
            else
            {
                var cellGroups = ParseCells(group);
                if (cells != null)
                {
                    throw new ArgumentException($"Quadruple constraint expects only one cell group.");
                }
                cells = cellGroups[0];
            }
        }

        if (cells == null)
        {
            throw new ArgumentException($"Quadruple constraint expects a cell group.");
        }

        cellsLookup = new(cells);
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        if (cells == null || requiredValues.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;
        uint availableMask = 0;
        List<(int, int)> possibleCells = new();
        foreach (var (i, j) in cells)
        {
            uint cellMask = board[i, j];
            if ((cellMask & requiredMask) != 0)
            {
                possibleCells.Add((i, j));
            }
            availableMask |= cellMask;
        }

        if ((availableMask & requiredMask) != requiredMask)
        {
            return LogicResult.Invalid;
        }

        if (possibleCells.Count < requiredValues.Count)
        {
            return LogicResult.Invalid;
        }

        bool changed = false;
        if (possibleCells.Count == requiredValues.Count)
        {
            foreach (var (i, j) in possibleCells)
            {
                uint clearMask = ~requiredMask & ALL_VALUES_MASK;
                var clearResult = sudokuSolver.ClearMask(i, j, clearMask);
                if (clearResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= clearResult == LogicResult.Changed;
            }

            if (ValueCount(requiredMask) == requiredValues.Count)
            {
                groupCells = possibleCells;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells == null || requiredValues.Count == 0)
        {
            return true;
        }

        var board = sudokuSolver.Board;
        if (cellsLookup.Contains((i, j)))
        {
            List<int> remainingValues = requiredValues.ToList();
            foreach (var cell in cells)
            {
                uint cellMask = board[cell.Item1, cell.Item2];
                if (IsValueSet(cellMask))
                {
                    remainingValues.Remove(GetValue(cellMask));
                }
            }

            if (remainingValues.Count > 0)
            {
                uint availableMask = 0;
                foreach (var cell in cells)
                {
                    uint cellMask = board[cell.Item1, cell.Item2];
                    if (!IsValueSet(cellMask))
                    {
                        availableMask |= cellMask;
                    }
                }

                uint remainingMask = 0;
                foreach (var value in remainingValues)
                {
                    remainingMask |= ValueMask(value);
                }

                if ((availableMask & remainingMask) != remainingMask)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => (cells != null && requiredMask != 0) ? InitLinksByRunningLogic(solver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => (cells != null && requiredMask != 0) ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells == null || requiredValues.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;

        List<int> remainingValues = requiredValues.ToList();
        foreach (var cell in cells)
        {
            uint cellMask = board[cell.Item1, cell.Item2];
            if (IsValueSet(cellMask))
            {
                remainingValues.Remove(GetValue(cellMask));
            }
        }

        if (remainingValues.Count == 0)
        {
            return LogicResult.None;
        }

        uint remainingRequiredMask = 0;
        foreach (var value in remainingValues)
        {
            remainingRequiredMask |= ValueMask(value);
        }
        
        int numRemainingRequired = remainingValues.Count;

        uint availableMask = 0;
        List<(int, int)> possibleCells = new();
        foreach (var (i, j) in cells)
        {
            uint cellMask = board[i, j];
            if (IsValueSet(cellMask))
            {
                continue;
            }

            if ((cellMask & remainingRequiredMask) != 0)
            {
                possibleCells.Add((i, j));
            }
            availableMask |= cellMask;
        }

        if ((availableMask & remainingRequiredMask) != remainingRequiredMask)
        {
            logicalStepDescription?.Append($"Can no longer fulfill all required values.");
            return LogicResult.Invalid;
        }

        if (possibleCells.Count < numRemainingRequired)
        {
            logicalStepDescription?.Append($"Can no longer fulfill all required values.");
            return LogicResult.Invalid;
        }

        if (possibleCells.Count == numRemainingRequired)
        {
            bool changed = false;
            foreach (var (i, j) in possibleCells)
            {
                uint cellMask = board[i, j];
                var result = sudokuSolver.ClearMask(i, j, ~remainingRequiredMask);
                if (result == LogicResult.Invalid)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Clear();
                        logicalStepDescription.Append($"{CellName(i, j)} must be one of the remaining quadruple values {MaskToString(remainingRequiredMask)} but it cannot be those values.");
                    }
                    return LogicResult.Invalid;
                }

                if (result == LogicResult.Changed)
                {
                    if (logicalStepDescription != null)
                    {
                        if (changed)
                        {
                            logicalStepDescription.Append($"The remaining value{(numRemainingRequired != 1 ? "s" : "")} {MaskToString(remainingRequiredMask)} must be in {CellName(i, j)}");
                        }
                        else
                        {
                            logicalStepDescription.Append($", {CellName(i, j)}");
                        }
                    }
                    changed = true;
                }
            }

            if (changed)
            {
                return LogicResult.Changed;
            }
        }

        // Check if only one cell can fulfill a value (hidden single, essentially)
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            uint valueMask = ValueMask(v);
            if ((remainingRequiredMask & valueMask) == 0)
            {
                continue;
            }

            int numCellsNeeded = remainingValues.Count(value => value == v);

            List<(int, int)> possibleSetCells = new();
            foreach (var (i, j) in cells)
            {
                uint cellMask = board[i, j];
                if (!IsValueSet(cellMask) && (cellMask & valueMask) != 0)
                {
                    possibleSetCells.Add((i, j));
                }
            }

            if (possibleSetCells.Count == numCellsNeeded)
            {
                foreach (var setCell in possibleSetCells)
                {
                    if (!sudokuSolver.SetValue(setCell.Item1, setCell.Item2, v))
                    {
                        logicalStepDescription?.Append($"{CellName(setCell)} is the only cell that can be the quadruple value {v} but it cannot be set to this value.");
                        return LogicResult.Invalid;
                    }
                }

                if (possibleSetCells.Count == 1)
                {
                    logicalStepDescription?.Append($"{CellName(possibleSetCells[0])} is the only cell that can be the quadruple value {v} and so it must be that value.");
                }
                else
                {
                    logicalStepDescription?.Append($"{sudokuSolver.CompactName(possibleSetCells)} are the only cells that can be the quadruple value {v} so they must all be that value.");
                }
                return LogicResult.Changed;
            }
        }

        // TODO: Pointing / Hidden Tuples

        return LogicResult.None;
    }
}
