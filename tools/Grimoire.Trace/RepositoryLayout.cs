namespace Grimoire.Trace;

/// <summary>A built test suite: which suite it is and where its assembly lies.</summary>
internal sealed record TestAssembly(string Suite, string Path);

/// <summary>One suite's assembly as one configuration built it, and when.</summary>
internal sealed record BuiltAssembly(string Suite, string Configuration, string Path, DateTimeOffset BuiltAt);

/// <summary>One of a suite's own source files, and when it was last written.</summary>
internal sealed record SourceFile(string Suite, string Path, DateTimeOffset WrittenAt);

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
    /// The built assembly of every project under <c>tests/</c>, in the requested configuration, and
    /// only where it is newer than every source file of its suite.
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

        var built = suites
            .SelectMany(s => Configurations.Select(c => (Suite: s, Configuration: c, Path: Locate(s, c))))
            .Where(b => b.Path is not null)
            .Select(b => new BuiltAssembly(b.Suite, b.Configuration, Relative(b.Path!), File.GetLastWriteTimeUtc(b.Path!)))
            .ToArray();

        return Choose(suites, built, [.. suites.SelectMany(SourcesOf)], configuration);
    }

    private static readonly string[] Configurations = ["Release", "Debug"];

    /// <summary>
    /// Which configuration's assemblies to read, from what is built and what it was built from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With none requested, Release is used when every suite has one <b>and every one is fresh</b>, and
    /// Debug otherwise — one configuration, never a mixture of two.
    /// </para>
    /// <para>
    /// <b>An assembly older than a source file of its suite is not read.</b> Its traits are those of a
    /// tree that no longer exists: #70 wrote <c>docs/trace.md</c> from Release assemblies older than
    /// the tests it listed, and the gate was green. A requested configuration that is stale, or no
    /// configuration fresh, ends the run naming the build and the path (Constitution IV.3: what the
    /// check cannot read, it fails on rather than skips).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TestAssembly> Choose(
        IReadOnlyList<string> suites,
        IReadOnlyList<BuiltAssembly> built,
        IReadOnlyList<SourceFile> sources,
        string? configuration)
    {
        string? stale = null;

        foreach (var candidate in configuration is null ? Configurations : [configuration])
        {
            var found = suites.Select(s => built.FirstOrDefault(b => b.Suite == s && b.Configuration == candidate)).ToArray();
            if (!Array.TrueForAll(found, f => f is not null))
            {
                continue;
            }

            var outdated = found
                .Select(f => (Assembly: f!, Newer: sources
                    .Where(source => source.Suite == f!.Suite && source.WrittenAt > f.BuiltAt)
                    .MaxBy(source => source.WrittenAt)))
                .FirstOrDefault(f => f.Newer is not null);

            if (outdated.Assembly is null)
            {
                return [.. found.Select(f => new TestAssembly(f!.Suite, f.Path))];
            }

            stale ??=
                $"the {outdated.Assembly.Configuration} build of {outdated.Assembly.Suite} ({outdated.Assembly.Path}) is older than "
                + $"{outdated.Newer!.Path}. Run `dotnet build {SolutionFile} --configuration {outdated.Assembly.Configuration}` "
                + "first — the check reads the built assemblies, and these are not the tree as it stands.";
        }

        if (stale is not null)
        {
            throw new TraceInputException(stale);
        }

        var missing = string.Join(", ", suites.Where(s => !built.Any(b => b.Suite == s && b.Configuration == (configuration ?? "Debug"))));
        throw new TraceInputException(
            $"not built: {missing}. Run `dotnet build {SolutionFile}` first — the check reads the built assemblies.");
    }

    /// <summary>A suite's own C# files: everything under its directory but what a build writes.</summary>
    private IEnumerable<SourceFile> SourcesOf(string suite)
    {
        var directory = Path.Combine(Root, "tests", suite);
        var output = new[] { Path.Combine(directory, "bin") + Path.DirectorySeparatorChar, Path.Combine(directory, "obj") + Path.DirectorySeparatorChar };

        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !output.Any(o => file.StartsWith(o, StringComparison.Ordinal)))
            .Select(file => new SourceFile(suite, Relative(file), File.GetLastWriteTimeUtc(file)));
    }

    private string Relative(string path) => Path.GetRelativePath(Root, path);

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
