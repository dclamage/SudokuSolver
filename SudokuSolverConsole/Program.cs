using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using SudokuSolver;

namespace SudokuSolverConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Useful for quickly testing a puzzle without changing commandline parameters
#if false
            args = new string[]
            {
                //@"-f=N4IgzglgXgpiBcA2ANCA5gJwgEwQbT2AF9ljSSzKiBdZQih8p42+5xq1q99rj/8nx7cWtEABcAFjAwBbAPayY4mflAAbCADsYYfHhAAlAIwBhYyFSGATOctGAzHasAWZ0YCs7w4jvVqFCCaOnrwBEa+iPaGXlGupnGOCdG2iSbJ/jREQA===",
                "-b=9",
                "-c=chess:v1,2,3,4,5,6,7;1,1;2,2;3,3;4,4;5,5;6,6;7,7;8,8",
                //"-c=ratio:neg2",
                //"-c=difference:neg1",
                //"-c=taxi:4",
                //"-o=candidates.txt",
                //"-uv",
                "-st",
            };
#endif

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            string blankGridSizeString = null;
            string outputPath = null;
            List<string> constraints = new();
            bool multiThread = false;
            bool solveBruteForce = false;
            bool solveRandomBruteForce = false;
            bool solveLogically = false;
            bool solutionCount = false;
            bool sortSolutionCount = false;
            bool check = false;
            bool trueCandidates = false;
            bool fpuzzlesOut = false;
            bool visitURL = false;
            bool print = false;
            bool listen = false;
            string portStr = null;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "b|blank=", "Use a blank grid of a square size.", b => blankGridSizeString = b },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "o|out=", "Output solution(s) to file.", o => outputPath = o },
                { "t|multithread", "Use multithreading.", t => multiThread = t != null },
                { "s|solve", "Provide a single brute force solution.", s => solveBruteForce = s != null },
                { "d|random", "Provide a single random brute force solution.", d => solveRandomBruteForce = d != null },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "z|sort", "Sort the solution count (requires reading all solutions into memory).", sort => sortSolutionCount = sort != null },
                { "u|url", "Write solution as f-puzzles URL.", u => fpuzzlesOut = u != null },
                { "v|visit", "Automatically visit the output URL with default browser (combine with -u).", v => visitURL = v != null },
                { "p|print", "Print the input board.", p => print = p != null },
                { "h|help", "Show this message and exit", h => showHelp = h != null },
                { "listen", "Listen for websocket connections", l => listen = l != null },
                { "port=", "Change the listen port for websocket connections (default 4545)", p => portStr = p },
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

            if (listen)
            {
                int port = 4545;
                if (!string.IsNullOrWhiteSpace(portStr))
                {
                    port = int.Parse(portStr);
                }
                using WebsocketListener websocketListener = new();
                await websocketListener.Listen("localhost", port);

                Console.WriteLine("Press CTRL + Q to quit.");

                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Q)
                    {
                        return;
                    }
                }
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

                if (outputPath != null)
                {
                    try
                    {
                        using StreamWriter file = new(outputPath);
                        await file.WriteLineAsync(solver.OutputString);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to write to file: {e.Message}");
                    }
                }

                if (fpuzzlesOut)
                {
                    OpenFPuzzles(solver, visitURL);
                }
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

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
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

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
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

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
                }
            }

            if (solutionCount)
            {
                Console.WriteLine("Finding solution count...");

                try
                {
                    Action<Solver> solutionEvent = null;
                    using StreamWriter file = (outputPath != null) ? new(outputPath) : null;
                    if (file != null)
                    {
                        solutionEvent = (Solver solver) =>
                        {
                            try
                            {
                                file.WriteLine(solver.GivenString);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Failed to write to file: {e.Message}");
                            }
                        };
                    }

                    ulong numSolutions = solver.CountSolutions(maxSolutions: 0, multiThread: multiThread, progressEvent: (ulong count) =>
                    {
                        ReplaceLine($"(In progress) Found {count} solutions in {watch.Elapsed}.");
                    },
                    solutionEvent: solutionEvent);

                    ReplaceLine($"\rFound {numSolutions} solutions.");
                    Console.WriteLine();

                    if (file != null && sortSolutionCount)
                    {
                        Console.WriteLine("Sorting...");
                        file.Close();

                        string[] lines = await File.ReadAllLinesAsync(outputPath);
                        Array.Sort(lines);
                        await File.WriteAllLinesAsync(outputPath, lines);
                        Console.WriteLine("Done.");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"ERROR: {e.Message}");
                }
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

        private static void OpenFPuzzles(Solver solver, bool visit)
        {
            string url = SolverFactory.ToFPuzzlesURL(solver);
            Console.WriteLine(url);
            if (visit)
            {
                try
                {
                    OpenUrl(url);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Cannot open URL: {e}");
                }
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
