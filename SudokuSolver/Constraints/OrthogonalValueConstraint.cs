namespace SudokuSolver.Constraints;

public abstract class OrthogonalValueConstraint : Constraint
{
    private static int numTimes = 0;

    public readonly Dictionary<(int, int, int, int), int> markers = new();
    public readonly bool negativeConstraint = false;
    public readonly Dictionary<int, uint[]> clearValuesPositiveByMarker = new();
    public readonly uint[] clearValuesNegative;
    public readonly HashSet<int> negativeConstraintValues = new();

    /// <summary>
    /// Determine if the pair of values are allowed to be across the constraint "marker" for a pair of cells.
    /// The opposite of this is used if the negative constraint is enabled.
    /// An example of a constraint "marker" is a black ratio dot, or an "X" for XV constraint.
    /// </summary>
    /// <param name="markerValue"></param>
    /// <param name="v0"></param>
    /// <param name="v1"></param>
    /// <returns>true if the pair of values is allowed across the constraint "marker."</returns>
    protected abstract bool IsPairAllowedAcrossMarker(int markerValue, int v0, int v1);

    /// <summary>
    /// Allows other constraints to override the negative constraint of this constraint.
    /// For exmaple: nonconsecutive "white" dots override the ratio "black" dot negative constraint,
    /// and vice versa, since they are both kropki dots.
    /// </summary>
    /// <param name="solver"></param>
    /// <returns>An enumerable of OrthogonalValueConstraint instances which override the negative constraint.</returns>
    protected virtual IEnumerable<OrthogonalValueConstraint> GetRelatedConstraints(Solver solver) => Enumerable.Empty<OrthogonalValueConstraint>();

    public override string GetHash(Solver solver)
    {
        StringBuilder result = new(base.GetHash(solver));

        // Include the negative constraint area "holes" in the hash
        if (negativeConstraint)
        {
            // The current constraint type could be represented by more than 1 object,
            // so concatenate results of the regular GetRelatedConstraints() implementation
            // with all constraints of the same type
            // to get the full list of constraints that may affect the set of ignored pairs for the negative constraint
            var relatedConstraints = solver.Constraints<OrthogonalValueConstraint>()
                .Where(constraint => constraint.GetType() == GetType())
                .Concat(GetRelatedConstraints(solver));
            var overrideMarkers = relatedConstraints.SelectMany(x => x.Markers.Keys).ToHashSet().ToSortedList();
            if (overrideMarkers.Count > 0)
            {
                result.Append("-");
                foreach (var (i1, j1, i2, j2) in overrideMarkers)
                {
                    result.Append(CellName(i1, j1));
                    result.Append(CellName(i2, j2));
                }
            }
        }

        return result.ToString();
    }

    protected abstract int DefaultMarkerValue { get; }

    public Dictionary<(int, int, int, int), int> Markers => markers;


    private static readonly Regex negRegex = new(@"neg(\d*)");
    private static readonly Regex twoCellsRegex = new(@"(\d*)r(\d+)c(\d+)r(\d+)c(\d+)");
    private static readonly Regex sharedRowRegex = new(@"(\d*)r(\d+)[,-](\d+)c(\d+)");
    private static readonly Regex sharedColRegex = new(@"(\d*)r(\d+)c(\d+)[,-](\d+)");

