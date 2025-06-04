using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver.Constraints
{
    public abstract class EqualSumsConstraint : Constraint
    {
        private List<(int, int)> cells;
        private HashSet<(int, int)> cellsHash;
        private List<List<(int, int)>> cellGroups;
        private List<SumCellsHelper> sumCellsHelpers;

        protected EqualSumsConstraint(Solver solver, string options) : base(solver, options)
        {
        }

        protected abstract List<List<(int, int)>> GetCellGroups(Solver solver);

        public override LogicResult InitCandidates(Solver solver)
        {
            cellGroups = GetCellGroups(solver);
            if (cellGroups == null || cellGroups.Count <= 1)
            {
                return LogicResult.None;
            }

            cells = cellGroups.SelectMany(group => group).ToList();
            cellsHash = cells.ToHashSet();

            sumCellsHelpers = cellGroups.Select(group => new SumCellsHelper(solver, group)).ToList();
            List<int> possibleSums = PossibleSums(solver);
            if (possibleSums.Count == 0)
            {
                return LogicResult.Invalid;
            }

            bool changed = false;
            foreach (var sumCellsHelper in sumCellsHelpers)
            {
                LogicResult initResult = sumCellsHelper.Init(solver, possibleSums);
                if (initResult == LogicResult.Invalid)
                {
                    return LogicResult.Invalid;
                }

                changed |= initResult == LogicResult.Changed;
            }
            
            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override bool EnforceConstraint(Solver solver, int i, int j, int val)
        {
            if (sumCellsHelpers == null)
            {
                return true;
            }

            if (!cellsHash.Contains((i, j)))
            {
                return true;
            }

            List<int> possibleSums = PossibleSums(solver);
            if (possibleSums.Count == 0)
            {
                return false;
            }

            return true;
        }

        public override LogicResult StepLogic(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
        {
            if (sumCellsHelpers == null)
            {
                return LogicResult.None;
            }

            List<int> possibleSums = PossibleSums(solver);
            if (possibleSums.Count == 0)
            {
                logicalStepDescription?.Add(new("There are no possible sums.", cells));
                return LogicResult.Invalid;
            }

            var board = solver.Board;
            uint[] origMasks = null;
            if (logicalStepDescription != null)
            {
                origMasks = new uint[cells.Count];
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    var (i, j) = cells[cellIndex];
                    origMasks[cellIndex] = board[i, j];
                }
            }

            bool changed = false;
            foreach (var sumCellsHelper in sumCellsHelpers)
            {
                LogicResult stepResult = sumCellsHelper.StepLogic(solver, possibleSums, (List<LogicalStepDesc>)null, isBruteForcing);
                if (stepResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Add(new($"Cells {solver.CompactName(sumCellsHelper.Cells)} cannot be restricted to sum{(possibleSums.Count > 1 ? "s" : "")} {string.Join(",", possibleSums)}.", sumCellsHelper.Cells));
                    return LogicResult.Invalid;
                }
                changed |= stepResult == LogicResult.Changed;
            }

            if (changed && logicalStepDescription != null)
            {
                List<int> elims = new();
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    var (i, j) = cells[cellIndex];
                    uint origMask = origMasks[cellIndex];
                    if (IsValueSet(origMask))
                    {
                        continue;
                    }

                    uint newMask = board[i, j];
                    if (origMask != newMask)
                    {
                        uint removedMask = origMask & ~newMask;
                        int minValue = MinValue(removedMask);
                        int maxValue = MaxValue(removedMask);
                        for (int v = minValue; v <= maxValue; v++)
                        {
                            if (HasValue(removedMask, v))
                            {
                                elims.Add(solver.CandidateIndex((i, j), v));
                            }
                        }
                    }
                }

                logicalStepDescription?.Add(new(
                    desc: $"Restricted to sum{(possibleSums.Count > 1 ? "s" : "")} {string.Join(",", possibleSums)} => {solver.DescribeElims(elims)}",
                    sourceCandidates: Enumerable.Empty<int>(),
                    elimCandidates: elims));
            }

            return changed ? LogicResult.Changed : LogicResult.None;
        }

        public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumCellsHelpers != null ? InitLinksByRunningLogic(solver, cells, logicalStepDescription) : LogicResult.None;
        
        public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumCellsHelpers != null ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

        private List<int> PossibleSums(Solver solver)
        {
            HashSet<int> possibleSums = null;
            foreach (var sumCellsHelper in sumCellsHelpers)
            {
                List<int> curPossibleSums = sumCellsHelper.PossibleSums(solver);
                if (curPossibleSums == null)
                {
                    possibleSums = null;
                    break;
                }

                if (possibleSums == null)
                {
                    possibleSums = curPossibleSums.ToHashSet();
                }
                else
                {
                    possibleSums.IntersectWith(curPossibleSums);
                }
            }

            if (possibleSums == null || possibleSums.Count == 0)
            {
                return new();
            }

            List<int> possibleSumsList = possibleSums.ToList();
            possibleSumsList.Sort();
            return possibleSumsList;
        }
    }
}
