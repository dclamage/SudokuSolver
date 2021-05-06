using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Options;
using SudokuSolver;
using System.Text;
using System.Diagnostics;
using LZStringCSharp;
using System.Threading;

namespace SudokuSolverConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            // Useful for quickly testing a puzzle without changing commandline parameters
#if false
            args = new string[]
            {
                //"-f",
                //@"N4IgzglgXgpiBcB2ANCALhNAbO8QEkAnQmAcwFcsBDQgAkQA9FaBpQgewAcBrCEVKuTQALdoQTowAY34hClGGBhoJbLr1rycYWlU6csAT3i0AOgDsAtLQDCMLFh1LONKmhgATWgCNDu2gDuwpgwtB7saLRS7OZoVBDmUTFKUkIQAG6hHhCkmGAAdLTWdg5OMC6Ebp4+flQ+1FLcYRFJsfGJ2bloOgGYwv4AjPAATJpuEOyFFtYA6n3sQrTmZOOZrWBolQloyLRg7EstVA60MAwQGwmkzd26JLS5meb5FhaypIQQHggA2j/AAF9kIDgUCQeCwQCALrIf6Q0EIiGgmFwxHwpGAlEYsFyMgTcwIAAsOJIuRiCAArCS8eT4FTgbiyQS6dDYaBSfiEABmalM7no9GMzkshkc2lUrFC2k80U05kANl5wuJsr58BV7LlRKVtMVyLZUuZMuxaNVwolMJA2QAZtaYCRzFJcP8QE7Sr8QAAlYY2Qmyb02CkgKE4t2OD0B+X+n1BkMMsNgCMDGxR1Ce5OIYOh+zh+A/L0Umxc/3yotZ+M5xN5r1cmwDf2Euvl0AJiOIMtp9vDZuuysRwt+tOlv1xlt96uewtBoeBrOWyoYdi/MfuifJ4tp5Mj7Or/PplP+5Oxne5vc+jden3biu7r3J7tpn3d0e92+e4cl2cv1sTxsXz2Ns+J5VnuhaZmmjaZiGIZAA",
                "-b=9",
                //"-c=ratio:neg2",
                //"-c=difference:neg1",
                "-c=taxi:4",
                "-c=difference:neg1",
                "-t=0",
                "-pn"
            };
#endif

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            string blankGridSizeString = null;
            int maxThreads = 1;
            List<string> constraints = new();
            bool solveBruteForce = false;
            bool solveRandomBruteForce = false;
            bool solveLogically = false;
            bool solutionCount = false;
            bool check = false;
            bool trueCandidates = false;
            bool print = false;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "b|blank=", "Use a blank grid of a square size.", b => blankGridSizeString = b },
                { "t|threads=", "The maximum number of threads to use when brute forcing. Use 0 or fewer for maximum threads.", (int t) => maxThreads = t <= 0 ? t = -1 : t },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "s|solve", "Provide a single brute force solution.", s => solveBruteForce = s != null },
                { "d|random", "Provide a single random brute force solution.", d => solveRandomBruteForce = d != null },
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
                List<string> constraintNames = ConstraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
                constraintNames.Sort();
                foreach (var constraintName in constraintNames)
                {
                    Console.WriteLine($"\t{constraintName}");
                }
                return;
            }

            bool haveFPuzzlesURL = !string.IsNullOrWhiteSpace(fpuzzlesURL);
            bool haveGivens = !string.IsNullOrWhiteSpace(givens);
            bool haveBlankGridSize = !string.IsNullOrWhiteSpace(blankGridSizeString);
            if (!haveFPuzzlesURL && !haveGivens && !haveBlankGridSize)
            {
                Console.WriteLine($"ERROR: Must provide either an f-puzzles URL or a givens string or a blank grid.");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                showHelp = true;
            }

            int numBoardsSpecified = 0;
            if (haveFPuzzlesURL)
            {
                numBoardsSpecified++;
            }
            if (haveGivens)
            {
                numBoardsSpecified++;
            }
            if (haveBlankGridSize)
            {
                numBoardsSpecified++;
            }
            if (numBoardsSpecified != 1)
            {
                Console.WriteLine($"ERROR: Cannot provide more than one set of givens (f-puzzles URL, given string, blank grid).");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                return;
            }

            Solver solver;
            try
            {
                if (haveBlankGridSize)
                {
                    if (int.TryParse(blankGridSizeString, out int blankGridSize) && blankGridSize > 0 && blankGridSize < 32)
                    {
                        solver = SolverFactory.CreateBlank(blankGridSize, constraints);
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Blank grid size must be between 1 and 31");
                        Console.WriteLine($"Try '{processName} --help' for more information.");
                        return;
                    }
                }
                else if (haveGivens)
                {
                    solver = SolverFactory.CreateFromGivens(givens, constraints);
                }
                else
                {
                    solver = SolverFactory.CreateFromFPuzzles(fpuzzlesURL, constraints);
                    Console.WriteLine($"Imported \"{solver.Title ?? "Untitled"}\" by {solver.Author ?? "Unknown"}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
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
                var logicResult = solver.ConsolidateBoard(stepsDescription);
                Console.WriteLine(stepsDescription);
                if (logicResult == LogicResult.Invalid)
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

            if (solveRandomBruteForce)
            {
                Console.WriteLine("Finding a random solution with brute force:");
                if (!solver.FindRandomSolution())
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
                if (!solver.FillRealCandidates(maxThreads))
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();
                }
            }

            if (solutionCount)
            {
                ulong numSolutions = solver.CountSolutions(0, maxThreads);
                Console.WriteLine($"Found {numSolutions} solutions.");
            }

            if (check)
            {
                ulong numSolutions = solver.CountSolutions(2, maxThreads);
                Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
            }

            watch.Stop();
            Console.WriteLine($"Took {watch.ElapsedMilliseconds}ms");
        }
    }
}
