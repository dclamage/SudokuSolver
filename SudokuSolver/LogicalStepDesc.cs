using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver
{
    public class LogicalStepDesc
    {
        public readonly string desc;
        public readonly List<(int, int)> highlightCells = null;
        public readonly List<int> sourceCandidates = null;
        public readonly List<int> elimCandidates = null;
        public readonly List<(int, int)> strongLinks = null;
        public readonly List<(int, int)> weakLinks = null;
        public readonly List<LogicalStepDesc> subSteps = null;
        public readonly bool isSingle = false;

        public LogicalStepDesc(string desc, IEnumerable<int> sourceCandidates, IEnumerable<int> elimCandidates, bool sourceIsAIC = false, bool isSingle = false, List<LogicalStepDesc> subSteps = null)
        {
            sourceCandidates ??= Enumerable.Empty<int>();
            elimCandidates ??= Enumerable.Empty<int>();

            this.desc = desc;
            this.sourceCandidates = sourceCandidates.ToList();
            this.elimCandidates = elimCandidates.ToList();
            this.isSingle = isSingle;
            this.subSteps = subSteps;

            if (sourceIsAIC)
            {
                strongLinks = new();
                weakLinks = new();
                bool strong = false;
                int prevCandidate = -1;
                foreach (int curCandidate in sourceCandidates)
                {
                    if (prevCandidate != -1)
                    {
                        var candidatePair = (prevCandidate, curCandidate);
                        if (strong)
                        {
                            strongLinks.Add(candidatePair);
                        }
                        else
                        {
                            weakLinks.Add(candidatePair);
                        }
                    }
                    prevCandidate = curCandidate;
                    strong = !strong;
                }
            }
        }

        public LogicalStepDesc(string desc, IEnumerable<(int, int)> highlightCells)
        {
            this.desc = desc;
            this.highlightCells = highlightCells.ToList();
        }

        public LogicalStepDesc(string desc, (int, int) highlightCell)
        {
            this.desc = desc;
            highlightCells = new() { highlightCell };
        }

        public override string ToString()
        {
            if (subSteps == null || subSteps.Count == 0)
            {
                return desc;
            }

            StringBuilder sb = new();
            sb.AppendLine(desc);
            foreach (var step in subSteps)
            {
                sb.Append("    ").AppendLine(step.ToString());
            }
            return sb.ToString();
        }
    }

    public static class LogicalStepDescUtil
    {
        public static StringBuilder Append(this StringBuilder sb, IEnumerable<LogicalStepDesc> logicalStepDescs)
        {
            foreach (var desc in logicalStepDescs)
            {
                sb.Append(desc);
            }
            return sb;
        }

        public static IEnumerable<T> ToEnumerable<T>(this T value)
        {
            yield return value;
        }
    }
}
