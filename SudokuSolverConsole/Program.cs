using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mono.Options;
using SudokuSolver;
using LZStringCSharp;
using System.IO;
using static SudokuSolver.SolverUtility;
using System.Text;

namespace SudokuSolverConsole
{
    class Program
    {
        static readonly ConstraintManager constraintManager = new();

        static void Main(string[] args)
        {
            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

            bool showHelp = false;
            string fpuzzlesURL = null;
            string givens = null;
            int maxThreads = 0;
            List<string> constraints = new();
            bool solveLogically = false;
            bool solutionCount = false;
            bool check = false;
            bool trueCandidates = false;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "t|threads=", "The maximum number of threads to use when brute forcing.", (int t) => maxThreads = t },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "h|help", "Show this message and exit", h => showHelp = h != null },
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
                List<string> constraintNames = constraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
                constraintNames.Sort();
                foreach (var constraintName in constraintNames)
                {
                    Console.WriteLine($"\t{constraintName}");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(fpuzzlesURL) && string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Must provide either an f-puzzles URL or a givens string.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(fpuzzlesURL) && !string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Cannot provide both an f-puzzles URL and a givens string.");
                return;
            }

            Solver solver = new();
            if (!string.IsNullOrWhiteSpace(givens))
            {
                givens = givens.Trim();

                if (givens.Length != 81)
                {
                    Console.WriteLine($"ERROR: A givens string must be exactly 81 characters long (Provided length: {givens.Length}).");
                    return;
                }

                ApplyConstraints(solver, constraints);
                if (!solver.FinalizeConstraints())
                {
                    Console.WriteLine($"ERROR: The constraints are invalid (no solutions).");
                    return;
                }

                for (int i = 0; i < givens.Length; i++)
                {
                    char c = givens[i];
                    if (c >= '1' && c <= '9')
                    {
                        if (!solver.SetValue(i / 9, i % 9, c - '1'))
                        {
                            Console.WriteLine($"ERROR: Givens cause there to be no solutions.");
                            return;
                        }
                    }
                }
            }
            else
            {
                if (fpuzzlesURL.Contains("?load="))
                {
                    int trimStart = fpuzzlesURL.IndexOf("?load=") + "?load=".Length;
                    fpuzzlesURL = fpuzzlesURL[trimStart..];
                }

                string fpuzzlesJson = LZString.DecompressFromBase64(fpuzzlesURL);
                var fpuzzlesData = JsonSerializer.Deserialize<FPuzzlesBoard>(fpuzzlesJson);

                Console.WriteLine($"Imported \"{fpuzzlesData.title}\" by {fpuzzlesData.author}");

                if (fpuzzlesData.diagonalp)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "diagonal+", string.Empty);
                }
                if (fpuzzlesData.diagonaln)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "diagonal-", string.Empty);
                }
                if (fpuzzlesData.antiknight)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "antiknight", string.Empty);
                }
                if (fpuzzlesData.antiking)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "antiking", string.Empty);
                }
                if (fpuzzlesData.disjointgroups)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "disjointgroups", string.Empty);
                }
                if (fpuzzlesData.nonconsecutive)
                {
                    constraintManager.AddConstraintByFPuzzlesName(solver, "nonconsecutive", string.Empty);
                }

                if (fpuzzlesData.arrow != null)
                {
                    foreach (var arrow in fpuzzlesData.arrow)
                    {
                        foreach (var lines in arrow.lines)
                        {
                            // Construct the arrow string
                            StringBuilder cells = new();
                            foreach (string cell in arrow.cells)
                            {
                                cells.Append(cell);
                            }
                            cells.Append(';');
                            foreach (string cell in lines)
                            {
                                if (!arrow.cells.Contains(cell))
                                {
                                    cells.Append(cell);
                                }
                            }
                            constraintManager.AddConstraintByFPuzzlesName(solver, "arrow", cells.ToString());
                        }
                    }
                }

                if (fpuzzlesData.killercage != null)
                {
                    foreach (var cage in fpuzzlesData.killercage)
                    {
                        StringBuilder cells = new();
                        if (cage.value != null)
                        {
                            cells.Append(cage.value).Append(';');
                        }
                        foreach (string cell in cage.cells)
                        {
                            cells.Append(cell);
                        }
                        constraintManager.AddConstraintByFPuzzlesName(solver, "killercage", cells.ToString());
                    }
                }

                if (fpuzzlesData.littlekillersum != null)
                {
                    foreach (var lksum in fpuzzlesData.littlekillersum)
                    {
                        if (!string.IsNullOrWhiteSpace(lksum.value) && lksum.value != "0")
                        {
                            constraintManager.AddConstraintByFPuzzlesName(solver, "littlekillersum", $"{lksum.value};{lksum.cell};{lksum.direction}");
                        }
                    }
                }

                int i = 0;
                foreach (var row in fpuzzlesData.grid)
                {
                    int j = 0;
                    foreach (var val in row)
                    {
                        if (val.value > 0)
                        {
                            solver.SetValue(i, j, val.value);
                        }
                        j++;
                    }
                    i++;
                }
            }

            Print(solver);
        }

        record ConstraintDummy(string type, int v = 1);

        static void ApplyConstraints(Solver solver, List<string> constraints)
        {
            foreach (string constraint in constraints)
            {
                string name = constraint.Trim();
                string options = "";
                int optionsIndex = 0;
                if ((optionsIndex = constraint.IndexOf(':')) > 0)
                {
                    name = constraint[0..optionsIndex].Trim();
                    options = constraint[(optionsIndex + 1)..].Trim();
                }
                constraintManager.AddConstraintByName(solver, name, options);
            }
        }

        public static void Print(Solver board)
        {
            Print(board, Console.Out);
        }

        public static void PrintBoardSimple(Solver board)
        {
            PrintBoardSimple(board, Console.Out);
        }

        public static void PrintBoardSimple(Solver board, TextWriter textWriter)
        {
            PrintBoardSimple(board, textWriter);
        }

        public static void PrintBoardSimple(uint[,] board, TextWriter textWriter)
        {
            int height = board.GetLength(0);
            int width = board.GetLength(1);
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    uint mask = board[i, j];
                    if (Solver.IsValueSet(mask))
                    {
                        textWriter.Write((char)('0' + Solver.GetValue(mask)));
                    }
                    else
                    {
                        textWriter.Write('?');
                    }
                }
                textWriter.WriteLine();
            }
        }

        public static void PrintBoardSingleLine(Solver board)
        {
            PrintBoardSingleLine(board.Board, Console.Out);
        }

        public static void PrintBoardSingleLine(Solver board, TextWriter textWriter)
        {
            PrintBoardSingleLine(board.Board, textWriter);
        }

        public static void PrintBoardSingleLine(uint[,] board, TextWriter textWriter)
        {
            int height = board.GetLength(0);
            int width = board.GetLength(1);
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    uint mask = board[i, j];
                    if (Solver.IsValueSet(mask))
                    {
                        textWriter.Write((char)('0' + Solver.GetValue(mask)));
                    }
                    else
                    {
                        textWriter.Write('?');
                    }
                }
            }
            textWriter.WriteLine();
        }

        private static string[][] bigNumbers = new string[][]
        {
            // 1
            new string[] {
                " ██  ",
                "  █  ",
                "  █  ",
                "  █  ",
                "  █  ",
            },
            // 2
            new string[] {
                " ███ ",
                "█   █",
                "  █  ",
                " █   ",
                "█████",
            },
            // 3
            new string[] {
                "█████",
                "    █",
                " ████",
                "    █",
                "█████",
            },
            // 4
            new string[] {
                "█   █",
                "█   █",
                "█████",
                "    █",
                "    █",
            },
            // 5
            new string[] {
                "█████",
                "█    ",
                "████ ",
                "    █",
                "████ ",
            },
            // 6
            new string[] {
                "█████",
                "█    ",
                "█████",
                "█   █",
                "█████",
            },
            // 7
            new string[] {
                "████ ",
                "   █ ",
                "  █  ",
                " █   ",
                "█    ",
            },
            // 8
            new string[] {
                "█████",
                "█   █",
                " ███ ",
                "█   █",
                "█████",
            },
            // 9
            new string[] {
                "█████",
                "█   █",
                "█████",
                "    █",
                "█████",
            },
        };

        public static void Write(string s, TextWriter writer, ConsoleColor color)
        {
            bool isConsole = writer == Console.Out;
            if (isConsole)
            {
                Console.ForegroundColor = color;
            }
            writer.Write(s);
            if (isConsole)
            {
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        public static void Print(Solver board, TextWriter writer)
        {
            PrintBoard(board.Board, writer);
        }

        public static void PrintBoard(uint[,] board, TextWriter writer)
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                if (i == 0)
                {
                    writer.Write("╔═════");
                    for (int j = 1; j < WIDTH; j++)
                    {
                        if ((j % BOX_WIDTH) == 0)
                        {
                            writer.Write("╦═════");
                        }
                        else
                        {
                            writer.Write("╤═════");
                        }
                    }
                    writer.WriteLine("╗");
                }
                else if ((i % BOX_HEIGHT) != 0)
                {
                    writer.Write("╟");
                    writer.Write("─────", writer, ConsoleColor.Gray);
                    for (int j = 1; j < WIDTH; j++)
                    {
                        if ((j % BOX_WIDTH) == 0)
                        {
                            writer.Write("╫");
                            writer.Write("─────", writer, ConsoleColor.Gray);
                        }
                        else
                        {
                            writer.Write("┼─────", writer, ConsoleColor.Gray);
                        }
                    }
                    writer.WriteLine("╢");
                }
                else
                {
                    writer.Write("╠═════");
                    for (int j = 1; j < WIDTH; j++)
                    {
                        if ((j % BOX_WIDTH) == 0)
                        {
                            writer.Write("╬═════");
                        }
                        else
                        {
                            writer.Write("╪═════");
                        }
                    }
                    writer.WriteLine("╣");
                }
                for (int line = 0; line < 5; line++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        if ((j % BOX_WIDTH) == 0)
                        {
                            writer.Write("║");
                        }
                        else
                        {
                            Write("│", writer, ConsoleColor.Gray);
                        }

                        if (!IsValueSet(board[i, j]))
                        {
                            if (line == 1 || line == 3)
                            {
                                writer.Write("     ");
                            }
                            else
                            {
                                string s = "";
                                for (int x = 0; x < 3; x++)
                                {
                                    int value = (line / 2) * 3 + x + 1;
                                    if (HasValue(board[i, j], value))
                                    {
                                        s += (char)('0' + value);
                                    }
                                    else
                                    {
                                        s += " ";
                                    }
                                    if (x != 2)
                                    {
                                        s += " ";
                                    }
                                }
                                writer.Write(s);
                            }
                        }
                        else
                        {
                            int value = GetValue(board[i, j]);
                            Write(bigNumbers[value - 1][line], writer, ConsoleColor.DarkCyan);
                        }
                    }
                    writer.Write("║");
                    writer.WriteLine();
                }
            }
            writer.Write("╚═════");
            for (int j = 1; j < WIDTH; j++)
            {
                if ((j % BOX_WIDTH) == 0)
                {
                    writer.Write("╩═════");
                }
                else
                {
                    writer.Write("╧═════");
                }
            }
            writer.WriteLine("╝");
            writer.WriteLine();
            writer.Flush();
        }
    }
}
