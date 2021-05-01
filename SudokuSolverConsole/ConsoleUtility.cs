using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SudokuSolver;
using static SudokuSolver.SolverUtility;

namespace SudokuSolverConsole
{
    static class ConsoleUtility
    {
        public static void Print(this Solver board)
        {
            Print(board, Console.Out);
            Console.WriteLine(board.GivenString);
        }

        public static void PrintBoardSimple(this Solver board)
        {
            PrintBoardSimple(board, Console.Out);
        }

        public static void PrintBoardSimple(this Solver board, TextWriter textWriter)
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

        public static void PrintBoardSingleLine(this Solver board)
        {
            PrintBoardSingleLine(board.Board, Console.Out);
        }

        public static void PrintBoardSingleLine(this Solver board, TextWriter textWriter)
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

        public static void Print(this Solver board, TextWriter writer)
        {
            PrintBoard(board.Board, board.Regions, writer);
        }

        public static void PrintBoard(uint[,] board, int[,] regions, TextWriter writer)
        {
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int line = 0; line < 6; line++)
                {
                    for (int j = 0; j <= WIDTH; j++)
                    {
                        if (line == 0)
                        {
                            if (i == 0)
                            {
                                if (j == 0)
                                {
                                    writer.Write("╔═════");
                                }
                                else if (j == WIDTH)
                                {
                                    writer.Write("╗");
                                }
                                else if (regions[i, j - 1] != regions[i, j])
                                {
                                    writer.Write("╦═════");
                                }
                                else
                                {
                                    writer.Write("╤═════");
                                }
                            }
                            else if (j == WIDTH)
                            {
                                if (regions[i - 1, WIDTH - 1] != regions[i, WIDTH - 1])
                                {
                                    writer.Write("╣");
                                }
                                else
                                {
                                    writer.Write("╢");
                                }
                            }
                            else if (regions[i - 1, j] == regions[i, j])
                            {
                                if (j == 0)
                                {
                                    writer.Write("╟");
                                    writer.Write("─────", writer, ConsoleColor.Gray);
                                }
                                else
                                {
                                    if (regions[i, j - 1] != regions[i, j])
                                    {
                                        writer.Write("╫");
                                        writer.Write("─────", writer, ConsoleColor.Gray);
                                    }
                                    else
                                    {
                                        writer.Write("┼─────", writer, ConsoleColor.Gray);
                                    }
                                }
                            }
                            else
                            {
                                if (j == 0)
                                {
                                    writer.Write("╠═════");
                                }
                                else
                                {
                                    if (regions[i, j - 1] != regions[i, j])
                                    {
                                        writer.Write("╬═════");
                                    }
                                    else
                                    {
                                        writer.Write("╪═════");
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (j == WIDTH)
                            {
                                writer.Write("║");
                                continue;
                            }

                            if (j == 0 || regions[i, j - 1] != regions[i, j])
                            {
                                writer.Write("║");
                            }
                            else
                            {
                                Write("│", writer, ConsoleColor.Gray);
                            }

                            if (!IsValueSet(board[i, j]))
                            {
                                if (line == 2 || line == 4)
                                {
                                    writer.Write("     ");
                                }
                                else
                                {
                                    string s = "";
                                    for (int x = 0; x < 3; x++)
                                    {
                                        int value = ((line - 1) / 2) * 3 + x + 1;
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
                                Write(bigNumbers[value - 1][line - 1], writer, ConsoleColor.DarkCyan);
                            }
                        }
                    }
                    writer.WriteLine();
                }
            }
            writer.Write("╚═════");
            for (int j = 1; j < WIDTH; j++)
            {
                if (regions[HEIGHT - 1, j - 1] != regions[HEIGHT - 1, j])
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