    public OrthogonalValueConstraint(Solver sudokuSolver, string options) : base(sudokuSolver, options)
    {
        HashSet<int> markerValues = new();
        options = options.ToLowerInvariant();
        foreach (string optionGroup in options.Split(separator: ';', options: StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = negRegex.Match(optionGroup);
            if (match.Success)
            {
                string valueStr = match.Groups[1].Value;
                int value = string.IsNullOrWhiteSpace(valueStr) ? DefaultMarkerValue : int.Parse(valueStr);
                negativeConstraintValues.Add(value);
                negativeConstraint = true;
                continue;
            }

            match = twoCellsRegex.Match(optionGroup);
            if (match.Success)
            {
                string valueStr = match.Groups[1].Value;
                int value = string.IsNullOrWhiteSpace(valueStr) ? DefaultMarkerValue : int.Parse(valueStr);
                int i0 = int.Parse(match.Groups[2].Value) - 1;
                int j0 = int.Parse(match.Groups[3].Value) - 1;
                int i1 = int.Parse(match.Groups[4].Value) - 1;
                int j1 = int.Parse(match.Groups[5].Value) - 1;
                markers.Add(CellPair((i0, j0), (i1, j1)), value);
                markerValues.Add(value);
                continue;
            }

            match = sharedRowRegex.Match(optionGroup);
            if (match.Success)
            {
                string valueStr = match.Groups[1].Value;
                int value = string.IsNullOrWhiteSpace(valueStr) ? DefaultMarkerValue : int.Parse(valueStr);
                int i0 = int.Parse(match.Groups[2].Value) - 1;
                int i1 = int.Parse(match.Groups[3].Value) - 1;
                int j = int.Parse(match.Groups[4].Value) - 1;
                markers.Add(CellPair((i0, j), (i1, j)), value);
                markerValues.Add(value);
                continue;
            }

            match = sharedColRegex.Match(optionGroup);
            if (match.Success)
            {
                string valueStr = match.Groups[1].Value;
                int value = string.IsNullOrWhiteSpace(valueStr) ? DefaultMarkerValue : int.Parse(valueStr);
                int i = int.Parse(match.Groups[2].Value) - 1;
                int j0 = int.Parse(match.Groups[3].Value) - 1;
                int j1 = int.Parse(match.Groups[4].Value) - 1;
                markers.Add(CellPair((i, j0), (i, j1)), value);
                markerValues.Add(value);
                continue;
            }

            throw new ArgumentException($"[{GetType().Name}] Unrecognized options group: {optionGroup}");
        }

        clearValuesNegative = initClearValuesNegative();
        initClearValuesPositiveByMarker(markerValues);
    }

    /// <summary>
    /// Create a single negative constraint by the forbidden value
    /// </summary>
    public OrthogonalValueConstraint(Solver sudokuSolver, int negativeConstraintValue) : base(sudokuSolver, "neg" + negativeConstraintValue)
    {
        negativeConstraint = true;
        negativeConstraintValues.Add(negativeConstraintValue);

        clearValuesNegative = initClearValuesNegative();
    }

    /// <summary>
    /// Create a single marker constraint by two cells and value
    /// </summary>
    public OrthogonalValueConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2) : base(sudokuSolver, markerValue + CellName(cell1) + CellName(cell2))
    {
        markers.Add(CellPair(cell1, cell2), markerValue);

        clearValuesNegative = initClearValuesNegative();
        initClearValuesPositiveByMarker(new int[] { markerValue });
    }

    protected abstract OrthogonalValueConstraint createNegativeConstraint(Solver sudokuSolver, int negativeConstraintValue);
    protected abstract OrthogonalValueConstraint createMarkerConstraint(Solver sudokuSolver, int markerValue, (int, int) cell1, (int, int) cell2);

    private uint[] initClearValuesNegative()
    {
        var clearValuesNegative = new uint[MAX_VALUE];
        for (int v0 = 1; v0 <= MAX_VALUE; v0++)
        {
            clearValuesNegative[v0 - 1] = ValueMask(v0);
            foreach (int markerValue in negativeConstraintValues)
            {
                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                {
                    if (v0 != v1)
                    {
                        if (IsPairAllowedAcrossMarker(markerValue, v0, v1))
                        {
                            clearValuesNegative[v0 - 1] |= ValueMask(v1);
                        }
                    }
                }
            }
        }
        return clearValuesNegative;
    }

