using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Options;
using SudokuSolver;
using System.Text;
using System.Diagnostics;

namespace SudokuSolverConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            //string json = LZString.DecompressFromBase64("N4IgzglgXgpiBcBOANCA5gJwgEwQbT1ADcBDAGwFc54BGVNCImAOwQBcMqBfZY8qhAGZ6jFu04wefStQBMIpq3gduvEAGMWbGBgAKLdRDIBZEhgDWYfABZkAVmQA2ALpS3vd8C7PkhT/49Arw8fPyCA4Mi3UKjwuNivGIjk8KT4lKi0hIzo32z09KycwtcgA");

#if false
            // Thermo + arrow puzzle
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QGEsIAHYmAExFQEMBXNACwHsAnBEABQYiNIAIAQlloBbGH2oBranwDmwkQH1upJlRAtaOMDDTsAyrXJNJtPgFo+MAG4wWATz4smAd2R8AxkwUA7d9R9yPgBmAA9gvgAjJlC+EVowNE8mHzRqCB8JLCw+RnFyCFlMMD4AM2cRPgBGc0QAOj59JjE+AqK0EuoWcWosbupyRyLbHzqAHR8JgEFUiHMAaQzZCysAR1pe1sLiuOpHHyYktCZaDwYt6lkU3qx7ccmfABUGOxFm3TsVtp34xL5ElgQDzYRwZDz9HRlCpRLSRXJMXIke7TFjOFwrPKeCAsDw4ILfJJeHyJTD0GAlTFgUR8JilXIvLbtTpYFLLTFdNH3dSyQGUeAAbX5wAAvsgRWLReKpZKZRKALrIIWy6US1UqkUKpVq5Ugay9Wi4ABsqGGMB8CDQmhgyptmvVNu12rtOr1wlwAFYTRARharS79bgql6ffBLQb/W6EAAWYNm33hp2K+2O5MapMOqW6gMIYKx82hv0pmXOoul6Ul1MZ4sKkABDCSJbxmCoPIsN5iNB2BBCkBEHzk7v8kAAJUN+Hd6mHAHZ8FHJwAOfDBBf4ABMIDlcslvYyA4FQ+H7rXk6jS8nwVnk9Xl83277e8FI9PE9Qw4vhqv+Cnn/nG63Yp3fswEHEcj1/V8x2/V8Zw/V9Fw/W8aw5Vxu1Ae9gP3UDL1fU9l1fC911fa9103VAPBgbIMIPI853/NDdyo6j8FgkdIMnGdwJHRdf1IkByMo7ssIQu8GJA4dFxfEcZznCCz1kkia34rAqK48cNxEoCxOvSS32Yk8v30njFIo5TBOHbT1IA9CxJnPDVJkkdEDU3ilJU6czy3LcgA=",
                "-pr"
            };
#endif

#if false
            // Killer blister (Killer + LK)
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QGkIscAnAAgCEsIw0YSRUBDAVzQAsB7BvAJSYB2AczABrRiBIscYGGgQgActwC2TLGQDKLACadRLMlJlkmABzNYAngDoAOgIea0gnUxI6yhYvXhkAKuwwZDoQQphgZBACpmQAxkxCwWAsKmRonOlB8VgswdFZwRlmZDgAZmhknGWF8YkwNmQAImERdQICnJUkMGYwTJUFTHVJ9o4Czq7ungAymNjB3qR+AMK5MJGcbJA6RdlCJBCe0aEJdLUpadW1oeFokeqcwrVmnNF0nqGJT+o2EgdHBAAbSBwAAvshwZCIVDYTD4dCALrIUEIuHQjHo8HI1GYtH4vE4rEE4lIlGkin4okkmmE8m0ylk3GMynUvHs1nIkCiIikBJJYGgOIwYhgYEgXgAZhWAEYJLwACyykBcgBu6jyCgA7CAYSBhaLxYqVgAmeVKyUq1Dq9YKS16g1YMXwIESgCsKwV8o9bu9KwAbFaQDbNXgdQ6RU6jf6VjrULwYwAOeUxxDyrUrNNqjW4EAygAMush+sjztdvBNK0t8elZprVaDIdzMuTEcNLolMbl8Yz3YlieV2dteBNhbbUY7vAzcYlGeT8YHM94A+TQ9DebN47LEplK198cr+4l0qPxt9a+bga30b3KYD6dvPfvC8f/fvF7treLju3y9N8oHas3y9D88BlcNv1LI1d0DeNdyXXd5wlSsdVAkATSvSD23LaVYOPWN5WlJCpUzRscwUGVNy5ag0AWHkfBIS5BRLYgFGNQtUB/I1pT7Ct/zghsuVCHo4gwJ4FAAVV4CQmwUUciyFSM2IHAsJC4ycMzTeMkz9JclTwkjT0rL0BMtISIBEsSBEkmYZPIvBJQgxTWL4AtX3U8tEE9ACGyfOtO0HVBhJgUSIHEvAJNs617I3BSWKwNi3I4+Lfy83iB38qdfICkz3VfY0DOlJdK2I3csyCiyQqsyTpOi4cQDdL0wURMEgA",
                "-s"
            };
