using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Options;
using SudokuSolver;
using System.Text;
using System.Diagnostics;
using LZStringCSharp;

namespace SudokuSolverConsole
{
    class Program
    {
        static void Main(string[] args)
        {
#if false
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QFkIA7CfAQwA8ACAZTKIBMB3CAYwAsRUyBXNdgPYAnBCAAyEMFDQwwXEEJ44wMNKIBywgLZkstHgwEBrHlUXKqZAA5WsATwB0VAKJkOZgU2RVWArDy0ib3oGKgBmCjCqACMBal8iNDJiMCoYN3YqIgDomCEqADMhAS0qAEYqNAEqRDSKN2w7KgEiVhgHAB0iLoARSQArAWI0fUMTMyVZSxt7JwBhGCwsVP4yEesrdPziSvYYKjAyLX2rAUgMFrNFtYgAN32q3ZgIfIYYAt4sEaEYAHMIFqpLQ8MAjIgCEYJJI7fj7Q7HLI5PKdbpEOiMFjuGgGYymcxTDazKgAFT2iK0uSEqQEfEgbyeVF+QggoTAgiYDLAAWaBQZDAg/zQqUOGLYe1CuTQTBgMCI5UsjBqVBh7DWHg5wh8fgCRBRXQA4j8mm0lqkWPxLEJikxUh9WMRfs0+EwyEJQsDQVrEsk5dkKXlUkz0jJ8qs5bo9MJ+AJfi0I00yAx+m5ZZDFss9URDTBjemzZhMq7rba3A7lUQXW6qB7IS1ob6kVSqMoVqrw0tmkJo7GiPHLEmU4kfHmUfImSyEABtSfAAC+yDnC/ni5Xy7XS4AusgZ+vV0v93u51udwfd2fT8fD+er5vtzf72fL9fnxe7y+H7eTx+H0/T3+f1uID8mAgzDEyNJWHI8BoIoMCoCKzBilyWhTqAJpYKIABKAAMcwABzyLcug8LgIBlARy4gOhWG4QA7IRxGkQATPRlHUXgmEAKxzNhDH+MxvFsemWFMTxfEkaITEAGwgEJSw0XMnHiaRFGAVoxAQMCKHwDOVHCRxolMbJC56fJBn4cZaH6SAmF4XMRlyRhHF2RRJnsTZ3FKbOamUJpASoaZTkefZlmBVhdleW51lcRZjkiYpoXuTFiCyRus5AA==",
                "-pl"
            };
#endif

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

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
                List<string> constraintNames = ConstraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
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

            Solver solver;
            try
            {
                if (!string.IsNullOrWhiteSpace(givens))
                {
                    solver = SolverFactory.CreateFromGivens(givens, constraints);
                }
                else
                {
                    solver = SolverFactory.CreateFromFPuzzles(fpuzzlesURL, constraints);
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
                if (!solver.FillRealCandidates())
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
    }
}
