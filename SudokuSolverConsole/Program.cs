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
                //@"N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+wkDGNCAewB2CAMxMKXHvyHwADGNITeghAEYxHNoqkIALPNDcl0gKz6ty+ACYiG/XdY2Gbe89sP3zuk5c+3n/9S0tERAA==",
                //@"N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSTQMY0IB7AOwQAYKQqaHnX27H4XSbary6Cenft2ESWAXWSEhHPgEYpyhGrHTVFPeW0b4Wyjs3qRxovMXi+AJgsTHhyy9NH3St04f6yxDYB/t4SAMy+CBGu4ZHw0R6WCaF8EUEpCAAscdkxfLnBBokSBRnwpXZZ1grFfACscQ15CE21LY0dRXpB/nEAbP2DzfADw6NtI9W2ZvAA7HHzw4uFAWXLawtTEwAccbvD+zt7x13ksrJEQA",
                //@"N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaCR3PgIm9hk0UM6zR4rssW0iQA=",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QBUALGAAnwFcA7SmLMEVAQ3LUIHsAnBEAJUcoAmbALYMQHcjjAw03AJIcOMAOaTGHEmHJCA1uRISpJRgAcTWAJ4A6ADqUAcuWEAjGBzAlh5MGhKU2vkomMIy+AO6YhBCUxiQ6EFg4GgDGjMowViRiyhwQAggA2gXAAL7IoErKEGyUCACMZRUq1bXwDeWNjeLNNQgATF2VLQgAzKgAboxY5Lh1qFXjMK1oEjCDPa1jIJPTuH3zEIvLq+tVvfBbOzOjB0cIKzMlALrIxd1nrQCsneXvw20/Uq/IbnAbAjY3bZTa7wAAstyW9xO4I+o1O/0u0NwADYEcdHi83iCvujzt8OhSgU1UfAwdSMRMsQgAOx4pGPFEMqG7BAADjZ8Aea0550xPKQAqFz1e9LJpJJIoVstaAAZ5f11bTNXS/qDtdKiRD4OTlQgTbqlRaENjNWrFRr7XDNbDnQbTfAbY7Pe7vVaPZrfcSEHb3S7HWHQ26/czNTHHXGfZreUnba7w1Gg/AE9HYwGU47k5S/SG/S7Ce7s5nK0bqzTA0bC+7G4Di88XiB4ok3Kl0oVQMlaHRCrw6gBhWFiHhjz6TsfY2ej5kL3kLxCTvrj9ej+eoHgbtdPLoDxL0eAFXgbpe7jcr3cjbeT+9X3j32+8WGLycft88D9r3efKOK6Hr8x5DmeI6jnUW7QXeUFfvBu4fn0k6AbBvCAShu7YohvDMvBIH9oOp7nnuo5YReo4jI+5E0dRcETkhVEIYxGGbthm6ESAYEkWx9G8Dh/E8PhFHCcxu68rhPCSaJklCYgtG7gp1FcTxw48PeM5MVp74PgBo46TwOGGfhhmSax0kGZOknzqpxHqfhO54Z+EkubwCkWQphkKU5PAKc+flAZOCkHke9kQTwgG+YBAWAf+Al6QlAU4T+OHxWJP74aFh5AA",
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
