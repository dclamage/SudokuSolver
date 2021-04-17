using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LZStringCSharp;
using SudokuSolver.PuzzleFormats;

namespace SudokuSolver
{
    public static class SolverFactory
    {
        public static Solver CreateFromGivens(string givens, IEnumerable<string> constraints = null)
        {
            Solver solver = new();
            givens = givens.Trim();

            if (givens.Length != 81)
            {
                throw new ArgumentException($"ERROR: A givens string must be exactly 81 characters long (Provided length: {givens.Length}).");
            }

            if (constraints != null)
            {
                ApplyConstraints(solver, constraints);
            }

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

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
            }
            return solver;
        }

        public static Solver CreateFromFPuzzles(string fpuzzlesURL, IEnumerable<string> additionalConstraints = null)
        {
            Solver solver = new();

            if (fpuzzlesURL.Contains("?load="))
            {
                int trimStart = fpuzzlesURL.IndexOf("?load=") + "?load=".Length;
                fpuzzlesURL = fpuzzlesURL[trimStart..];
            }

            string fpuzzlesJson = LZString.DecompressFromBase64(fpuzzlesURL);
            var fpuzzlesData = JsonSerializer.Deserialize<FPuzzlesBoard>(fpuzzlesJson);

            Console.WriteLine($"Imported \"{fpuzzlesData.title}\" by {fpuzzlesData.author}");

            // Extra groups
            if (fpuzzlesData.diagonalp)
            {
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "diagonal+", string.Empty);
            }
            if (fpuzzlesData.diagonaln)
            {
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "diagonal-", string.Empty);
            }
            if (fpuzzlesData.antiknight)
            {
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "antiknight", string.Empty);
            }
            if (fpuzzlesData.antiking)
            {
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "antiking", string.Empty);
            }
            if (fpuzzlesData.disjointgroups)
            {
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "disjointgroups", string.Empty);
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
                        ConstraintManager.AddConstraintByFPuzzlesName(solver, "arrow", cells.ToString());
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
                    ConstraintManager.AddConstraintByFPuzzlesName(solver, "killercage", cells.ToString());
                }
            }

            if (fpuzzlesData.littlekillersum != null)
            {
                foreach (var lksum in fpuzzlesData.littlekillersum)
                {
                    if (!string.IsNullOrWhiteSpace(lksum.value) && lksum.value != "0")
                    {
                        ConstraintManager.AddConstraintByFPuzzlesName(solver, "littlekillersum", $"{lksum.value};{lksum.cell};{lksum.direction}");
                    }
                }
            }

            if (fpuzzlesData.odd != null)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.odd)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "odd", cells.ToString());
            }

            if (fpuzzlesData.even != null)
            {
                StringBuilder cells = new();
                foreach (var cell in fpuzzlesData.even)
                {
                    if (!string.IsNullOrWhiteSpace(cell.cell))
                    {
                        cells.Append(cell.cell);
                    }
                }
                ConstraintManager.AddConstraintByFPuzzlesName(solver, "even", cells.ToString());
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
                    ConstraintManager.AddConstraintByFPuzzlesName(solver, "extraregion", cells.ToString());
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
                        ConstraintManager.AddConstraintByFPuzzlesName(solver, "thermometer", cells.ToString());
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
                        ConstraintManager.AddConstraintByFPuzzlesName(solver, "palindrome", cells.ToString());
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
                            ratioParams.Append($"{cell}");
                        }
                    }
                }

                if (ratioParams.Length > 0)
                {
                    ConstraintManager.AddConstraintByFPuzzlesName(solver, "ratio", ratioParams.ToString());
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
                            differenceParams.Append($"{cell}");
                        }
                    }
                }

                if (differenceParams.Length > 0)
                {
                    ConstraintManager.AddConstraintByFPuzzlesName(solver, "difference", differenceParams.ToString());
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
                    ConstraintManager.AddConstraintByFPuzzlesName(solver, "clone", cloneParams.ToString());
                }
            }

            // Apply any command-line constraints
            if (additionalConstraints != null)
            {
                ApplyConstraints(solver, additionalConstraints);
            }

            int i = 0;
            foreach (var row in fpuzzlesData.grid)
            {
                int j = 0;
                foreach (var val in row)
                {
                    if (val.value > 0)
                    {
                        solver.SetValue(i, j, val.value);
                    }
                    j++;
                }
                i++;
            }

            if (!solver.FinalizeConstraints())
            {
                throw new ArgumentException("ERROR: The constraints are invalid (no solutions).");
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
