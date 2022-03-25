using System.Diagnostics;
using System.Runtime.InteropServices;
using McMaster.Extensions.CommandLineUtils;
using SudokuSolver;

namespace SudokuSolverConsole;

[Command(Name = "Sudoku Solver", Description = $"SudokuSolver version {SudokuSolverVersion.version} created by David Clamage (\"Rangsk\").\nhttps://github.com/dclamage/SudokuSolver")]
[HelpOption()]
class Program
{
    public async static Task Main(string[] args) =>
        await CommandLineApplication.ExecuteAsync<Program>(args);

    // Input board options
    [Option("-b|--blank", Description = "Use a blank grid of a square size.")]
    private string BlankGridSizeString { get; set; }

    [Option("-g|--givens", Description = "Provide a digit string to represent the givens for the puzzle.")]
    private string Givens { get; set; }

    [Option("-a|--candidates", Description = "Provide a candidate string of height^3 numbers.")]
    private string Candidates { get; set; }

    [Option("-f|--fpuzzles", Description = "Import a full f-puzzles URL (Everything after '?load=').")]
    private string FpuzzlesURL { get; set; }

    [Option("-c|--constraint", Description = "Provide a constraint to use.")]
    string[] Constraints { get; set; }

    // Pre-solve options
    [Option("-p|--print", Description = "Print the input board.")]
    private bool Print { get; set; }

    // Solve options
    [Option("-s|--solve", Description = "Provide a single brute force solution.")]
    private bool SolveBruteForce { get; set; }

    [Option("-d|--random", Description = "Provide a single random brute force solution.")]
    private bool SolveRandomBruteForce { get; set; }

    [Option("-l|--logical", Description = "Attempt to solve the puzzle logically.")]
    private bool SolveLogically { get; set; }

    [Option("-r|--truecandidates", Description = "Find the true candidates for the puzzle (union of all solutions).")]
    private bool TrueCandidates { get; set; }

    [Option("-k|--check", Description = "Check if there are 0, 1, or 2+ solutions.")]
    private bool Check { get; set; }

    [Option("-n|--solutioncount", Description = "Provide an exact solution count.")]
    private bool SolutionCount { get; set; }

    [Option("-x|--maxcount", Description = "Specify an maximum solution count.")]
    private ulong MaxSolutionCount { get; set; }

    [Option("-t|--multithread", Description = "Use multithreading.")]
    private bool MultiThread { get; set; }

    // Post-solve options
    [Option("-o|--out", Description = "Output solution(s) to file.")]
    private string OutputPath { get; set; }

    [Option("-z|--sort", Description = "Sort the solution count (requires reading all solutions into memory).")]
    private bool SortSolutionCount { get; set; }

    [Option("-u|--url", Description = "Write solution as f-puzzles URL.")]
    private bool FpuzzlesOut { get; set; }

    [Option("-v|--visit", Description = "Automatically visit the output URL with default browser (combine with -u).")]
    private bool VisitURL { get; set; }

    // Websocket options
    [Option("--listen", Description = "Listen for websocket connections.")]
    private bool Listen { get; set; }

    [Option("--port", Description = "Change the listen port for websocket connections (default 4545)")]
    private string PortStr { get; set; }

    // Help-related options
    [Option("--list-constraints", Description = "List all available constraints.")]
    private bool ListConstraints { get; set; }

