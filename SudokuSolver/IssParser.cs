using System.Text;

namespace SudokuSolver;

/// <summary>
/// Thrown when an ISS puzzle uses a constraint this solver has no equivalent for. Callers doing
/// bulk import are expected to catch this and skip the puzzle.
/// </summary>
public sealed class IssUnsupportedConstraintException(string constraintName)
    : NotSupportedException($"ISS constraint '{constraintName}' has no equivalent in this solver.")
{
    public string ConstraintName { get; } = constraintName;
}

/// <summary>
/// Parses the Interactive Sudoku Solver text format.
/// </summary>
/// <remarks>
/// The format is one directive per line, each starting with '.', with '~'-separated arguments:
/// <code>
/// .Shape~6x6
/// .~R4C2_5~R8C8_3            // givens; R1C1_1_2_3 restricts a cell to those candidates
/// .Cage~10~R1C1~R1C2~R1C3    // leading argument, then cells
/// .Renban~R1C1~R1C2          // cells only
/// .AntiKnight                // no arguments
/// </code>
/// This exists to import the CTC puzzle index at https://sigh.github.io/iss-sudoku-index/, which
/// is a far larger and more varied corpus than anything maintained here, and which ships known
/// solution counts that can be used to validate the import.
///
/// Coverage is deliberately partial. ISS models many puzzles with a general constraint language
/// (Var/Or/And/Replicate/AllDifferent) that has no counterpart here; those raise
/// <see cref="IssUnsupportedConstraintException"/> rather than being silently mistranslated, which
/// would produce a solver that quietly disagrees with ISS.
/// </remarks>
public static class IssParser
{
    /// <summary>Builds a solver from ISS puzzle text.</summary>
    /// <exception cref="IssUnsupportedConstraintException">An unmapped constraint was present.</exception>
    public static Solver Parse(string issText, IEnumerable<string> additionalConstraints = null)
    {
        int size = 9;
        List<(int row, int col, int value)> givens = [];
        List<(int row, int col, List<int> values)> restrictions = [];
        List<string> constraints = [];

        foreach (string rawLine in issText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith('.'))
            {
                continue;
            }

            string[] parts = line[1..].Split('~');
            string name = parts[0];
            string[] args = parts[1..];

            if (name.Length == 0)
            {
                ParseGivens(args, givens, restrictions);
                continue;
            }

            if (name == "Shape")
            {
                size = ParseShape(args);
                continue;
            }

            constraints.Add(TranslateConstraint(name, args));
        }

        if (size > 9)
        {
            // Givens are passed as a one-character-per-cell string below, which cannot express
            // two-digit values.
            throw new IssUnsupportedConstraintException($"Shape~{size}x{size}");
        }

        var givenChars = new char[size * size];
        Array.Fill(givenChars, '0');
        foreach ((int row, int col, int value) in givens)
        {
            givenChars[row * size + col] = (char)('0' + value);
        }

        if (additionalConstraints != null)
        {
            constraints.AddRange(additionalConstraints);
        }

        Solver solver = SolverFactory.CreateFromGivens(new string(givenChars), constraints);

        foreach ((int row, int col, List<int> values) in restrictions)
        {
            uint mask = 0;
            foreach (int value in values)
            {
                mask |= SolverUtility.ValueMask(value);
            }
            if (solver.KeepMask(row, col, mask) == LogicResult.Invalid)
            {
                throw new ArgumentException($"ISS candidate restriction at r{row + 1}c{col + 1} left no candidates.");
            }
        }

