namespace Grimoire.Trace;

/// <summary>A built test suite: which suite it is and where its assembly lies.</summary>
internal sealed record TestAssembly(string Suite, string Path);

/// <summary>Raised when the tool cannot read what it was asked to check.</summary>
internal sealed class TraceInputException(string message) : Exception(message);

/// <summary>Where the repository keeps the two things <c>trace-check</c> reads.</summary>
internal sealed class RepositoryLayout
{
    private const string SolutionFile = "Grimoire.slnx";

    private RepositoryLayout(string root) => Root = root;

    public string Root { get; }

    public string CapabilitiesDirectory => Path.Combine(Root, "docs", "capabilities");

    public string TraceDocument => Path.Combine(Root, "docs", "trace.md");

    public static RepositoryLayout Discover()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return new RepositoryLayout(directory.FullName);
            }
        }

        throw new TraceInputException($"no {SolutionFile} in {Environment.CurrentDirectory} or any directory above it");
    }

    /// <summary>
    /// The built assembly of every project under <c>tests/</c>, in the requested configuration.
    /// With none requested, Release is used when every suite has one and Debug otherwise, so the
    /// tool reads one configuration rather than a mixture of two.
    /// </summary>
    public IReadOnlyList<TestAssembly> TestAssemblies(string? configuration)
    {
        var suites = Directory.GetDirectories(Path.Combine(Root, "tests"))
            .Where(d => Directory.GetFiles(d, "*.csproj").Length == 1)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (suites.Length == 0)
        {
            throw new TraceInputException($"no test projects under {Path.Combine(Root, "tests")}");
        }

        foreach (var candidate in configuration is null ? (string[])["Release", "Debug"] : [configuration])
        {
            var found = suites.Select(s => (Suite: s, Path: Locate(s, candidate))).ToArray();
            if (Array.TrueForAll(found, f => f.Path is not null))
            {
                return [.. found.Select(f => new TestAssembly(f.Suite, f.Path!))];
            }
        }

        var missing = string.Join(", ", suites.Where(s => Locate(s, configuration ?? "Debug") is null));
        throw new TraceInputException(
            $"not built: {missing}. Run `dotnet build {SolutionFile}` first — the check reads the built assemblies.");
    }

    private string? Locate(string suite, string configuration)
    {
        var output = Path.Combine(Root, "tests", suite, "bin", configuration);
        if (!Directory.Exists(output))
        {
            return null;
        }

        // One directory per target framework; the tree targets exactly one.
        var assemblies = Directory.GetFiles(output, $"{suite}.dll", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return assemblies.Length == 1
            ? assemblies[0]
            : assemblies.Length == 0 ? null
            : throw new TraceInputException($"{suite} is built for more than one target framework under {output}");
    }
}
