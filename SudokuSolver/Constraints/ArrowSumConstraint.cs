namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Arrow", ConsoleName = "arrow")]
public class ArrowSumConstraint : Constraint
{
    public readonly List<(int, int)> circleCells;
    public readonly List<(int, int)> arrowCells;
    private readonly HashSet<(int, int)> allCells;
    private List<(int, int)> groupCells;
    private bool isArrowGroup = false;
    private bool isCircleGroup = false;
    private bool isAllGrouped = false;

    public ArrowSumConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 2)
        {
            throw new ArgumentException($"Arrow constraint expects 2 cell groups, got {cellGroups.Count}.");
        }

        circleCells = cellGroups[0];
        arrowCells = cellGroups[1];
        allCells = new(circleCells.Concat(arrowCells));
    }

    public override IEnumerable<(int, int)> SeenCells((int, int) cell)
    {
        if (arrowCells.Count != 1 && circleCells.Count == 1)
        {
            if (allCells.Contains(cell))
            {
                if (circleCells.Contains(cell))
                {
                    return arrowCells;
                }
                if (arrowCells.Contains(cell))
                {
                    return circleCells;
                }
            }
        }
        return Enumerable.Empty<(int, int)>();
    }

    public override string SpecificName => $"Arrow at {CellName(circleCells[0])}";

    public override List<(int, int)> Group => isArrowGroup ? groupCells : null;

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        bool changed = false;
        var board = sudokuSolver.Board;

        if (arrowCells.Count > 1)
        {
            isAllGrouped = sudokuSolver.IsGroup(allCells.ToList());
            if (isAllGrouped)
            {
                isArrowGroup = true;
                isCircleGroup = true;
                groupCells = allCells.ToList();
            }
            else
            {
                isArrowGroup = sudokuSolver.IsGroup(arrowCells);
                if (isArrowGroup)
                {
                    groupCells = arrowCells.ToList();
                    if (circleCells.Count == 1)
                    {
                        groupCells.Add(circleCells[0]);
                    }
                }
                isCircleGroup = sudokuSolver.IsGroup(circleCells);
            }
        }

        if (circleCells.Count == 1)
        {
            int maxValue = MAX_VALUE - arrowCells.Count + 1;
            if (maxValue < MAX_VALUE)
            {
                uint maxValueMask = (1u << maxValue) - 1;
                foreach (var cell in arrowCells)
                {
                    uint cellMask = board[cell.Item1, cell.Item2];
                    if (!IsValueSet(cellMask))
                    {
                        if ((cellMask & maxValueMask) != cellMask)
                        {
                            board[cell.Item1, cell.Item2] &= maxValueMask;
                            changed = true;
                        }
                    }
                }
            }

            int minSum = arrowCells.Count - 1;
            if (minSum > 0)
            {
                var sumCell = circleCells[0];
                uint minValueMask = ~((1u << minSum) - 1);
                uint cellMask = board[sumCell.Item1, sumCell.Item2];
                if (!IsValueSet(cellMask))
                {
                    if ((cellMask & minValueMask) != cellMask)
                    {
                        board[sumCell.Item1, sumCell.Item2] &= minValueMask;
                        changed = true;
                    }
                }
            }
        }
        else if (circleCells.Count > 1)
        {
            int maxSum = arrowCells.Count * MAX_VALUE;
            if (maxSum.Length() < circleCells.Count)
            {
                return LogicResult.Invalid;
            }

            int maxSumPrefix = maxSum / (int)Math.Pow(10, circleCells.Count - 1);

            if (maxSumPrefix < MAX_VALUE)
            {
                var sumCell = circleCells[0];
                uint maxValueMask = (1u << maxSumPrefix) - 1;
                uint cellMask = board[sumCell.Item1, sumCell.Item2];
                if (!IsValueSet(cellMask))
                {
                    if ((cellMask & maxValueMask) != cellMask)
                    {
                        board[sumCell.Item1, sumCell.Item2] &= maxValueMask;
                        changed = true;
                    }
                }
            }
        }
        else
        {
            return LogicResult.Invalid;
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (!allCells.Contains((i, j)))
        {
            return true;
        }

        if (HasCircleValue(sudokuSolver) &&
            HasArrowValue(sudokuSolver) &&
            CircleValue(sudokuSolver) != ArrowValue(sudokuSolver))
        {
            return false;
        }

        if (circleCells.Count == 1 && arrowCells.Count == 1)
        {
            bool isCircleCell = circleCells.Contains((i, j));
            bool isArrowCell = arrowCells.Contains((i, j));

            if (isCircleCell)
            {
                if (!sudokuSolver.SetValue(arrowCells[0].Item1, arrowCells[0].Item2, val))
                {
                    return false;
                }
            }
            else if (isArrowCell)
            {
                if (!sudokuSolver.SetValue(circleCells[0].Item1, circleCells[0].Item2, val))
                {
                    return false;
                }
            }
            return true;
        }

        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        var board = sudokuSolver.Board;
        bool circleCellsFilled = HasCircleValue(sudokuSolver);
        bool arrowCellsFilled = HasArrowValue(sudokuSolver);
        if (circleCellsFilled && arrowCellsFilled)
        {
            // Both the sum and arrow cell values are known, so check to ensure the sum in correct
            int circleValue = CircleValue(sudokuSolver);
            int arrowValue = ArrowValue(sudokuSolver);
            if (circleValue != arrowValue)
            {
                logicalStepDescription?.Add(new($"Sum of circle {circleValue} and arrow {arrowValue} do not match.", allCells));
                return LogicResult.Invalid;
            }
            return LogicResult.None;
        }

        if (arrowCellsFilled)
        {
            // Even though we know the sum of the arrow, there could be more than one arrangement of the digits in the circle. 

            int arrowSum = ArrowValue(sudokuSolver);
            var res = FillCircleWithKnownSum(sudokuSolver, arrowSum, logicalStepDescription);

            if (res != LogicResult.None)
            {
                return res;
            }
        }

        if (circleCellsFilled)
        {
            // The circle sum is known, so adjust the candidates in the arrow cells to match that sum
            int circleValue = CircleValue(sudokuSolver);
            return AdjustArrowCandidatesWithKnownSum(sudokuSolver, circleValue, logicalStepDescription);
        }

        // Neither circle nor arrow values filled.
        var possibleArrowSums = PossibleArrowSums(sudokuSolver);
        if (possibleArrowSums.Count == 0)
        {
            logicalStepDescription?.Add(new($"There are no value sums for the arrow.", allCells));
            return LogicResult.Invalid;
        }

        if (possibleArrowSums.Count == 1)
        {
            // Only one circle sum is possible, so fill it
            var res = FillCircleWithKnownSum(sudokuSolver, possibleArrowSums.First(), logicalStepDescription);

            if (res != LogicResult.None)
            {
                return res;
            }
        }

        var result = AdjustCircleCandidatesFromPossibleSums(sudokuSolver, possibleArrowSums, logicalStepDescription);

        if (result != LogicResult.None)
        {
            return result;
        }

        List<int> elims = null;

        // Adjust the arrow candidates based on the possible sums
        int numArrowCells = arrowCells.Count;
        uint[] arrowCandidates = new uint[numArrowCells];
        foreach (int sum in possibleArrowSums)
        {
            AppendArrowCandidatesForSums(sudokuSolver, sum, arrowCandidates);
        }
        for (int cellIndex = 0; cellIndex < numArrowCells; cellIndex++)
        {
            var (ai, aj) = arrowCells[cellIndex];
            uint cellMask = board[ai, aj];
            if (IsValueSet(cellMask))
            {
                continue;
            }

            uint keepMask = arrowCandidates[cellIndex];
            uint clearMask = (cellMask & ~keepMask);
            if (clearMask != 0)
            {
                var curElims = sudokuSolver.CandidateIndexes(clearMask, (ai, aj).ToEnumerable());
                if (curElims.Count != 0)
                {
                    elims ??= new();
                    elims.AddRange(curElims);
                }
            }
        }

        if (elims != null && elims.Count > 0)
        {
            bool invalid = !sudokuSolver.ClearCandidates(elims);
            logicalStepDescription?.Add(new(
                desc: $"Impossible arrow cell value{(elims.Count > 1 ? "s" : "")} => {sudokuSolver.DescribeElims(elims)}",
                sourceCandidates: Enumerable.Empty<int>(),
                elimCandidates: elims
            ));
            return invalid ? LogicResult.Invalid : LogicResult.Changed;
        }

        return LogicResult.None;
    }

    private LogicResult AdjustCircleCandidatesFromPossibleSums(Solver sudokuSolver, HashSet<int> possibleArrowSums, List<LogicalStepDesc> logicalStepDescription = null)
    {
        var board = sudokuSolver.Board;

        // We're going to store all possible values for each cell so we can remove anything that's not been captured.
        var keepMasks = new uint[circleCells.Count];

        foreach (var sum in possibleArrowSums)
        {
            // Valid circle arrangement works for all circle cell counts.
            foreach (var arrangement in ValidCircleArrangements(sudokuSolver, sum))
            {
                // arrangement is now set to a plausable arrangement of cell values for this sum

                for (var cellIndex = 0; cellIndex < circleCells.Count; cellIndex++)
                {
                    keepMasks[cellIndex] |= ValueMask(arrangement[cellIndex]);
                }
            }
        }

        // Now we've recorded all the possible values for each circle cell, remove every other candidate.
        var elims = new List<int>();

        for (var cellIndex = 0; cellIndex < circleCells.Count; cellIndex++)
        {
            var circleCell = circleCells[cellIndex];
            uint circleMask = board[circleCell.Item1, circleCell.Item2] & ~valueSetMask;
            uint clearMask = circleMask & ~keepMasks[cellIndex];
            if (clearMask != 0)
            {
                var curElims = sudokuSolver.CandidateIndexes(clearMask, circleCell.ToEnumerable());
                if (curElims.Count != 0)
                {
                    elims ??= new();
                    elims.AddRange(curElims);
                }
            }
        }

        if (elims != null && elims.Count > 0)
        {
            bool invalid = !sudokuSolver.ClearCandidates(elims);
            logicalStepDescription?.Add(new(
                desc: $"Impossible sum{(elims.Count > 1 ? "s" : "")} => {sudokuSolver.DescribeElims(elims)}",
                sourceCandidates: Enumerable.Empty<int>(),
                elimCandidates: elims
            ));
            return invalid ? LogicResult.Invalid : LogicResult.Changed;
        }

        return LogicResult.None;
    }

    private HashSet<int> PossibleArrowSums(Solver sudokuSolver)
    {
        HashSet<int> possibleArrowSums = new();

        var board = sudokuSolver.Board;
        if (!isArrowGroup)
        {
            int minSum = 0;
            int maxSum = 0;
            foreach (var (ai, aj) in arrowCells)
            {
                uint arrowMask = board[ai, aj];
                if (IsValueSet(arrowMask))
                {
                    int val = GetValue(arrowMask);
                    minSum += val;
                    maxSum += val;
                }
                else
                {
                    minSum += MinValue(arrowMask);
                    maxSum += MaxValue(arrowMask);
                }
            }
            for (int sum = minSum; sum <= maxSum; sum++)
            {
                if (IsPossibleCircleValue(sudokuSolver, sum))
                {
                    possibleArrowSums.Add(sum);
                }
            }
        }
        else
        {
            int baseSum = 0;
            List<(int, int)> remainingArrowCells = new();
            uint allArrowMask = 0;
            foreach (var (ai, aj) in arrowCells)
            {
                uint arrowMask = board[ai, aj];
                if (IsValueSet(arrowMask))
                {
                    int val = GetValue(arrowMask);
                    baseSum += val;
                }
                else
                {
                    allArrowMask |= board[ai, aj];
                    remainingArrowCells.Add((ai, aj));
                }
            }
            if (remainingArrowCells.Count >= 1)
            {
                int numRemainingArrowCells = remainingArrowCells.Count;
                int numPossibleValues = ValueCount(allArrowMask);
                if (numPossibleValues >= numRemainingArrowCells)
                {
                    List<int> possibleValues = new(ValueCount(allArrowMask));
                    int minValue = MinValue(allArrowMask);
                    int maxValue = MaxValue(allArrowMask);
                    for (int val = minValue; val <= maxValue; val++)
                    {
                        if ((allArrowMask & ValueMask(val)) != 0)
                        {
                            possibleValues.Add(val);
                        }
                    }

                    foreach (var valueCombination in possibleValues.Combinations(numRemainingArrowCells))
                    {
                        int valueComboSum = baseSum + valueCombination.Sum();
                        if (possibleArrowSums.Contains(valueComboSum))
                        {
                            continue;
                        }

                        if (!IsPossibleCircleValue(sudokuSolver, valueComboSum))
                        {
                            continue;
                        }

                        bool canUseCombination = true;

                        if (isAllGrouped)
                        {
                            // Only allow this combination if we can find a valid arrangement for the circle cells.
                            canUseCombination = false;

                            // We need to find at least one possible combination
                            foreach (var possibleArrangement in PossibleCircleArrangements(valueComboSum, circleCells.Count, MAX_VALUE))
                            {
                                bool arrangementValid = true;

                                for (var i = 0; i < circleCells.Count; i++)
                                {
                                    if (!arrowCells.Contains(circleCells[i]) && valueCombination.Contains(possibleArrangement[i]))
                                    {
                                        // This combination of circle digits is not valid since one of the values repeats on the arrow :(
                                        arrangementValid = false;
                                        break;
                                    }
                                }

                                if (arrangementValid)
                                {
                                    canUseCombination = true;

                                    // We've found at least one working combination
                                    break;
                                }
                            }

                        }
                        if (!canUseCombination)
                        {
                            continue;
                        }

                        bool valueCombinationPossible = false;
                        foreach (var cellPermutation in remainingArrowCells.Permuatations())
                        {
                            bool permutationPossible = true;
                            for (int cellIndex = 0; cellIndex < numRemainingArrowCells; cellIndex++)
                            {
                                var (i, j) = cellPermutation[cellIndex];
                                int val = valueCombination[cellIndex];
                                uint cellMask = board[i, j];
                                if ((cellMask & ValueMask(val)) == 0)
                                {
                                    permutationPossible = false;
                                    break;
                                }
                            }
                            if (permutationPossible)
                            {
                                valueCombinationPossible = true;
                                break;
                            }
                        }
                        if (valueCombinationPossible)
                        {
                            possibleArrowSums.Add(valueComboSum);
                        }
                    }
                }
            }
        }

        return possibleArrowSums;
    }

    public static IEnumerable<int[]> PossibleCircleArrangements(int total, int cells, int maxValue)
    {
        if (total == 0)
        {
            yield break;
        }

        if (cells == 1 && total <= maxValue)
        {
            yield return new int[] { total };
            yield break;
        }

        if (cells > 1)
        {
            // Max digits per cell is TotalDigits - (CircleCells - 1)
            for (var numberDigits = 1; numberDigits <= total.Length() - (cells - 1); numberDigits++)
            {
                var first = total.Take(numberDigits);

                // We can't continue if first is now > max
                if (first > maxValue) yield break;

                var remaining = total.Skip(numberDigits, out int leadingZeros);

                if (leadingZeros > 0)
                {
                    // If we took these digits, the remaining digits would have a leading 0!
                    continue;
                }

                foreach (var remainingCombination in PossibleCircleArrangements(remaining, cells - 1, maxValue))
                {
                    var combination = new int[remainingCombination.Length + 1];

                    combination[0] = first;
                    remainingCombination.CopyTo(combination, 1);

                    yield return combination;
                }
            }

        }
    }

    private IEnumerable<int[]> ValidCircleArrangements(Solver sudokuSolver, int circleValue)
    {
        // We need to...
        // 1 - See if this arrangement actually fits in the circle cells
        // 2 - Make sure we're not violating any group constraints

        var board = sudokuSolver.Board;

        foreach (var possible in PossibleCircleArrangements(circleValue, circleCells.Count, MAX_VALUE))
        {
            bool isValid = true;

            // Make sure every location can house the associated value
            for (var cellIndex = 0; cellIndex < circleCells.Count; cellIndex++)
            {
                uint circleCellMask = board[circleCells[cellIndex].Item1, circleCells[cellIndex].Item2];

                if ((circleCellMask & ValueMask(possible[cellIndex])) == 0)
                {
                    isValid = false;
                    break;
                }
            }

            if (isValid)
            {
                // We need to make sure the circle cells are unique if in a group.
                if (circleCells.Count > 1 && isCircleGroup)
                {
                    if (possible.Distinct().ToArray().Length != possible.Length)
                    {
                        // Oh well, let's try another combination
                        continue;
                    }
                }

                yield return possible;
            }
        }

        // Try as we might, we can't find an arrangement that works :(
        yield break;
    }

    private bool IsPossibleCircleValue(Solver sudokuSolver, int circleValue)
    {
        // If there are any valid arrangements, then it's a possible value.
        return ValidCircleArrangements(sudokuSolver, circleValue).Any();
    }

    private LogicResult FillCircleWithKnownSum(Solver sudokuSolver, int arrowSum, List<LogicalStepDesc> logicalStepDescription = null)
    {
        var validArrangements = ValidCircleArrangements(sudokuSolver, arrowSum).ToList();

        if (validArrangements.Count == 0)
        {
            logicalStepDescription?.Add(new($"Sum of arrow ({arrowSum}) is impossible to fill into {(circleCells.Count > 1 ? "pill" : "circle")}.", circleCells));
            return LogicResult.Invalid;
        }
        else if (validArrangements.Count > 1)
        {
            return AdjustCircleCandidatesFromPossibleSums(sudokuSolver, new HashSet<int>() { arrowSum }, logicalStepDescription);
        }

        // Now we know there is exactly 1 valid arrangement of values in the circle cells.
        var finalArrangement = validArrangements[0];

        var setCandidates = new List<int>();
        var changed = false;
        var board = sudokuSolver.Board;

        for (var circleIndex = 0; circleIndex < circleCells.Count; circleIndex++)
        {
            var cell = circleCells[circleIndex];
            var mask = board[cell.Item1, cell.Item2];

            if (IsValueSet(mask))
            {
                // This digit is already set
                continue;
            }

            if (!sudokuSolver.SetValue(cell.Item1, cell.Item2, finalArrangement[circleIndex]))
            {
                logicalStepDescription?.Add(new($"Cannot fill {finalArrangement[circleIndex]} into {CellName(cell)}", cell));
                return LogicResult.Invalid;
            }

            setCandidates.Add(sudokuSolver.CandidateIndex(cell, finalArrangement[circleIndex]));
            changed = true;
        }

        logicalStepDescription?.Add(new($"Circle Sum: {sudokuSolver.CompactName(circleCells)}={arrowSum}", setCandidates, null, isSingle: true));

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    private void AppendArrowCandidatesForSums(Solver sudokuSolver, int sum, uint[] arrowCandidates)
    {
        var board = sudokuSolver.Board;
        if (!isArrowGroup)
        {
            int sumRemaining = sum;
            int numRemainingCells = 0;
            for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
            {
                var (i, j) = arrowCells[cellIndex];
                uint arrowMask = board[i, j];
                if (IsValueSet(arrowMask))
                {
                    sumRemaining -= GetValue(arrowMask);
                }
                else
                {
                    numRemainingCells++;
                }
            }

            int maxValue = Math.Min(MAX_VALUE, sumRemaining - numRemainingCells + 1);
            uint keepMask = MaskValAndLower(maxValue);
            for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
            {
                var (i, j) = arrowCells[cellIndex];
                uint arrowMask = board[i, j];
                if (!IsValueSet(arrowMask))
                {
                    arrowCandidates[cellIndex] |= keepMask;
                }
            }
        }
        else
        {
            int sumRemaining = sum;
            uint allArrowMask = 0;
            List<(int, int, int)> remainingArrowCells = new();
            for (int cellIndex = 0; cellIndex < arrowCells.Count; cellIndex++)
            {
                var (i, j) = arrowCells[cellIndex];
                uint arrowMask = board[i, j];
                if (IsValueSet(arrowMask))
                {
                    int arrowVal = GetValue(arrowMask);
                    sumRemaining -= arrowVal;
                }
                else
                {
                    allArrowMask |= arrowMask;
                    remainingArrowCells.Add((i, j, cellIndex));
                }
            }

            if (sumRemaining < 0)
            {
                return;
            }

            int remainingArrowCellsCount = remainingArrowCells.Count;
            int numPossibleValues = ValueCount(allArrowMask);
            if (numPossibleValues >= remainingArrowCellsCount)
            {
                List<int> possibleValues = new(numPossibleValues);
                int minValue = MinValue(allArrowMask);
                int maxValue = MaxValue(allArrowMask);
                for (int curVal = minValue; curVal <= maxValue; curVal++)
                {
                    if ((allArrowMask & ValueMask(curVal)) != 0)
                    {
                        possibleValues.Add(curVal);
                    }
                }

                foreach (var valueCombination in possibleValues.Combinations(remainingArrowCellsCount))
                {
                    int valueComboSum = valueCombination.Sum();
                    if (valueComboSum != sumRemaining)
                    {
                        continue;
                    }

                    foreach (var cellPermutation in remainingArrowCells.Permuatations())
                    {
                        bool permutationPossible = true;
                        for (int remainingCellIndex = 0; remainingCellIndex < remainingArrowCellsCount; remainingCellIndex++)
                        {
                            var (i, j, _) = cellPermutation[remainingCellIndex];
                            int val = valueCombination[remainingCellIndex];
                            uint cellMask = board[i, j];
                            uint valMask = ValueMask(val);
                            if ((cellMask & valMask) == 0)
                            {
                                permutationPossible = false;
                                break;
                            }
                        }
                        if (permutationPossible)
                        {
                            for (int remainingCellIndex = 0; remainingCellIndex < remainingArrowCellsCount; remainingCellIndex++)
                            {
                                int val = valueCombination[remainingCellIndex];
                                uint valMask = ValueMask(val);
                                int cellIndex = cellPermutation[remainingCellIndex].Item3;
                                arrowCandidates[cellIndex] |= valMask;
                            }
                        }
                    }
                }
            }
        }
    }

    private LogicResult AdjustArrowCandidatesWithKnownSum(Solver sudokuSolver, int sum, List<LogicalStepDesc> logicalStepDescription = null)
    {
        int numArrowCells = arrowCells.Count;
        uint[] arrowCandidates = new uint[numArrowCells];
        AppendArrowCandidatesForSums(sudokuSolver, sum, arrowCandidates);

        var board = sudokuSolver.Board;
        List<int> elims = null;
        for (int cellIndex = 0; cellIndex < numArrowCells; cellIndex++)
        {
            var (i, j) = arrowCells[cellIndex];
            uint cellMask = board[i, j];
            if (IsValueSet(cellMask))
            {
                continue;
            }

            uint clearMask = ALL_VALUES_MASK & ~arrowCandidates[cellIndex];
            var curElims = sudokuSolver.CandidateIndexes(clearMask, (i, j).ToEnumerable());
            if (curElims.Count != 0)
            {
                elims ??= new();
                elims.AddRange(curElims);
            }
        }

        if (elims != null && elims.Count > 0)
        {
            bool invalid = !sudokuSolver.ClearCandidates(elims);
            logicalStepDescription?.Add(new(
                desc: $"Circle value of {sum} => {sudokuSolver.DescribeElims(elims)}",
                sourceCandidates: Enumerable.Empty<int>(),
                elimCandidates: elims
            ));
            return invalid ? LogicResult.Invalid : LogicResult.Changed;
        }
        return LogicResult.None;
    }

    private static bool HasValue(Solver board, IEnumerable<(int, int)> cells)
    {
        foreach (var cell in cells)
        {
            if (ValueCount(board.Board[cell.Item1, cell.Item2]) != 1)
            {
                return false;
            }
        }
        return true;
    }

    public bool HasCircleValue(Solver board) => HasValue(board, circleCells);

    public bool HasArrowValue(Solver board) => HasValue(board, arrowCells);

    public int CircleValue(Solver board)
    {
        int sum = 0;
        foreach (int v in circleCells.Select(cell => board.GetValue(cell)))
        {
            sum *= 10;
            sum += v;
        }
        return sum;
    }
    public int ArrowValue(Solver board) => arrowCells.Select(cell => board.GetValue(cell)).Sum();

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription) => InitLinksByRunningLogic(sudokuSolver, allCells, logicalStepDescription);
}
