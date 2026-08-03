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
        // Translation is deferred so that it never depends on ".Shape" having been seen yet: some
        // constraints (a quadruple's 2x2 footprint) need the grid size to be translated at all.
        List<(string name, string[] args)> directives = [];

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

            directives.Add((name, args));
        }

        foreach ((string name, string[] args) in directives)
        {
            constraints.Add(TranslateConstraint(name, args, size));
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

    /// <summary>Parses ".Shape~9x9" and validates the optional digit-set argument.</summary>
    /// <remarks>
    /// A second argument overrides the digit set: ".Shape~9x9~0-8" is a 9x9 whose cells hold 0–8,
    /// and ".Shape~6x6~9" a 6x6 whose cells hold 1–9. This solver's value range is always
    /// 1..<c>size</c>, so any other set has to be refused. Ignoring it looks harmless and is not:
    /// it silently produces a different puzzle, which is how ".Shape~9x9~0-8" was caught losing its
    /// only solution during a bulk import.
    /// </remarks>
    private static int ParseShape(string[] args)
    {
        // "6x6", "9x9", …
        string shape = args.Length > 0 ? args[0] : "9x9";
        int x = shape.IndexOf('x');
        int size = x > 0 && int.TryParse(shape[..x], out int parsed) ? parsed : 9;

        if (args.Length > 1 && args[1].Length > 0 && !IsDefaultValueSet(args[1], size))
        {
            throw new IssUnsupportedConstraintException($"Shape digit set '{args[1]}'");
        }

        return size;
    }

    /// <summary>True when an ISS digit-set spec ("9", "1-9") is exactly this solver's 1..size.</summary>
    private static bool IsDefaultValueSet(string spec, int size)
    {
        int dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return int.TryParse(spec, out int max) && max == size;
        }
        return int.TryParse(spec[..dash], out int min)
            && int.TryParse(spec[(dash + 1)..], out int rangeMax)
            && min == 1
            && rangeMax == size;
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
    /// <remarks>
    /// ISS also allows an auxiliary variable declared by <c>.Var</c> to appear anywhere a cell can,
    /// referenced as "V" plus the variable's name ("VD1", "VR"). Those are part of the general
    /// constraint DSL this solver has no counterpart for, so they are reported as unsupported —
    /// a puzzle using them is skipped by bulk import rather than failing it.
    /// </remarks>
    private static (int row, int col) ParseCell(string text)
    {
        if (text.Length > 1 && (text[0] == 'V' || text[0] == 'v'))
        {
            throw new IssUnsupportedConstraintException("Var (auxiliary variable reference)");
        }

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
        return $"arrow:{Cells(args[..1])};{DistinctCells("Arrow", args[1..])}";
    }

    /// <summary>
    /// Renders cells like <see cref="Cells"/>, but refuses a list that names the same cell twice.
    /// </summary>
    /// <remarks>
    /// ISS writes a closed loop by repeating the starting cell at the end, and lets a line revisit a
    /// cell in general. Whether that survives translation depends entirely on the constraint:
    ///
    /// <list type="bullet">
    /// <item>Constraints defined on a sliding window of neighbours — whispers, entropic, modular —
    /// translate correctly as-is, since the repeat only closes the window. Verified by exhaustive
    /// count (a closed whisper loop equals its four adjacent pairs) and by three index puzzles with
    /// repeated cells importing with the right solution count.</item>
    /// <item>Constraints defined over the line as a set or by position — renban, nabner, between
    /// lines, region-sum lines, thermometers — do not. A repeated cell makes this solver's version
    /// unsatisfiable: a renban loop counts 0 where the open line counts 5,640,192.</item>
    /// </list>
    ///
    /// So the second group refuses repeats rather than silently answering 0, the same way the arrow
    /// refuses a repeated shaft cell (ISS counts such a cell once per occurrence, which this
    /// solver's arrow cannot express, and de-duplicating would quietly change the puzzle).
    /// </remarks>
    private static string DistinctCells(string name, string[] args)
    {
        var seen = new HashSet<string>(args.Length);
        foreach (string cell in args)
        {
            if (!seen.Add(cell))
            {
                throw new IssUnsupportedConstraintException($"{name} (repeated cell)");
            }
        }
        return Cells(args);
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

    /// <summary>
    /// Translates a quadruple, whose ISS form names only the top-left cell of the 2x2 it covers.
    /// </summary>
    private static string TranslateQuad(string[] args, int size)
    {
        (int row, int col) = ParseCell(args[0]);
        if (row + 1 >= size || col + 1 >= size)
        {
            // A 2x2 that runs off the grid is a shape this solver's quadruple cannot hold.
            throw new IssUnsupportedConstraintException("Quad (2x2 extends past the grid)");
        }

        string cells = $"r{row + 1}c{col + 1}r{row + 1}c{col + 2}r{row + 2}c{col + 1}r{row + 2}c{col + 2}";
        return $"quad:{cells};{string.Join(';', args[1..])}";
    }

    /// <summary>
    /// Translates a sandwich clue, which ISS attaches to a whole row ("R6") or column ("C2").
    /// </summary>
    /// <remarks>
    /// This solver's sandwich takes a cell reference and infers the direction from which coordinate
    /// is out of range, so a row clue becomes "…r6c0" and a column clue "…r0c2". Both solvers use
    /// 1 and the maximum digit as the crusts.
    /// </remarks>
    private static string TranslateSandwich(string[] args)
    {
        string location = args[1];
        char axis = char.ToUpperInvariant(location[0]);
        if (!int.TryParse(location[1..], out int lineIndex))
        {
            throw new ArgumentException($"'{location}' is not a valid ISS sandwich location.");
        }

        return axis switch
        {
            'R' => $"sandwich:{args[0]}r{lineIndex}c0",
            'C' => $"sandwich:{args[0]}r0c{lineIndex}",
            _ => throw new IssUnsupportedConstraintException($"Sandwich location '{location}'"),
        };
    }

    private static string TranslateConstraint(string name, string[] args, int size) => name switch
    {
        // No arguments.
        "AntiKnight" => "knight:",
        "AntiKing" => "king:",
        // No two orthogonally adjacent cells may differ by one — the negative form of a white dot.
        "AntiConsecutive" => "difference:neg1",

        // Cells only, and positional or set-like, so a repeated cell has to be refused — see
        // DistinctCells.
        "Thermo" => $"thermo:{DistinctCells(name, args)}",
        "Palindrome" => $"palindrome:{DistinctCells(name, args)}",
        "Renban" => $"renban:{DistinctCells(name, args)}",
        "Zipper" => $"zipper:{DistinctCells(name, args)}",
        "RegionSumLine" => $"rsl:{DistinctCells(name, args)}",
        "DoubleArrow" => $"doublearrow:{DistinctCells(name, args)}",
        "BetweenLine" => $"betweenline:{DistinctCells(name, args)}",
        "Nabner" => $"nabner:{DistinctCells(name, args)}",
        // An arbitrary set of cells that must all differ, which is exactly an extra region.
        "AllDifferent" => $"extraregion:{DistinctCells(name, args)}",

        // Cells only, and a sliding window of neighbours, so a repeated cell translates correctly.
        "Entropic" => $"entrol:{Cells(args)}",

        // Cells only, and per-cell, so order and repeats are immaterial.
        "Even" => $"even:{Cells(args)}",
        "Odd" => $"odd:{Cells(args)}",

        // A strict inequality between two cells, which a two-cell thermometer expresses exactly.
        // ISS names the larger cell first; a thermometer ascends from its bulb, so the order flips.
        "GreaterThan" when args.Length == 2 => $"thermo:{Cells([args[1], args[0]])}",

        // ISS's slope: +1 is the ↗ diagonal, -1 the ↘ one. A bare ".Diagonal~" does not say which,
        // so it is refused rather than guessed at.
        "Diagonal" when args.Length > 0 && args[0] == "1" => "dpos:",
        "Diagonal" when args.Length > 0 && args[0] == "-1" => "dneg:",

        // Each cell indexes its own row/column: a value v in the cell says where v sits in that line.
        "Indexing" when args.Length > 1 && args[0] == "R" => $"rowindexer:{Cells(args[1..])}",
        "Indexing" when args.Length > 1 && args[0] == "C" => $"colindexer:{Cells(args[1..])}",

        // Leading numeric argument, then cells, separated by ';'. A cage is a set (distinct values
        // summing to the total), so like a renban it cannot hold a repeat; a whisper is a sliding
        // window, so it can.
        "Cage" => $"killer:{args[0]};{DistinctCells(name, args[1..])}",
        "Whisper" => $"whispers:{args[0]};{Cells(args[1..])}",

        // Orthogonal pair markers take the value with no separator: "1r1c1r1c2".
        "WhiteDot" => $"difference:1{Cells(args)}",
        "BlackDot" => $"ratio:2{Cells(args)}",
        "X" => $"sum:10{Cells(args)}",
        "V" => $"sum:5{Cells(args)}",

        // The bulb is the first cell; the rest form the shaft.
        "Arrow" => TranslateArrow(args),

        // Leading numeric argument, then a row or column reference rather than cells.
        "Sandwich" when args.Length > 1 => TranslateSandwich(args),

        // The named cell is the top-left of the 2x2; the rest are the required values.
        "Quad" when args.Length > 1 => TranslateQuad(args, size),

        // Modular lines here are always mod 3; anything else would be mistranslated.
        "Modular" when args.Length > 0 && args[0] == "3" => $"modl:{Cells(args[1..])}",

        _ => throw new IssUnsupportedConstraintException(name),
    };
}
