namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Skyscraper", ConsoleName = "skyscraper")]
public class SkyscraperConstraint : Constraint
{
    public readonly int clue;
    public readonly (int, int) cellStart;
    private readonly List<(int, int)> cells;
    private readonly HashSet<(int, int)> cellsLookup;
    private readonly string specificName;
    private bool needsLogic = true;
    private string memoPrefix;

    public override string SpecificName => specificName;

    private static readonly Regex optionsRegex = new(@"(\d+)[rR](\d+)[cC](\d+)");
    public SkyscraperConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var match = optionsRegex.Match(options);
        if (!match.Success)
        {
            throw new ArgumentException($"X-Sum options \"{options}\" invalid. Expecting: \"SrXcY\"");
        }

        clue = int.Parse(match.Groups[1].Value);
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

        specificName = $"Skyscraper {clue} at {CellName(cellStart)}";
        memoPrefix = $"Skyscraper|c{clue}";
    }

    public override LogicResult InitCandidates(Solver solver)
    {
        var board = solver.Board;
        if (!needsLogic)
        {
            return LogicResult.None;
        }

        // A clue of 1 means the max value must be in the first cell
        if (clue == 1)
        {
            needsLogic = false;

            var (i, j) = cells[0];
            uint keepMask = ValueMask(MAX_VALUE);
            return solver.KeepMask(i, j, keepMask);
        }

        // A clue of MAX_VALUE means that all digits must be in strict order
        bool changed = false;
        if (clue == MAX_VALUE)
        {
            needsLogic = false;

            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint keepMask = ValueMask(v);
                var (i, j) = cells[v - 1];
                var logicResult = solver.KeepMask(i, j, keepMask);
                if (logicResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                if (logicResult == LogicResult.Changed)
                {
                    changed = true;
                }
            }
        }
        else
        {
            // Restrict high digits
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var (i, j) = cells[cellIndex];
                int maxVal = MAX_VALUE - clue + 1 + cellIndex;
                if (maxVal < MAX_VALUE)
                {
                    uint keepMask = MaskValAndLower(maxVal);
                    var logicResult = solver.KeepMask(i, j, keepMask);
                    if (logicResult == LogicResult.Invalid)
                    {
                        return LogicResult.Invalid;
                    }
                    if (logicResult == LogicResult.Changed)
                    {
                        changed = true;
                    }
                }
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
        int numSeen = 0;
        int minValueSeen = 0;
        bool haveUnset = false;
        foreach (var (i1, j1) in cells)
        {
            uint curMask = board[i1, j1];
            if (!IsValueSet(curMask) && ValueCount(curMask) > 1)
            {
                haveUnset = true;
                break;
            }
            else
            {
                int curVal = GetValue(curMask);
                if (curVal > minValueSeen)
                {
                    numSeen++;
                    minValueSeen = curVal;
                }
            }
        }

        return haveUnset && numSeen <= clue || !haveUnset && numSeen == clue;
    }

    private record StepLogicMemo(uint[] KeepMasks);

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (!needsLogic)
        {
            return LogicResult.None;
        }
        
        bool changed = false;

        var board = solver.Board;
        StringBuilder memoKeyBuilder = new();
        memoKeyBuilder.Append(memoPrefix);
        List<int> unsetCellIndexes = new(cells.Count);
        List<int> curVals = new(cells.Count);
        uint unsetMask = 0;
        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            if (!IsValueSet(board[i, j]))
            {
                unsetCellIndexes.Add(cellIndex);
                unsetMask |= board[i, j];
                curVals.Add(0);
                memoKeyBuilder.AppendFormat("|{0:x}", board[i, j]);
            }
            else
            {
                int val = GetValue(board[i, j]);
                curVals.Add(val);
                memoKeyBuilder.Append("|s").Append(val);
            }
        }
        if (unsetCellIndexes.Count == 0)
        {
            return LogicResult.None;
        }

        uint[] keepMasks;
        string memoKey = memoKeyBuilder.ToString();
        var memo = solver.GetMemo<StepLogicMemo>(memoKey);
        if (memo != null)
        {
            keepMasks = memo.KeepMasks;
            if (keepMasks == null)
            {
                logicalStepDescription?.Append($"Clue value {clue} is impossible.");
                return LogicResult.Invalid;
            }
        }
        else
        {
            List<int> unsetVals = new(ValueCount(unsetMask));
            int minVal = MinValue(unsetMask);
            int maxVal = MaxValue(unsetMask);
            for (int v = minVal; v <= maxVal; v++)
            {
                if (HasValue(unsetMask, v))
                {
                    unsetVals.Add(v);
                }
            }

            bool haveValidPerm = false;
            keepMasks = new uint[cells.Count];
            int numUnsetCells = unsetCellIndexes.Count;
            foreach (var perm in unsetVals.Permuatations())
            {
                for (int unsetCellIndex = 0; unsetCellIndex < numUnsetCells; unsetCellIndex++)
                {
                    int cellIndex = unsetCellIndexes[unsetCellIndex];
                    curVals[cellIndex] = perm[unsetCellIndex];
                }
                if (SeenCount(curVals) != clue)
                {
                    continue;
                }

                bool needCheck = false;
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    if ((keepMasks[cellIndex] & ValueMask(curVals[cellIndex])) == 0)
                    {
                        needCheck = true;
                        break;
                    }
                }

                if (!needCheck)
                {
                    continue;
                }

                if (!solver.CanPlaceDigits(cells, curVals))
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    keepMasks[cellIndex] |= ValueMask(curVals[cellIndex]);
                }
                haveValidPerm = true;
            }

            if (!haveValidPerm)
            {
                logicalStepDescription?.Append($"Clue value {clue} is impossible.");
                solver.StoreMemo(memoKey, new StepLogicMemo(null));
                return LogicResult.Invalid;
            }
            else
            {
                solver.StoreMemo(memoKey, new StepLogicMemo(keepMasks));
            }
        }

        List<int> elims = null;
        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var (i, j) = cells[cellIndex];
            uint curMask = board[i, j];
            if (IsValueSet(curMask))
            {
                continue;
            }

            uint elimMask = curMask & ~keepMasks[cellIndex];
            if (elimMask == 0)
            {
                continue;
            }

            var logicResult = solver.KeepMask(i, j, keepMasks[cellIndex]);
            if (logicResult == LogicResult.Invalid)
            {
                return LogicResult.Invalid;
            }
            if (logicResult == LogicResult.Changed)
            {
                if (logicalStepDescription != null)
                {
                    elims ??= new();
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (HasValue(elimMask, v))
                        {
                            elims.Add(CandidateIndex(i, j, v));
                        }
                    }
                }
                changed = true;
            }
        }

        if (elims != null)
        {
            logicalStepDescription.Append($"Re-evaluated clue {clue} => {solver.DescribeElims(elims)}");
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private static int SeenCount(List<int> values)
    {
        int count = 0;
        int maxValueSeen = 0;
        foreach (int v in values)
        {
            if (v > maxValueSeen)
            {
                maxValueSeen = v;
                count++;
            }
        }
        return count;
    }
}
