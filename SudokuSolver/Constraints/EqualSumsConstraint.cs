using System.Collections.Generic;
using System.Text;

namespace SudokuSolver.Constraints
{
    public abstract class EqualSumsConstraint : Constraint
    {
        private List<(int, int)> cells;
        private int[] cellIndices;
        private List<List<(int, int)>> cellGroups;
        private SumTerm[] sumTerms;
        private SumEqualityRelation sumRelation;

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

            if (sumRelation == null)
            {
                cells = cellGroups.SelectMany(group => group).ToList();
                cellIndices = cells
                    .Select(cell => cell.Item1 * WIDTH + cell.Item2)
                    .Distinct()
                    .Order()
                    .ToArray();
                sumTerms = cellGroups
                    .Select(group => solver.SumConstraints.RegisterOpenSum(this, group))
                    .ToArray();
                sumRelation = solver.SumConstraints.RegisterEquality(this, sumTerms);
            }

            return sumRelation.InitCandidates(solver);
        }

        public override bool EnforceConstraint(Solver solver, int i, int j, int val)
        {
            if (sumRelation == null)
            {
                return true;
            }

            return sumRelation.EnforcePossible(solver, i * WIDTH + j);
        }

        public override LogicResult StepLogic(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)
        {
            if (sumRelation == null)
            {
                return LogicResult.None;
            }

            if (logicalStepDescription == null)
            {
                return sumRelation.StepLogic(solver, null, isBruteForcing);
            }

            List<int> possibleSums = sumRelation.PossibleSums(solver);
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
            foreach (var sumTerm in sumTerms)
            {
                LogicResult stepResult = sumTerm.StepLogic(solver, possibleSums, (StringBuilder)null, false);
                if (stepResult == LogicResult.Invalid)
                {
                    logicalStepDescription?.Add(new($"Cells {solver.CompactName(sumTerm.Cells)} cannot be restricted to sum{(possibleSums.Count > 1 ? "s" : "")} {string.Join(",", possibleSums)}.", sumTerm.Cells));
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

        public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing) => sumRelation != null ? InitLinksByRunningLogic(solver, cells, logicalStepDescription) : LogicResult.None;
        
        public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => sumRelation != null ? CellsMustContainByRunningLogic(sudokuSolver, cells, value) : null;

        public override IReadOnlyList<int> CellIndicesForPropagationQueue => cellIndices;
    }
}
