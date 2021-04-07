using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SudokuSolver;
using SudokuSolver.Constraints;

namespace SudokuSolverConsole
{
    class ConstraintManager
    {
        public ConstraintManager()
        {
            foreach (Type type in GetAllConstraints())
            {
                var constraintAttribute = GetConstraintAttribute(type);
                if (constraintAttribute.ConsoleName != null && constraintConsoleNameLookup.TryGetValue(constraintAttribute.ConsoleName, out Type existingNameType))
                {
                    Console.Error.WriteLine($"**WARNING** Duplicate constraint name of {constraintAttribute.ConsoleName} used by {existingNameType.FullName} and {type.FullName}.");
                }
                else if (constraintAttribute.FPuzzlesName != null && constraintFPuzzleNameLookup.TryGetValue(constraintAttribute.FPuzzlesName, out Type existingFPuzzlesNameType))
                {
                    Console.Error.WriteLine($"**WARNING** Duplicate constraint fpuzzles name of {constraintAttribute.FPuzzlesName} used by {existingFPuzzlesNameType.FullName} and {type.FullName}.");
                }
                else
                {
                    if (constraintAttribute.ConsoleName != null)
                    {
                        constraintConsoleNameLookup[constraintAttribute.ConsoleName] = type;
                    }
                    if (constraintAttribute.FPuzzlesName != null)
                    {
                        constraintFPuzzleNameLookup[constraintAttribute.FPuzzlesName] = type;
                    }
                }
            }
        }

        public void AddConstraintByName(Solver solver, string name, string options)
        {
            if (!constraintConsoleNameLookup.TryGetValue(name, out Type type))
            {
                Console.Error.WriteLine($"**ERROR** Cannot find constraint named {name} so this constraint is ignored.");
                return;
            }
            AddConstraint(solver, type, options);
        }

        public void AddConstraintByFPuzzlesName(Solver solver, string name, string options)
        {
            if (!constraintFPuzzleNameLookup.TryGetValue(name, out Type type))
            {
                Console.Error.WriteLine($"**ERROR** Cannot find constraint with fpuzzles name {name} so this constraint is ignored.");
                return;
            }
            AddConstraint(solver, type, options);
        }

        private static void AddConstraint(Solver solver, Type type, string options)
        {
            try
            {
                switch (Activator.CreateInstance(type, options))
                {
                    case Constraint constraint:
                        solver.AddConstraint(constraint);
                        break;
                    case IConstraintGroup constraintGroup:
                        constraintGroup.AddConstraints(solver);
                        break;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"**ERROR** Cannot instantiate constraint {type.Name}: {e.Message}");
            }
        }

        public IEnumerable<ConstraintAttribute> ConstraintAttributes => constraintConsoleNameLookup.Values.Select(GetConstraintAttribute);

        private static IEnumerable<Type> GetAllConstraints()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(IsValidConstraint);
        }

        private static bool IsValidConstraint(Type type)
        {
            if (type.IsInterface || type.IsAbstract)
            {
                return false;
            }

            if (!typeof(Constraint).IsAssignableFrom(type) && !typeof(IConstraintGroup).IsAssignableFrom(type))
            {
                return false;
            }

            if (GetConstraintAttribute(type) == null)
            {
                Console.Error.WriteLine($"**WARNING** Constraint with type {type.FullName} cannot be used because it is missing the Constraint attribute.");
                return false;
            }

            return true;
        }

        private static ConstraintAttribute GetConstraintAttribute(Type type)
        {
            return Attribute.GetCustomAttribute(type, typeof(ConstraintAttribute)) as ConstraintAttribute;
        }

        private Dictionary<string, Type> constraintConsoleNameLookup = new();
        private Dictionary<string, Type> constraintFPuzzleNameLookup = new();
    }
}
