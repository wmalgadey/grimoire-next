using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// The bodies, not just the operations: every request and response schema the contract describes
    /// is compared field by field against the served one — property names, base types and
    /// nullability, recursively, and every field the contract requires is required when served.
    /// </summary>
    /// <remarks>
    /// Two things are compared more loosely, and deliberately. Enums: the hub's DTOs carry states as
    /// strings, so the served document declares none to compare; the values are held to the contract
    /// by the endpoint tests. Problem bodies: those are the framework's own <c>ProblemDetails</c>, in
    /// which every field is optional, and making it match the contract's stricter schema would mean
    /// wrapping the framework (constitution VII.1) — so there the contract's fields must all be
    /// served, and nothing more is asserted.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task ServesEveryBodyWithTheShapeTheContractGivesIt()
    {
        using var client = _hub.CreateClient();
        var served = JsonNode.Parse(
            await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var committed = ToJson(new DeserializerBuilder().Build().Deserialize<object>(File.ReadAllText(ContractPath)))!;

        var drift = new List<string>();
        foreach (var (path, operations) in committed["paths"]!.AsObject())
        {
            foreach (var (method, operation) in operations!.AsObject())
            {
                var servedOperation = served["paths"]?[path]?[method];
                if (servedOperation is null)
                {
                    continue; // Reported by ServesExactlyTheOperationsTheContractDescribes.
                }

                var key = $"{method.ToUpperInvariant()} {path}";
                if (Body(operation!["requestBody"], committed) is { } request)
                {
                    Compare(Shape(request, committed), Shape(Body(servedOperation["requestBody"], served), served),
                        $"{key} request", drift, loose: false);
                }

                foreach (var (status, response) in operation["responses"]!.AsObject())
                {
                    if (Body(response, committed) is not { } expected)
                    {
                        continue;
                    }

                    var problem = Resolve(response, committed)?["content"]?["application/problem+json"] is not null;
                    Compare(Shape(expected, committed),
                        Shape(Body(servedOperation["responses"]?[status], served), served),
                        $"{key} {status}", drift, loose: problem);
                }
            }
        }

        Assert.True(drift.Count is 0,
            $"The served bodies have drifted from contracts/hub-api.openapi.yaml:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", drift));
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

    /// <summary>A schema reduced to what the comparison holds: type, nullability, fields, items.</summary>
    private sealed record Schema(
        string? Type,
        bool Nullable,
        IReadOnlyDictionary<string, Schema>? Properties,
        IReadOnlyList<string> Required,
        Schema? Items);

    private static JsonNode? Resolve(JsonNode? node, JsonNode root)
    {
        while (node?["$ref"]?.GetValue<string>() is { } reference)
        {
            var parts = reference.TrimStart('#', '/').Split('/');
            node = parts.Aggregate<string, JsonNode?>(root, (current, part) => current?[part]);
        }

        return node;
    }

    /// <summary>The JSON body schema of a request body or a response, if it has one.</summary>
    private static JsonNode? Body(JsonNode? requestOrResponse, JsonNode root)
    {
        var content = Resolve(requestOrResponse, root)?["content"];
        return content?["application/json"]?["schema"] ?? content?["application/problem+json"]?["schema"];
    }

    private static Schema? Shape(JsonNode? node, JsonNode root)
    {
        node = Resolve(node, root);
        if (node is null)
        {
            return null;
        }

        // `oneOf: [X, {type: null}]` is how the contract spells a nullable reference.
        if ((node["oneOf"] ?? node["anyOf"]) is JsonArray options)
        {
            var rest = options.Where(option => option?["type"]?.ToString() is not "null").ToList();
            var shape = Shape(rest[0], root)!;
            return shape with { Nullable = shape.Nullable || rest.Count < options.Count };
        }

        // `allOf` composes; the served document flattens. Merge properties, union what is required.
        if (node["allOf"] is JsonArray parts)
        {
            var merged = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() };
            foreach (var part in parts.Select(part => Resolve(part, root)!))
            {
                foreach (var (name, property) in part["properties"]?.AsObject() ?? [])
                {
                    merged["properties"]![name] = property?.DeepClone();
                }

                foreach (var required in part["required"]?.AsArray() ?? [])
                {
                    merged["required"]!.AsArray().Add(required?.DeepClone());
                }
            }

            node = merged;
        }

        var types = node["type"] switch
        {
            JsonArray many => many.Select(type => type!.ToString()).ToList(),
            JsonNode one => [one.ToString()],
            null => node["properties"] is not null ? ["object"] : [],
        };

        // Integers are served as ["integer", "string"] with a pattern — the runtime's number
        // handling — and are integers all the same.
        var baseType = types.Contains("integer") ? "integer" : types.FirstOrDefault(type => type is not "null");

        return new Schema(
            baseType,
            types.Contains("null"),
            node["properties"]?.AsObject().ToDictionary(pair => pair.Key, pair => Shape(pair.Value, root)!),
            node["required"]?.AsArray().Select(name => name!.ToString()).ToList() ?? [],
            baseType is "array" ? Shape(node["items"], root) : null);
    }

    private static void Compare(Schema? contract, Schema? served, string at, List<string> drift, bool loose)
    {
        if (contract is null)
        {
            return;
        }

        if (served is null)
        {
            drift.Add($"{at}: described by the contract, served with no body");
            return;
        }

        if (!loose && contract.Type != served.Type)
        {
            drift.Add($"{at}: contract type '{contract.Type}', served '{served.Type}'");
        }

        if (!loose && contract.Nullable != served.Nullable)
        {
            drift.Add($"{at}: contract nullable={contract.Nullable}, served nullable={served.Nullable}");
        }

        var contractProperties = contract.Properties ?? new Dictionary<string, Schema>();
        var servedProperties = served.Properties ?? new Dictionary<string, Schema>();
        foreach (var (name, property) in contractProperties)
        {
            if (!servedProperties.TryGetValue(name, out var servedProperty))
            {
                drift.Add($"{at}.{name}: in the contract, not served");
                continue;
            }

            Compare(property, servedProperty, $"{at}.{name}", drift, loose);
        }

        if (!loose)
        {
            drift.AddRange(servedProperties.Keys.Except(contractProperties.Keys)
                .Select(name => $"{at}.{name}: served, not in the contract"));
            drift.AddRange(contract.Required.Except(served.Required)
                .Select(name => $"{at}.{name}: required by the contract, optional as served"));
        }

        if (contract.Items is not null)
        {
            Compare(contract.Items, served.Items, $"{at}[]", drift, loose);
        }
    }

    /// <summary>YamlDotNet's object graph as JSON, so both documents are read by one comparison.</summary>
    private static JsonNode? ToJson(object? yaml) => yaml switch
    {
        Dictionary<object, object> map => new JsonObject(
            map.Select(pair => KeyValuePair.Create(pair.Key.ToString()!, ToJson(pair.Value)))),
        List<object> list => new JsonArray(list.Select(ToJson).ToArray()),
        null => null,
        var scalar => JsonValue.Create(scalar.ToString()),
    };

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
