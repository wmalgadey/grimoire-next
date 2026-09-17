using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using YamlDotNet.Serialization;

namespace Grimoire.Tests.Hub;

/// <summary>
/// T035 / TS-15 / ADR-0008. The frontend↔hub contract is <c>contracts/hub-api.openapi.yaml</c>,
/// committed and reviewed; the frontend's types are generated from it and the hub's served
/// document is diffed against it here, so a contract change cannot happen without appearing in
/// the PR diff (constitution V.5).
/// </summary>
/// <remarks>
/// This is also the single wire-up test constitution III.6 permits: it asserts the hub's OpenAPI
/// registration is present at all. Everything else it asserts can fail because of a change to our
/// own code — an endpoint added, removed, renamed, retagged, or given a response the contract does
/// not describe.
/// </remarks>
public sealed class ContractDriftTests : IClassFixture<HubFixture>
{
    private readonly HubFixture _hub;

    public ContractDriftTests(HubFixture hub) => _hub = hub;

    [Fact]
    public async System.Threading.Tasks.Task ServesAnOpenApiDocumentAtAll()
    {
        using var client = _hub.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode,
            $"The hub served no OpenAPI document ({(int)response.StatusCode}).");
    }

    [Fact]
    public async System.Threading.Tasks.Task ServesExactlyTheOperationsTheContractDescribes()
    {
        var served = await ServedOperations();
        var committed = CommittedOperations();

        var missing = committed.Keys.Except(served.Keys).Order().ToList();
        var extra = served.Keys.Except(committed.Keys).Order().ToList();

        Assert.True(
            missing.Count is 0 && extra.Count is 0,
            $"The served document has drifted from contracts/hub-api.openapi.yaml.{Environment.NewLine}"
            + $"  In the contract but not served: {Describe(missing)}{Environment.NewLine}"
            + $"  Served but not in the contract: {Describe(extra)}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GivesEveryOperationTheOperationIdAndTagTheContractGivesIt()
    {
        var served = await ServedOperations();
        var committed = CommittedOperations();

        foreach (var (key, expected) in committed)
        {
            if (!served.TryGetValue(key, out var actual))
            {
                continue; // Reported by ServesExactlyTheOperationsTheContractDescribes.
            }

            Assert.Equal(expected.OperationId, actual.OperationId);
            Assert.Equal(expected.Tags, actual.Tags);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task DescribesEveryStatusCodeTheContractDescribes()
    {
        var served = await ServedOperations();
        var committed = CommittedOperations();

        foreach (var (key, expected) in committed)
        {
            if (!served.TryGetValue(key, out var actual))
            {
                continue;
            }

            var missing = expected.StatusCodes.Except(actual.StatusCodes).Order().ToList();
            Assert.True(missing.Count is 0,
                $"{key} is served without the response(s) the contract describes: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void CoversBothTheTasksAndTheOperationsTags()
    {
        var tags = CommittedOperations().Values.SelectMany(operation => operation.Tags).ToHashSet();

        // The drift test covers the whole served surface, which is why the two operations
        // endpoints live in the same document (plan V.5).
        Assert.Contains("Tasks", tags);
        Assert.Contains("Operations", tags);
    }

    [Fact]
    public void TheRepositoryRootMirrorIsTheFeaturesContractUnchanged()
    {
        var root = File.ReadAllBytes(ContractPath);
        var feature = File.ReadAllBytes(Path.Combine(
            RepositoryRoot, "specs", "001-source-ingest-agent-run", "contracts", "hub-api.openapi.yaml"));

        Assert.Equal(feature, root);
    }

    private sealed record Operation(string? OperationId, IReadOnlyList<string> Tags, IReadOnlyList<string> StatusCodes);

    private async System.Threading.Tasks.Task<Dictionary<string, Operation>> ServedOperations()
    {
        using var client = _hub.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        var operations = new Dictionary<string, Operation>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("paths", out var paths))
        {
            return operations;
        }

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                operations[$"{method.Name.ToUpperInvariant()} {path.Name}"] = new Operation(
                    method.Value.TryGetProperty("operationId", out var id) ? id.GetString() : null,
                    method.Value.TryGetProperty("tags", out var tags)
                        ? tags.EnumerateArray().Select(tag => tag.GetString()!).ToList()
                        : [],
                    method.Value.TryGetProperty("responses", out var responses)
                        ? responses.EnumerateObject().Select(response => response.Name).Order().ToList()
                        : []);
            }
        }

        return operations;
    }

    private static Dictionary<string, Operation> CommittedOperations()
    {
        var yaml = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(File.ReadAllText(ContractPath));

        var operations = new Dictionary<string, Operation>(StringComparer.Ordinal);
        var paths = (Dictionary<object, object>)yaml["paths"];

        foreach (var (pathKey, pathValue) in paths)
        {
            foreach (var (methodKey, methodValue) in (Dictionary<object, object>)pathValue)
            {
                var operation = (Dictionary<object, object>)methodValue;
                operations[$"{methodKey.ToString()!.ToUpperInvariant()} {pathKey}"] = new Operation(
                    operation.TryGetValue("operationId", out var id) ? id.ToString() : null,
                    operation.TryGetValue("tags", out var tags)
                        ? ((List<object>)tags).Select(tag => tag.ToString()!).ToList()
                        : [],
                    operation.TryGetValue("responses", out var responses)
                        ? ((Dictionary<object, object>)responses).Keys.Select(key => key.ToString()!).Order().ToList()
                        : []);
            }
        }

        return operations;
    }

    private static string Describe(IReadOnlyList<string> keys) =>
        keys.Count is 0 ? "(none)" : string.Join(", ", keys);

    private static string ContractPath => Path.Combine(RepositoryRoot, "contracts", "hub-api.openapi.yaml");

    /// <summary>Walks up from the test binary to the repository root.</summary>
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
