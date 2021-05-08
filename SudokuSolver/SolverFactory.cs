using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LZStringCSharp;
using SudokuSolver.PuzzleFormats;
using SudokuSolver.Constraints;

namespace SudokuSolver
{
    public static class SolverFactory
    {
        public static Solver CreateBlank(int size, IEnumerable<string> constraints = null)
        {
            Solver solver = new(size, size, size);
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

        public static Solver CreateFromGivens(string givens, IEnumerable<string> constraints = null)
        {
            givens = givens.Trim();

            int size;
            if (givens.Length <= 81)
            {
                size = (int)Math.Sqrt(givens.Length);
                if (givens.Length != size * size)
                {
                    throw new ArgumentException($"ERROR: A givens string must be a perfect square in length (Provided length: {givens.Length}).");
                }
            }
            else
            {
                size = (int)Math.Sqrt(givens.Length / 2);
                if (givens.Length != size * size * 2)
                {
                    throw new ArgumentException($"ERROR: A givens string must be a perfect square in length (Provided length: {givens.Length}).");
                }
            }

            Solver solver = new(size, size, size);
            if (constraints != null)
            {
                ApplyConstraints(solver, constraints);
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }

            if (size <= 9)
            {
                for (int i = 0; i < givens.Length; i++)
                {
                    char c = givens[i];
                    if (c >= '1' && c <= '9')
                    {
                        if (!solver.SetValue(i / 9, i % 9, c - '0'))
                        {
                            throw new ArgumentException($"ERROR: Givens cause there to be no solutions.");
                        }
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
                    }
                }
            }
            return solver;
        }

        public static Solver CreateFromFPuzzles(string fpuzzlesURL, IEnumerable<string> additionalConstraints = null)
        {
            if (fpuzzlesURL.Contains("?load="))
            {
                int trimStart = fpuzzlesURL.IndexOf("?load=") + "?load=".Length;
                fpuzzlesURL = fpuzzlesURL[trimStart..];
            }

            string fpuzzlesJson = LZString.DecompressFromBase64(fpuzzlesURL);
            var fpuzzlesData = JsonSerializer.Deserialize<FPuzzlesBoard>(fpuzzlesJson);

            // Set the default regions
            int i, j;
            int height = fpuzzlesData.grid.Length;
            int width = fpuzzlesData.grid[0].Length;
            if (height != width)
            {
                throw new ArgumentException($"f-puzzles import is non-square {height}x{width}");
            }
            
            // Start with default regions
            int[,] regions = SolverUtility.DefaultRegions(height);

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

            Solver solver = new(width, width, width)
            {
                Title = fpuzzlesData.title,
                Author = fpuzzlesData.author,
                Rules = fpuzzlesData.ruleset
            };
            solver.SetRegions(regions);

            // Extra groups
            if (fpuzzlesData.diagonalp)
            {
                ConstraintManager.AddConstraint(solver, typeof(DiagonalPositiveGroupConstraint), string.Empty);
            }
            if (fpuzzlesData.diagonaln)
            {
                ConstraintManager.AddConstraint(solver, typeof(DiagonalNegativeGroupConstraint), string.Empty);
            }
            if (fpuzzlesData.antiknight)
            {
                ConstraintManager.AddConstraint(solver, typeof(KnightConstraint), string.Empty);
            }
            if (fpuzzlesData.antiking)
            {
                ConstraintManager.AddConstraint(solver, typeof(KingConstraint), string.Empty);
            }
            if (fpuzzlesData.disjointgroups)
            {
                ConstraintManager.AddConstraint(solver, typeof(DisjointConstraintGroup), string.Empty);
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
                        foreach (string cell in lines)
                        {
                            if (!arrow.cells.Contains(cell))
                            {
                                cells.Append(cell);
                            }
                        }
                        ConstraintManager.AddConstraint(solver, typeof(ArrowSumConstraint), cells.ToString());
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
                    ConstraintManager.AddConstraint(solver, typeof(KillerCageConstraint), cells.ToString());
                }
            }

            if (fpuzzlesData.littlekillersum != null)
            {
                foreach (var lksum in fpuzzlesData.littlekillersum)
                {
                    if (!string.IsNullOrWhiteSpace(lksum.value) && lksum.value != "0")
                    {
                        ConstraintManager.AddConstraint(solver, typeof(LittleKillerConstraint), $"{lksum.value};{lksum.cell};{lksum.direction}");
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
                ConstraintManager.AddConstraint(solver, typeof(OddConstraint), cells.ToString());
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
                ConstraintManager.AddConstraint(solver, typeof(EvenConstraint), cells.ToString());
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
                ConstraintManager.AddConstraint(solver, typeof(MinimumConstraint), cells.ToString());
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
                ConstraintManager.AddConstraint(solver, typeof(MaximumConstraint), cells.ToString());
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
                    ConstraintManager.AddConstraint(solver, typeof(ExtraRegionConstraint), cells.ToString());
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
                        ConstraintManager.AddConstraint(solver, typeof(ThermometerConstraint), cells.ToString());
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
                        ConstraintManager.AddConstraint(solver, typeof(PalindromeConstraint), cells.ToString());
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
                        ConstraintManager.AddConstraint(solver, typeof(BetweenLineConstraint), cells.ToString());
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
                    ConstraintManager.AddConstraint(solver, typeof(RatioConstraint), ratioParams.ToString());
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
                    ConstraintManager.AddConstraint(solver, typeof(DifferenceConstraint), differenceParams.ToString());
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
                    ConstraintManager.AddConstraint(solver, typeof(SumConstraint), sumParams.ToString());
                }
            }

            if (fpuzzlesData.clone != null && fpuzzlesData.clone.Length > 0)
            {
                StringBuilder cloneParams = new();
                foreach (var clone in fpuzzlesData.clone)
                {
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
                }
                if (cloneParams.Length > 0)
                {
                    ConstraintManager.AddConstraint(solver, typeof(CloneConstraint), cloneParams.ToString());
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
                        ConstraintManager.AddConstraint(solver, typeof(QuadrupleConstraint), quadParams.ToString());
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

                    ConstraintManager.AddConstraint(solver, typeof(SandwichConstraint), $"{sandwich.value}{sandwich.cell}");
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

            i = 0;
            foreach (var row in fpuzzlesData.grid)
            {
                j = 0;
                foreach (var val in row)
                {
                    if (val.centerPencilMarks != null && val.centerPencilMarks.Length > 0)
                    {
                        uint marksMask = 0;
                        foreach (int v in val.centerPencilMarks)
                        {
                            marksMask |= SolverUtility.ValueMask(v);
                        }
                        solver.ClearMask(i, j, (~marksMask) & solver.ALL_VALUES_MASK);
                    }
                    else if (val.value > 0)
                    {
                        solver.SetValue(i, j, val.value);
                    }
                    j++;
                }
                i++;
            }
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
    }
}
