namespace Grimoire.Tests.Architecture;

/// <summary>
/// Quality gate 3 / T037 / TS-17. The first level under <c>src/</c> is domain slices and nothing
/// else (constitution V.1): a technical-layer directory there is how a codebase stops being
/// organised around what it does and starts being organised around what its parts are made of.
/// </summary>
public sealed class SliceStructureTests
{
    /// <summary>Directory names that describe a technical layer rather than a domain concept.</summary>
    private static readonly string[] ForbiddenAtFirstLevel =
    [
        "controllers", "services", "utils", "helpers", "models", "common", "shared", "core",
        "infrastructure", "domain", "application", "handlers", "managers", "providers",
    ];

    [Fact]
    public void NoTechnicalLayerDirectoryExistsAtTheFirstLevelUnderSrc()
    {
        var offenders = FirstLevelDirectories("src")
            .Where(name => ForbiddenAtFirstLevel.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Order()
            .ToList();

        Assert.True(offenders.Count is 0,
            $"src/ holds technical-layer directories at its first level: {string.Join(", ", offenders)}. "
            + "First-level directories are domain slices (constitution V.1).");
    }

    [Fact]
    public void NoTestKindDirectoryExistsAtTheFirstLevelUnderTests()
    {
        var actual = FirstLevelDirectories("tests").ToHashSet(StringComparer.Ordinal);

        // tests/ mirrors the slices, not test kinds (plan "Project Structure").
        foreach (var kind in new[] { "unit", "integration", "e2e", "acceptance", "functional" })
        {
            Assert.DoesNotContain(kind, actual);
        }
    }

    [Fact]
    public void DeployContainsNoCompiledApplicationCode()
    {
        var deploy = Path.Combine(RepositoryRoot, "deploy");
        if (!Directory.Exists(deploy))
        {
            return;
        }

        var code = Directory
            .EnumerateFiles(deploy, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".ts" or ".csproj" or ".svelte")
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .Order()
            .ToList();

        // deploy/ holds image and compose definitions and deliberately contains no domain code,
        // so the first-level-slice gate stays honest (plan "Structure Decision").
        Assert.True(code.Count is 0,
            $"deploy/ holds application code: {string.Join(", ", code)}");
    }

    private static IEnumerable<string> FirstLevelDirectories(string relative) =>
        Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, relative))
            .Select(path => Path.GetFileName(path)!)
            .Where(name => !name.StartsWith('.') && name is not "bin" and not "obj" and not "node_modules");

    internal static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Grimoire.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not find the repository root from the test binary.");
        }
    }
}
