using SudokuSolver;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SudokuSolverConsole;

[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(SolutionCountResult))]
[JsonSerializable(typeof(SolutionListResult))]
[JsonSerializable(typeof(SolutionProgressResult))]
[JsonSerializable(typeof(TrueCandidatesResult))]
[JsonSerializable(typeof(TrueCandidatesProgressResult))]
[JsonSerializable(typeof(LogicalSolveResult))]
[JsonSerializable(typeof(LogicalStepInfo))]
[JsonSerializable(typeof(BruteForceSolveResult))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class JsonResultContext : JsonSerializerContext
{
}

#pragma warning disable IDE1006 // Naming Styles
public class ErrorResult
{
    public string type { get; set; } = "error";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public string error { get; set; }
}

public class SolutionCountResult
{
    public string type { get; set; } = "solutionCount";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public long count { get; set; }
}

public class SolutionListResult
{
    public string type { get; set; } = "solutionList";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public long count { get; set; }
    public List<string> solutions { get; set; }
}

public class SolutionProgressResult
{
    public string type { get; set; } = "solutionCountProgress";
    public bool isProgress { get; set; } = true;
    public long count { get; set; }
    public DateTime timestamp { get; set; }
    public double duration { get; set; }
}

public class TrueCandidatesResult
{
    public string type { get; set; } = "trueCandidates";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public string board { get; set; }
    public long[] candidateCounts { get; set; }
}

public class TrueCandidatesProgressResult
{
    public string type { get; set; } = "trueCandidatesProgress";
    public bool isProgress { get; set; } = true;
    public DateTime timestamp { get; set; }
    public double duration { get; set; }
    public string board { get; set; }
    public long[] candidateCounts { get; set; }
}

public class LogicalStepInfo
{
    public string description { get; set; }
    public string beforeState { get; set; }
    public string afterState { get; set; }
}

public class LogicalSolveResult
{
    public string type { get; set; } = "logicalSolve";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public string initialBoard { get; set; }
    public List<LogicalStepInfo> steps { get; set; }
    public string finalBoard { get; set; }
}

public class BruteForceSolveResult
{
    public string type { get; set; } = "bruteForceSolve";
    public DateTime startTimestamp { get; set; }
    public DateTime finishTimestamp { get; set; }
    public double duration { get; set; }
    public string initialBoard { get; set; }
    public string solution { get; set; }
    public bool foundSolution { get; set; }
}
#pragma warning restore IDE1006 // Naming Styles

public static class JsonResultHandler
{
    public static void HandleJsonOutput(Program program, CancellationToken cancellationToken)
    {
        DateTime start = DateTime.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Solver solver;
        try
        {
            solver = program.BlankGridSize is >= 1 and <= 31
                ? SolverFactory.CreateBlank(program.BlankGridSize, program.Constraints)
                : !string.IsNullOrWhiteSpace(program.Givens)
                    ? SolverFactory.CreateFromGivens(program.Givens, program.Constraints)
                    : !string.IsNullOrWhiteSpace(program.FpuzzlesURL)
                ? SolverFactory.CreateFromFPuzzles(program.FpuzzlesURL, program.Constraints)
                : SolverFactory.CreateFromCandidates(program.Candidates, program.Constraints);
        }
        catch (Exception e)
        {
            OutputJson(new ErrorResult
            {
                startTimestamp = start,
                finishTimestamp = DateTime.UtcNow,
                duration = 0.0,
                error = e.Message
            });
            return;
        }

        if (program.SolutionCount || program.Check)
        {
            OutputSolutionCount(program, solver, start, stopwatch, cancellationToken);
            return;
        }
        if (program.TrueCandidates)
        {
            OutputTrueCandidates(program, solver, start, stopwatch, cancellationToken);
            return;
        }
        if (program.SolveLogically)
        {
            OutputLogicalSolve(program, solver, start, stopwatch, cancellationToken);
            return;
        }
        if (program.SolveBruteForce || program.SolveRandomBruteForce)
        {
            OutputBruteForceSolve(program, solver, start, stopwatch, cancellationToken);
            return;
        }
        // Add more as needed
        OutputJson(new ErrorResult
        {
            startTimestamp = start,
            finishTimestamp = DateTime.UtcNow,
            duration = stopwatch.Elapsed.TotalSeconds,
            error = "No supported JSON output mode selected."
        });
    }

    private static void OutputSolutionCount(Program program, Solver solver, DateTime start, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        List<string> solutions = [];
        long count = 0;
        Action<Solver> solutionEvent = null;
        if (program.SortSolutionCount || program.OutputPath != null)
        {
            solutionEvent = (Solver s) => solutions.Add(s.GivenString);
        }
        Action<long> progressEvent = null;
        if (program.JsonProgress)
        {
            progressEvent = (long c) => OutputJson(new SolutionProgressResult
            {
                count = c,
                timestamp = DateTime.UtcNow,
                duration = stopwatch.Elapsed.TotalSeconds
            });
        }
        count = solver.CountSolutions(
            maxSolutions: program.Check ? 2 : program.MaxSolutionCount,
            multiThread: program.MultiThread,
            progressEvent: progressEvent,
            solutionEvent: solutionEvent,
            cancellationToken: cancellationToken);
        stopwatch.Stop();
        if (program.SortSolutionCount)
        {
            solutions.Sort();
        }

        DateTime finish = DateTime.UtcNow;
        if (solutionEvent != null)
        {
            OutputJson(new SolutionListResult
            {
                startTimestamp = start,
                finishTimestamp = finish,
                duration = stopwatch.Elapsed.TotalSeconds,
                count = count,
                solutions = solutions
            });
        }
        else
        {
            OutputJson(new SolutionCountResult
            {
                startTimestamp = start,
                finishTimestamp = finish,
                duration = stopwatch.Elapsed.TotalSeconds,
                count = count
            });
        }
    }

    private static void OutputTrueCandidates(Program program, Solver solver, DateTime start, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        long[] trueCandidateCounts = null;
        Action<long[]> progressEvent = null;
        if (program.JsonProgress)
        {
            progressEvent = (long[] curCounts) => 
            {
                UpdateBoardFromCandidateCounts(solver, curCounts);
                OutputJson(new TrueCandidatesProgressResult
                {
                    timestamp = DateTime.UtcNow,
                    duration = stopwatch.Elapsed.TotalSeconds,
                    board = solver.OutputString,
                    candidateCounts = curCounts
                });
            };
        }
        trueCandidateCounts = solver.TrueCandidates(
            multiThread: program.MultiThread,
            numSolutionsCap: program.MaxSolutionCount,
            progressEvent: progressEvent,
            cancellationToken: cancellationToken);
        stopwatch.Stop();
        DateTime finish = DateTime.UtcNow;
        
        UpdateBoardFromCandidateCounts(solver, trueCandidateCounts);
        OutputJson(new TrueCandidatesResult
        {
            startTimestamp = start,
            finishTimestamp = finish,
            duration = stopwatch.Elapsed.TotalSeconds,
            board = solver.OutputString,
            candidateCounts = trueCandidateCounts
        });
    }

    private static void OutputLogicalSolve(Program program, Solver solver, DateTime start, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        List<LogicalStepInfo> steps = [];
        string initialBoard = solver.OutputString;

        while (!cancellationToken.IsCancellationRequested)
        {
            List<LogicalStepDesc> stepDescs = [];
            string beforeState = solver.OutputString;
            LogicResult result = solver.StepLogic(stepDescs);
            
            if (result == LogicResult.None || result == LogicResult.PuzzleComplete)
            {
                break;
            }

            string afterState = solver.OutputString;
            steps.Add(new LogicalStepInfo
            {
                description = string.Join("\n", stepDescs.ConvertAll(s => s.ToString())),
                beforeState = beforeState,
                afterState = afterState
            });

            if (result == LogicResult.Invalid)
            {
                break;
            }
        }

        stopwatch.Stop();
        DateTime finish = DateTime.UtcNow;
        OutputJson(new LogicalSolveResult
        {
            startTimestamp = start,
            finishTimestamp = finish,
            duration = stopwatch.Elapsed.TotalSeconds,
            initialBoard = initialBoard,
            steps = steps,
            finalBoard = solver.OutputString
        });
    }

    private static void OutputBruteForceSolve(Program program, Solver solver, DateTime start, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        string initialBoard = solver.OutputString;
        bool foundSolution = solver.FindSolution(
            multiThread: program.MultiThread,
            cancellationToken: cancellationToken,
            isRandom: program.SolveRandomBruteForce);
        
        stopwatch.Stop();
        DateTime finish = DateTime.UtcNow;
        OutputJson(new BruteForceSolveResult
        {
            startTimestamp = start,
            finishTimestamp = finish,
            duration = stopwatch.Elapsed.TotalSeconds,
            initialBoard = initialBoard,
            solution = foundSolution ? solver.OutputString : null,
            foundSolution = foundSolution
        });
    }

    private static void UpdateBoardFromCandidateCounts(Solver solver, long[] candidateCounts)
    {
        // Convert to board state
        uint[] board = new uint[solver.NUM_CELLS];
        for (int cellIndex = 0; cellIndex < solver.NUM_CELLS; cellIndex++)
        {
            for (int value = 1; value <= solver.MAX_VALUE; value++)
            {
                int candidateIndex = cellIndex * solver.MAX_VALUE + value - 1;
                if (candidateCounts[candidateIndex] > 0)
                {
                    board[cellIndex] |= SolverUtility.ValueMask(value);
                }
            }
        }
        for (int cellIndex = 0; cellIndex < solver.NUM_CELLS; cellIndex++)
        {
            int i = cellIndex / solver.WIDTH;
            int j = cellIndex % solver.WIDTH;
            _ = solver.SetMask(i, j, board[cellIndex]);
        }
    }

    private static void OutputJson<T>(T obj) where T : notnull
    {
        JsonResultContext context = JsonResultContext.Default;
        JsonTypeInfo<T> typeInfo = (JsonTypeInfo<T>)context.GetTypeInfo(typeof(T));
        Console.WriteLine(JsonSerializer.Serialize(obj, typeInfo));
    }
}