#endif

#if false
            args = new string[]
            {
                "-f",
                @"N4IgzglgXgpiBcBOANCALhNAbO8QGEsB7AOxgHcBDATzAAIBxSgWxjqIDM6AZCDuVJQCuaABZEATghAA1ShIhEhYEKglCcYGGmkA5Sc0pY6AY2Jk66zXUoAHW1mp0AFCcoBzGPU50xbMCxsJkTEEjYSbBAAJjAkGG5YAJQAOiT6EobGzBAkAPSGAB429o6p6Zl0AIISEkTklhpexQ7UZQZGdAAauTIN1nYtLiREdGTulBgAbkGkYGgSlDloKSQAQtTwqZSiwrZoqatEaJQA1kIHsamdpDBRqQAKohBYELZ0uhSGJKkAEpQmJwgEAC3xIAEl8D8AKoAFQAogBlVIARRyWCEBVSDBethgCywqhA7gUUQQAG0yaATNIAMSrAAMADFGQyQABfZBU2kM5msjlcvA05m8+nszkgamC4Us0X8uWc+XANkAXWQlIl3KZMrFApAdK1fPFkr10sNuqFwrNGqlltlCqNmpFOutep52rlqspiv5LotTu9DsFbqtxv1/oVnvNzMq9JjztD0djdqjjJjcYDSvtmaVkYzvtNyfztp1PoTqaT8dpifTgZN5fTuazparxebNvDKY73sb2cVRa7tb97sHBZLPd9wcLocnJabI9b847/fd4+nBqnjuHvbnna3KtVEvMuHVJhgWCwKngZJAACUAIz4ADMhPv+AALC+AExPr/v38AVhfR8/wPMwbnwM8L3JW9/z/VAb1gwD4IANjg29UKQ9D8GQl8AHZ8EA5VS0gy9rwQtDyMwm9UI/FCCJfVCcPg/DCNQMCyAg89SNvfDcLw/AAA4XwE/A+PgkShPE/BEBfRBBJAIijRI6Cb14/jJNvESxM0+SpJk+C5KE0Cj04qCr1vB9n3gh9aNvb8rLsijvyo4CP0UqllPM187xfB9P1/Hz4O/fzjPAzyyO/bSb2/DSb2AqLgKM4iuJUyLf1i+KgPk0KOPCnT/Kkhybzkgrbzk593IlPKbxE0qap/Az8Dq8qFLYkzqofQKLKagLfxC5KzLI2Cotg2LYJknKYFM7ibzffAutmnr4LmiqBpmuaFrmuqVtaw8wpSrzhpfUbjukhSVUEGo6nJUAXjIUiyJ2+DYNs6j6OY7DhNE2TstA6qtpfHbKrurxySGs7nt0mDvuVP6DvBiafRBh6sKY6G0cWjG5r42G2OqxjzvFZGwcc2Lv307qJrhwbSfOg9shICBmCEZgbqq89pHq/yLpAQomZZtnTw5vBor/AbOfwnyeYKSZBeqiSvpx1BJiMIRcBATp43l77GqVkAVfRdWZHZUCPGPDz4e6ha/N6oKeoPJRsByKbaXpN33cJDhSDQfBXfdt2tct18ips38iu/V7nKytzUEdkHfcFf2A9QL24gTvUk6naqXtOqiaIY97Uf41iQDj5305pTPPe9iuq7WlS1I+2KtK+5uIbK7LY5EeO/f96u097j368OnXobGs6He78vB+TkBU59mes6DjbAaW28ga7p2OMX/uF8TpPA5p0WopirKEs70up+3/e+5Tmud+Hsjaq+oqSp+irN57m+Pbvgfv4DlUbIgA==",
                "-ps"
            };
