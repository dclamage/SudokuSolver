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
            int digitWidth = (height >= 10 || width >= 10) ? 2 : 1;
            string nonGiven = new('?', digitWidth);
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    uint mask = board[i, j];
                    if (Solver.IsValueSet(mask))
                    {
                        int v = Solver.GetValue(mask);
                        if (v <= 9)
                        {
                            textWriter.Write('0');
                        }
                        textWriter.Write(v);
                    }
                    else
                    {
                        textWriter.Write(nonGiven);
                    }
                }
            }
            textWriter.WriteLine();
        }

        private static readonly string[][] bigNumbers = new string[][]
        {
            // 0
            new string[] {
                "█████",
                "█   █",
                "█   █",
                "█   █",
                "█████",
            },
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
            int HEIGHT = board.GetLength(0);
            int WIDTH = board.GetLength(1);
            uint allValuesMask = 0;
            for (int i = 0; i < HEIGHT; i++)
            {
                for (int j = 0; j < WIDTH; j++)
                {
                    allValuesMask |= board[i, j];
                }
            }
            allValuesMask &= ~valueSetMask;
            int maxValue = MaxValue(allValuesMask);
            int cellSize = (int)Math.Sqrt(maxValue);
            if (cellSize * cellSize != maxValue)
            {
                cellSize++;
            }
            cellSize = Math.Max(cellSize, 3);
            int numberSize = maxValue > 9 ? 2 : 1;
            int innerCellSize = cellSize * numberSize + cellSize - 1;
            int bigNumTotalPad1 = innerCellSize - 5;
            string bigNumLeftPad1 = new(' ', bigNumTotalPad1 / 2);
            string bugNumRightPad1 = new(' ', bigNumTotalPad1 - bigNumLeftPad1.Length);
            int bigNumTotalPad2 = Math.Max(0, innerCellSize - 11);
            string bigNumLeftPad2 = new(' ', bigNumTotalPad2 / 2);
            string bugNumRightPad2 = new(' ', bigNumTotalPad2 - bigNumLeftPad2.Length);

            int innerCellHeight = cellSize * 2 - 1;
            int bigNumTotalVPad = innerCellHeight - 5;
            int bigNumTopVPad = bigNumTotalVPad / 2;
            int bigNumBotVPad = bigNumTotalVPad - bigNumTopVPad;

            for (int i = 0; i < HEIGHT; i++)
            {
                for (int line = 0; line < cellSize * 2; line++)
                {
                    for (int j = 0; j < WIDTH; j++)
                    {
                        bool thickV = i == 0 || regions[i - 1, j] != regions[i, j];
                        bool thickH = j == 0 || regions[i, j - 1] != regions[i, j];

                        if (line == 0)
                        {
                            bool drawThickLine = false;
                            bool drawThinLine = false;
                            if (i == 0)
                            {
                                if (j == 0)
                                {
                                    writer.Write('╔');
                                    drawThickLine = true;
                                }
                                else if (j == WIDTH)
                                {
                                    writer.Write("╗");
                                }
                                else if (thickH)
                                {
                                    writer.Write('╦');
                                    drawThickLine = true;
                                }
                                else
                                {
                                    writer.Write('╤');
                                    drawThickLine = true;
                                }
                            }
                            else if (!thickV)
                            {
                                if (j == 0)
                                {
                                    writer.Write("╟");
                                    drawThinLine = true;
                                }
                                else
                                {
                                    if (thickH)
                                    {
                                        writer.Write("╫");
                                        drawThinLine = true;
                                    }
                                    else
                                    {
                                        Write("┼", writer, ConsoleColor.Gray);
                                        drawThinLine = true;
                                    }
                                }
                            }
                            else
                            {
                                if (j == 0)
                                {
                                    writer.Write('╠');
                                    drawThickLine = true;
                                }
                                else
                                {
                                    if (thickH)
                                    {
                                        writer.Write('╬');
                                        drawThickLine = true;
                                    }
                                    else
                                    {
                                        writer.Write('╪');
                                        drawThickLine = true;
                                    }
                                }
                            }
                            if (drawThickLine)
                            {
                                writer.Write(new string('═', innerCellSize));
                            }
                            else if (drawThinLine)
                            {
                                Write(new string('─', innerCellSize), writer, ConsoleColor.Gray);
                            }
                        }
                        else
                        {
                            if (thickH)
                            {
                                writer.Write('║');
                            }
                            else
                            {
                                Write("│", writer, ConsoleColor.Gray);
                            }

                            if (!IsValueSet(board[i, j]))
                            {
                                if ((line % 2) == 0)
                                {
                                    writer.Write(new string(' ', innerCellSize));
                                }
                                else
                                {
                                    string s = "";
                                    for (int x = 0; x < cellSize; x++)
                                    {
                                        int value = ((line - 1) / 2) * cellSize + x + 1;
                                        if (value <= maxValue && HasValue(board[i, j], value))
                                        {
                                            if (value <= 9 && numberSize > 1)
                                            {
                                                s += new string(' ', numberSize - 1);
                                            }
                                            s += value.ToString();
                                        }
                                        else
                                        {
                                            s += new string(' ', numberSize);
                                        }
                                        if (x != cellSize - 1)
                                        {
                                            s += " ";
                                        }
                                    }
                                    writer.Write(s);
                                }
                            }
                            else 
                            {
                                int bigNumLine = line - 1 - bigNumTopVPad;
                                if (bigNumLine < 0 || bigNumLine >= bigNumbers[0].Length)
                                {
                                    writer.Write(new string(' ', innerCellSize));
                                }
                                else
                                {
                                    int value = GetValue(board[i, j]);
                                    if (value <= 9)
                                    {
                                        writer.Write(bigNumLeftPad1);
                                        Write(bigNumbers[value][bigNumLine], writer, ConsoleColor.DarkCyan);
                                        writer.Write(bugNumRightPad1);
                                    }
                                    else
                                    {
                                        int value0 = value / 10;
                                        int value1 = value % 10;

                                        writer.Write(bigNumLeftPad2);
                                        Write(bigNumbers[value0][bigNumLine], writer, ConsoleColor.DarkCyan);
                                        writer.Write(' ');
                                        Write(bigNumbers[value1][bigNumLine], writer, ConsoleColor.DarkCyan);
                                        writer.Write(bugNumRightPad2);
                                    }
                                }
                            }
                        }
                    }

                    if (line == 0)
                    {
                        if (i == 0)
                        {
                            writer.WriteLine("╗");
                        }
                        else if (regions[i - 1, WIDTH - 1] != regions[i, WIDTH - 1])
                        {
                            writer.WriteLine("╣");
                        }
                        else
                        {
                            writer.WriteLine("╢");
                        }
                    }
                    else
                    {
                        writer.WriteLine("║");
                    }
                }
            }

            writer.Write('╚');
            writer.Write(new string('═', innerCellSize));
            for (int j = 1; j < WIDTH; j++)
            {
                if (regions[HEIGHT - 1, j - 1] != regions[HEIGHT - 1, j])
                {
                    writer.Write('╩');
                }
                else
                {
                    writer.Write('╧');
                }
                writer.Write(new string('═', innerCellSize));
            }
            writer.WriteLine("╝");
            writer.WriteLine();
            writer.Flush();
        }
    }
}
