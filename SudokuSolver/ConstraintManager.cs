namespace SudokuSolver;

public static class ConstraintManager
{
    static ConstraintManager()
    {
        foreach (Type type in constraintTypeConstructors.Keys.Concat(constraintGroupTypeConstructors.Keys))
        {
            var constraintAttribute = GetConstraintAttribute(type);
            if (constraintAttribute.ConsoleName != null && constraintConsoleNameLookup.TryGetValue(constraintAttribute.ConsoleName, out Type existingNameType))
            {
                Console.Error.WriteLine($"**WARNING** Duplicate constraint name of {constraintAttribute.ConsoleName} used by {existingNameType.FullName} and {type.FullName}.");
            }
            else
            {
                if (constraintAttribute.ConsoleName != null)
                {
                    constraintConsoleNameLookup[constraintAttribute.ConsoleName] = type;
                }
            }
        }
    }

    public static void AddConstraintByName(this Solver solver, string name, string options)
    {
        if (!constraintConsoleNameLookup.TryGetValue(name, out Type type))
        {
            throw new ArgumentException($"**ERROR** Cannot find constraint named {name} so this constraint is ignored.");
        }
        AddConstraint(solver, type, options);
    }

    public static void AddConstraint(this Solver solver, Type type, string options)
    {
        try
        {
            if (constraintTypeConstructors.TryGetValue(type, out var constraintConstructor))
            {
                solver.AddConstraint(constraintConstructor(solver, options));
            }
            else if (constraintGroupTypeConstructors.TryGetValue(type, out var constraintGroupConstructor))
            {
                constraintGroupConstructor(solver, options).AddConstraints(solver);
            }

            List<string> constraintStrings;
            if (solver.customInfo.TryGetValue("ConstraintStrings", out object constraintStringsObj))
            {
                constraintStrings = (List<string>)constraintStringsObj;
            }
            else
            {
                solver.customInfo["ConstraintStrings"] = constraintStrings = new();
            }

            if (string.IsNullOrWhiteSpace(options))
            {
                constraintStrings.Add(type.Name);
            }
            else
            {
                constraintStrings.Add($"{type.Name}:{options}");
            }
        }
        catch (Exception e)
        {
            throw new ArgumentException($"**ERROR** Cannot instantiate constraint {type.Name}: {e.Message}");
        }
    }

    public static IEnumerable<ConstraintAttribute> ConstraintAttributes => constraintConsoleNameLookup.Values.Select(GetConstraintAttribute);

    private static ConstraintAttribute GetConstraintAttribute(Type type)
    {
        return Attribute.GetCustomAttribute(type, typeof(ConstraintAttribute)) as ConstraintAttribute;
    }

    private static readonly Dictionary<string, Type> constraintConsoleNameLookup = new();

    private static readonly Dictionary<Type, Func<Solver, string, Constraint>> constraintTypeConstructors = new()
    {
        { typeof(ArrowSumConstraint), (solver, options) => new ArrowSumConstraint(solver, options) },
        { typeof(BetweenLineConstraint), (solver, options) => new BetweenLineConstraint(solver, options) },
        { typeof(ChessConstraint), (solver, options) => new ChessConstraint(solver, options) },
        { typeof(CloneConstraint), (solver, options) => new CloneConstraint(solver, options) },
        { typeof(DiagonalNegativeGroupConstraint), (solver, options) => new DiagonalNegativeGroupConstraint(solver, options) },
        { typeof(DiagonalNonconsecutiveConstraint), (solver, options) => new DiagonalNonconsecutiveConstraint(solver, options) },
        { typeof(DiagonalPositiveGroupConstraint), (solver, options) => new DiagonalPositiveGroupConstraint(solver, options) },
        { typeof(DifferenceConstraint), (solver, options) => new DifferenceConstraint(solver, options) },
        { typeof(DisjointGroupConstraint), (solver, options) => new DisjointGroupConstraint(solver, options) },
        { typeof(EvenConstraint), (solver, options) => new EvenConstraint(solver, options) },
        { typeof(ExtraRegionConstraint), (solver, options) => new ExtraRegionConstraint(solver, options) },
        { typeof(RowIndexerConstraint), (solver, options) => new RowIndexerConstraint(solver, options) },
        { typeof(ColIndexerConstraint), (solver, options) => new ColIndexerConstraint(solver, options) },
        { typeof(BoxIndexerConstraint), (solver, options) => new BoxIndexerConstraint(solver, options) },
        { typeof(KillerCageConstraint), (solver, options) => new KillerCageConstraint(solver, options) },
        { typeof(MultiSumKillerCageConstraint), (solver, options) => new MultiSumKillerCageConstraint(solver, options) },
        { typeof(KingConstraint), (solver, options) => new KingConstraint(solver, options) },
        { typeof(KnightConstraint), (solver, options) => new KnightConstraint(solver, options) },
        { typeof(LittleKillerConstraint), (solver, options) => new LittleKillerConstraint(solver, options) },
        { typeof(MaximumConstraint), (solver, options) => new MaximumConstraint(solver, options) },
        { typeof(MinimumConstraint), (solver, options) => new MinimumConstraint(solver, options) },
        { typeof(OddConstraint), (solver, options) => new OddConstraint(solver, options) },
        { typeof(PalindromeConstraint), (solver, options) => new PalindromeConstraint(solver, options) },
        { typeof(QuadrupleConstraint), (solver, options) => new QuadrupleConstraint(solver, options) },
        { typeof(RatioConstraint), (solver, options) => new RatioConstraint(solver, options) },
        { typeof(RegionSumLinesConstraint), (solver, options) => new RegionSumLinesConstraint(solver, options) },
        { typeof(RenbanConstraint), (solver, options) => new RenbanConstraint(solver, options) },
        { typeof(SandwichConstraint), (solver, options) => new SandwichConstraint(solver, options) },
        { typeof(XSumConstraint), (solver, options) => new XSumConstraint(solver, options) },
        { typeof(SkyscraperConstraint), (solver, options) => new SkyscraperConstraint(solver, options) },
        { typeof(SelfTaxicabConstraint), (solver, options) => new SelfTaxicabConstraint(solver, options) },
        { typeof(SumConstraint), (solver, options) => new SumConstraint(solver, options) },
        { typeof(TaxicabConstraint), (solver, options) => new TaxicabConstraint(solver, options) },
        { typeof(ThermometerConstraint), (solver, options) => new ThermometerConstraint(solver, options) },
        { typeof(WhispersConstraint), (solver, options) => new WhispersConstraint(solver, options) },
    };

    private static readonly Dictionary<Type, Func<Solver, string, IConstraintGroup>> constraintGroupTypeConstructors = new()
    {
        { typeof(DisjointConstraintGroup), (solver, options) => new DisjointConstraintGroup(solver, options) },
    };
}
