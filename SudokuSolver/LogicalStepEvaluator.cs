using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver
{
	class LogicalStepEvaluator
	{
		private bool hadInvalidSolver = false;
		private LogicalStepDesc bestStep = null;
		public Solver bestStepSolver = null;
		private (double, double, double) bestScoreTuple;
		private bool singlesRevealInvalid = false;
		private bool basicsRevealInvalid = false;
		private readonly double preferEffectivnessMetric;
		private readonly bool checkBasics;
		private readonly int initialCandidatesRemaining;
		private readonly double softDifficultyMax;

		public LogicalStepEvaluator(Solver initialSolver, double preferEffectivnessMetric, bool checkBasics, double softDifficultyMax)
        {
			this.preferEffectivnessMetric = preferEffectivnessMetric;
			this.checkBasics = checkBasics;
			this.initialCandidatesRemaining = initialSolver.GetNumCandidatesRemaining() - initialSolver.NUM_CELLS;
			this.softDifficultyMax = softDifficultyMax;
		}

		/// <summary>
		/// Forces an invalid state for the evaluator.
		/// </summary>
		/// <param name="solver">The Solver that's in an invalid state.</param>
		/// <param name="step"></param>
		public void ReportInvalid(Solver solver, LogicalStepDesc step)
        {
			if (!hadInvalidSolver)
			{
				hadInvalidSolver = true;
				bestStep = step;
				bestStepSolver = solver;
			}
		}

		/// <summary>
		/// Evaluates a Solver to see if it's a better step than anything evaluated previously.
		/// </summary>
		/// <param name="solver">The Solver to evaluate.</param>
		/// <param name="step">The logical step that was performed on the Solver.</param>
		/// <param name="difficultyScore">A difficulty score. Lower values are preferred over higher values if all else is equal.</param>
		/// <returns>false if the solver is invalid.</returns>
		public void Evaluate(Solver solver, LogicalStepDesc step, int difficultyScore)
		{
			if (hadInvalidSolver || singlesRevealInvalid)
			{
				return;
			}

			Solver singlesSolver = solver.Clone();
			if (singlesSolver.ApplySingles() == LogicResult.Invalid)
			{
				// Always prefer steps that reveal that the board cannot be solved.
				singlesRevealInvalid = true;
				bestStep = step;
				bestStepSolver = solver;
				return;
			}

			if (basicsRevealInvalid)
            {
				return;
            }

			Solver basicsSolver = null;
			if (checkBasics)
			{
				basicsSolver = singlesSolver.Clone();
				basicsSolver.SetToBasicsOnly();
				if (basicsSolver.ConsolidateBoard() == LogicResult.Invalid)
				{
					// Always prefer steps that reveal that the board cannot be solved.
					basicsRevealInvalid = true;
					bestStep = step;
					bestStepSolver = solver;
					return;
				}
			}

			// Convert the difficulty score
			double difficultyScoreNorm = difficultyScore / softDifficultyMax;

			int singlesCandidatesRemaining = singlesSolver.GetNumCandidatesRemaining() - solver.NUM_CELLS;
			int basicsCandidatesRemaining = checkBasics ? basicsSolver.GetNumCandidatesRemaining() - solver.NUM_CELLS : 0;

			// Combine these into a single effectiveness metric
			double effectivenessMetric;
			if (checkBasics)
            {
				int combinedCandidatesRemaining = basicsCandidatesRemaining * initialCandidatesRemaining + singlesCandidatesRemaining;
				effectivenessMetric = combinedCandidatesRemaining / ((double)initialCandidatesRemaining * initialCandidatesRemaining - 1);
			}
			else
            {
				effectivenessMetric = singlesCandidatesRemaining / (double)initialCandidatesRemaining;
			}

			double combinedScore = preferEffectivnessMetric * effectivenessMetric + (1.0 - preferEffectivnessMetric) * difficultyScoreNorm;

			(double, double, double) scoreTuple = preferEffectivnessMetric < 0.5
				? (combinedScore, difficultyScoreNorm, effectivenessMetric)
				: (combinedScore, effectivenessMetric, difficultyScoreNorm);
			if (bestStep == null || scoreTuple.CompareTo(bestScoreTuple) < 0)
			{
				bestStep = step;
				bestStepSolver = solver;
				bestScoreTuple = scoreTuple;
			}
		}

		public LogicResult ApplyBest(Solver solver, List<LogicalStepDesc> logicalStepDescs)
        {
			if (bestStep == null)
            {
				return LogicResult.None;
            }

			Array.Copy(bestStepSolver.Board, solver.Board, solver.NUM_CELLS);
			logicalStepDescs.Add(bestStep);
			return hadInvalidSolver ? LogicResult.Invalid : LogicResult.Changed;
        }
	}
}