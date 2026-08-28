using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Quadruple", ConsoleName = "quad")]
public class QuadrupleConstraint : Constraint
{
    public readonly List<(int, int)> cells = null;
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

    /// <summary>
    /// Fills <paramref name="outstanding"/> with how many further times each value must still be
    /// placed in <see cref="cells"/>, and returns the mask of the values that are still outstanding.
    /// </summary>
    /// <remarks>
    /// This is the multiset bookkeeping that <c>EnforceConstraint</c> and <c>StepLogic</c> both used
    /// to do with a per-call <c>requiredValues.ToList()</c> plus <c>List.Remove</c>. A quadruple may
    /// legitimately ask for the same digit twice -- <c>.Quad~R3C3~1~9~1~4</c> occurs in the ISS corpus
    /// -- so a plain "distinct required values" mask is not enough and the counts have to be kept.
    /// <paramref name="outstanding"/> is expected to be a <c>stackalloc</c> span of at least
    /// <c>MAX_VALUE + 1</c> entries: the constraint instance is shared across cloned solvers and
    /// across threads (see <c>Solver</c>'s copy constructor, <c>constraints = other.constraints</c>),
    /// so a reusable instance-level scratch buffer would be a race.
    /// </remarks>
    private uint FillOutstanding(BoardView board, Span<int> outstanding, out int numOutstanding)
    {
        outstanding.Clear();
        foreach (int value in requiredValues)
        {
            outstanding[value]++;
        }

        foreach (var (i, j) in cells)
        {
            uint cellMask = board[i, j];
            if (IsValueSet(cellMask))
            {
                // A placed digit satisfies at most one required copy of itself, and a placed digit
                // that was never required satisfies nothing. Both match List.Remove's behavior.
                int value = GetValue(cellMask);
                if (outstanding[value] > 0)
                {
                    outstanding[value]--;
                }
            }
        }

        uint outstandingMask = 0;
        numOutstanding = 0;
        for (int value = 1; value <= MAX_VALUE; value++)
        {
            int count = outstanding[value];
            if (count > 0)
            {
                outstandingMask |= ValueMask(value);
                numOutstanding += count;
            }
        }
        return outstandingMask;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells == null || requiredValues.Count == 0)
        {
            return true;
        }

        // Only a placement inside the quadruple can break it. A linear scan beats the hash lookup
        // this used to do: the group is four cells.
        bool insideQuad = false;
        foreach (var (ci, cj) in cells)
        {
            if (ci == i && cj == j)
            {
                insideQuad = true;
                break;
            }
        }
        if (!insideQuad)
        {
            return true;
        }

        var board = sudokuSolver.Board;
        Span<int> outstanding = stackalloc int[MAX_VALUE + 1];
        uint remainingMask = FillOutstanding(board, outstanding, out int numRemaining);
        if (numRemaining == 0)
        {
            return true;
        }

        uint availableMask = 0;
        foreach (var (ci, cj) in cells)
        {
            uint cellMask = board[ci, cj];
            if (!IsValueSet(cellMask))
            {
                availableMask |= cellMask;
            }
        }

        return (availableMask & remainingMask) == remainingMask;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => (cells != null && requiredMask != 0) ? InitLinksByRunningLogic(solver, cells, logicalStepDescription) : LogicResult.None;
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => (cells != null && requiredMask != 0) ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

    /// <summary>
    /// A required value must appear among the quadruple's cells by definition, so that case is
    /// answered directly. Only a non-required value needs the clone-and-step fallback, which is
    /// what <see cref="Constraint.MustContainValue"/> does. This matters because
    /// <see cref="Group"/> is only non-null once the cells have been restricted to
    /// <c>requiredMask</c>, so hidden single detection inside brute force always takes the
    /// direct answer and never clones.
    /// </summary>
    public override bool MustContainValue(Solver sudokuSolver, int value) =>
        cells != null && requiredMask != 0 && (HasValue(requiredMask, value) || base.MustContainValue(sudokuSolver, value));

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells == null || requiredValues.Count == 0)
        {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;

        Span<int> outstanding = stackalloc int[MAX_VALUE + 1];
        uint remainingRequiredMask = FillOutstanding(board, outstanding, out int numRemainingRequired);
        if (numRemainingRequired == 0)
        {
            return LogicResult.None;
        }

        // Indices into `cells`, not coordinates, so that they stay valid across a SetValue.
        Span<int> possible = stackalloc int[cells.Count];

        uint availableMask = 0;
        int numPossibleCells = 0;
        for (int k = 0; k < cells.Count; k++)
        {
            var (i, j) = cells[k];
            uint cellMask = board[i, j];
            if (IsValueSet(cellMask))
            {
                continue;
            }

            if ((cellMask & remainingRequiredMask) != 0)
            {
                possible[numPossibleCells++] = k;
            }
            availableMask |= cellMask;
        }

        if ((availableMask & remainingRequiredMask) != remainingRequiredMask)
        {
            logicalStepDescription?.Append($"Can no longer fulfill all required values.");
            return LogicResult.Invalid;
        }

        if (numPossibleCells < numRemainingRequired)
        {
            logicalStepDescription?.Append($"Can no longer fulfill all required values.");
            return LogicResult.Invalid;
        }

        if (numPossibleCells == numRemainingRequired)
        {
            bool changed = false;
            for (int p = 0; p < numPossibleCells; p++)
            {
                var (i, j) = cells[possible[p]];
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
                            logicalStepDescription.Append($", {CellName(i, j)}");
                        }
                        else
                        {
                            logicalStepDescription.Append($"The remaining value{(numRemainingRequired != 1 ? "s" : "")} {MaskToString(remainingRequiredMask)} must be in {CellName(i, j)}");
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

            int numCellsNeeded = outstanding[v];

            int numPossibleSetCells = 0;
            for (int k = 0; k < cells.Count; k++)
            {
                var (i, j) = cells[k];
                uint cellMask = board[i, j];
                if (!IsValueSet(cellMask) && (cellMask & valueMask) != 0)
                {
                    possible[numPossibleSetCells++] = k;
                }
            }

            if (numPossibleSetCells != numCellsNeeded)
            {
                continue;
            }

            for (int p = 0; p < numPossibleSetCells; p++)
            {
                var (i, j) = cells[possible[p]];
                if (!sudokuSolver.SetValue(i, j, v))
                {
                    logicalStepDescription?.Append($"{CellName(i, j)} is the only cell that can be the quadruple value {v} but it cannot be set to this value.");
                    return LogicResult.Invalid;
                }
            }

            if (logicalStepDescription != null)
            {
                if (numPossibleSetCells == 1)
                {
                    logicalStepDescription.Append($"{CellName(cells[possible[0]])} is the only cell that can be the quadruple value {v} and so it must be that value.");
                }
                else
                {
                    List<(int, int)> setCells = new(numPossibleSetCells);
                    for (int p = 0; p < numPossibleSetCells; p++)
                    {
                        setCells.Add(cells[possible[p]]);
                    }
                    logicalStepDescription.Append($"{sudokuSolver.CompactName(setCells)} are the only cells that can be the quadruple value {v} so they must all be that value.");
                }
            }
            return LogicResult.Changed;
        }

        // TODO: Pointing / Hidden Tuples

        return LogicResult.None;
    }
}
