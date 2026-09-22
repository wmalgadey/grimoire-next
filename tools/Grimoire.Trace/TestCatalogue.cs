using System.Reflection;
using System.Runtime.InteropServices;

namespace Grimoire.Trace;

/// <summary>One xunit trait, as the two arguments of the attribute spell it.</summary>
internal readonly record struct TestTrait(string Name, string Value);

/// <summary>
/// Reads the <c>level</c> and <c>req</c> traits off the built test assemblies with
/// <see cref="MetadataLoadContext"/> — metadata only. No test is executed and no runner is
/// involved, which is what makes the check deterministic (Constitution IV.3, research.md R-05).
/// </summary>
/// <remarks>
/// Finding the methods is <see cref="Read"/> and reading what they carry is
/// <see cref="Describe"/>. The split is not decoration: every judgment the catalogue makes about
/// a method is in <see cref="Describe"/>, which is given traits, so the gate's own rules are
/// provable without an assembly to carry them — including the one an assembly this gate reads
/// cannot carry, a test with no level.
/// </remarks>
internal static class TestCatalogue
{
    private const string TraitAttribute = "Xunit.TraitAttribute";
    private const string FactAttribute = "Xunit.FactAttribute";

    public static readonly string[] Levels = ["fast", "contract", "e2e", "deploy"];

    public static IReadOnlyList<TestMethod> Read(IReadOnlyList<TestAssembly> assemblies)
    {
        var tests = new List<TestMethod>();

        foreach (var assembly in assemblies)
        {
            tests.AddRange(ReadOne(assembly));
        }

        return tests;
    }

    private static IEnumerable<TestMethod> ReadOne(TestAssembly assembly)
    {
        var directory = Path.GetDirectoryName(assembly.Path)!;

        // The assembly's own folder wins over the shared framework, so the xunit types resolve to
        // the build under test rather than to whatever the tool itself was compiled against.
        var searchPaths = Directory.GetFiles(directory, "*.dll")
            .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        using var context = new MetadataLoadContext(new PathAssemblyResolver(searchPaths));
        var loaded = context.LoadFromAssemblyPath(assembly.Path);

        foreach (var type in loaded.GetTypes())
        {
            var typeTraits = Traits(type.GetCustomAttributesData());

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var attributes = method.GetCustomAttributesData();
                if (!IsTest(attributes))
                {
                    continue;
                }

                yield return Describe(
                    assembly.Suite,
                    type.FullName ?? type.Name,
                    method.Name,
                    [.. typeTraits.Concat(Traits(attributes))]);
            }
        }
    }

    /// <summary>
    /// One test, as the traits it carries describe it. The class's traits come before the
    /// method's, which is the order a level is read in. A method carrying none is a test with no
    /// level, which the gate has something to say about (Constitution IV.3) — so this says it
    /// rather than refusing to describe the method.
    /// </summary>
    public static TestMethod Describe(string suite, string typeName, string methodName, IReadOnlyList<TestTrait> traits) =>
        new(suite,
            typeName,
            methodName,
            Level(traits),
            [.. traits.Where(t => t.Name.Equals("req", StringComparison.OrdinalIgnoreCase))
                      .Select(t => t.Value)
                      .Distinct(StringComparer.Ordinal)
                      .Order(StringComparer.Ordinal)]);

    /// <summary>A method xunit would run: <c>[Fact]</c>, <c>[Theory]</c>, or anything deriving from them.</summary>
    private static bool IsTest(IList<CustomAttributeData> attributes) =>
        attributes.Any(a => DerivesFromFact(a.AttributeType));

    private static bool DerivesFromFact(Type? type)
    {
        for (; type is not null; type = SafeBaseType(type))
        {
            if (type.FullName == FactAttribute)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A base type that cannot be resolved ends the walk rather than the run: an assembly whose
    /// dependencies are not all beside it still yields the tests it does describe.
    /// </summary>
    private static Type? SafeBaseType(Type type)
    {
        try
        {
            return type.BaseType;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string? Level(IEnumerable<TestTrait> traits)
    {
        var declared = traits
            .Where(t => t.Name.Equals("level", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value.ToLowerInvariant())
            .FirstOrDefault(v => Levels.Contains(v, StringComparer.Ordinal));

        return declared;
    }

    private static List<TestTrait> Traits(IEnumerable<CustomAttributeData> attributes) =>
        [.. attributes
            .Where(a => a.AttributeType.FullName == TraitAttribute && a.ConstructorArguments.Count == 2)
            .Select(a => new TestTrait(
                a.ConstructorArguments[0].Value as string ?? string.Empty,
                a.ConstructorArguments[1].Value as string ?? string.Empty))];
}
