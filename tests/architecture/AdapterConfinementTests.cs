using System.Reflection;
using Grimoire.Wiki.Adapters;
using NetArchTest.Rules;

namespace Grimoire.Tests.Architecture;

/// <summary>
/// Quality gate 2 / T038 / TS-17. External systems are reached through one adapter each, and that
/// adapter is the only place their library appears (constitution V.3). Ports are for external
/// systems that are doubled or have more than one adapter — today, the model port alone; the
/// filesystem, git, the clock, process spawning and the network get adapter confinement instead.
/// </summary>
/// <remarks>
/// Asserted over the <b>built assemblies</b>, so it catches a reference added anywhere in the
/// slice, not only one a grep would find. The TypeScript layer's half of this gate is the
/// <c>dependency-cruiser</c> rule in <c>src/agentrun/.dependency-cruiser.cjs</c> (T039).
/// </remarks>
public sealed class AdapterConfinementTests
{
    private static Assembly Ingest => typeof(Grimoire.Ingest.Source).Assembly;

    private static Assembly Tasks => typeof(Grimoire.Tasks.Task).Assembly;

    private static Assembly Dispatch => typeof(Grimoire.Dispatch.Adapters.RunnerEvent).Assembly;

    private static Assembly Wiki => typeof(GitCli).Assembly;

    private static Assembly Hub => typeof(Grimoire.Hub.HubConfiguration).Assembly;

    private static Assembly Egress => typeof(Grimoire.Egress.EgressMarker).Assembly;

    /// <summary>Every C# slice assembly, so "only under X" means "nowhere else" rather than "not here".</summary>
    private static IReadOnlyList<(string Slice, Assembly Assembly)> AllSlices =>
    [
        ("src/ingest", Ingest),
        ("src/tasks", Tasks),
        ("src/dispatch", Dispatch),
        ("src/wiki", Wiki),
        ("src/hub", Hub),
        ("src/egress", Egress),
    ];

    // The three below are confined to adapter namespaces, not to slices: a slice-level rule would let
    // the library sit in the slice's domain code, beside the adapter it is meant to be behind.

    [Fact]
    public void SqliteIsReferencedOnlyInsideTheTasksAdapters()
        => AssertConfinedToNamespaces("Microsoft.Data.Sqlite", "Grimoire.Tasks.Adapters");

    [Fact]
    public void ProcessIsReferencedOnlyInsideTheDispatchAndWikiAdapters()
        => AssertConfinedToNamespaces(
            "System.Diagnostics.Process", "Grimoire.Dispatch.Adapters", "Grimoire.Wiki.Adapters");

    [Fact]
    public void HttpClientIsReferencedOnlyInsideTheIngestAdapters()
        => AssertConfinedToNamespaces("System.Net.Http.HttpClient", "Grimoire.Ingest.Adapters");

    [Fact]
    public void YarpIsReferencedOnlyUnderSrcEgress()
        => AssertConfinedTo("Yarp.ReverseProxy", "src/egress");

    /// <summary>
    /// The network below HTTP. Confined to adapter namespaces rather than to slices: the egress
    /// reachability probe is a network call like any other, and a slice-level rule would let it sit
    /// in a slice's domain code — which is where it was, twice, before this rule named it.
    /// </summary>
    [Fact]
    public void SocketsAreReferencedOnlyInsideAdapters()
        => AssertConfinedToNamespaces(
            "System.Net.Sockets",
            "Grimoire.Ingest.Adapters",
            "Grimoire.Dispatch.Adapters",
            // The proxy is the network boundary itself; the whole slice is its adapter.
            "Grimoire.Egress");

    [Fact]
    public void TheModelPortIsTheOnlyPortAndItDoesNotLiveInCSharp()
    {
        // The single port is the model port, and it lives in src/agentrun/ because that is where
        // the SDK and therefore the LLM actually are (plan V.2). No C# slice may introduce an
        // interface with one implementation and nothing external behind it (constitution V.2).
        foreach (var (slice, assembly) in AllSlices)
        {
            var singleImplementationInterfaces = Types.InAssembly(assembly)
                .That().AreInterfaces().And().ArePublic()
                .GetTypes()
                .Where(candidate => assembly.GetTypes().Count(type =>
                    type is { IsInterface: false, IsAbstract: false } && candidate.IsAssignableFrom(type)) is 1)
                .Select(type => type.FullName!)
                .Order()
                .ToList();

            Assert.True(singleImplementationInterfaces.Count is 0,
                $"{slice} declares an interface with exactly one implementation and no external system "
                + $"behind it: {string.Join(", ", singleImplementationInterfaces)}. "
                + "Ports are only for external systems that are doubled or have multiple adapters "
                + "(constitution V.2).");
        }
    }

    private static void AssertConfinedToNamespaces(string dependency, params string[] permittedNamespaces)
    {
        var offenders = AllSlices
            .SelectMany(slice => Types.InAssembly(slice.Assembly)
                .That().HaveDependencyOn(dependency)
                .GetTypes()
                .Where(type => !permittedNamespaces.Any(permitted =>
                    type.Namespace == permitted
                    || (type.Namespace ?? "").StartsWith(permitted + ".", StringComparison.Ordinal)))
                .Select(type => $"{slice.Slice}: {type.FullName}"))
            .Order()
            .ToList();

        Assert.True(offenders.Count is 0,
            $"'{dependency}' is confined to {string.Join(", ", permittedNamespaces)} "
            + $"(constitution V.3), but it is also referenced by:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", offenders));
    }

    private static void AssertConfinedTo(string dependency, params string[] permittedSlices)
    {
        var offenders = new List<string>();

        foreach (var (slice, assembly) in AllSlices)
        {
            if (permittedSlices.Contains(slice, StringComparer.Ordinal))
            {
                continue;
            }

            var using_ = Types.InAssembly(assembly)
                .That().HaveDependencyOn(dependency)
                .GetTypes()
                .Select(type => $"{slice}: {type.FullName}")
                .ToList();

            offenders.AddRange(using_);
        }

        Assert.True(offenders.Count is 0,
            $"'{dependency}' is confined to {string.Join(" and ", permittedSlices)} "
            + $"(constitution V.3), but it is also referenced by:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", offenders.Order()));
    }
}
