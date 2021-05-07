using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Options;
using SudokuSolver;
using System.Text;
using System.Diagnostics;

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
                @"-f=N4IgzglgXgpiBcBOANCALhNAbO8QHkAnAI0xFQEMBXNACwHtCEQBFegExgGNyRCqcYGGmYARCAHNMYAAQUs9AHYS5iuYUL0A7jIhq6MGRMIR2MgLZUwaGWCrmZaeo9qH2kzLrXSZXCIS4cADoZcSk0WXMKAE8ZQhgABxgKG3klFQo1Cg1tIIAdRQKwnzTlGXcKCSV5WT13LhSYM2JY7M0tWXoaSE4XQ2NTCysbOwcnPqMIADcYNUV7YhhCORs6FPV2kOKIixi4xOTUhTKKcohK6qx8wsUAYRgsLFktTFo5I3jYvwCcIesZRYyDjsEL3R7PV7vYwwWJgACOVGyhks/0BMBmiiCvAG7AQAG08aAEtlMNFmABRDEgAC+yGAtPpdIZzKZrKJJLQZLw+HYuOpAF1kISWYzRSLxYLhWzxdLpZKxbKFUqBULlYqZfT5RrtXLVTq1WKterjYbVSBiSZOcweXyTfrzRyuSBKbMafzBSAsJhsDAANYQR5LUb40BcB5YZgAJQAjAAGW4Adl4YfB+JAkcQtwAHLxI1nbogQB73PEuBglMwAKqR3hTeRUXAgHMMkApiN4SMAZluceT4bAacjACYC7no9mi6gS9xy4oqwAZWv1xtJlttqPxzt91PwPHp8dD3Mj6OTkDTssQCt4USL1B1rAN5gANhpdNb4ajCduse3T0H+ZPVAM1uQ9i38GdLzna8azvZdmELNcPw7eMk1QNsB13fdbhfICRwAVlzbsABZcyI24tyAvCQNzJ8e1Pc9ZzEW8QHvR88E7Zs33XDsv17ND+0HWjC0oicgLI1D027HD03wsdbhIsDS0YvBK2Y1jGyI1cuKQ/dN1/DC92AkigPzAigK/aTI1oiTIyonMxNHRSIKvEBqyXB8NMPRDHijbsf34ndDLIwD0yow8gNoij0y/Yz01M3NMxfJyLxc0QYJYuC8CIrcVRANptBDT09BgAzDO7GyR0s8czJk+Siw9dDB3KosWy9RQSvxMryNI6iRJCqzqPdAK/0wrtuv5VritK6LupMuqgMzGq82w+rhoMmatwmt82o63dDK/GzaPs0LRwcnMhvfQKZqTLbQB26bbMTHqkoagTRqom7Jvah7uyWsiFNeq6xoI26iu+zqTqigaAbWwcqM2r7doJGaltol7YdGr8QYFakgA",
                //"-b=9",
                //"-c=ratio:neg2",
                //"-c=difference:neg1",
                //"-c=taxi:4",
                "-st"
            };
#endif

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            string blankGridSizeString = null;
            List<string> constraints = new();
            bool multiThread = false;
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
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "t|multithread", "Use multithreading.", t => multiThread = t != null },
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
                if (!solver.FindSolution(multiThread: multiThread))
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
                if (!solver.FindSolution(multiThread: multiThread, isRandom: true))
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
                int currentLineCursor = Console.CursorTop;
                object consoleLock = new();
                if (!solver.FillRealCandidates(multiThread: multiThread, progressEvent: (uint[] board) =>
                {
                    uint[,] board2d = new uint[solver.HEIGHT, solver.WIDTH];
                    for (int i = 0; i < solver.HEIGHT; i++)
                    {
                        for (int j = 0; j < solver.WIDTH; j++)
                        {
                            int cellIndex = i * solver.WIDTH + j;
                            board2d[i, j] = board[cellIndex];
                        }
                    }
                    lock (consoleLock)
                    {
                        ConsoleUtility.PrintBoard(board2d, solver.Regions, Console.Out);
                        Console.SetCursorPosition(0, currentLineCursor);
                    }
                }))
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
                Console.WriteLine("Finding solution count...");
                ulong numSolutions = solver.CountSolutions(maxSolutions: 0, multiThread: multiThread, progressEvent: (ulong count) =>
                {
                    ReplaceLine($"(In progress) Found {count} solutions in {watch.Elapsed}.");
                });
                ReplaceLine($"\rFound {numSolutions} solutions.");
                Console.WriteLine();
            }

            if (check)
            {
                Console.WriteLine("Checking...");
                ulong numSolutions = solver.CountSolutions(2, multiThread);
                Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
            }

            watch.Stop();
            Console.WriteLine($"Took {watch.Elapsed}");
        }

        private static void ReplaceLine(string text) =>
            Console.Write("\r" + text + new string(' ', Console.WindowWidth - text.Length) + "\r");
    }
}
