using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mono.Options;
using SudokuSolver;
using LZStringCSharp;
using System.IO;
using System.Text;

namespace SudokuSolverConsole
{
    class Program
    {
        static readonly ConstraintManager constraintManager = new();

        static void Main(string[] args)
        {
            //string json = LZString.DecompressFromBase64("N4IgzglgXgpiBcBOANCALhNAbO8QFkAnAOgAIANEVAQwFc0ALAe0IRAAVq1CIBjAayohCtHGBho2AORYBballIBlWgBMm/WqRFjS1AA76sATzJTasgEYxCYUrNpg0pAHZNnhGPphdSEF3qk/BBYOISkvNQA5jBkACoMMK4W1rZ+AYxJkTH2js5gFqRoTEWJpFEQAG4wGe4K6aVJxfqkOABmaGQAmkxaDNTVRSVt/qoUpCO2zsXlEo1DaApgxEJRPKoIANqbwAC+yHsH+4cnx2dHALrIO+enR/d3e1c3D7dvr8+P71+X1z//b0+32BHz+IIBvxeEIBQNecJhVxAwVCNmyuB2IF4MFCYC2IAASgBGADChKEROJACZySSAMzkymkhlU8m0pkXY6Y7FYXHwTYEtnUjkHLk4vH4xn04WgLFivkEkkAFhpxIArCqAGwgaWinnikkAdmZRtQEuJAA5WRarYhtZzZXr5RTLaaSbbTYzbTqHbz+fi2VrTYriYGCcGjd7ub6CYz1aa2erI3K/WzlUHifT08qk46/cG4wTVWryRrizno/jS6HK8SI/ao+KiybC9by43iV768mCaWXT2O+SDdbTUPOyKfeLELXyVO+/ip2OZQ2nUOC/jzWWu7mCVOyaap9T9xm7ePl37S3uCUPL/ih0KtxXS/fT92a5n+9mH+2b0XDy2pbsiJolsS6voK2qoJUCi0LgIDkAA9H+vTYP4MDEmwADEAAMOG4UIbRMC4aDoXg2G4ThIBfk6koQSAUFYDBbCUKgyFYKhJEgGR5H4YRxGYeRFFUX6sZWomkHQbBzEgKx7H8QJPFERxXF4UJArEmmYbHlmtH0YxeBSTJLhoXJ3GoARikmSpL7bhSGkUmuJJaoiumwZSBn0GxRlKQJWEKXxpE+ZR1kViSN4kn+FLvmaN6MhFbJks5ElsIq7koV5lkUWZvHefJqkUs2ZoFYyc5siVA6JQxsEpUIhnGQF8lZRZ9XcXlJJzm6zJeuJlVMTVHmyc1eGNf5nGBXlAbksG1bhjpSV4G5fVpXVo0NSA5kjcpgnBe2BVFpaFV6SAC0sf16WDZla3ZRlvl5UWXV0XNcGIYtnnLZtvnDTlLXbU6vYlgOI7Dle5XdYdx3Sadb2BZ911BaBNlVv9Eag5JL0DStpmXU1GNWfDj5aR+s09fpaNnTjF3rV9uO6vjQoo0xADUl61VTFNXedN0/eeTKAzed5E2D5AM0hkOsx9WMbWNXMtj+LKmkWUr0yTJ1LWLflq3l+bkkWa6lmJD3E0dqWvWrMMc3DNPiqu5IbvrLm9SrJuw2b5Oc3jk48zucte4rBuHcb6PversN5VOBWzjOIN+7BtIB2TQcu0HgG7EAA");

            //args[1] = @"N4IgzglgXgpiBcBOANCA5gJwgEwQbT1ADcBDAGwFc54BmVNCImAOwQBcMqBfZYHv3vyGCRfALrJCIUpWoAmeoxbtOMYQI3qtEqTKoIAjIqat4HbqK2XLOzdbsOutq4/tPJrz9o8vfo6eT68ABsxspmqu6Efl68AbIIAKxhpuZqzm6ZGvFBACwpKtxiEiAADuQQzNgYAPYAttRSZJUwYPh4IABKuQDCBiConYk9cgNdwT00Y50A7D250xOJ08PB070z0zQ9ABzTcj2IIMVOXEA===";

            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

            bool showHelp = false;
            string fpuzzlesURL = null;
            string givens = null;
            int maxThreads = 0;
            List<string> constraints = new();
            bool solveLogically = false;
            bool solutionCount = false;
            bool check = false;
            bool trueCandidates = false;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "t|threads=", "The maximum number of threads to use when brute forcing.", (int t) => maxThreads = t },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "h|help", "Show this message and exit", h => showHelp = h != null },
            };

            List<string> extra;
            try
            {
                // parse the command line
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                // output some error message
                Console.WriteLine($"{processName}: {e.Message}");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                return;
            }

            if (showHelp)
            {
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.WriteLine();
                Console.WriteLine("Constraints:");
                List<string> constraintNames = constraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
                constraintNames.Sort();
                foreach (var constraintName in constraintNames)
                {
                    Console.WriteLine($"\t{constraintName}");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(fpuzzlesURL) && string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Must provide either an f-puzzles URL or a givens string.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(fpuzzlesURL) && !string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Cannot provide both an f-puzzles URL and a givens string.");
                return;
            }

            Solver solver = new();
            if (!string.IsNullOrWhiteSpace(givens))
            {
                givens = givens.Trim();

                if (givens.Length != 81)
                {
                    Console.WriteLine($"ERROR: A givens string must be exactly 81 characters long (Provided length: {givens.Length}).");
                    return;
                }

                ApplyConstraints(solver, constraints);

                for (int i = 0; i < givens.Length; i++)
                {
                    char c = givens[i];
                    if (c >= '1' && c <= '9')
                    {
                        if (!solver.SetValue(i / 9, i % 9, c - '1'))
                        {
                            solver.Print();
                            Console.WriteLine($"ERROR: Givens cause there to be no solutions.");
                            return;
                        }
                    }
                }
            }
            else
            {
                if (fpuzzlesURL.Contains("?load="))
                {
                    int trimStart = fpuzzlesURL.IndexOf("?load=") + "?load=".Length;
                    fpuzzlesURL = fpuzzlesURL[trimStart..];
                }

                string fpuzzlesJson = LZString.DecompressFromBase64(fpuzzlesURL);
                var fpuzzlesData = JsonSerializer.Deserialize<FPuzzlesBoard>(fpuzzlesJson);

                Console.WriteLine($"Imported \"{fpuzzlesData.title}\" by {fpuzzlesData.author}");

                if (fpuzzlesData.diagonalp)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "diagonal+", string.Empty);
                }
                if (fpuzzlesData.diagonaln)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "diagonal-", string.Empty);
                }
                if (fpuzzlesData.antiknight)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "antiknight", string.Empty);
                }
                if (fpuzzlesData.antiking)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "antiking", string.Empty);
                }
                if (fpuzzlesData.disjointgroups)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "disjointgroups", string.Empty);
                }
                if (fpuzzlesData.nonconsecutive)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "nonconsecutive", string.Empty);
                }

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
                            constraintManager.AddConstraintByFPuzzlesName(solver, "arrow", cells.ToString());
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
                        constraintManager.AddConstraintByFPuzzlesName(solver, "killercage", cells.ToString());
                    }
                }

                if (fpuzzlesData.littlekillersum != null)
                {
                    foreach (var lksum in fpuzzlesData.littlekillersum)
                    {
                        if (!string.IsNullOrWhiteSpace(lksum.value) && lksum.value != "0")
                        {
                            constraintManager.AddConstraintByFPuzzlesName(solver, "littlekillersum", $"{lksum.value};{lksum.cell};{lksum.direction}");
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
                    constraintManager.AddConstraintByFPuzzlesName(solver, "odd", cells.ToString());
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
                    constraintManager.AddConstraintByFPuzzlesName(solver, "even", cells.ToString());
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
                        constraintManager.AddConstraintByFPuzzlesName(solver, "extraregion", cells.ToString());
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
                            constraintManager.AddConstraintByFPuzzlesName(solver, "thermometer", cells.ToString());
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
                            constraintManager.AddConstraintByFPuzzlesName(solver, "palindrome", cells.ToString());
                        }
                    }
                }

                // Apply any command-line constraints
                ApplyConstraints(solver, constraints);

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
            }

            if (!solver.FinalizeConstraints())
            {
                solver.Print();
                Console.WriteLine($"ERROR: The constraints are invalid (no solutions).");
                return;
            }

            solver.Print();

            if (!solver.ConsolidateBoard())
            {
                Console.WriteLine($"Board is invalid!");
            }
            solver.Print();
        }

        static void ApplyConstraints(Solver solver, List<string> constraints)
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
                constraintManager.AddConstraintByName(solver, name, options);
            }
        }
    }
}
