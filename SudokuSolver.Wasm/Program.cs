using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SudokuSolver;

namespace SudokuSolver.Wasm;

#nullable enable

public partial class Program
{
    public static void Main()
    {
        Console.WriteLine("SudokuSolver Wasm Initialized");
    }

    [JSExport]
    public static string Solve(string jsonInput)
    {
        try
        {
            var input = JsonSerializer.Deserialize(jsonInput, WasmJsonContext.Default.SolverInput);
            if (input == null)
            {
                return ErrorJson("Invalid JSON input");
            }

            Solver solver;
            try
            {
                if (input.blankGridSize.HasValue)
                {
                    solver = SolverFactory.CreateBlank(input.blankGridSize.Value, input.constraints ?? []);
                }
                else if (!string.IsNullOrEmpty(input.blankWithR1))
                {
                    StringBuilder givens = new();
                    givens.Append(input.blankWithR1);
                    if (input.blankWithR1.Length <= 9)
                    {
                        for (int i = 0; i < input.blankWithR1.Length * (input.blankWithR1.Length - 1); i++)
                        {
                            givens.Append('0');
                        }
                    }
                    else
                    {
                        int gridSize = input.blankWithR1.Length / 2;
                        for (int i = 0; i < gridSize * (gridSize - 1); i++)
                        {
                            givens.Append('0');
                            givens.Append('0');
                        }
                    }
                    solver = SolverFactory.CreateFromGivens(givens.ToString(), input.constraints ?? []);
                }
                else if (!string.IsNullOrEmpty(input.givens))
                {
                    solver = SolverFactory.CreateFromGivens(input.givens, input.constraints ?? []);
                }
                else if (!string.IsNullOrEmpty(input.fpuzzlesURL))
                {
                    solver = SolverFactory.CreateFromFPuzzles(input.fpuzzlesURL, input.constraints ?? []);
                }
                else if (!string.IsNullOrEmpty(input.candidates))
                {
                    solver = SolverFactory.CreateFromCandidates(input.candidates, input.constraints ?? []);
                }
                else
                {
                    return ErrorJson("No puzzle definition provided (givens, blank, fpuzzlesURL, etc.)");
                }
            }
            catch (Exception ex)
            {
                return ErrorJson($"Error creating solver: {ex.Message}");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            CancellationToken cancellationToken = CancellationToken.None; // TODO: Support cancellation

            if (input.solutionCount || input.check)
            {
                return HandleSolutionCount(input, solver, stopwatch, cancellationToken);
            }
            if (input.trueCandidates)
            {
                return HandleTrueCandidates(input, solver, stopwatch, cancellationToken);
            }
            if (input.logicalSolve)
            {
                return HandleLogicalSolve(input, solver, stopwatch, cancellationToken);
            }
            if (input.bruteForceSolve)
            {
                return HandleBruteForceSolve(input, solver, stopwatch, cancellationToken);
            }
            if (input.estimateCount)
            {
                return HandleEstimate(input, solver, stopwatch, cancellationToken);
            }

            return ErrorJson("No operation specified");
        }
        catch (Exception ex)
        {
            return ErrorJson($"Unhandled exception: {ex.Message}");
        }
    }

    private static string HandleSolutionCount(SolverInput input, Solver solver, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        List<string> solutions = [];
        Action<Solver>? solutionEvent = null;
        
        long count = solver.CountSolutions(
            maxSolutions: input.check ? 2 : (input.maxSolutions ?? 0),
            multiThread: false,
            progressEvent: null,
            solutionEvent: solutionEvent,
            cancellationToken: cancellationToken);
        
        stopwatch.Stop();
        
        return JsonSerializer.Serialize(new SolutionCountResult
        {
            duration = stopwatch.Elapsed.TotalSeconds,
            count = count
        }, WasmJsonContext.Default.SolutionCountResult);
    }

    private static string HandleTrueCandidates(SolverInput input, Solver solver, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        long[] trueCandidateCounts = solver.TrueCandidates(
            multiThread: false,
            numSolutionsCap: input.maxSolutions ?? 1,
            progressEvent: null,
            cancellationToken: cancellationToken);
        
        stopwatch.Stop();
        
        UpdateBoardFromCandidateCounts(solver, trueCandidateCounts);
        
        return JsonSerializer.Serialize(new TrueCandidatesResult
        {
            duration = stopwatch.Elapsed.TotalSeconds,
            board = solver.OutputString,
            candidateCounts = trueCandidateCounts
        }, WasmJsonContext.Default.TrueCandidatesResult);
    }

    private static string HandleLogicalSolve(SolverInput input, Solver solver, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        List<LogicalStepInfo> steps = [];
        string initialBoard = solver.OutputString;

        while (!cancellationToken.IsCancellationRequested)
        {
            List<LogicalStepDesc> stepDescs = [];
            string beforeState = solver.OutputString;
            LogicResult result = solver.StepLogic(stepDescs, cancellationToken);
            
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
        
        return JsonSerializer.Serialize(new LogicalSolveResult
        {
            duration = stopwatch.Elapsed.TotalSeconds,
            initialBoard = initialBoard,
            steps = steps,
            finalBoard = solver.OutputString
        }, WasmJsonContext.Default.LogicalSolveResult);
    }

    private static string HandleBruteForceSolve(SolverInput input, Solver solver, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        string initialBoard = solver.OutputString;
        bool foundSolution = solver.FindSolution(
            multiThread: false,
            cancellationToken: cancellationToken,
            isRandom: input.solveRandom);
        
        stopwatch.Stop();
        
        return JsonSerializer.Serialize(new BruteForceSolveResult
        {
            duration = stopwatch.Elapsed.TotalSeconds,
            initialBoard = initialBoard,
            solution = foundSolution ? solver.OutputString : null,
            foundSolution = foundSolution
        }, WasmJsonContext.Default.BruteForceSolveResult);
    }

    private static string HandleEstimate(SolverInput input, Solver solver, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        double lastEstimate = 0, lastStderr = 0;
        long lastIterations = 0;
        const double z95 = 1.96;
        
        Action<(double estimate, double stderr, long iterations)> progressEvent = (progressData) =>
        {
            lastEstimate = progressData.estimate;
            lastStderr = progressData.stderr;
            lastIterations = progressData.iterations;
        };

        solver.EstimateSolutions(
            numIterations: input.estimateIterations ?? 10000,
            progressEvent: progressEvent,
            multiThread: false,
            cancellationToken: cancellationToken);
        
        stopwatch.Stop();
        
        double lowerFinal = lastEstimate - z95 * lastStderr;
        double upperFinal = lastEstimate + z95 * lastStderr;
        double relErrPercentFinal = lastEstimate != 0 ? 100.0 * (z95 * lastStderr) / lastEstimate : 0.0;
        
        return JsonSerializer.Serialize(new EstimateResult
        {
            duration = stopwatch.Elapsed.TotalSeconds,
            estimate = lastEstimate,
            stderr = lastStderr,
            iterations = lastIterations,
            ci95_lower = lowerFinal,
            ci95_upper = upperFinal,
            relErrPercent = relErrPercentFinal
        }, WasmJsonContext.Default.EstimateResult);
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

    private static string ErrorJson(string message)
    {
        return JsonSerializer.Serialize(new ErrorResult
        {
            error = message,
            duration = 0
        }, WasmJsonContext.Default.ErrorResult);
    }
}