        return solver;
    }

    private static int ParseShape(string[] args)
    {
        // "6x6", "9x9", …
        string shape = args.Length > 0 ? args[0] : "9x9";
        int x = shape.IndexOf('x');
        return x > 0 && int.TryParse(shape[..x], out int size) ? size : 9;
    }

    /// <summary>
    /// A givens directive is a list of <c>R{row}C{col}_{value}…</c> entries. A single trailing
    /// value is a given; several are a candidate restriction.
    /// </summary>
    private static void ParseGivens(
        string[] args,
        List<(int, int, int)> givens,
        List<(int, int, List<int>)> restrictions)
    {
        foreach (string arg in args)
        {
            string[] fields = arg.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            (int row, int col) = ParseCell(fields[0]);
            List<int> values = [];
            for (int i = 1; i < fields.Length; i++)
            {
                values.Add(int.Parse(fields[i]));
            }

            if (values.Count == 1)
            {
                givens.Add((row, col, values[0]));
            }
            else
            {
                restrictions.Add((row, col, values));
            }
        }
    }

    /// <summary>Parses "R4C2" into a zero-based (row, col).</summary>
    private static (int row, int col) ParseCell(string text)
    {
        int c = text.IndexOf('C', StringComparison.OrdinalIgnoreCase);
        if (text.Length < 4 || (text[0] != 'R' && text[0] != 'r') || c < 0)
        {
            throw new ArgumentException($"'{text}' is not a valid ISS cell reference.");
        }
        return (int.Parse(text[1..c]) - 1, int.Parse(text[(c + 1)..]) - 1);
    }

    /// <summary>
    /// Translates an arrow, rejecting the one shape this solver cannot express.
    /// </summary>
    /// <remarks>
    /// ISS allows the same cell to appear on a shaft more than once, and counts it once per
    /// occurrence — verified empirically: <c>.Arrow~R2C2~R2C1~R2C1~R1C1~R1C2</c> accepts 9 in the
    /// circle with 3 in r2c1, because r2c1 contributes twice. This solver's arrow has no way to
    /// double-count a cell, and de-duplicating would silently change the puzzle (that example
    /// would become 3+1+2=6 and lose its solution), so such arrows are refused.
    /// </remarks>
    private static string TranslateArrow(string[] args)
    {
        string[] shaft = args[1..];
        var seen = new HashSet<string>(shaft.Length);
        foreach (string cell in shaft)
        {
            if (!seen.Add(cell))
            {
                throw new IssUnsupportedConstraintException("Arrow (repeated shaft cell)");
            }
        }
        return $"arrow:{Cells(args[..1])};{Cells(shaft)}";
    }

    /// <summary>Renders ISS cell references as this solver's "r1c1r2c3" cell-group syntax.</summary>
    private static string Cells(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (string arg in args)
        {
            (int row, int col) = ParseCell(arg);
            _ = sb.Append($"r{row + 1}c{col + 1}");
        }
        return sb.ToString();
    }

    private static string TranslateConstraint(string name, string[] args) => name switch
    {
        // No arguments.
        "AntiKnight" => "knight:",
        "AntiKing" => "king:",

        // Cells only.
        "Thermo" => $"thermo:{Cells(args)}",
        "Palindrome" => $"palindrome:{Cells(args)}",
        "Renban" => $"renban:{Cells(args)}",
        "Zipper" => $"zipper:{Cells(args)}",
        "Entropic" => $"entrol:{Cells(args)}",
        "RegionSumLine" => $"rsl:{Cells(args)}",
        "DoubleArrow" => $"doublearrow:{Cells(args)}",
        "BetweenLine" => $"betweenline:{Cells(args)}",
        "Nabner" => $"nabner:{Cells(args)}",
        "Even" => $"even:{Cells(args)}",
        "Odd" => $"odd:{Cells(args)}",

        // Leading numeric argument, then cells, separated by ';'.
        "Cage" => $"killer:{args[0]};{Cells(args[1..])}",
        "Whisper" => $"whispers:{args[0]};{Cells(args[1..])}",

        // Orthogonal pair markers take the value with no separator: "1r1c1r1c2".
        "WhiteDot" => $"difference:1{Cells(args)}",
        "BlackDot" => $"ratio:2{Cells(args)}",
        "X" => $"sum:10{Cells(args)}",
        "V" => $"sum:5{Cells(args)}",

        // The bulb is the first cell; the rest form the shaft.
        "Arrow" => TranslateArrow(args),

        // Modular lines here are always mod 3; anything else would be mistranslated.
        "Modular" when args.Length > 0 && args[0] == "3" => $"modl:{Cells(args[1..])}",

        _ => throw new IssUnsupportedConstraintException(name),
    };
}
