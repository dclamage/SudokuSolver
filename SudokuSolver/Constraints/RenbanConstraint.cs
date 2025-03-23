﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using static SudokuSolver.SolverUtility;

namespace SudokuSolver.Constraints
{
    [Constraint(DisplayName = "Renban", ConsoleName = "renban")]
    public class RenbanConstraint : Constraint
    {
        public readonly List<(int, int)> cells;
        private readonly HashSet<(int, int)> cellsSet;

        public RenbanConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
        {
            var cellGroups = ParseCells(options);
            if (cellGroups.Count != 1)
            {
                throw new ArgumentException($"Renban constraint expects 1 cell group, got {cellGroups.Count}.");
            }

            cells = cellGroups[0];
            cellsSet = new(cells);

            if (cells.Count > MAX_VALUE)
            {
                throw new ArgumentException($"Renban can only contain up to {MAX_VALUE} cells, but {cells.Count} were provided.");
            }
        }

        public override string SpecificName => $"Renban from {CellName(cells[0])} - {CellName(cells[^1])}";

        // Digits cannot repeat on a renban line
        public override List<(int, int)> Group => cells;

        public override LogicResult InitCandidates(Solver sudokuSolver)
        {
            int numCells = cells.Count;
            if (numCells <= 1 || numCells >= MAX_VALUE)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;
            uint allValsMask = 0;
            foreach (var cell in cells)
            {
                allValsMask |= board[cell.Item1, cell.Item2];
            }
            allValsMask &= ~valueSetMask;

            uint[] newCellMasks = new uint[numCells];
            int maxStartVal = MAX_VALUE - numCells + 1;
            for (int startVal = 1; startVal <= maxStartVal; startVal++)
            {
                int endVal = startVal + numCells - 1;
                uint rangeMask = MaskBetweenInclusive(startVal, endVal);
                if ((rangeMask & allValsMask) != rangeMask)
                {
                    continue;
                }

                for (int i = 0; i < numCells; i++)
                {
                    newCellMasks[i] |= rangeMask;
                }
            }

            bool changed = false;
            for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
            {
                var cell = cells[cellIndex];
                var keepResult = sudokuSolver.KeepMask(cell.Item1, cell.Item2, newCellMasks[cellIndex]);
                if (keepResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }
                changed |= keepResult == LogicResult.Changed;
            }
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
        {
            int numCells = cells.Count;
            if (numCells <= 1 || numCells >= MAX_VALUE)
            {
                return true;
            }

            if (cellsSet.Contains((i, j)))
            {
                var board = sudokuSolver.Board;
                uint setValsMask = 0;
                for (int cellIndex = 0; cellIndex < numCells; cellIndex++)
                {
                    var curCell = cells[cellIndex];
                    uint mask = board[curCell.Item1, curCell.Item2];
                    if (ValueCount(mask) == 1)
                    {
                        setValsMask |= mask;
                    }
                }
                setValsMask &= ~valueSetMask;

                int minVal = MinValue(setValsMask);
                int maxVal = MaxValue(setValsMask);
                int rangeUsed = maxVal - minVal + 1;
                if (rangeUsed > numCells)
                {
                    return false;
                }

                int numCellsRemaining = numCells - rangeUsed;
                int minAllowed = Math.Max(minVal - numCellsRemaining, 1);
                int maxAllowed = Math.Min(maxVal + numCellsRemaining, MAX_VALUE);
                uint keepMask = MaskBetweenInclusive(minAllowed, maxAllowed);

                for (int ti = 0; ti < numCells; ti++)
                {
                    var cell = cells[ti];
                    var keepResult = sudokuSolver.KeepMask(cell.Item1, cell.Item2, keepMask);
                    if (keepResult == LogicResult.Invalid)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
        {
            int numCells = cells.Count;
            if (numCells <= 1 || numCells >= MAX_VALUE)
            {
                return LogicResult.None;
            }

            var board = sudokuSolver.Board;

            uint allValsMask = 0;
            uint setValsMask = 0;
            foreach (var cell in cells)
            {
                uint mask = board[cell.Item1, cell.Item2];
                if (ValueCount(mask) == 1)
                {
                    setValsMask |= mask;
                }
                allValsMask |= mask;
            }
            setValsMask &= ~valueSetMask;
            allValsMask &= ~valueSetMask;
            int minAllVal = MinValue(allValsMask);
            int maxAllVal = MaxValue(allValsMask);

            uint[] clearedMasks = null;
            List<(int, int)> unsetCells = cells.Where(cell => ValueCount(board[cell.Item1, cell.Item2]) > 1).ToList();
            int numUnsetCells = unsetCells.Count;
            if (numUnsetCells > 0)
            {
                // Ensure candidates are in range of the known values
                if (setValsMask != 0)
                {
                    int minVal = MinValue(setValsMask);
                    int maxVal = MaxValue(setValsMask);
                    int rangeUsed = maxVal - minVal + 1;
                    if (rangeUsed > numCells)
                    {
                        logicalStepDescription?.Append($"Value range of set values {minVal} to {maxVal} is too large.");
                        return LogicResult.Invalid;
                    }

                    int numCellsRemaining = numCells - rangeUsed;
                    int minAllowed = Math.Max(minVal - numCellsRemaining, 1);
                    int maxAllowed = Math.Min(maxVal + numCellsRemaining, MAX_VALUE);
                    uint keepMask = MaskBetweenInclusive(minAllowed, maxAllowed);
                    uint clearMask = ~keepMask & ALL_VALUES_MASK;
                    if ((allValsMask & clearMask) != 0)
                    {
                        for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                        {
                            var cell = unsetCells[cellIndex];
                            uint cellMask = board[cell.Item1, cell.Item2];
                            uint curClearMask = cellMask & clearMask;
                            if (curClearMask != 0)
                            {
                                var clearResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, curClearMask);
                                if (clearResult == LogicResult.Invalid)
                                {
                                    logicalStepDescription?.Append($"{CellName(cell)} has no more valid candidates.");
                                    return LogicResult.Invalid;
                                }
                                if (clearResult == LogicResult.Changed)
                                {
                                    if (clearedMasks == null)
                                    {
                                        clearedMasks = new uint[numUnsetCells];
                                    }
                                    clearedMasks[cellIndex] |= curClearMask;
                                }
                            }
                        }
                    }
                }

                // Look for missing values
                uint[] newCellMasks = new uint[numUnsetCells];
                int maxStartVal = maxAllVal - numCells + 1;
                for (int startVal = minAllVal; startVal <= maxStartVal; startVal++)
                {
                    int endVal = startVal + numCells - 1;
                    uint rangeMask = MaskBetweenInclusive(startVal, endVal);
                    if ((rangeMask & allValsMask) != rangeMask)
                    {
                        continue;
                    }
                    rangeMask &= ~setValsMask;
                    if (ValueCount(rangeMask) != unsetCells.Count)
                    {
                        continue;
                    }

                    for (int i = 0; i < numUnsetCells; i++)
                    {
                        newCellMasks[i] |= rangeMask;
                    }
                }

                for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                {
                    var cell = unsetCells[cellIndex];
                    uint cellMask = board[cell.Item1, cell.Item2];
                    uint clearMask = ~newCellMasks[cellIndex] & ALL_VALUES_MASK & cellMask;
                    if (clearMask != 0)
                    {
                        var clearResult = sudokuSolver.ClearMask(cell.Item1, cell.Item2, clearMask);
                        if (clearResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"{CellName(cell)} has no more valid candidates.");
                            return LogicResult.Invalid;
                        }
                        if (clearResult == LogicResult.Changed)
                        {
                            if (clearedMasks == null)
                            {
                                clearedMasks = new uint[numUnsetCells];
                            }
                            clearedMasks[cellIndex] |= clearMask;
                        }
                    }
                }

                if (clearedMasks != null)
                {
                    if (logicalStepDescription != null)
                    {
                        logicalStepDescription.Append($"Cleared values");
                        bool first = true;
                        for (int cellIndex = 0; cellIndex < numUnsetCells; cellIndex++)
                        {
                            if (clearedMasks[cellIndex] != 0)
                            {
                                var cell = unsetCells[cellIndex];
                                if (!first)
                                {
                                    logicalStepDescription.Append(';');
                                }
                                logicalStepDescription.Append($" {MaskToString(clearedMasks[cellIndex])} from {CellName(cell)}");
                                first = false;
                            }
                        }
                    }
                    return LogicResult.Changed;
                }
            }

            return LogicResult.None;
        }

        public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value)
        {
            int numCells = cells.Count;
            if (numCells <= 1)
            {
                return null;
            }

            var board = sudokuSolver.Board;
            uint valueMask = ValueMask(value);
            var cellsWithValue = cells.Where(cell => (board[cell.Item1, cell.Item2] & valueMask) != 0).ToList();
            if (cellsWithValue.Count == 0)
            {
                return null;
            }

            if (numCells >= MAX_VALUE)
            {
                return cellsWithValue.Count != 0 ? cellsWithValue : null;
            }

            if (cellsWithValue.Count == 1)
            {
                var cell = cellsWithValue[0];
                if (ValueCount(board[cell.Item1, cell.Item2]) == 1)
                {
                    return cellsWithValue;
                }
            }

            uint allValsMask = 0;
            uint setValsMask = 0;
            foreach (var cell in cells)
            {
                uint mask = board[cell.Item1, cell.Item2];
                if (ValueCount(mask) == 1)
                {
                    setValsMask |= mask;
                }
                allValsMask |= mask;
            }
            allValsMask &= ~valueSetMask;
            setValsMask &= ~valueSetMask;

            List<(int, int)> unsetCells = cells.Where(cell => ValueCount(board[cell.Item1, cell.Item2]) > 1).ToList();
            int numUnsetCells = unsetCells.Count;
            if (numUnsetCells > 0)
            {
                int minVal = MinValue(allValsMask);
                int maxVal = MaxValue(allValsMask);
                int maxStartVal = maxVal - numCells + 1;
                for (int startVal = minVal; startVal <= maxStartVal; startVal++)
                {
                    int endVal = startVal + numCells - 1;

                    // Only need to check ranges which do not contain the desired value
                    if (startVal <= value && value <= endVal)
                    {
                        continue;
                    }

                    uint rangeMask = MaskBetweenInclusive(startVal, endVal);
                    if ((allValsMask & rangeMask) != rangeMask)
                    {
                        continue;
                    }
                    rangeMask &= ~setValsMask;

                    List<int> rangeVals = new(endVal - startVal + 1);
                    for (int v = startVal; v <= endVal; v++)
                    {
                        if ((rangeMask & ValueMask(v)) != 0)
                        {
                            rangeVals.Add(v);
                        }
                    }

                    foreach (var permutation in rangeVals.Permuatations())
                    {
                        if (sudokuSolver.CanPlaceDigits(unsetCells, permutation))
                        {
                            return null;
                        }
                    }
                }
            }
            return cellsWithValue;
        }

        public override void InitLinks(Solver sudokuSolver)
        {
            if (cells.Count <= 1 || cells.Count >= MAX_VALUE)
            {
                return;
            }

            int numCells = cells.Count;
            foreach (var cell0 in cells)
            {
                int cellIndex0 = FlatIndex(cell0) * MAX_VALUE;
                foreach (var cell1 in cells)
                {
                    int cellIndex1 = FlatIndex(cell1) * MAX_VALUE;
                    if (cellIndex0 == cellIndex1)
                    {
                        continue;
                    }

                    for (int v0 = 1; v0 <= MAX_VALUE; v0++)
                    {
                        int candIndex0 = cellIndex0 + v0 - 1;
                        for (int v1 = 1; v1 <= MAX_VALUE; v1++)
                        {
                            if (Math.Abs(v1 - v0) >= numCells)
                            {
                                int candIndex1 = cellIndex1 + v1 - 1;
                                sudokuSolver.AddWeakLink(candIndex0, candIndex1);
                            }
                        }
                    }
                }
            }
        }
    }
}
