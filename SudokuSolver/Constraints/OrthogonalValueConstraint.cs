using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    public abstract class OrthogonalValueConstraint : Constraint
    {
        protected readonly Dictionary<(int, int, int, int), int> markers = new();
        protected readonly bool negativeConstraint = false;
        protected readonly Dictionary<int, uint[]> clearValuesPositiveByMarker = new();
        protected readonly uint[] clearValuesNegative;

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

        protected abstract int DefaultMarkerValue { get; }

        public Dictionary<(int, int, int, int), int> Markers => markers;


        private static readonly Regex negRegex = new(@"neg(\d*)");
        private static readonly Regex twoCellsRegex = new(@"(\d*)r(\d+)c(\d+)r(\d+)c(\d+)");
        private static readonly Regex sharedRowRegex = new(@"(\d*)r(\d+)[,-](\d+)c(\d+)");
        private static readonly Regex sharedColRegex = new(@"(\d*)r(\d+)c(\d+)[,-](\d+)");

        public OrthogonalValueConstraint(string options)
        {
            HashSet<int> negativeConstraintValues = new();
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

            clearValuesNegative = new uint[MAX_VALUE];
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

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            var overrideMarkers = GetRelatedConstraints(sudokuSolver).SelectMany(x => x.Markers.Keys).ToHashSet();

            var cell0 = (i, j);
            foreach (var cell1 in AdjacentCells(i, j))
            {
                var pair = CellPair(cell0, cell1);
                if (markers.TryGetValue(pair, out int markerValue))
                {
                    var clearResult = sudokuSolver.ClearMask(cell1.Item1, cell1.Item2, clearValuesPositiveByMarker[markerValue][val - 1]);
                    if (clearResult == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
                else if (negativeConstraint && !overrideMarkers.Contains(pair))
                {
                    var clearResult = sudokuSolver.ClearMask(cell1.Item1, cell1.Item2, clearValuesNegative[val - 1]);
                    if (clearResult == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            var overrideMarkers = GetRelatedConstraints(sudokuSolver).SelectMany(x => x.Markers.Keys).ToHashSet();

            var board = sudokuSolver.Board;
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

                    int maskValueCount = ValueCount(mask);
                    if (maskValueCount > 0 && maskValueCount <= 3)
                    {
                        // Determine if there are any digits that all the candidates in this cell remove
                        bool haveChanges = false;
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
                                LogicResult clearResult = sudokuSolver.ClearMask(cell1.Item1, cell1.Item2, clearMask);
                                if (clearResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} with values {MaskToString(mask)} removes the only candidates {MaskToString(clearMask)} from {CellName(cell1)}");
                                    return LogicResult.Invalid;
                                }

                                if (clearResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName((i, j))} having candidates {MaskToString(mask)} removes {MaskToString(clearMask)} from {CellName(cell1)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {MaskToString(clearMask)} from {CellName(cell1)}");
                                    }
                                }
                            }
                        }
                        if (haveChanges)
                        {
                            return LogicResult.Changed;
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
                    // The only way to currently guarantee this is by only applying it to groups of size 9.
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
                        foreach (var pair in group.Cells)
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
                                uint clearMask = clearValuesNegative[val - 1];

                                bool changed = false;
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
                                            LogicResult clearResult = sudokuSolver.ClearMask(i, j, clearMask);
                                            if (clearResult == LogicResult.Invalid)
                                            {
                                                logicalStepDescription.Clear();
                                                logicalStepDescription.Append($"{group} has {val} always adjacent to {CellName(i, j)}, but cannot clear values {MaskToString(clearMask)} from that cell.");
                                                return LogicResult.Invalid;
                                            }
                                            if (clearResult == LogicResult.Changed)
                                            {
                                                if (!changed)
                                                {
                                                    logicalStepDescription.Append($"{group} has {val} always adjacent to one or more cells, removing {MaskToString(clearMask)} from {CellName(i, j)}");
                                                    changed = true;
                                                }
                                                else
                                                {
                                                    logicalStepDescription.Append($", {CellName(i, j)}");
                                                }
                                            }
                                        }
                                    }
                                }
                                if (changed)
                                {
                                    return LogicResult.Changed;
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
                        uint[] clearValuesArray = markers.TryGetValue(pair, out int markerValue) ? clearValuesPositiveByMarker[markerValue]: negativeConstraint && !overrideMarkers.Contains(pair) ? clearValuesNegative : null;
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
                        bool haveChanges = false;
                        if (mustHaveVal != 0)
                        {
                            uint mustHaveMask = ValueMask(mustHaveVal);
                            foreach (var otherCell in sudokuSolver.SeenCells(cellA, cellB))
                            {
                                uint otherMask = board[otherCell.Item1, otherCell.Item2];
                                if (IsValueSet(otherMask))
                                {
                                    continue;
                                }
                                LogicResult findResult = sudokuSolver.ClearMask(otherCell.Item1, otherCell.Item2, mustHaveMask);
                                if (findResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription.Clear();
                                    logicalStepDescription.Append($"{CellName(i, j)} with candidates {MaskToString(maskA)} and {CellName(cellB)} with candidates {MaskToString(maskB)} are adjacent, meaning they must contain {mustHaveVal}, but cannot clear that value from {CellName(otherCell)}.");
                                    return LogicResult.Invalid;
                                }
                                if (findResult == LogicResult.Changed)
                                {
                                    if (!haveChanges)
                                    {
                                        logicalStepDescription.Append($"{CellName(i, j)} with candidates {MaskToString(maskA)} and {CellName(cellB)} with candidates {MaskToString(maskB)} are adjacent, meaning they must contain {mustHaveVal}, clearing it from {CellName(otherCell)}");
                                        haveChanges = true;
                                    }
                                    else
                                    {
                                        logicalStepDescription.Append($", {CellName(otherCell)}");
                                    }
                                }
                            }
                        }
                        if (haveChanges)
                        {
                            return LogicResult.Changed;
                        }
                    }
                }
            }
            return LogicResult.None;
        }
    }
}