        public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken cancellationToken = default)
    {
        // Useful for quickly testing a puzzle without changing commandline parameters
#if false
            args = new string[]
            {
                //"-g=........1.....2.3...4.5.6.....2...7..7...4..28...9.5....5..69...1.3.....6.8.4....",
                @"-f=N4IgzglgXgpiBcBOANCALhNAbO8QHkAnAI0xFQEMBXNACwHtCEQBFegExgGNyRCqcYGGmYARCAHNMYAAQUs9AHYS5iuYUL0A7jIhq6MGRMIR2MgLZUwaGWCrmZaeo9qH2kzLrXSZXCIS4cADoZcSk0WXMKAE8ZQhgABxgKG3klFQo1Cg1tIIAdRQKwnzTlGXcKCSV5WT13LhSYM2JY7M0tWXoaSE4XQ2NTCysbOwcnPqMIADcYNUV7YhhCORs6FPV2kOKIixi4xOTUhTKKcohK6qx8wsUAYRgsLFktTFo5I3jYvwCcIesZRYyDjsEL3R7PV7vYwwWJgACOVGyhks/0BMBmiiCvAG7AQAG08aAEtlMNFmABRDEgAC+yGAtPpdIZzKZrKJJLQZLw+HYuOpAF1kISWYzRSLxYLhWzxdLpZKxbKFUqBULlYqZfT5RrtXLVTq1WKterjYbVSBiSZOcweXyTfrzRyuSBKbMafzBSAsJhsDAANYQR5LUb40BcB5YZgAJQAjAAGW4Adl4YfB+JAkcQtwAHLxI1nbogQB73PEuBglMwAKqR3hTeRUXAgHMMkApiN4SMAZluceT4bAacjACYC7no9mi6gS9xy4oqwAZWv1xtJlttqPxzt91PwPHp8dD3Mj6OTkDTssQCt4USL1B1rAN5gANhpdNb4ajCduse3T0H+ZPVAM1uQ9i38GdLzna8azvZdmELNcPw7eMk1QNsB13fdbhfICRwAVlzbsABZcyI24tyAvCQNzJ8e1Pc9ZzEW8QHvR88E7Zs33XDsv17ND+0HWjC0oicgLI1D027HD03wsdbhIsDS0YvBK2Y1jGyI1cuKQ/dN1/DC92AkigPzAigK/aTI1oiTIyonMxNHRSIKvEBqyXB8NMPRDHijbsf34ndDLIwD0yow8gNoij0y/Yz01M3NMxfJyLxc0QYJYuC8CIrcVRANptBDT09BgAzDO7GyR0s8czJk+Siw9dDB3KosWy9RQSvxMryNI6iRJCqzqPdAK/0wrtuv5VritK6LupMuqgMzGq82w+rhoMmatwmt82o63dDK/GzaPs0LRwcnMhvfQKZqTLbQB26bbMTHqkoagTRqom7Jvah7uyWsiFNeq6xoI26iu+zqTqigaAbWwcqM2r7doJGaltol7YdGr8QYFakgA",
                //"-b=9",
                //"-c=renban:r1-6c1",
                //"-c=chess:v1,2,3,4,5,6,7;1,1;2,2;3,3;4,4;5,5;6,6;7,7;8,8",
                //"-c=ratio:neg2",
                //"-c=difference:neg1",
                //"-c=taxi:4",
                //"-o=candidates.txt",
                //"-uv",
                "-n",
                //"-pl",
            };
#endif

        Stopwatch watch = Stopwatch.StartNew();
        string processName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($"SudokuSolver version {SudokuSolverVersion.version} created by David Clamage (\"Rangsk\").");
        Console.WriteLine("https://github.com/dclamage/SudokuSolver");
        Console.WriteLine();

        if (ListConstraints)
        {
            Console.WriteLine("Constraints:");
            List<string> constraintNames = ConstraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
            constraintNames.Sort();
            foreach (var constraintName in constraintNames)
            {
                Console.WriteLine($"\t{constraintName}");
            }
            return 0;
        }

        if (Listen)
        {
            int port = 4545;
            if (!string.IsNullOrWhiteSpace(PortStr))
            {
                port = int.Parse(PortStr);
            }
            using WebsocketListener websocketListener = new();
            await websocketListener.Listen("localhost", port, Constraints);

            Console.WriteLine("Press CTRL + Q to quit.");

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Q)
                {
                    return 0;
                }
            }
        }

        bool haveFPuzzlesURL = !string.IsNullOrWhiteSpace(FpuzzlesURL);
        bool haveGivens = !string.IsNullOrWhiteSpace(Givens);
        bool haveBlankGridSize = !string.IsNullOrWhiteSpace(BlankGridSizeString);
        bool haveCandidates = !string.IsNullOrWhiteSpace(Candidates);
        if (!haveFPuzzlesURL && !haveGivens && !haveBlankGridSize && !haveCandidates)
        {
            Console.WriteLine($"ERROR: Must provide either an f-puzzles URL or a givens string or a blank grid or a candidates string, or must be run in listen mode.");
            Console.WriteLine($"Try '{processName} --help' for more information.");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
            return 0;
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
        if (haveCandidates)
        {
            numBoardsSpecified++;
        }

        if (numBoardsSpecified != 1)
        {
            Console.WriteLine($"ERROR: Cannot provide more than one set of givens (f-puzzles URL, given string, blank grid, candidates).");
            Console.WriteLine($"Try '{processName} --help' for more information.");
            return 1;
        }

        Solver solver;
        try
        {
            if (haveBlankGridSize)
            {
                if (int.TryParse(BlankGridSizeString, out int blankGridSize) && blankGridSize > 0 && blankGridSize < 32)
                {
                    solver = SolverFactory.CreateBlank(blankGridSize, Constraints);
                }
                else
                {
                    Console.WriteLine($"ERROR: Blank grid size must be between 1 and 31");
                    Console.WriteLine($"Try '{processName} --help' for more information.");
                    return 1;
                }
            }
            else if (haveGivens)
            {
                solver = SolverFactory.CreateFromGivens(Givens, Constraints);
            }
            else if (haveFPuzzlesURL)
            {
                solver = SolverFactory.CreateFromFPuzzles(FpuzzlesURL, Constraints);
                Console.WriteLine($"Imported \"{solver.Title ?? "Untitled"}\" by {solver.Author ?? "Unknown"}");
            }
            else // if (haveCandidates)
            {
                solver = SolverFactory.CreateFromCandidates(Candidates, Constraints);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }

        if (Print)
        {
            Console.WriteLine("Input puzzle:");
            solver.Print();
        }

        if (SolveLogically)
        {
            Console.WriteLine("Solving logically:");
            List<LogicalStepDesc> logicalStepDescs = new();
            var logicResult = solver.ConsolidateBoard(logicalStepDescs);
            foreach (var step in logicalStepDescs)
            {
                Console.WriteLine(step.ToString());
            }
            if (logicResult == LogicResult.Invalid)
            {
                Console.WriteLine($"Board is invalid!");
            }
            solver.Print();

            if (OutputPath != null)
            {
                try
                {
                    using StreamWriter file = new(OutputPath);
                    await file.WriteLineAsync(solver.OutputString);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to write to file: {e.Message}");
                }
            }

            if (FpuzzlesOut)
            {
                OpenFPuzzles(solver, VisitURL);
            }
        }

        if (SolveBruteForce)
        {
            Console.WriteLine("Finding a solution with brute force:");
            if (!solver.FindSolution(multiThread: MultiThread))
            {
                Console.WriteLine($"No solutions found!");
            }
            else
            {
                solver.Print();

                if (OutputPath != null)
                {
                    try
                    {
                        using StreamWriter file = new(OutputPath);
                        await file.WriteLineAsync(solver.OutputString);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to write to file: {e.Message}");
                    }
                }

                if (FpuzzlesOut)
                {
                    OpenFPuzzles(solver, VisitURL);
                }
            }
        }

        if (SolveRandomBruteForce)
        {
            Console.WriteLine("Finding a random solution with brute force:");
            if (!solver.FindSolution(multiThread: MultiThread, isRandom: true))
            {
                Console.WriteLine($"No solutions found!");
            }
            else
            {
                solver.Print();

                if (OutputPath != null)
                {
                    try
                    {
                        using StreamWriter file = new(OutputPath);
                        await file.WriteLineAsync(solver.OutputString);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to write to file: {e.Message}");
                    }
                }

                if (FpuzzlesOut)
                {
                    OpenFPuzzles(solver, VisitURL);
                }
            }
        }

        if (TrueCandidates)
        {
            Console.WriteLine("Finding true candidates:");
            int currentLineCursor = Console.CursorTop;
            object consoleLock = new();
            if (!solver.FillRealCandidates(multiThread: MultiThread, progressEvent: (uint[] board) =>
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

                if (OutputPath != null)
                {
                    try
                    {
                        using StreamWriter file = new(OutputPath);
                        await file.WriteLineAsync(solver.OutputString);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to write to file: {e.Message}");
                    }
                }

                if (FpuzzlesOut)
                {
                    OpenFPuzzles(solver, VisitURL);
                }
            }
        }

        if (SolutionCount)
        {
            Console.WriteLine("Finding solution count...");

            try
            {
                Action<Solver> solutionEvent = null;
                using StreamWriter file = (OutputPath != null) ? new(OutputPath) : null;
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

                ulong numSolutions = solver.CountSolutions(maxSolutions: MaxSolutionCount, multiThread: MultiThread, progressEvent: (ulong count) =>
                {
                    ReplaceLine($"(In progress) Found {count} solutions in {watch.Elapsed}.");
                },
                solutionEvent: solutionEvent);

                if (MaxSolutionCount > 0)
                {
                    numSolutions = Math.Min(numSolutions, MaxSolutionCount);
                }

                if (MaxSolutionCount == 0 || numSolutions < MaxSolutionCount)
                {
                    ReplaceLine($"\rThere are exactly {numSolutions} solutions.");
                }
                else
                {
                    ReplaceLine($"\rThere are at least {numSolutions} solutions.");
                }
                Console.WriteLine();

                if (file != null && SortSolutionCount)
                {
                    Console.WriteLine("Sorting...");
                    file.Close();

                    string[] lines = await File.ReadAllLinesAsync(OutputPath);
                    Array.Sort(lines);
                    await File.WriteAllLinesAsync(OutputPath, lines);
                    Console.WriteLine("Done.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message}");
            }
        }

        if (Check)
        {
            Console.WriteLine("Checking...");
            ulong numSolutions = solver.CountSolutions(2, MultiThread);
            Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
        }

        watch.Stop();
        Console.WriteLine($"Took {watch.Elapsed}");
        return 0;
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
