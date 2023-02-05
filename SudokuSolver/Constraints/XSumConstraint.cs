using System.Collections.Generic;

namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "X-Sum", ConsoleName = "xsum")]
public class XSumConstraint : Constraint
{
    public readonly int sum;
    public readonly (int, int) cellStart;
    private readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsLookup;
    private readonly List<SumGroup> sumGroups;
    private readonly string specificName;
    private bool needsLogic = true;

    public override string SpecificName => specificName;

    private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
    public XSumConstraint(Solver solver, string options) : base(solver, options)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"X-Sum options \"{options}\" invalid. Expecting: \"SrXcY\"");
        }

        sum = int.Parse(match.Groups[1].Value);
        cellStart = (int.Parse(match.Groups[2].Value) - 1, int.Parse(match.Groups[3].Value) - 1);

        bool isCol = cellStart.Item1 < 0 || cellStart.Item1 >= MAX_VALUE;
        bool isRow = cellStart.Item2 < 0 || cellStart.Item2 >= MAX_VALUE;

        if (isRow && isCol || !isRow && !isCol)
        {
            throw new ArgumentException($"X-Sum options \"{options}\" has invalid location.");
        }

        cells = new();
        if (isRow)
        {
            int i = cellStart.Item1;
            for (int j = 0; j < WIDTH; j++)
            {
                cells.Add((i, j));
            }
            if (cellStart.Item2 >= WIDTH)
            {
                cells.Reverse();
            }
        }
        else
        {
            int j = cellStart.Item2;
            for (int i = 0; i < HEIGHT; i++)
            {
                cells.Add((i, j));
            }
            if (cellStart.Item1 >= HEIGHT)
            {
                cells.Reverse();
            }
        }
        cellsLookup = new(cells);

        sumGroups = new(MAX_VALUE);
        for (int v = 1; v <= MAX_VALUE; v++)
        {
            sumGroups.Add(new(solver, cells.GetRange(1, v - 1), excludeValue: v));
        }

        specificName = $"X-Sum {sum} at {CellName(cellStart)}";
    }
    public override LogicResult InitCandidates(Solver solver)
    {
        var board = solver.Board;
        if (!needsLogic)
        {
            return LogicResult.None;
        }

        // A sum of 1 is just a given 1
        if (sum == 1)
        {
            needsLogic = false;

            var (i, j) = cells[0];
            uint keepMask = ValueMask(1);
            return solver.KeepMask(i, j, keepMask);
        }

        // A sum of the maximum value for a row is a given MAX_VALUE
        if (sum == (MAX_VALUE * (MAX_VALUE + 1)) / 2)
        {
            needsLogic = false;

            var (i, j) = cells[0];
            uint keepMask = ValueMask(MAX_VALUE);
            return solver.KeepMask(i, j, keepMask);
        }

        uint[] newMasks = CalcNewMasks(solver);
        if (newMasks == null)
        {
            return LogicResult.Invalid;
        }

        bool changed = false;
        for (int cellIndex = 0; cellIndex < newMasks.Length; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            var logicResult = solver.KeepMask(i, j, newMasks[cellIndex]);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (logicResult == LogicResult.Changed)
            {
                changed = true;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        if (!needsLogic || !cellsLookup.Contains((i, j)))
        {
            return true;
        }

        var board = solver.Board;
        var (i0, j0) = cells[0];
        uint mask0 = board[i0, j0];
        if (!IsValueSet(mask0))
        {
            return true;
        }

        int numValues = GetValue(mask0);
        int minSum = numValues;
        int maxSum = numValues;
        for (int cellIndex = 1; cellIndex < numValues; cellIndex++)
        {
            var (i1, j1) = cells[cellIndex];
            uint mask1 = board[i1, j1] & ~valueSetMask;
            minSum += MinValue(mask1);
            maxSum += MaxValue(mask1);
        }
        return sum >= minSum && sum <= maxSum;
    }

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (!needsLogic)
        {
            return LogicResult.None;
        }

        var board = solver.Board;
        uint[] newMasks = CalcNewMasks(solver);
        if (newMasks == null)
        {
            logicalStepDescription?.Append($"Sum {sum} is impossible.");
            return LogicResult.Invalid;
        }

        bool changed = false;
        List<int> elims = null;
        for (int cellIndex = 0; cellIndex < newMasks.Length; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            if (!IsValueSet(board[i, j]))
            {
                uint elimMask = board[i, j] & ~newMasks[cellIndex];
                if (elimMask != 0)
                {
                    if (logicalStepDescription != null)
                    {
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if (HasValue(elimMask, v))
                            {
                                elims ??= new();
                                elims.Add(CandidateIndex(i, j, v));
                            }
                        }
                    }
                    board[i, j] &= newMasks[cellIndex];
                    changed = true;
                }
            }
        }

        if (elims != null)
        {
            logicalStepDescription.Append($"Re-evaluated sum {sum} => {solver.DescribeElims(elims)}");
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private uint[] CalcNewMasks(Solver solver)
    {
        var board = solver.Board;
        var (i0, j0) = cells[0];
        uint cell0Mask = board[i0, j0];
        int smallestSumVal = -1;
        uint[] newMasks = new uint[cells.Count];
        for (int v = 2; v < MAX_VALUE; v++)
        {
            if (!HasValue(cell0Mask, v))
            {
                continue;
            }

            int sumLeft = sum - v;
            if (sumLeft <= 0)
            {
                continue;
            }

            SumGroup sumGroup = sumGroups[v - 1];
            if (sumGroup.RestrictSumToArray(solver, sumLeft, out var resultMasks) != LogicResult.Invalid)
            {
                if (smallestSumVal == -1)
                {
                    smallestSumVal = v;
                }
                newMasks[0] |= ValueMask(v);

                bool reversed = sumGroup.Cells[0] != cells[1];
                for (int i = 1; i < smallestSumVal; i++)
                {
                    newMasks[i] |= resultMasks[reversed ? resultMasks.Length - i : i - 1];
                }
            }
        }

        return smallestSumVal > 0 ? newMasks[0..smallestSumVal] : null;
    }
}
