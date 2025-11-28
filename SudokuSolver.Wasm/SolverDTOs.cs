using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SudokuSolver.Wasm;

#nullable enable

public class SolverInput
{
    public int? blankGridSize { get; set; }
    public string? blankWithR1 { get; set; }
    public string? givens { get; set; }
    public string? candidates { get; set; }
    public string? fpuzzlesURL { get; set; }
    public string[]? constraints { get; set; }
    
    // Operations
    public bool solutionCount { get; set; }
    public bool trueCandidates { get; set; }
    public bool logicalSolve { get; set; }
    public bool bruteForceSolve { get; set; }
    public bool estimateCount { get; set; }
    
    // Options
    public long? maxSolutions { get; set; }
    public long? estimateIterations { get; set; }
    public bool solveRandom { get; set; }
    public bool check { get; set; }
}

public class ErrorResult
{
    public string type { get; set; } = "error";
    public double duration { get; set; }
    public string? error { get; set; }
}

public class SolutionCountResult
{
    public string type { get; set; } = "solutionCount";
    public double duration { get; set; }
    public long count { get; set; }
}

public class SolutionListResult
{
    public string type { get; set; } = "solutionList";
    public double duration { get; set; }
    public long count { get; set; }
    public List<string>? solutions { get; set; }
}

public class TrueCandidatesResult
{
    public string type { get; set; } = "trueCandidates";
    public double duration { get; set; }
    public string? board { get; set; }
    public long[]? candidateCounts { get; set; }
}

public class LogicalStepInfo
{
    public string? description { get; set; }
    public string? beforeState { get; set; }
    public string? afterState { get; set; }
}

public class LogicalSolveResult
{
    public string type { get; set; } = "logicalSolve";
    public double duration { get; set; }
    public string? initialBoard { get; set; }
    public List<LogicalStepInfo>? steps { get; set; }
    public string? finalBoard { get; set; }
}

public class BruteForceSolveResult
{
    public string type { get; set; } = "bruteForceSolve";
    public double duration { get; set; }
    public string? initialBoard { get; set; }
    public string? solution { get; set; }
    public bool foundSolution { get; set; }
}

public class EstimateResult
{
    public string type { get; set; } = "estimate";
    public double duration { get; set; }
    public double estimate { get; set; }
    public double stderr { get; set; }
    public long iterations { get; set; }
    public double ci95_lower { get; set; }
    public double ci95_upper { get; set; }
    public double relErrPercent { get; set; }
}

[JsonSerializable(typeof(SolverInput))]
[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(SolutionCountResult))]
[JsonSerializable(typeof(SolutionListResult))]
[JsonSerializable(typeof(TrueCandidatesResult))]
[JsonSerializable(typeof(LogicalSolveResult))]
[JsonSerializable(typeof(BruteForceSolveResult))]
[JsonSerializable(typeof(EstimateResult))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<LogicalStepInfo>))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
internal partial class WasmJsonContext : JsonSerializerContext
{
}