    private void initClearValuesPositiveByMarker(IEnumerable<int> markerValues)
    {
        foreach (int markerValue in markerValues)
        {
            uint[] positiveArray = clearValuesPositiveByMarker[markerValue] = new uint[MAX_VALUE];

            for (int v0 = 1; v0 <= MAX_VALUE; v0++)
            {
                positiveArray[v0 - 1] = ValueMask(v0);
                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                {
                    if (v0 != v1)
                    {
                        if (!IsPairAllowedAcrossMarker(markerValue, v0, v1))
                        {
                            positiveArray[v0 - 1] |= ValueMask(v1);
                        }
                    }
                }
            }
        }
    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        var board = sudokuSolver.Board;
        bool changed = false;
        foreach (var (markerCells, markerVal) in markers)
        {
            var (i0, j0, i1, j1) = markerCells;
            uint cellMask0 = board[i0, j0] & ~valueSetMask;
            uint cellMask1 = board[i1, j1] & ~valueSetMask;

            // Find which values are compatable between these masks
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                uint valueMask = ValueMask(v);
                uint clearValuesMask = clearValuesPositiveByMarker[markerVal][v - 1];

                // If cell0 has this value and setting it would clear all values from cell1,
                // then remove this value as a candidate from cell0.
                if ((cellMask0 & valueMask) != 0 && (cellMask1 & ~clearValuesMask) == 0)
                {
                    if (!sudokuSolver.ClearValue(i0, j0, v))
                    {
                        return LogicResult.Invalid;
                    }
                    changed = true;
                }

                // If cell1 has this value and setting it would clear all values from cell0,
                // then remove this value as a candidate from cell1.
                if ((cellMask1 & valueMask) != 0 && (cellMask0 & ~clearValuesMask) == 0)
                {
                    if (!sudokuSolver.ClearValue(i1, j1, v))
                    {
                        return LogicResult.Invalid;
                    }
                    changed = true;
                }
            }
        }
        return changed ? LogicResult.Changed : LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        // Enforced by weak links
        return true;
    }

    public override LogicResult StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
    {
        if (isBruteForcing)
        {
            return LogicResult.None;
        }

        var overrideMarkers = GetRelatedConstraints(sudokuSolver).SelectMany(x => x.Markers.Keys).ToHashSet();

        var board = sudokuSolver.Board;

        // Remove candidates which would remove all values from any orthogonal cells
        for (int i0 = 0; i0 < HEIGHT; i0++)
        {
            for (int j0 = 0; j0 < WIDTH; j0++)
            {
                var cell0 = (i0, j0);
                uint mask0 = board[i0, j0];
                if (IsValueSet(mask0))
                {
                    continue;
                }
                foreach (var cell1 in AdjacentCells(i0, j0))
                {
                    var (i1, j1) = cell1;
                    uint mask1 = board[i1, j1];
                    if (IsValueSet(mask1))
                    {
                        continue;
                    }

                    var pair = CellPair(cell0, cell1);
                    uint[] clearValuesArray = markers.TryGetValue(pair, out int markerValue) ? clearValuesPositiveByMarker[markerValue] : negativeConstraint && !overrideMarkers.Contains(pair) ? clearValuesNegative : null;
                    if (clearValuesArray == null)
                    {
                        continue;
                    }

                    List<int> elims = null;
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        uint valueMask = ValueMask(v);
                        uint clearValuesMask = clearValuesArray[v - 1];

                        // If cell0 has this value and setting it would clear all values from cell1,
                        // then remove this value as a candidate from cell0.
                        if ((mask0 & valueMask) != 0 && (mask1 & ~clearValuesMask) == 0)
                        {
                            elims ??= new();
                            elims.Add(sudokuSolver.CandidateIndex((i0, j0), v));
                        }
                    }
                    if (elims != null && elims.Count > 0)
                    {
                        bool invalid = !sudokuSolver.ClearCandidates(elims);
                        logicalStepDescription?.Add(new(
                            desc: $"{MaskToString(mask1)}{CellName(cell1)} => {sudokuSolver.DescribeElims(elims)}",
                            sourceCandidates: sudokuSolver.CandidateIndexes(mask0, cell0.ToEnumerable()),
                            elimCandidates: elims
                        ));
                        return invalid ? LogicResult.Invalid : LogicResult.Changed;
                    }
                }
            }
        }

        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                var cell0 = (i, j);
                uint mask = board[i, j];
                if (IsValueSet(mask))
                {
                    continue;
                }

                List<int> elims = null;
                int maskValueCount = ValueCount(mask);
                if (maskValueCount > 0 && maskValueCount <= 3)
                {
                    // Determine if there are any digits that all the candidates in this cell remove
                    foreach (var cell1 in AdjacentCells(i, j))
                    {
                        var pair = CellPair(cell0, cell1);
                        uint[] clearValuesArray = markers.TryGetValue(pair, out int markerValue) ? clearValuesPositiveByMarker[markerValue] : negativeConstraint && !overrideMarkers.Contains(pair) ? clearValuesNegative : null;
                        if (clearValuesArray == null)
                        {
                            continue;
                        }

                        uint clearMask = ALL_VALUES_MASK;
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if ((mask & ValueMask(v)) != 0)
                            {
                                clearMask &= clearValuesArray[v - 1];
                            }
                        }

                        if (clearMask != 0)
                        {
                            elims ??= new();
                            elims.AddRange(sudokuSolver.CandidateIndexes(clearMask, cell1.ToEnumerable()));
                        }
                    }

                    if (elims != null && elims.Count > 0)
                    {
                        bool invalid = !sudokuSolver.ClearCandidates(elims);
                        logicalStepDescription?.Add(new(
                            desc: $"{MaskToString(mask)}{CellName((i, j))} => {sudokuSolver.DescribeElims(elims)}",
                            sourceCandidates: sudokuSolver.CandidateIndexes(mask, (i, j).ToEnumerable()),
                            elimCandidates: elims
                        ));
                        return invalid ? LogicResult.Invalid : LogicResult.Changed;
                    }
                }
            }
        }

        if (negativeConstraint)
        {
            // Look for groups where a particular digit is locked to 2, 3, or 4 places
            // For the case of 2 places, if they are adjacent then neither can be a banned digit
            // For the case of 3 places, if they are all adjacent then the center one cannot be a banned digit
            // For all cases, any cell that is adjacent to all of them cannot be a banned digit
            // That last one should be a generalized version of the first two if we count a cell as adjacent to itself
            var valInstances = new (int, int)[MAX_VALUE];
            foreach (var group in sudokuSolver.Groups)
            {
                // This logic only works if the value found must be in the group.
                // The only way to currently guarantee this is by only applying it to groups of maximum size.
                // In the future, it might be useful to track stuff like "this killer cage must contain a 1"
                // and then apply this logic there.
                if (group.Cells.Count != MAX_VALUE)
                {
                    continue;
                }

                for (int val = 1; val <= MAX_VALUE; val++)
                {
                    uint valMask = ValueMask(val);
                    int numValInstances = 0;
                    foreach (var pair in group.Cells.Select(sudokuSolver.CellIndexToCoord))
                    {
                        uint mask = board[pair.Item1, pair.Item2];
                        if (IsValueSet(mask))
                        {
                            if ((mask & valMask) != 0)
                            {
                                numValInstances = 0;
                                break;
                            }
                            continue;
                        }
                        if ((mask & valMask) != 0)
                        {
                            valInstances[numValInstances++] = pair;
                        }
                    }
                    if (numValInstances >= 2 && numValInstances <= 5)
                    {
                        numTimes++;

                        bool tooFar = false;
                        var firstCell = valInstances[0];
                        var minCoord = firstCell;
                        var maxCoord = firstCell;
                        for (int i = 1; i < numValInstances; i++)
                        {
                            var curCell = valInstances[i];
                            int curDist = TaxicabDistance(firstCell.Item1, firstCell.Item2, curCell.Item1, curCell.Item2);
                            if (curDist > 2)
                            {
                                tooFar = true;
                                break;
                            }
                            minCoord = (Math.Min(minCoord.Item1, curCell.Item1), Math.Min(minCoord.Item2, curCell.Item2));
                            maxCoord = (Math.Max(maxCoord.Item1, curCell.Item1), Math.Max(maxCoord.Item2, curCell.Item2));
                        }

                        if (!tooFar)
                        {
                            uint clearMask = clearValuesNegative[val - 1] & ~ValueMask(val);

                            List<int> elims = null;
                            for (int i = minCoord.Item1; i <= maxCoord.Item1; i++)
                            {
                                for (int j = minCoord.Item2; j <= maxCoord.Item2; j++)
                                {
                                    uint mask1 = board[i, j];
                                    if (IsValueSet(mask1) || (mask1 & clearMask) == 0)
                                    {
                                        continue;
                                    }

                                    var cell0 = (i, j);
                                    bool allAdjacent = true;
                                    bool hasAnyMarker = false;
                                    for (int valIndex = 0; valIndex < numValInstances; valIndex++)
                                    {
                                        var cell1 = valInstances[valIndex];
                                        if (!IsAdjacent(i, j, cell1.Item1, cell1.Item2))
                                        {
                                            allAdjacent = false;
                                            break;
                                        }
                                        var pair = CellPair(cell0, cell1);
                                        if (markers.ContainsKey(pair) || overrideMarkers.Contains(pair))
                                        {
                                            hasAnyMarker = true;
                                            break;
                                        }
                                    }
                                    if (allAdjacent && !hasAnyMarker)
                                    {
                                        elims ??= new();
                                        elims.AddRange(sudokuSolver.CandidateIndexes(clearMask, (i, j).ToEnumerable()));
                                    }
                                }
                            }

                            if (elims != null && elims.Count > 0)
                            {
                                bool invalid = !sudokuSolver.ClearCandidates(elims);
                                logicalStepDescription?.Add(new(
                                    desc: $"{group} has {val} always adjacent to one or more cells => {sudokuSolver.DescribeElims(elims)}",
                                    sourceCandidates: sudokuSolver.CandidateIndexes(valMask, valInstances),
                                    elimCandidates: elims
                                ));
                                return invalid ? LogicResult.Invalid : LogicResult.Changed;
                            }
                        }
                    }
                }
            }
        }

        // Look for adjacent squares with a shared value plus two values that cannot be adjacent.
        // The shared value must be in one of those two squares, eliminating it from
        // the rest of their shared groups.
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                (int, int) cellA = (i, j);
                uint maskA = board[i, j];
                if (IsValueSet(maskA) || ValueCount(maskA) > 3)
                {
                    continue;
                }
                for (int d = 0; d < 2; d++)
                {
                    if (d == 0 && i == HEIGHT - 1)
                    {
                        continue;
                    }
                    if (d == 1 && j == WIDTH - 1)
                    {
                        continue;
                    }
                    (int, int) cellB = d == 0 ? (i + 1, j) : (i, j + 1);
                    uint maskB = board[cellB.Item1, cellB.Item2];
                    if (IsValueSet(maskB))
                    {
                        continue;
                    }

                    uint combinedMask = maskA | maskB;
                    if (ValueCount(combinedMask) != 3)
                    {
                        continue;
                    }

                    var pair = CellPair(cellA, cellB);
                    uint[] clearValuesArray = markers.TryGetValue(pair, out int markerValue) ? clearValuesPositiveByMarker[markerValue] : negativeConstraint && !overrideMarkers.Contains(pair) ? clearValuesNegative : null;
                    if (clearValuesArray == null)
                    {
                        continue;
                    }

                    int valA = 0;
                    int valB = 0;
                    int valC = 0;
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if ((combinedMask & ValueMask(v)) != 0)
                        {
                            if (valA == 0)
                            {
                                valA = v;
                            }
                            else if (valB == 0)
                            {
                                valB = v;
                            }
                            else
                            {
                                valC = v;
                                break;
                            }
                        }
                    }

                    uint valMaskA = ValueMask(valA);
                    uint valMaskB = ValueMask(valB);
                    uint valMaskC = ValueMask(valC);

                    int mustHaveVal = 0;
                    if ((clearValuesArray[valA - 1] & valMaskB) != 0)
                    {
                        mustHaveVal = valC;
                    }
                    else if ((clearValuesArray[valA - 1] & valMaskC) != 0)
                    {
                        mustHaveVal = valB;
                    }
                    else if ((clearValuesArray[valB - 1] & valMaskC) != 0)
                    {
                        mustHaveVal = valA;
                    }
                    List<int> elims = null;
                    if (mustHaveVal != 0)
                    {
                        uint mustHaveMask = ValueMask(mustHaveVal);

                        elims ??= new();
                        elims.AddRange(sudokuSolver.CandidateIndexes(mustHaveMask, sudokuSolver.SeenCellsByValueMask(mustHaveMask, cellA, cellB)));
                    }

                    if (elims != null && elims.Count > 0)
                    {
                        bool invalid = !sudokuSolver.ClearCandidates(elims);
                        logicalStepDescription?.Add(new(
                            desc: $"{MaskToString(maskA)}{CellName(i, j)} and {MaskToString(maskB)}{CellName(cellB)} are adjacent meaning they must contain {mustHaveVal} => {sudokuSolver.DescribeElims(elims)}",
                            sourceCandidates: sudokuSolver.CandidateIndexes(ALL_VALUES_MASK, new (int, int)[] { (i, j), cellB }),
                            elimCandidates: elims
                        ));
                        return invalid ? LogicResult.Invalid : LogicResult.Changed;
                    }
                }
            }
        }
        return LogicResult.None;
    }

    public override LogicResult InitLinks(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)
    {
        if (!isInitializing)
        {
            return LogicResult.None;
        }

        var overrideMarkers = GetRelatedConstraints(sudokuSolver).SelectMany(x => x.Markers.Keys).ToHashSet();

        for (int i0 = 0; i0 < HEIGHT; i0++)
        {
            for (int j0 = 0; j0 < WIDTH; j0++)
            {
                var cell0 = (i0, j0);
                int cellIndex0 = FlatIndex(cell0);
                foreach (var cell1 in AdjacentCells(i0, j0))
                {
                    int cellIndex1 = FlatIndex(cell1);
                    var pair = CellPair(cell0, cell1);
                    if (markers.TryGetValue(pair, out int markerValue))
                    {
                        for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                        {
                            uint clearValues = clearValuesPositiveByMarker[markerValue][v0 - 1];
                            if (clearValues != 0)
                            {
                                int candIndex0 = cellIndex0 * MAX_VALUE + v0 - 1;
                                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                                {
                                    if ((clearValues & ValueMask(v1)) != 0)
                                    {
                                        int candIndex1 = cellIndex1 * MAX_VALUE + v1 - 1;
                                        sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                                    }
                                }
                            }
                        }
                    }
                    else if (negativeConstraint && !overrideMarkers.Contains(pair))
                    {
                        for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                        {
                            uint clearValues = clearValuesNegative[v0 - 1];
                            if (clearValues != 0)
                            {
                                int candIndex0 = cellIndex0 * MAX_VALUE + v0 - 1;
                                for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                                {
                                    if ((clearValues & ValueMask(v1)) != 0)
                                    {
                                        int candIndex1 = cellIndex1 * MAX_VALUE + v1 - 1;
                                        sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return LogicResult.None;
    }

    public override IEnumerable<Constraint> SplitToPrimitives(Solver sudokuSolver)
    {
        // Return the list of each marker and each negative constraint as an individual constraint object

        var constraints = markers.Select(marker => createMarkerConstraint(
            sudokuSolver,
            marker.Value,
            (marker.Key.Item1, marker.Key.Item2),
            (marker.Key.Item3, marker.Key.Item4)
        ));

        if (negativeConstraint)
        {
            constraints = constraints.Concat(negativeConstraintValues.Select(
                value => createNegativeConstraint(sudokuSolver, value)
            ));
        }

        return constraints;
    }
}
