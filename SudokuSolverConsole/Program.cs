using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using SudokuSolver;

namespace SudokuSolverConsole;

public class Program
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
		app.Name = "SudokuSolver";
		app.FullName = "Sudoku Solver";
		app.Description = descriptionString;

        app.HelpOption();

        // Info
        var listConstraints = app.Option("--list-constraints", "List all available constraints.", CommandOptionType.NoValue);

        // Board input
        var blankGridSize = app.Option<int>("-b|--blank", "Use a blank grid of a square size.", CommandOptionType.SingleValue)
			.Accepts(v => v.Range(1, 31));
		var blankWithR1 = app.Option("--blank-with-r1", "Use a blank grid of a square size, but fill the first row with the given values.", CommandOptionType.SingleValue);
        var givens = app.Option("-g|--givens", "Provide a digit string to represent the givens for the puzzle.", CommandOptionType.SingleValue);
		var candidates = app.Option("-a|--candidates", "Provide a candidate string of height^3 numbers.", CommandOptionType.SingleValue);
		var fpuzzlesURL = app.Option("-f|--fpuzzles", "Import a full f-puzzles URL (Everything after '?load=').", CommandOptionType.SingleValue);
		var constraints = app.Option("-c|--constraint", "Provide a constraint to use.", CommandOptionType.MultipleValue);

		// Solver actions
		var solveBruteForce = app.Option("-s|--solve", "Provide a single brute force solution.", CommandOptionType.NoValue);
		var solveRandomBruteForce = app.Option("-d|--random", "Provide a single random brute force solution.", CommandOptionType.NoValue);
		var solveLogically = app.Option("-l|--logical", "Attempt to solve the puzzle logically.", CommandOptionType.NoValue);
        var trueCandidates = app.Option("-r|--truecandidates",
			"Find the true candidates for the puzzle (the union of all solutions). " +
			"Defaults to a per-candidate solution cap of 1. Use -x to set a different cap; " +
			"0 or less disables the cap. " +
			"Reported counts above the cap are approximate and may be heuristically useful, but not exact.",
			CommandOptionType.NoValue);
        var check = app.Option("-k|--check", "Check if there are 0, 1, or 2+ solutions.", CommandOptionType.NoValue);
        var solutionCount = app.Option("-n|--solutioncount",
            "Count the total number of solutions. " +
            "By default, the count is uncapped. Use -x to set a limit.",
            CommandOptionType.NoValue);
		var estimateCount = app.Option<long>("-e|--estimatecount",
			"Estimate the number of solutions using a Monte-Carlo technique. " +
			"Specify the number of iterations to do, or 0 to go forever.",
			CommandOptionType.SingleValue);
        var listen = app.Option("--listen", "Listen for websocket connections.", CommandOptionType.NoValue);

        // Options
        var maxSolutionCount = app.Option<long>("-x|--maxcount",
            "Set a maximum number of solutions to consider. " +
            "When used with --solutioncount, this limits the total count. " +
            "When used with --truecandidates, this caps the count per candidate. " +
            "Defaults: uncapped for solution count, 1 per candidate for true candidates.",
            CommandOptionType.SingleValue);
        var multiThread = app.Option("-t|--multithread", "Use multithreading.", CommandOptionType.NoValue);
        var outputPath = app.Option("-o|--out",
            "Write solution(s) to a file. " +
            "In JSON mode, the path is ignored, but enabling this option causes solutions to be included in the JSON output. " +
            "You must specify any valid path (e.g., -o=\"json\") when using JSON mode, though the actual path is not used.",
            CommandOptionType.SingleValue)
			.Accepts(v => v.LegalFilePath());
		var sortSolutionCount = app.Option("-z|--sort", "Sort the solution count (requires reading all solutions into memory).", CommandOptionType.NoValue);
		var fpuzzlesOut = app.Option("-u|--url", "Write solution as f-puzzles URL.", CommandOptionType.NoValue);
		var visitURL = app.Option("-v|--visit", "Automatically visit the output URL with default browser (combine with -u).", CommandOptionType.NoValue);
		var listenSingleThreaded = app.Option("--listen-singlethreaded", "Option to force all websocket commands to execute single threaded.", CommandOptionType.NoValue);
		var port = app.Option<int>("--port", "Change the listen port for websocket connections (default 4545)", CommandOptionType.SingleValue)
			.Accepts(v => v.Range(1024, 49151));
		port.DefaultValue = 4545;
        var hideBanner = app.Option("--hide-banner", "Do not show the text with app version and support links.", CommandOptionType.NoValue);
		var print = app.Option("-p|--print", "Print the input board.", CommandOptionType.NoValue);
        var verbose = app.Option("--verbose", "Print verbose logs.", CommandOptionType.NoValue);
        var jsonOutput = app.Option("--json", "Output results as JSON, suppress all other output.", CommandOptionType.NoValue);
        var jsonProgress = app.Option("--json-progress", "Output progress as JSON objects (default off).", CommandOptionType.NoValue);

        app.OnExecuteAsync(async cancellationToken =>
		{
			Program program = new()
			{
				BlankGridSize = blankGridSize.ParsedValue,
				BlankWithR1 = blankWithR1.Value(),
                Givens = givens.Value(),
                Candidates = candidates.Value(),
                FpuzzlesURL = fpuzzlesURL.Value(),
                Constraints = [.. constraints.Values],
				Print = print.HasValue(),
                SolveBruteForce = solveBruteForce.HasValue(),
                SolveRandomBruteForce = solveRandomBruteForce.HasValue(),
                SolveLogically = solveLogically.HasValue(),
                TrueCandidates = trueCandidates.HasValue(),
                Check = check.HasValue(),
                SolutionCount = solutionCount.HasValue(),
                MaxSolutionCount = maxSolutionCount.HasValue() || !trueCandidates.HasValue() ? maxSolutionCount.ParsedValue : 1,
                MultiThread = multiThread.HasValue(),
				EstimateCount = estimateCount.HasValue(),
				EstimateCountIterations = estimateCount.ParsedValue,
                OutputPath = outputPath.Value(),
                SortSolutionCount = sortSolutionCount.HasValue(),
                FpuzzlesOut = fpuzzlesOut.HasValue(),
                VisitURL = visitURL.HasValue(),
                Listen = listen.HasValue(),
				ListenSingleThreaded = listenSingleThreaded.HasValue(),
				Port = port.ParsedValue,
                ListConstraints = listConstraints.HasValue(),
                HideBanner = hideBanner.HasValue(),
                VerboseLogs = verbose.HasValue(),
                JsonOutput = jsonOutput.HasValue(),
                JsonProgress = jsonProgress.HasValue(),
            };

            await program.OnExecuteAsync(app, cancellationToken);
		});

		return await app.ExecuteAsync(args);
    }

	// Input board options
	public required int BlankGridSize { get; init; }
	public required string BlankWithR1 { get; init; }
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
    public required long MaxSolutionCount { get; init; }
    public required bool MultiThread { get; init; }
    public required bool EstimateCount { get; init; }
	public required long EstimateCountIterations { get; init; }

    // Post-solve options
    public required string OutputPath { get; init; }
    public required bool SortSolutionCount { get; init; }
    public required bool FpuzzlesOut { get; init; }
    public required bool VisitURL { get; init; }

	// Websocket options
    public required bool Listen { get; init; }
	public required bool ListenSingleThreaded { get; init; }
    public required int Port { get; init; }

	// Help-related options
    public required bool ListConstraints { get; init; }
	public required bool HideBanner { get; init; }
    public required bool VerboseLogs { get; init; }
    public required bool JsonOutput { get; init; }
    public required bool JsonProgress { get; init; }

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

		bool defaultToListen = false;
		bool haveFPuzzlesURL = !string.IsNullOrWhiteSpace(FpuzzlesURL);
		bool haveGivens = !string.IsNullOrWhiteSpace(Givens);
		bool haveBlankGridSize = BlankGridSize >= 1 && BlankGridSize <= 31;
		bool haveBlankWithR1 = !string.IsNullOrWhiteSpace(BlankWithR1);
		bool haveCandidates = !string.IsNullOrWhiteSpace(Candidates);
		// If no board input is specified, default to listen mode
		if (!Listen && !haveFPuzzlesURL && !haveGivens && !haveBlankGridSize && !haveBlankWithR1 && !haveCandidates)
		{
			defaultToListen = true;
			Console.WriteLine("INFO: No board input was specified, defaulting to --listen");
		}

        if (Listen || defaultToListen)
		{
			using WebsocketListener websocketListener = new();
			await websocketListener.Listen("localhost", Port, Constraints, VerboseLogs, ListenSingleThreaded);

			if (Listen)
			{
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
			else
			{
				Console.WriteLine("Ready");
				while (true)
				{
                    await Task.Delay(1000, CancellationToken.None);
                }
			}
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
		if (haveBlankWithR1)
		{
			numBoardsSpecified++;
		}
		if (haveCandidates)
		{
			numBoardsSpecified++;
		}

		if (numBoardsSpecified != 1)
		{
			string errMsg = "ERROR: Cannot provide more than one set of givens (f-puzzles URL, given string, blank grid, candidates).";
			if (JsonOutput)
			{
				JsonResultHandler.OutputError(errMsg);
				return 1;
			}
			Console.WriteLine(errMsg);
			Console.WriteLine($"Try '{processName} --help' for more information.");
			return 1;
		}

		int numSolveStepsSpecified = 0;
		if (SolveLogically)
		{
			numSolveStepsSpecified++;
		}
		if (SolveBruteForce)
		{
			numSolveStepsSpecified++;
		}
		if (SolveRandomBruteForce)
		{
			numSolveStepsSpecified++;
		}
		if (TrueCandidates)
		{
			numSolveStepsSpecified++;
		}
		if (Check)
		{
			numSolveStepsSpecified++;
		}
		if (SolutionCount)
		{
			numSolveStepsSpecified++;
		}
		if (EstimateCount)
		{
            numSolveStepsSpecified++;
		}

		if (numSolveStepsSpecified == 0)
		{
			string errMsg = "ERROR: No solve command specified (e.g. --solve, --logical, --check, --solutioncount, --estimatecount).";
			if (JsonOutput)
			{
				JsonResultHandler.OutputError(errMsg);
				return 1;
			}
			Console.WriteLine(errMsg);
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
			else if (haveBlankWithR1)
			{
				StringBuilder givens = new();
				givens.Append(BlankWithR1);
				if (BlankWithR1.Length <= 9)
				{
					for (int i = 0; i < BlankWithR1.Length * (BlankWithR1.Length - 1); i++)
					{
						givens.Append('0');
					}
				}
				else
				{
					int gridSize = BlankWithR1.Length / 2;
                    for (int i = 0; i < gridSize * (gridSize - 1); i++)
                    {
                        givens.Append('0');
                        givens.Append('0');
                    }
                }
                solver = SolverFactory.CreateFromGivens(givens.ToString(), Constraints);
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
			if (JsonOutput)
			{
				JsonResultHandler.OutputError(e.Message);
				return 1;
			}
			Console.WriteLine(e.Message);
			return 1;
		}

        if (JsonOutput)
        {
            JsonResultHandler.HandleJsonOutput(this, solver, cancellationToken);
            return 0;
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

			uint[] CountsToBoard(long[] counts)
			{
				uint[] board = new uint[solver.NUM_CELLS];
				for (int cellIndex = 0; cellIndex < solver.NUM_CELLS; cellIndex++)
				{
					for (int value = 1; value <= solver.MAX_VALUE; value++)
					{
						int candidateIndex = cellIndex * solver.MAX_VALUE + value - 1;
						if (counts[candidateIndex] > 0)
						{
							board[cellIndex] |= SolverUtility.ValueMask(value);
						}
                    }
                }
				return board;
            }

			void PrintProgressBoard(long[] curCandidateCounts)
			{
				uint[] board = CountsToBoard(curCandidateCounts);
				BoardView board2d = new(board, solver.HEIGHT, solver.WIDTH);
				lock (consoleLock)
				{
					ConsoleUtility.PrintBoard(board2d, solver.Regions, Console.Out);
					Console.SetCursorPosition(0, currentLineCursor);
				}
			}

            long[] trueCandidateCounts = solver.TrueCandidates(multiThread: MultiThread, numSolutionsCap: MaxSolutionCount, progressEvent: PrintProgressBoard, cancellationToken: cancellationToken);

			uint[] board = CountsToBoard(trueCandidateCounts);
			if (board.Any(cell => cell == 0))
            {
				Console.WriteLine($"No solutions found!");
			}
			else
			{
				for (int cellIndex = 0; cellIndex < solver.NUM_CELLS; cellIndex++)
				{
					int i = cellIndex / solver.WIDTH;
					int j = cellIndex % solver.WIDTH;
					solver.SetMask(i, j, board[cellIndex]);
                }

                // First display the final board
				solver.Print();

                // Then display the counts for each cell and candidate
                Console.WriteLine("\nCandidate counts for each cell:");
				Console.WriteLine($"[{string.Join(",", trueCandidateCounts)}]");

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

				long numSolutions = solver.CountSolutions(maxSolutions: MaxSolutionCount, multiThread: MultiThread, cancellationToken: cancellationToken, progressEvent: (long count) =>
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

        if (EstimateCount)
        {
            Console.WriteLine("Estimating solution count...");

            try
            {
                solver.EstimateSolutions(numIterations: EstimateCountIterations, multiThread: MultiThread, cancellationToken: cancellationToken, progressEvent: (progressData) =>
                {
                    // 95% confidence multiplier for a normal distribution
                    const double z95 = 1.96;

                    double estimate = progressData.estimate;
                    double stderr = progressData.stderr;
					long iterations = progressData.iterations;

                    double lower = estimate - z95 * stderr;
                    double upper = estimate + z95 * stderr;
                    double relErrPercent = 100.0 * (z95 * stderr) / estimate;

					string iterationsStr = EstimateCountIterations <= 0 ? "inf" : EstimateCountIterations.ToString();
                    Console.WriteLine($"[{watch.Elapsed}] Estimate after {iterations} / {iterationsStr} iterations: {estimate:E6}  (95% CI: {lower:E6} – {upper:E6}, ±{relErrPercent:F2}%)");
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message}");
            }
        }

        if (Check)
		{
			Console.WriteLine("Checking...");
			long numSolutions = solver.CountSolutions(2, MultiThread);
			Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
		}

		watch.Stop();
		Console.WriteLine($"Took {watch.Elapsed}");
		return 0;
	}

	private static void ReplaceLine(string text)
	{
		try
		{
			if (Console.IsOutputRedirected)
			{
				Console.WriteLine(text);
			}
			else
			{
				Console.Write("\r" + text + new string(' ', Console.WindowWidth - text.Length) + "\r");
			}
		}
		catch (Exception)
		{
            Console.WriteLine(text);
        }
	}

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
