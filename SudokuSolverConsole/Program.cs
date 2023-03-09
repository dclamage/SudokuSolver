using System.Diagnostics;
using System.Runtime.InteropServices;
using McMaster.Extensions.CommandLineUtils;
using SudokuSolver;

namespace SudokuSolverConsole;

class Program
{
	private static string descriptionString = $"Version {SudokuSolverVersion.version} created by David Clamage (\"Rangsk\").\n" +
        "https://github.com/dclamage/SudokuSolver\n\n" +
        "This is free, open source software and is supported by the community.\n" +
        "Watch me on YouTube: https://youtube.com/rangsk\n" +
        "Support me on Patreon: https://www.patreon.com/rangsk\n" +
        "Buy me a Coffee: https://ko-fi.com/rangsk";

    public static async Task<int> Main(string[] args)
	{
        var app = new CommandLineApplication();
		app.Name = args[0];
		app.FullName = "Sudoku Solver";
		app.Description = descriptionString;

        app.HelpOption();

		var blankGridSize = app.Option<int>("-b|--blank", "Use a blank grid of a square size.", CommandOptionType.SingleValue)
			.Accepts(v => v.Range(1, 31));
		var givens = app.Option("-g|--givens", "Provide a digit string to represent the givens for the puzzle.", CommandOptionType.SingleValue);
		var candidates = app.Option("-a|--candidates", "Provide a candidate string of height^3 numbers.", CommandOptionType.SingleValue);
		var fpuzzlesURL = app.Option("-f|--fpuzzles", "Import a full f-puzzles URL (Everything after '?load=').", CommandOptionType.SingleValue);
		var constraints = app.Option("-c|--constraint", "Provide a constraint to use.", CommandOptionType.MultipleValue);
		var print = app.Option("-p|--print", "Print the input board.", CommandOptionType.NoValue);
		var solveBruteForce = app.Option("-s|--solve", "Provide a single brute force solution.", CommandOptionType.NoValue);
		var solveRandomBruteForce = app.Option("-d|--random", "Provide a single random brute force solution.", CommandOptionType.NoValue);
		var solveLogically = app.Option("-l|--logical", "Attempt to solve the puzzle logically.", CommandOptionType.NoValue);
		var trueCandidates = app.Option("-r|--truecandidates", "Find the true candidates for the puzzle (union of all solutions).", CommandOptionType.NoValue);
		var check = app.Option("-k|--check", "Check if there are 0, 1, or 2+ solutions.", CommandOptionType.NoValue);
		var solutionCount = app.Option("-n|--solutioncount", "Provide an exact solution count.", CommandOptionType.NoValue);
		var maxSolutionCount = app.Option<ulong>("-x|--maxcount", "Specify an maximum solution count.", CommandOptionType.SingleValue);
		var multiThread = app.Option("-t|--multithread", "Use multithreading.", CommandOptionType.NoValue);
		var outputPath = app.Option("-o|--out", "Output solution(s) to file.", CommandOptionType.SingleValue)
			.Accepts(v => v.LegalFilePath());
		var sortSolutionCount = app.Option("-z|--sort", "Sort the solution count (requires reading all solutions into memory).", CommandOptionType.NoValue);
		var fpuzzlesOut = app.Option("-u|--url", "Write solution as f-puzzles URL.", CommandOptionType.NoValue);
		var visitURL = app.Option("-v|--visit", "Automatically visit the output URL with default browser (combine with -u).", CommandOptionType.NoValue);
		var listen = app.Option("--listen", "Listen for websocket connections.", CommandOptionType.NoValue);
		var port = app.Option<int>("--port", "Change the listen port for websocket connections (default 4545)", CommandOptionType.SingleValue)
			.Accepts(v => v.Range(1024, 49151));
		port.DefaultValue = 4545;
        var listConstraints = app.Option("--list-constraints", "List all available constraints.", CommandOptionType.NoValue);
        var hideBanner = app.Option("--hide-banner", "Do not show the text with app version and support links.", CommandOptionType.NoValue);
        var verbose = app.Option("--verbose", "Print verbose logs.", CommandOptionType.NoValue);

        app.OnExecuteAsync(async cancellationToken =>
		{
			Program program = new()
			{
				BlankGridSize = blankGridSize.ParsedValue,
                Givens = givens.Value(),
                Candidates = candidates.Value(),
                FpuzzlesURL = fpuzzlesURL.Value(),
                Constraints = constraints.Values.ToArray(),
				Print = print.HasValue(),
                SolveBruteForce = solveBruteForce.HasValue(),
                SolveRandomBruteForce = solveRandomBruteForce.HasValue(),
                SolveLogically = solveLogically.HasValue(),
                TrueCandidates = trueCandidates.HasValue(),
                Check = check.HasValue(),
                SolutionCount = solutionCount.HasValue(),
                MaxSolutionCount = maxSolutionCount.ParsedValue,
                MultiThread = multiThread.HasValue(),
                OutputPath = outputPath.Value(),
                SortSolutionCount = sortSolutionCount.HasValue(),
                FpuzzlesOut = fpuzzlesOut.HasValue(),
                VisitURL = visitURL.HasValue(),
                Listen = listen.HasValue(),
				Port = port.ParsedValue,
                ListConstraints = listConstraints.HasValue(),
                HideBanner = hideBanner.HasValue(),
                VerboseLogs = verbose.HasValue(),
            };

            await program.OnExecuteAsync(app, cancellationToken);
		});

		return await app.ExecuteAsync(args);
    }

	// Input board options
	public required int BlankGridSize { get; init; }
    public required string Givens { get; init; }
    public required string Candidates { get; init; }
    public required string FpuzzlesURL { get; init; }
    public required string[] Constraints { get; init; }

    // Pre-solve options
    public required bool Print { get; init; }

    // Solve options
    public required bool SolveBruteForce { get; init; }
    public required bool SolveRandomBruteForce { get; init; }
    public required bool SolveLogically { get; init; }
    public required bool TrueCandidates { get; init; }
    public required bool Check { get; init; }
    public required bool SolutionCount { get; init; }
    public required ulong MaxSolutionCount { get; init; }
    public required bool MultiThread { get; init; }

	// Post-solve options
    public required string OutputPath { get; init; }
    public required bool SortSolutionCount { get; init; }
    public required bool FpuzzlesOut { get; init; }
    public required bool VisitURL { get; init; }

	// Websocket options
    public required bool Listen { get; init; }
    public required int Port { get; init; }

	// Help-related options
    public required bool ListConstraints { get; init; }
	public required bool HideBanner { get; init; }
    public required bool VerboseLogs { get; init; }

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

		if (!HideBanner)
		{
			Console.WriteLine("------------------------------------");
			Console.WriteLine("Sudoku Solver");
            Console.WriteLine();

            Console.WriteLine(descriptionString);
            Console.WriteLine();
			Console.WriteLine("(Hide this banner with the --hide-banner option)");
			Console.WriteLine("------------------------------------");
			Console.WriteLine();
        }

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
			using WebsocketListener websocketListener = new();
			await websocketListener.Listen("localhost", Port, Constraints, VerboseLogs);

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
		bool haveBlankGridSize = BlankGridSize >= 1 && BlankGridSize <= 31;
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
				solver = SolverFactory.CreateBlank(BlankGridSize, Constraints);
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
