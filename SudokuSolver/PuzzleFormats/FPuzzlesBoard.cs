using System.Text.Json.Serialization;

namespace SudokuSolver.PuzzleFormats;

static class FPuzzlesUtility
{
    private static readonly Regex parseCellRegex = new(@"R(\d+)C(\d+)");
    public static (int, int) ParseCell(string cell)
    {
        var match = parseCellRegex.Match(cell);
        if (!match.Success)
        {
            throw new Exception($"Cannot parse cell {cell}");
        }
        return (int.Parse(match.Groups[1].Value) - 1, int.Parse(match.Groups[2].Value) - 1);
    }
}

public class FPuzzlesBoard
{
    public int size { get; set; }
    public string title { get; set; }
    public string author { get; set; }
    public string ruleset { get; set; }
    public FPuzzlesGridEntry[][] grid { get; set; }
    [property: JsonPropertyName("diagonal+")] public bool diagonalp { get; set; }
    [property: JsonPropertyName("diagonal-")] public bool diagonaln { get; set; }
    public bool antiknight { get; set; }
    public bool antiking { get; set; }
    public bool disjointgroups { get; set; }
    public bool nonconsecutive { get; set; }
    public string[] negative { get; set; }
    public FPuzzlesArrowEntry[] arrow { get; set; }
    public FPuzzlesKillerCageEntry[] killercage { get; set; }
    public FPuzzlesKillerCageEntry[] cage { get; set; }
    public FPuzzlesLittleKillerSumEntry[] littlekillersum { get; set; }
    public FPuzzlesCell[] odd { get; set; }
    public FPuzzlesCell[] even { get; set; }
    public FPuzzlesCell[] minimum { get; set; }
    public FPuzzlesCell[] maximum { get; set; }
    public FPuzzlesCells[] rowindexer { get; set; }
    public FPuzzlesCells[] columnindexer { get; set; }
    public FPuzzlesCells[] boxindexer { get; set; }
    public FPuzzlesCells[] extraregion { get; set; }
    public FPuzzlesLines[] thermometer { get; set; }
    public FPuzzlesLines[] palindrome { get; set; }
    public FPuzzlesLines[] renban { get; set; }
    public FPuzzlesLines[] whispers { get; set; }
    public FPuzzlesLines[] regionsumline { get; set; }
    public FPuzzlesCells[] difference { get; set; }
    public FPuzzlesCells[] xv { get; set; }
    public FPuzzlesCells[] ratio { get; set; }
    public FPuzzlesClone[] clone { get; set; }
    public FPuzzlesQuadruple[] quadruple { get; set; }
    public FPuzzlesLines[] betweenline { get; set; }
    public FPuzzlesCell[] sandwichsum { get; set; }
    public FPuzzlesCell[] xsum { get; set; }
    public FPuzzlesCell[] skyscraper { get; set; }
    public FPuzzlesLines[] entropicline { get; set; }
    public FPuzzlesLines[] modularline { get; set; }
    public string[] disabledlogic { get; set; } = null;
    public string[] truecandidatesoptions { get; set; } = null;
};

public class FPuzzlesGridEntry
{
    public int value { get; set; }
    public bool given { get; set; }
    public int[] centerPencilMarks { get; set; }
    public int[] givenPencilMarks { get; set; }
    public int region { get; set; } = -1;
}

public class FPuzzlesArrowEntry
{
    public string[][] lines { get; set; }
    public string[] cells { get; set; }
}

public record FPuzzlesKillerCageEntry
{
    public string[] cells { get; set; }
    public string value { get; set; }
}

public record FPuzzlesLittleKillerSumEntry
{
    public string cell { get; set; }
    public string direction { get; set; }
    public string value { get; set; }
}

public record FPuzzlesCell
{
    public string cell { get; set; }
    public string value { get; set; }

    public void AddConstraint(Solver solver, Type constraintType)
    {
        if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out _) && !string.IsNullOrWhiteSpace(cell))
        {
            solver.AddConstraint(constraintType, $"{value}{cell}");
        }
    }
}

public record FPuzzlesCells
{
    public string[] cells { get; set; }
    public string value { get; set; } = "";
}

public record FPuzzlesLines
{
    public string[][] lines { get; set; }
}

public record FPuzzlesClone
{
    public string[] cells { get; set; }
    public string[] cloneCells { get; set; }
}

public record FPuzzlesQuadruple
{
    public string[] cells { get; set; }
    public int[] values { get; set; }
}