#endif

            //args[1] = @"N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaCR3PgIm9hk0UM6zR4rssX0QAQwwYA9gHd8oADYQAdjDD48IAEoBmAMK2QqawBZHzmwFYPLnwCYQWjUAYxhDQwt4KzsPWgoQYzMoghsHAHZPN3tMvxysnwAOINDwyPw0/PjSRNNzSxsANntXLPTfG0K40oiUppaghKT66Jjmrzb7CZcuieDUMN6K63HBmuGUsftGydzOqp7y6P6d6qM6zZtEewBGLK7Al3a7l2a7+ZBFo5jr96GLhrWa6Ie72YpPewg16QkoLMp9IEws61ZKAm4daz+AYudHTGxY06HBHopzIjZo/IuAmwz7w5bozJkgGjGzuPHWHzs9yEuFLY7ZOb/VEszFgrIOcGVEEfL4IrHFeLxIA==";

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            int maxThreads = 0;
            List<string> constraints = new();
            bool solveBruteForce = false;
            bool solveLogically = false;
            bool solutionCount = false;
            bool check = false;
            bool trueCandidates = false;
            bool print = false;

            var options = new OptionSet {
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "t|threads=", "The maximum number of threads to use when brute forcing.", (int t) => maxThreads = t },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },
                { "s|solve", "Provide a single brute force solution.", s => solveBruteForce = s != null },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "p|print", "Print the input board.", p => print = p != null },
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
                List<string> constraintNames = ConstraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
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
                Console.WriteLine($"Try '{processName} --help' for more information.");
                showHelp = true;
            }

            if (!string.IsNullOrWhiteSpace(fpuzzlesURL) && !string.IsNullOrWhiteSpace(givens))
            {
                Console.WriteLine($"ERROR: Cannot provide both an f-puzzles URL and a givens string.");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                return;
            }

            Solver solver;
            try
            {
                if (!string.IsNullOrWhiteSpace(givens))
                {
                    solver = SolverFactory.CreateFromGivens(givens, constraints);
                }
                else
                {
                    solver = SolverFactory.CreateFromFPuzzles(fpuzzlesURL, constraints);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return;
            }

            if (print)
            {
                Console.WriteLine("Input puzzle:");
                solver.Print();
            }

            if (solveLogically)
            {
                Console.WriteLine("Solving logically:");
                StringBuilder stepsDescription = new();
                bool valid = solver.ConsolidateBoard(stepsDescription);
                Console.WriteLine(stepsDescription);
                if (!valid)
                {
                    Console.WriteLine($"Board is invalid!");
                }
                solver.Print();
            }

            if (solveBruteForce)
            {
                Console.WriteLine("Finding a solution with brute force:");
                if (!solver.FindSolution())
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();
                }
            }

            if (trueCandidates)
            {
                Console.WriteLine("Finding true candidates:");
                if (!solver.FillRealCandidates())
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();
                }
            }

            if (solutionCount)
            {
                ulong numSolutions = solver.CountSolutions();
                Console.WriteLine($"Found {numSolutions} solutions.");
            }

            if (check)
            {
                ulong numSolutions = solver.CountSolutions(2);
                Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
            }

            watch.Stop();
            Console.WriteLine($"Took {watch.ElapsedMilliseconds}ms");
        }
    }
}
