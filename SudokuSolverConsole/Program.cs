using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mono.Options;
using SudokuSolver;
using LZStringCSharp;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SudokuSolverConsole
{
    class Program
    {
        static readonly ConstraintManager constraintManager = new();

        static void Main(string[] args)
        {
            //string json = LZString.DecompressFromBase64("N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaCR3PgIm9hk0UM6zR4rssX0QAYwA2AewB2ceIQ0xNmsPhAAlACwBhAEwhUlgKwOnVtwGYQarXphbEzMLS3tbFw9LLwiomIA2XwpjU3NDKwAOdz8dfSDU0KzE2mT1YLS8KwB2W0QorLrnRFrfVH888tCARlsuqPC+5xi+kpKgA==");

#if false
            // Thermo + arrow puzzle
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QGEsIAHYmAExFQEMBXNACwHsAnBEABQYiNIAIAQlloBbGH2oBranwDmwkQH1upJlRAtaOMDDTsAyrXJNJtPgFo+MAG4wWATz4smAd2R8AxkwUA7d9R9yPgBmAA9gvgAjJlC+EVowNE8mHzRqCB8JLCw+RnFyCFlMMD4AM2cRPgBGc0QAOj59JjE+AqK0EuoWcWosbupyRyLbHzqAHR8JgEFUiHMAaQzZCysAR1pe1sLiuOpHHyYktCZaDwYt6lkU3qx7ccmfABUGOxFm3TsVtp34xL5ElgQDzYRwZDz9HRlCpRLSRXJMXIke7TFjOFwrPKeCAsDw4ILfJJeHyJTD0GAlTFgUR8JilXIvLbtTpYFLLTFdNH3dSyQGUeAAbX5wAAvsgRWLReKpZKZRKALrIIWy6US1UqkUKpVq5Ugay9Wi4ABsqGGMB8CDQmhgyptmvVNu12rtOr1wlwAFYTRARharS79bgql6ffBLQb/W6EAAWYNm33hp2K+2O5MapMOqW6gMIYKx82hv0pmXOoul6Ul1MZ4sKkABDCSJbxmCoPIsN5iNB2BBCkBEHzk7v8kAAJUN+Hd6mHAHZ8FHJwAOfDBBf4ABMIDlcslvYyA4FQ+H7rXk6jS8nwVnk9Xl83277e8FI9PE9Qw4vhqv+Cnn/nG63Yp3fswEHEcj1/V8x2/V8Zw/V9Fw/W8aw5Vxu1Ae9gP3UDL1fU9l1fC911fa9103VAPBgbIMIPI853/NDdyo6j8FgkdIMnGdwJHRdf1IkByMo7ssIQu8GJA4dFxfEcZznCCz1kkia34rAqK48cNxEoCxOvSS32Yk8v30njFIo5TBOHbT1IA9CxJnPDVJkkdEDU3ilJU6czy3LcgA=",
                "-pr"
            };
#endif

#if false
            // Killer blister (Killer + LK)
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QGkIscAnAAgCEsIw0YSRUBDAVzQAsB7BvAJSYB2AczABrRiBIscYGGgQgActwC2TLGQDKLACadRLMlJlkmABzNYAngDoAOgIea0gnUxI6yhYvXhkAKuwwZDoQQphgZBACpmQAxkxCwWAsKmRonOlB8VgswdFZwRlmZDgAZmhknGWF8YkwNmQAImERdQICnJUkMGYwTJUFTHVJ9o4Czq7ungAymNjB3qR+AMK5MJGcbJA6RdlCJBCe0aEJdLUpadW1oeFokeqcwrVmnNF0nqGJT+o2EgdHBAAbSBwAAvshwZCIVDYTD4dCALrIUEIuHQjHo8HI1GYtH4vE4rEE4lIlGkin4okkmmE8m0ylk3GMynUvHs1nIkCiIikBJJYGgOIwYhgYEgXgAZhWAEYJLwACyykBcgBu6jyCgA7CAYSBhaLxYqVgAmeVKyUq1Dq9YKS16g1YMXwIESgCsKwV8o9bu9KwAbFaQDbNXgdQ6RU6jf6VjrULwYwAOeUxxDyrUrNNqjW4EAygAMush+sjztdvBNK0t8elZprVaDIdzMuTEcNLolMbl8Yz3YlieV2dteBNhbbUY7vAzcYlGeT8YHM94A+TQ9DebN47LEplK198cr+4l0qPxt9a+bga30b3KYD6dvPfvC8f/fvF7treLju3y9N8oHas3y9D88BlcNv1LI1d0DeNdyXXd5wlSsdVAkATSvSD23LaVYOPWN5WlJCpUzRscwUGVNy5ag0AWHkfBIS5BRLYgFGNQtUB/I1pT7Ct/zghsuVCHo4gwJ4FAAVV4CQmwUUciyFSM2IHAsJC4ycMzTeMkz9JclTwkjT0rL0BMtISIBEsSBEkmYZPIvBJQgxTWL4AtX3U8tEE9ACGyfOtO0HVBhJgUSIHEvAJNs617I3BSWKwNi3I4+Lfy83iB38qdfICkz3VfY0DOlJdK2I3csyCiyQqsyTpOi4cQDdL0wURMEgA",
                "-s"
            };
#endif

            //args[1] = @"N4IgzglgXgpiBcBOANCALhNAbO8QGEsIAHYmAExFQEMBXNACwHsAnBEABQYiNIAIAQlloBbGH2oBranwDmwkQH1upJlRAtaOMDDTsAyrXJNJtPgFo+MAG4wWATz4smAd2R8AxkwUA7d9R9yPgBmAA9gvgAjJlC+EVowNE8mHzRqCB8JLCw+RnFyCFlMMD4AM2cRPgBGc0QAOj59JjE+AqK0EuoWcWosbupyRyLbHzqAHR8JgEFUiHMAaQzZCysAR1pe1sLiuOpHHyYktCZaDwYt6lkU3qx7ccmfABUGOxFm3TsVtp34xL5ElgQDzYRwZDz9HRlCpRLSRXJMXIke7TFjOFwrPKeCAsDw4ILfJJeHyJTD0GAlTFgUR8JilXIvLbtTpYFLLTFdNH3dSyQGUeAAbX5wAAvsgRWLReKpZKZRKALrIIWy6US1UqkUKpVq5Ugay9Wi4ABsqGGMB8CDQmhgyptmvVNu12rtOr1wlwAFYTRARharS79bgql6ffBLQb/W6EAAWYNm33hp2K+2O5MapMOqW6gMIYKx82hv0pmXOoul6Ul1MZ4sKkABDCSJbxmCoPIsN5iNB2BBCkBEHzk7v8kAAJUN+Hd6mHAHZ8FHJwAOfDBBf4ABMIDlcslvYyA4FQ+H7rXk6jS8nwVnk9Xl83277e8FI9PE9Qw4vhqv+Cnn/nG63Yp3fswEHEcj1/V8x2/V8Zw/V9Fw/W8aw5Vxu1Ae9gP3UDL1fU9l1fC911fa9103VAPBgbIMIPI853/NDdyo6j8FgkdIMnGdwJHRdf1IkByMo7ssIQu8GJA4dFxfEcZznCCz1kkia34rAqK48cNxEoCxOvSS32Yk8v30njFIo5TBOHbT1IA9CxJnPDVJkkdEDU3ilJU6czy3LcgA=";

            Stopwatch watch = Stopwatch.StartNew();
            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            int maxThreads = 0;
            List<string> constraints = new();
            bool solveBruteForce = false;
            bool solveLogically = false;
            bool solutionCount = false;
            bool check = false;
            bool trueCandidates = false;
            bool print = false;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "t|threads=", "The maximum number of threads to use when brute forcing.", (int t) => maxThreads = t },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "s|solve", "Provide a single brute force solution.", s => solveBruteForce = s != null },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "p|print", "Print the input board.", p => print = p != null },
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
                Console.WriteLine($"Try '{processName} --help' for more information.");
                showHelp = true;
            }

            if (!string.IsNullOrWhiteSpace(fpuzzlesURL) && !string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Cannot provide both an f-puzzles URL and a givens string.");
                Console.WriteLine($"Try '{processName} --help' for more information.");
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
                        if (!solver.SetValue(i / 9, i % 9, c - '0'))
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

                // Extra groups
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

                bool negativeRatio = fpuzzlesData.negative?.Contains("ratio") ?? false;
                if (fpuzzlesData.ratio != null && fpuzzlesData.ratio.Length > 0 || negativeRatio)
                {
                    StringBuilder ratioParams = new();
                    if (negativeRatio)
                    {
                        HashSet<string> ratioValues = fpuzzlesData.ratio != null ? new(fpuzzlesData.ratio.Select(r => r.value)) : new() { "2" };
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
                        constraintManager.AddConstraintByFPuzzlesName(solver, "ratio", ratioParams.ToString());
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
                        constraintManager.AddConstraintByFPuzzlesName(solver, "difference", differenceParams.ToString());
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

            if (print)
            {
                Console.WriteLine("Input puzzle:");
                solver.Print();
            }

            if (solveLogically)
            {
                Console.WriteLine("Solving logically:");
                StringBuilder stepsDescription = new();
                bool valid = solver.ConsolidateBoard(stepsDescription);
                Console.WriteLine(stepsDescription);
                if (!valid)
                {
                    Console.WriteLine($"Board is invalid!");
                }
                solver.Print();
            }

            if (solveBruteForce)
            {
                Console.WriteLine("Finding a solution with brute force:");
                if (!solver.FindSolution())
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();
                }
            }

            if (trueCandidates)
            {
                Console.WriteLine("Finding true candidates:");
                solver.FillRealCandidates();
                solver.Print();
            }

            if (solutionCount)
            {
                ulong numSolutions = solver.CountSolutions();
                Console.WriteLine($"Found {numSolutions} solutions.");
            }

            if (check)
            {
                ulong numSolutions = solver.CountSolutions(2);
                Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
            }
            watch.Stop();
            Console.WriteLine($"Took {watch.ElapsedMilliseconds}ms");
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
