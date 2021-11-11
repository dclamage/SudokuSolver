using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LZStringCSharp;
using SudokuSolver.Constraints;
using SudokuSolver.PuzzleFormats;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver
{
    [JsonSerializable(typeof(FPuzzlesBoard))]
    internal partial class FpuzzlesJsonContext : JsonSerializerContext
    {
    }

    public static class SolverFactory
    {
        public static Solver CreateBlank(int size, IEnumerable<string> constraints = null)
        {
            Solver solver = new(size, size, size);
            solver.SetRegions(DefaultRegions(size));
            if (constraints != null)
            {
                ApplyConstraints(solver, constraints);
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }
            return solver;
        }

        public static Solver CreateFromCandidates(string candidates, IEnumerable<string> constraints = null)
        {
            candidates = candidates.Trim();

            int size;
            if (candidates.Length <= 729) // all cases where the digits can be of length 1
            {
                size = (int)Math.Cbrt(candidates.Length);
                if (candidates.Length != size * size * size)
                {
                    throw new ArgumentException($"ERROR: A candidates string must be a perfect cube in length (Provided length: {candidates.Length}).");
                }
            }
            else
            {
                size = (int)Math.Cbrt(candidates.Length / 2); // digits are of length 2 here.
                if (candidates.Length != size * size * size * 2)
                {
                    throw new ArgumentException($"ERROR: A candidates string must be a perfect cube in length (Provided length: {candidates.Length}).");
                }
            }

            Solver solver = new(size, size, size);
            solver.SetRegions(DefaultRegions(size));
            if (constraints != null)
            {
                ApplyConstraints(solver, constraints);
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }

            // I would personally extract this to a method (str -> FlatMap), but your style appears to be more based around big functions, what do you think?
            int digitLength = size <= 9 ? 1 : 2;
            int sectionLength = size * digitLength;

            List<List<int>> candidatesFlatMap = new();
            int sectionStartIndex = 0;
            do
            {
                string currentCellCandidates = candidates.Substring(sectionStartIndex, sectionLength);
                candidatesFlatMap.Add(new List<int>());
                for (int currentNumIndex = 0; currentNumIndex < sectionLength; currentNumIndex += digitLength)
                {
                    string maybeNumber = currentCellCandidates.Substring(currentNumIndex, digitLength);
                    if (maybeNumber == (digitLength == 1 ? "." : "..")) // No number is parsed as a dot
                    {
                        continue;
                    }

                    try
                    {
                        candidatesFlatMap.Last().Add(Int32.Parse(maybeNumber));
                    }
                    catch (FormatException)
                    {
                        throw new ArgumentException($"ERROR: Could not parse a number in a candidates string: {maybeNumber}");
                    }

                }

                sectionStartIndex += sectionLength;
            }
            while (sectionStartIndex < candidates.Length);

            bool[,] isOriginalGiven = new bool[size, size];
            solver.customInfo["Givens"] = isOriginalGiven;

            int flatMapIndex = 0;
            for (int row_i = 0; row_i < size; row_i++)
            {
                for (int col_i = 0; col_i < size; col_i++)
                {
                    if (!solver.SetMask(row_i, col_i, candidatesFlatMap.ElementAt(flatMapIndex)))
                    {
                        throw new ArgumentException($"ERROR: Candidates string is of invalid board (no solutions).");
                    }
                    isOriginalGiven[row_i, col_i] = candidatesFlatMap.ElementAt(flatMapIndex).Count == 1;

                    flatMapIndex++;
                }
            }

            return solver;
        }

        public static Solver CreateFromGivens(string givens, IEnumerable<string> constraints = null)
        {
            givens = givens.Trim();

            int size;
            if (givens.Length <= 81)
            {
                size = (int)Math.Sqrt(givens.Length);
                if (givens.Length != size * size)
                {
                    throw new WrongLengthGivensException($"ERROR: A givens string must be a perfect square in length (Provided length: {givens.Length}).");
                }
            }
            else
            {
                size = (int)Math.Sqrt(givens.Length / 2);
                if (givens.Length != size * size * 2)
                {
                    throw new WrongLengthGivensException($"ERROR: A givens string must be a perfect square in length (Provided length: {givens.Length}).");
                }
            }

            Solver solver = new(size, size, size);
            solver.SetRegions(DefaultRegions(size));
            if (constraints != null)
            {
                ApplyConstraints(solver, constraints);
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }

            bool[,] isOriginalGiven = new bool[size, size];
            solver.customInfo["Givens"] = isOriginalGiven;

            if (size <= 9)
            {
                for (int i = 0; i < givens.Length; i++)
                {
                    char c = givens[i];
                    if (c >= '1' && c <= '9')
                    {
                        if (!solver.SetValue(i / size, i % size, c - '0'))
                        {
                            throw new ArgumentException($"ERROR: Givens cause there to be no solutions.");
                        }
                        isOriginalGiven[i / size, i % size] = true;
                    }
                }
            }
            else
            {
                int numVals = givens.Length / 2;
                for (int i = 0; i < numVals; i++)
                {
                    char c0 = givens[i * 2];
                    char c1 = givens[i * 2 + 1];
                    if (c0 >= '1' && c0 <= '9' && c1 >= '0' && c1 <= '9' || c0 == '0' && c1 >= '1' && c1 <= '9')
                    {
                        int val = (c0 - '0') * 10 + (c1 - '0');
                        if (!solver.SetValue(i / size, i % size, val))
                        {
                            throw new ArgumentException($"ERROR: Givens cause there to be no solutions.");
                        }
                        isOriginalGiven[i / size, i % size] = true;
                    }
                }
            }
            return solver;
        }

        public static string FixGivensString(string givens)
        {
            givens = givens.Trim();

            int delta = int.MaxValue;
            for (int potSideLength = 1; potSideLength <= 31; potSideLength++) // 31 is the max grid size
            {
                int potStrLength = potSideLength <= 9 ? potSideLength * potSideLength : potSideLength * potSideLength * 2;
                int potError = givens.Length - potStrLength;
                if (Math.Abs(potError) < Math.Abs(delta))
                {
                    delta = potError;
                }
            }

            int potIntendedLength = givens.Length - delta;

            while (givens.Length < potIntendedLength)
            {
                givens += '.';
            }
            if (givens.Length > potIntendedLength)
            {
                givens = givens.Substring(0, potIntendedLength);
            }

            return givens;
        }
        public static Solver CreateFromFPuzzles(string fpuzzlesURL, IEnumerable<string> additionalConstraints = null, bool onlyGivens = false)
        {
            using MemoryStream comparableDataStream = new();
            using BinaryWriter comparableData = new(comparableDataStream);

            if (fpuzzlesURL.Contains("?load="))
            {
                int trimStart = fpuzzlesURL.IndexOf("?load=") + "?load=".Length;
                fpuzzlesURL = fpuzzlesURL[trimStart..];
            }

            string fpuzzlesJson = LZString.DecompressFromBase64(fpuzzlesURL);
            var fpuzzlesData = JsonSerializer.Deserialize(fpuzzlesJson, FpuzzlesJsonContext.Default.FPuzzlesBoard);

            // Set the default regions
            int i, j;
            int height = fpuzzlesData.grid.Length;
            int width = fpuzzlesData.grid[0].Length;
            if (height != width)
            {
                throw new ArgumentException($"f-puzzles import is non-square {height}x{width}");
            }
            comparableData.Write(height);
            comparableData.Write(width);

            // Start with default regions
            int[,] regions = DefaultRegions(height);

            // Override regions
            for (i = 0; i < height; i++)
            {
                for (j = 0; j < width; j++)
                {
                    if (fpuzzlesData.grid[i][j].region != -1)
                    {
                        regions[i, j] = fpuzzlesData.grid[i][j].region;
                    }
                }
            }

            int[] regionSanityCheck = new int[width];
            for (i = 0; i < height; i++)
            {
                for (j = 0; j < width; j++)
                {
                    regionSanityCheck[regions[i, j]]++;
                }
            }
            for (i = 0; i < width; i++)
            {
                if (regionSanityCheck[i] != width)
                {
                    throw new ArgumentException($"Region {i + 1} has {regionSanityCheck[i]} cells, expected {width}");
                }
            }

            for (i = 0; i < height; i++)
            {
                for (j = 0; j < width; j++)
                {
                    comparableData.Write(regions[i, j]);
                }
            }

            Solver solver = new(width, width, width)
            {
                Title = fpuzzlesData.title,
                Author = fpuzzlesData.author,
                Rules = fpuzzlesData.ruleset
            };
            uint disabledLogicFlags = 0;
            if (fpuzzlesData.disabledlogic != null)
            {
                foreach (string logicName in fpuzzlesData.disabledlogic)
                {
                    switch (logicName.ToLowerInvariant())
                    {
                        case "tuples":
                            solver.DisableTuples = true;
                            disabledLogicFlags |= (1u << 0);
                            break;
                        case "pointing":
                            solver.DisablePointing = true;
                            disabledLogicFlags |= (1u << 1);
                            break;
                        case "fishes":
                            solver.DisableFishes = true;
                            disabledLogicFlags |= (1u << 2);
                            break;
                        case "wings":
                            solver.DisableWings = true;
                            disabledLogicFlags |= (1u << 3);
                            break;
                        case "aic":
                            solver.DisableAIC = true;
                            disabledLogicFlags |= (1u << 4);
                            break;
                        case "contradictions":
                            solver.DisableContradictions = true;
                            disabledLogicFlags |= (1u << 5);
                            break;
                    }
                }
            }
            comparableData.Write(disabledLogicFlags);

            uint trueCandidatesOptionFlags = 0;
            if (fpuzzlesData.truecandidatesoptions != null)
            {
                if (fpuzzlesData.truecandidatesoptions.Contains("colored"))
                {
                    solver.customInfo.Add("truecandidatescolored", true);
                    trueCandidatesOptionFlags |= (1u << 0);
                }
                if (fpuzzlesData.truecandidatesoptions.Contains("logical"))
                {
                    solver.customInfo.Add("truecandidateslogical", true);
                    trueCandidatesOptionFlags |= (1u << 1);
                }
            }
            comparableData.Write(trueCandidatesOptionFlags);

            solver.SetRegions(regions);

            // Extra groups
            if (fpuzzlesData.diagonalp)
            {
                solver.AddConstraint(typeof(DiagonalPositiveGroupConstraint), string.Empty);
            }
            if (fpuzzlesData.diagonaln)
            {
                solver.AddConstraint(typeof(DiagonalNegativeGroupConstraint), string.Empty);
            }
            if (fpuzzlesData.antiknight)
            {
                solver.AddConstraint(typeof(KnightConstraint), string.Empty);
            }
            if (fpuzzlesData.antiking)
            {
                solver.AddConstraint(typeof(KingConstraint), string.Empty);
            }
            if (fpuzzlesData.disjointgroups)
            {
                solver.AddConstraint(typeof(DisjointConstraintGroup), string.Empty);
            }

            // Marked constraints
            if (fpuzzlesData.arrow != null)
            {
                foreach (var arrow in fpuzzlesData.arrow)
                {
                    foreach (var lines in arrow.lines)
                    {
                        // Construct the arrow string
                        StringBuilder cells = new();
                        foreach (string cell in arrow.cells)
                        {
                            cells.Append(cell);
                        }
                        cells.Append(';');

                        // The first cell specified is the one the line origintates from
                        foreach (string cell in lines.Skip(1))
                        {
                            cells.Append(cell);
                        }
                        solver.AddConstraint(typeof(ArrowSumConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.killercage != null)
            {
                foreach (var cage in fpuzzlesData.killercage)
                {
                    StringBuilder cells = new();
                    if (cage.value != null)
                    {
                        cells.Append(cage.value).Append(';');
                    }
                    foreach (string cell in cage.cells)
                    {
                        cells.Append(cell);
                    }
                    solver.AddConstraint(typeof(KillerCageConstraint), cells.ToString());
                }
            }

            if (fpuzzlesData.littlekillersum != null)
            {
                foreach (var lksum in fpuzzlesData.littlekillersum)
                {
                    if (!string.IsNullOrWhiteSpace(lksum.value) && lksum.value != "0")
                    {
                        solver.AddConstraint(typeof(LittleKillerConstraint), $"{lksum.value};{lksum.cell};{lksum.direction}");
                    }
                }
            }

            if (fpuzzlesData.odd != null && fpuzzlesData.odd.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.odd)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                solver.AddConstraint(typeof(OddConstraint), cells.ToString());
            }

            if (fpuzzlesData.even != null && fpuzzlesData.even.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.even)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                solver.AddConstraint(typeof(EvenConstraint), cells.ToString());
            }

            if (fpuzzlesData.minimum != null && fpuzzlesData.minimum.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.minimum)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                solver.AddConstraint(typeof(MinimumConstraint), cells.ToString());
            }

            if (fpuzzlesData.maximum != null && fpuzzlesData.maximum.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.maximum)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                solver.AddConstraint(typeof(MaximumConstraint), cells.ToString());
            }

            if (fpuzzlesData.rowindexer != null && fpuzzlesData.rowindexer.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var rowindexer in fpuzzlesData.rowindexer)
                {
                    foreach (var cell in rowindexer.cells)
                    {
                        if (!string.IsNullOrWhiteSpace(cell))
                        {
                            cells.Append(cell);
                        }
                    }
                }
                solver.AddConstraint(typeof(RowIndexerConstraint), cells.ToString());
            }

            if (fpuzzlesData.columnindexer != null && fpuzzlesData.columnindexer.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var colindexer in fpuzzlesData.columnindexer)
                {
                    foreach (var cell in colindexer.cells)
                    {
                        if (!string.IsNullOrWhiteSpace(cell))
                        {
                            cells.Append(cell);
                        }
                    }
                }
                solver.AddConstraint(typeof(ColIndexerConstraint), cells.ToString());
            }

            if (fpuzzlesData.boxindexer != null && fpuzzlesData.boxindexer.Length > 0)
            {
                StringBuilder cells = new();
                foreach (var boxindexer in fpuzzlesData.boxindexer)
                {
                    foreach (var cell in boxindexer.cells)
                    {
                        if (!string.IsNullOrWhiteSpace(cell))
                        {
                            cells.Append(cell);
                        }
                    }
                }
                solver.AddConstraint(typeof(BoxIndexerConstraint), cells.ToString());
            }

            if (fpuzzlesData.extraregion != null)
            {
                foreach (var extraRegion in fpuzzlesData.extraregion)
                {
                    StringBuilder cells = new();
                    foreach (var cell in extraRegion.cells)
                    {
                        if (!string.IsNullOrWhiteSpace(cell))
                        {
                            cells.Append(cell);
                        }
                    }
                    solver.AddConstraint(typeof(ExtraRegionConstraint), cells.ToString());
                }
            }

            if (fpuzzlesData.thermometer != null)
            {
                foreach (var thermo in fpuzzlesData.thermometer)
                {
                    foreach (var line in thermo.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(ThermometerConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.palindrome != null)
            {
                foreach (var palindrome in fpuzzlesData.palindrome)
                {
                    foreach (var line in palindrome.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(PalindromeConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.renban != null)
            {
                foreach (var renban in fpuzzlesData.renban)
                {
                    foreach (var line in renban.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(RenbanConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.whispers != null)
            {
                foreach (var whispers in fpuzzlesData.whispers)
                {
                    foreach (var line in whispers.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(WhispersConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.regionsumline != null)
            {
                foreach (var regionSumLine in fpuzzlesData.regionsumline)
                {
                    foreach (var line in regionSumLine.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(RegionSumLinesConstraint), cells.ToString());
                    }
                }
            }

            if (fpuzzlesData.betweenline != null)
            {
                foreach (var betweenline in fpuzzlesData.betweenline)
                {
                    foreach (var line in betweenline.lines)
                    {
                        StringBuilder cells = new();
                        foreach (var cell in line)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        solver.AddConstraint(typeof(BetweenLineConstraint), cells.ToString());
                    }
                }
            }

            bool negativeRatio = fpuzzlesData.negative?.Contains("ratio") ?? false;
            if (fpuzzlesData.ratio != null && fpuzzlesData.ratio.Length > 0 || negativeRatio)
            {
                StringBuilder ratioParams = new();
                if (negativeRatio)
                {
                    HashSet<string> ratioValues = fpuzzlesData.ratio != null && fpuzzlesData.ratio.Length > 0 ? new(fpuzzlesData.ratio.Select(r => r.value)) : new() { "2" };
                    foreach (string ratioValue in ratioValues)
                    {
                        if (ratioParams.Length > 0)
                        {
                            ratioParams.Append(';');
                        }
                        ratioParams.Append($"neg{ratioValue}");
                    }
                }

                if (fpuzzlesData.ratio != null)
                {
                    foreach (var ratio in fpuzzlesData.ratio)
                    {
                        if (ratioParams.Length > 0)
                        {
                            ratioParams.Append(';');
                        }
                        ratioParams.Append(ratio.value);
                        foreach (var cell in ratio.cells)
                        {
                            ratioParams.Append(cell);
                        }
                    }
                }

                if (ratioParams.Length > 0)
                {
                    solver.AddConstraint(typeof(RatioConstraint), ratioParams.ToString());
                }
            }

            if (fpuzzlesData.difference != null && fpuzzlesData.difference.Length > 0 || fpuzzlesData.nonconsecutive)
            {
                StringBuilder differenceParams = new();
                if (fpuzzlesData.nonconsecutive)
                {
                    // f-puzzles only supports negative constraint for difference of 1, which
                    // it calls nonconsecutive.
                    if (differenceParams.Length > 0)
                    {
                        differenceParams.Append(';');
                    }
                    differenceParams.Append("neg1");
                }

                if (fpuzzlesData.difference != null)
                {
                    foreach (var difference in fpuzzlesData.difference)
                    {
                        if (differenceParams.Length > 0)
                        {
                            differenceParams.Append(';');
                        }
                        differenceParams.Append(difference.value);
                        foreach (var cell in difference.cells)
                        {
                            differenceParams.Append(cell);
                        }
                    }
                }

                if (differenceParams.Length > 0)
                {
                    solver.AddConstraint(typeof(DifferenceConstraint), differenceParams.ToString());
                }
            }

            bool negativeXV = fpuzzlesData.negative?.Contains("xv") ?? false;
            if (fpuzzlesData.xv != null && fpuzzlesData.xv.Length > 0 || negativeXV)
            {
                StringBuilder sumParams = new();
                if (negativeXV)
                {
                    // f-puzzles always does negative constraint for both X and V when enabled.
                    if (sumParams.Length > 0)
                    {
                        sumParams.Append(';');
                    }
                    sumParams.Append("neg5;neg10");
                }

                if (fpuzzlesData.xv != null)
                {
                    foreach (var xv in fpuzzlesData.xv)
                    {
                        if (string.IsNullOrWhiteSpace(xv.value))
                        {
                            continue;
                        }

                        var xvValue = xv.value switch
                        {
                            "x" or "X" => "10",
                            "v" or "V" => "5",
                            _ => throw new ArgumentException($"Unrecognized XV value: {xv.value}"),
                        };
                        if (sumParams.Length > 0)
                        {
                            sumParams.Append(';');
                        }
                        sumParams.Append(xvValue);
                        foreach (var cell in xv.cells)
                        {
                            sumParams.Append(cell);
                        }
                    }
                }

                if (sumParams.Length > 0)
                {
                    solver.AddConstraint(typeof(SumConstraint), sumParams.ToString());
                }
            }

            if (fpuzzlesData.clone != null && fpuzzlesData.clone.Length > 0)
            {
                foreach (var clone in fpuzzlesData.clone)
                {
                    StringBuilder cloneParams = new();
                    for (int cloneIndex = 0; cloneIndex < clone.cells.Length; cloneIndex++)
                    {
                        string cell0 = clone.cells[cloneIndex];
                        string cell1 = clone.cloneCells[cloneIndex];
                        if (cell0 == cell1)
                        {
                            continue;
                        }

                        if (cloneParams.Length > 0)
                        {
                            cloneParams.Append(';');
                        }
                        cloneParams.Append(cell0).Append(cell1);
                    }

                    if (cloneParams.Length > 0)
                    {
                        solver.AddConstraint(typeof(CloneConstraint), cloneParams.ToString());
                    }
                }
            }

            if (fpuzzlesData.quadruple != null && fpuzzlesData.quadruple.Length > 0)
            {
                foreach (var quad in fpuzzlesData.quadruple)
                {
                    StringBuilder quadParams = new();
                    foreach (int value in quad.values)
                    {
                        if (quadParams.Length > 0)
                        {
                            quadParams.Append(';');
                        }
                        quadParams.Append(value);
                    }

                    if (quadParams.Length > 0)
                    {
                        quadParams.Append(';');
                    }
                    foreach (var cell in quad.cells)
                    {
                        quadParams.Append(cell);
                    }

                    if (quadParams.Length > 0)
                    {
                        solver.AddConstraint(typeof(QuadrupleConstraint), quadParams.ToString());
                    }
                }
            }

            if (fpuzzlesData.sandwichsum != null)
            {
                foreach (var sandwich in fpuzzlesData.sandwichsum)
                {
                    if (string.IsNullOrWhiteSpace(sandwich.value) || string.IsNullOrWhiteSpace(sandwich.cell))
                    {
                        continue;
                    }

                    solver.AddConstraint(typeof(SandwichConstraint), $"{sandwich.value}{sandwich.cell}");
                }
            }

            // Apply any command-line constraints
            if (additionalConstraints != null)
            {
                ApplyConstraints(solver, additionalConstraints);
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }

            bool[,] isOriginalGiven = new bool[height, width];
            solver.customInfo["Givens"] = isOriginalGiven;

            uint[,] originalCenterMarks = !onlyGivens ? new uint[height, width] : null;
            solver.customInfo["OriginalCenterMarks"] = originalCenterMarks;

            i = 0;
            foreach (var row in fpuzzlesData.grid)
            {
                j = 0;
                foreach (var val in row)
                {
                    if (!onlyGivens)
                    {
                        if (val.value != 0)
                        {
                            originalCenterMarks[i, j] = ValueMask(val.value);
                        }
                        else if (val.centerPencilMarks != null)
                        {
                            foreach (int v in val.centerPencilMarks)
                            {
                                originalCenterMarks[i, j] |= ValueMask(v);
                            }
                        }
                    }

                    bool wroteValue = false;
                    int value = val.given || !onlyGivens ? val.value : 0;
                    int[] pencilMarks = !onlyGivens && val.centerPencilMarks != null && val.centerPencilMarks.Length > 0 ? val.centerPencilMarks : val.givenPencilMarks;
                    if (value != 0)
                    {
                        if (!solver.SetValue(i, j, value))
                        {
                            throw new ArgumentException("ERROR: The givens are invalid (no solutions).");
                        }
                        comparableData.Write(ValueMask(val.value) | valueSetMask);
                        wroteValue = true;
                    }
                    else if (pencilMarks != null && pencilMarks.Length > 0)
                    {
                        uint marksMask = 0;
                        foreach (int v in pencilMarks)
                        {
                            marksMask |= ValueMask(v);
                        }
                        if (solver.KeepMask(i, j, marksMask) == LogicResult.Invalid)
                        {
                            throw new ArgumentException("ERROR: The center marks are invalid (no solutions).");
                        }
                        comparableData.Write(marksMask);
                        wroteValue = true;
                    }

                    if (!wroteValue)
                    {
                        comparableData.Write((uint)0);
                    }

                    isOriginalGiven[i, j] = val.given;
                    j++;
                }
                i++;
            }

            if (solver.customInfo.TryGetValue("ConstraintStrings", out object constraintStringsObj) && constraintStringsObj is List<string> constraintStrings)
            {
                constraintStrings.Sort();
                foreach (string constraint in constraintStrings)
                {
                    comparableData.Write(constraint);
                }
            }
            solver.customInfo["ComparableData"] = comparableDataStream.ToArray();

            return solver;
        }

        public static void ApplyConstraints(Solver solver, IEnumerable<string> constraints)
        {
            foreach (string constraint in constraints)
            {
                string name = constraint.Trim();
                string options = "";
                int optionsIndex = 0;
                if ((optionsIndex = constraint.IndexOf(':')) > 0)
                {
                    name = constraint[0..optionsIndex].Trim();
                    options = constraint[(optionsIndex + 1)..].Trim();
                }
                ConstraintManager.AddConstraintByName(solver, name, options);
            }
        }

        public static string ToFPuzzlesURL(Solver solver, bool justBase64 = false)
        {
            bool[,] isGiven;
            if (solver.customInfo.TryGetValue("Givens", out object isGivenObj))
            {
                isGiven = (bool[,])isGivenObj;
            }
            else
            {
                isGiven = new bool[solver.HEIGHT, solver.WIDTH];
            }

            FPuzzlesGridEntry[][] grid = new FPuzzlesGridEntry[solver.HEIGHT][];
            for (int i = 0; i < solver.HEIGHT; i++)
            {
                grid[i] = new FPuzzlesGridEntry[solver.WIDTH];
                for (int j = 0; j < solver.WIDTH; j++)
                {
                    uint mask = solver.Board[i, j];
                    bool given = isGiven[i, j];
                    int value = IsValueSet(mask) ? GetValue(mask) : 0;
                    int[] centerPencilMarks = null;
                    if (value == 0)
                    {
                        centerPencilMarks = new int[ValueCount(mask)];
                        int markIndex = 0;
                        for (int v = 1; v <= solver.MAX_VALUE; v++)
                        {
                            if ((mask & ValueMask(v)) != 0)
                            {
                                centerPencilMarks[markIndex++] = v;
                            }
                        }
                    }
                    int region = solver.Regions[i, j];
                    grid[i][j] = new FPuzzlesGridEntry()
                    {
                        value = value,
                        given = given,
                        centerPencilMarks = centerPencilMarks,
                        givenPencilMarks = null,
                        region = region
                    };
                }
            }

            List<string> negative = new();
            if (solver.Constraints<RatioConstraint>().Any(c => c.negativeConstraint && c.negativeConstraintValues.Contains(2)))
            {
                negative.Add("ratio");
            }
            if (solver.Constraints<SumConstraint>().Any(c => c.negativeConstraint && c.negativeConstraintValues.Contains(5) && c.negativeConstraintValues.Contains(10)))
            {
                negative.Add("xv");
            }

            static string CN((int, int) cell) => CellName(cell).ToUpperInvariant();

            List<FPuzzlesArrowEntry> arrow = new();
            foreach (var c in solver.Constraints<ArrowSumConstraint>())
            {
                string startCell = null;
                int startCellDist = 0;
                var firstArrowCell = c.arrowCells.FirstOrDefault();
                foreach (var circleCell in c.circleCells)
                {
                    int curCellDist = TaxicabDistance(circleCell.Item1, circleCell.Item2, firstArrowCell.Item1, firstArrowCell.Item2);
                    if (startCell == null || curCellDist < startCellDist)
                    {
                        startCell = CN(circleCell);
                        startCellDist = curCellDist;
                    }
                }

                string[][] lines = new string[1][];
                lines[0] = new string[] { startCell }.Concat(c.arrowCells.Select(CN)).ToArray();
                string[] cells = c.circleCells.Select(CN).ToArray();
                arrow.Add(new() { lines = lines, cells = cells });
            }

            List<FPuzzlesKillerCageEntry> killercage = new();
            foreach (var c in solver.Constraints<KillerCageConstraint>())
            {
                string value = c.sum != 0 ? c.sum.ToString() : null;
                string[] cells = c.cells.Select(CN).ToArray();
                killercage.Add(new() { cells = cells, value = value });
            }

            List<FPuzzlesLittleKillerSumEntry> littlekillersum = new();
            foreach (var c in solver.Constraints<LittleKillerConstraint>())
            {
                string cell = CN(c.outerCell);
                string value = c.sum != 0 ? c.sum.ToString() : null;
                string direction = null;
                switch (c.direction)
                {
                    case LittleKillerConstraint.Direction.UpRight:
                        direction = "UR";
                        break;
                    case LittleKillerConstraint.Direction.UpLeft:
                        direction = "UL";
                        break;
                    case LittleKillerConstraint.Direction.DownRight:
                        direction = "DR";
                        break;
                    case LittleKillerConstraint.Direction.DownLeft:
                        direction = "DL";
                        break;
                }
                littlekillersum.Add(new() { cell = cell, direction = direction, value = value });
            }

            List<FPuzzlesCell> odd = new();
            foreach (var cell in solver.Constraints<OddConstraint>().SelectMany(c => c.cells))
            {
                odd.Add(new() { cell = CN(cell) });
            }

            List<FPuzzlesCell> even = new();
            foreach (var cell in solver.Constraints<EvenConstraint>().SelectMany(c => c.cells))
            {
                even.Add(new() { cell = CN(cell) });
            }

            List<FPuzzlesCell> minimum = new();
            foreach (var cell in solver.Constraints<MinimumConstraint>().SelectMany(c => c.cells))
            {
                minimum.Add(new() { cell = CN(cell) });
            }

            List<FPuzzlesCell> maximum = new();
            foreach (var cell in solver.Constraints<MaximumConstraint>().SelectMany(c => c.cells))
            {
                maximum.Add(new() { cell = CN(cell) });
            }

            List<FPuzzlesCells> rowindexer = new();
            foreach (var c in solver.Constraints<RowIndexerConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                rowindexer.Add(new() { cells = cells });
            }

            List<FPuzzlesCells> columnindexer = new();
            foreach (var c in solver.Constraints<ColIndexerConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                columnindexer.Add(new() { cells = cells });
            }

            List<FPuzzlesCells> boxindexer = new();
            foreach (var c in solver.Constraints<BoxIndexerConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                boxindexer.Add(new() { cells = cells });
            }

            List<FPuzzlesCells> extraregion = new();
            foreach (var c in solver.Constraints<ExtraRegionConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                extraregion.Add(new() { cells = cells });
            }

            List<FPuzzlesLines> thermometer = new();
            foreach (var c in solver.Constraints<ThermometerConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                thermometer.Add(new() { lines = new string[][] { cells } });
            }

            List<FPuzzlesLines> palindrome = new();
            foreach (var c in solver.Constraints<PalindromeConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                palindrome.Add(new() { lines = new string[][] { cells } });
            }

            List<FPuzzlesLines> renban = new();
            foreach (var c in solver.Constraints<RenbanConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                renban.Add(new() { lines = new string[][] { cells } });
            }

            List<FPuzzlesLines> whispers = new();
            foreach (var c in solver.Constraints<WhispersConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                whispers.Add(new() { lines = new string[][] { cells } });
            }

            List<FPuzzlesLines> regionSumLines = new();
            foreach (var c in solver.Constraints<RegionSumLinesConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                regionSumLines.Add(new() { lines = new string[][] { cells } });
            }

            List<FPuzzlesCells> difference = new();
            foreach (var marker in solver.Constraints<DifferenceConstraint>().SelectMany(c => c.markers))
            {
                var cell1 = (marker.Key.Item1, marker.Key.Item2);
                var cell2 = (marker.Key.Item3, marker.Key.Item4);
                string value = marker.Value != 1 ? marker.Value.ToString() : null;
                difference.Add(new() { cells = new string[] { CN(cell1), CN(cell2) }, value = value });
            }

            List<FPuzzlesCells> xv = new();
            foreach (var marker in solver.Constraints<SumConstraint>().SelectMany(c => c.markers))
            {
                var cell1 = (marker.Key.Item1, marker.Key.Item2);
                var cell2 = (marker.Key.Item3, marker.Key.Item4);
                string value = marker.Value.ToString();
                if (marker.Value == 10)
                {
                    value = "X";
                }
                else if (marker.Value == 5)
                {
                    value = "V";
                }
                xv.Add(new() { cells = new string[] { CN(cell1), CN(cell2) }, value = value });
            }

            List<FPuzzlesCells> ratio = new();
            foreach (var marker in solver.Constraints<RatioConstraint>().SelectMany(c => c.markers))
            {
                var cell1 = (marker.Key.Item1, marker.Key.Item2);
                var cell2 = (marker.Key.Item3, marker.Key.Item4);
                string value = marker.Value != 2 ? marker.Value.ToString() : null;
                ratio.Add(new() { cells = new string[] { CN(cell1), CN(cell2) }, value = value });
            }

            List<FPuzzlesClone> clone = new();
            foreach (var c in solver.Constraints<CloneConstraint>())
            {
                string[] cells = c.cellPairs.Select(pair => CN(pair.Item1)).ToArray();
                string[] cloneCells = c.cellPairs.Select(pair => CN(pair.Item2)).ToArray();
                clone.Add(new() { cells = cells, cloneCells = cloneCells });
            }

            List<FPuzzlesQuadruple> quadruple = new();
            foreach (var c in solver.Constraints<QuadrupleConstraint>())
            {
                string[] cells = c.cells.Select(CN).ToArray();
                int[] values = new int[ValueCount(c.requiredMask)];
                int valueIndex = 0;
                for (int v = 1; v <= solver.MAX_VALUE; v++)
                {
                    if ((c.requiredMask & ValueMask(v)) != 0)
                    {
                        values[valueIndex++] = v;
                    }
                }
                quadruple.Add(new() { cells = cells, values = values });
            }

            List<FPuzzlesLines> betweenline = new();
            foreach (var c in solver.Constraints<BetweenLineConstraint>())
            {
                string[][] lines = new string[1][];
                lines[0] = new string[c.innerCells.Count + 2];
                int cellIndex = 0;
                lines[0][cellIndex++] = CN(c.outerCell0);
                foreach (var cell in c.innerCells.Select(CN))
                {
                    lines[0][cellIndex++] = cell;
                }
                lines[0][cellIndex++] = CN(c.outerCell1);
                betweenline.Add(new() { lines = lines });
            }

            List<FPuzzlesCell> sandwichsum = new();
            foreach (var c in solver.Constraints<SandwichConstraint>())
            {
                sandwichsum.Add(new() { cell = CN(c.cellStart), value = c.sum.ToString() });
            }

            FPuzzlesBoard fp = new()
            {
                size = solver.WIDTH,
                title = solver.Title,
                author = solver.Author,
                ruleset = solver.Rules,
                grid = grid,
                diagonalp = solver.Constraints<DiagonalPositiveGroupConstraint>().Any(),
                diagonaln = solver.Constraints<DiagonalNegativeGroupConstraint>().Any(),
                antiknight = solver.Constraints<KnightConstraint>().Any(),
                antiking = solver.Constraints<KingConstraint>().Any(),
                disjointgroups = solver.Constraints<DisjointGroupConstraint>().Count() == solver.MAX_VALUE,
                nonconsecutive = solver.Constraints<DifferenceConstraint>().Any(c => c.negativeConstraint && c.negativeConstraintValues.Contains(1)),
                negative = negative.ToArray(),
                arrow = arrow.ToArray(),
                killercage = killercage.ToArray(),
                littlekillersum = littlekillersum.ToArray(),
                odd = odd.ToArray(),
                even = even.ToArray(),
                minimum = minimum.ToArray(),
                maximum = maximum.ToArray(),
                rowindexer = rowindexer.ToArray(),
                columnindexer = columnindexer.ToArray(),
                boxindexer = boxindexer.ToArray(),
                extraregion = extraregion.ToArray(),
                thermometer = thermometer.ToArray(),
                palindrome = palindrome.ToArray(),
                renban = renban.ToArray(),
                whispers = whispers.ToArray(),
                regionsumline = regionSumLines.ToArray(),
                difference = difference.ToArray(),
                xv = xv.ToArray(),
                ratio = ratio.ToArray(),
                clone = clone.ToArray(),
                quadruple = quadruple.ToArray(),
                betweenline = betweenline.ToArray(),
                sandwichsum = sandwichsum.ToArray()
            };

            string fpuzzlesJson = JsonSerializer.Serialize(fp, FpuzzlesJsonContext.Default.FPuzzlesBoard);
            string fpuzzlesBase64 = LZString.CompressToBase64(fpuzzlesJson);
            return justBase64 ? fpuzzlesBase64 : $"https://www.f-puzzles.com/?load={fpuzzlesBase64}";
        }
    }
}
