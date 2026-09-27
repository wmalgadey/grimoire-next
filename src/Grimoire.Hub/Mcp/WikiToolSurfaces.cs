using System.Reflection;
using Grimoire.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Grimoire.Hub.Mcp;

/// <summary>
/// The two tool surfaces the hub serves, and which of them a session is given (GUARD-002, GUARD-005).
/// </summary>
/// <remarks>
/// <para>
/// <b>A session's catalogue is its grant.</b> Each surface is built once, from its own attributed
/// type, and a session opened on <c>/mcp/questions/{runId}</c> is given the read-only one — so
/// <c>write_page</c>, <c>write_index</c> and <c>append_log</c> are not in that session's catalogue at
/// all. There is no flag that would turn one on and no name that would reach one, which is DEC-011's
/// deny-by-default <em>by construction</em> rather than an allow-list over a larger surface.
/// </para>
/// <para>
/// <b>Why this and not two routes.</b> research.md R-06 had two <c>MapMcp</c> patterns giving two
/// catalogues. They do not: <c>AddMcpServer().WithTools&lt;A&gt;().WithTools&lt;B&gt;()</c> builds
/// <em>one</em> collection and <c>MapMcp</c> serves that same one at every pattern — measured, and it
/// merged the duplicate read tools silently rather than failing, so the questions route served all
/// five. <c>HttpServerTransportOptions.ConfigureSessionOptions</c> is the hook the library does give:
/// it runs per session with that request's <c>HttpContext</c> and that session's
/// <c>McpServerOptions</c>, whose <c>ToolCollection</c> is what the session serves.
/// </para>
/// <para>
/// <b>Why each surface comes from its own type</b> rather than by taking two of the five by name: a
/// type that has only the two reads has no place a third could be selected from by mistake. The cost
/// is two attributed methods with their bodies living once in <see cref="WikiReads"/>, which plan.md
/// §Complexity Tracking carries openly.
/// </para>
/// </remarks>
internal static class WikiToolSurfaces
{
    /// <summary>
    /// The route a question's run is served at, built from the segment its grant records — so the
    /// door, the grant and the address the agent is given cannot disagree (GUARD-005).
    /// </summary>
    public const string QuestionsDoor = "/mcp/" + ToolGrant.Questions;

    /// <summary>The route an ingest run is served at, from its grant's segment for the same reason.</summary>
    public const string RunsDoor = "/mcp/" + ToolGrant.Runs;

    /// <summary>
    /// Everything the two surfaces need from the container, registered here so that the surfaces and
    /// their dependencies are declared in one place.
    /// </summary>
    public static IServiceCollection AddWikiToolSurfaces(this IServiceCollection services)
    {
        services.AddSingleton<WikiToolsServer>();
        services.AddSingleton<WikiReadToolsServer>();
        return services;
    }

    /// <summary>
    /// The catalogue this session is to serve, decided from the route the session was opened on.
    /// </summary>
    /// <remarks>
    /// The questions route is matched by its path and not by a route value, because this runs on the
    /// transport's own request rather than inside the endpoint that carries <c>{runId}</c>. Anything
    /// that is not the questions route gets the full surface, which is the safe direction to be wrong
    /// in only one way — a route this does not know about serves an ingest run's tools, and there is
    /// exactly one such route and it is an ingest run's.
    /// </remarks>
    public static McpServerPrimitiveCollection<McpServerTool> For(HttpContext context, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Path.StartsWithSegments(QuestionsDoor)
            ? ToolsOf<WikiReadToolsServer>(services)
            : ToolsOf<WikiToolsServer>(services);
    }

    /// <summary>
    /// The tools one attributed type exposes, as a catalogue of its own.
    /// </summary>
    /// <remarks>
    /// Built from the type's own <c>[McpServerTool]</c> methods, which is the one reading
    /// <see cref="WikiReadToolsServer.NamesOf"/> already takes for the grant — so a catalogue and the
    /// grant beside it cannot name different tools.
    /// </remarks>
    private static McpServerPrimitiveCollection<McpServerTool> ToolsOf<TSurface>(IServiceProvider services)
        where TSurface : class
    {
        var catalogue = new McpServerPrimitiveCollection<McpServerTool>();
        var options = new McpServerToolCreateOptions { Services = services };

        foreach (var method in typeof(TSurface).GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            catalogue.Add(McpServerTool.Create(
                method,
                _ => services.GetRequiredService<TSurface>(),
                options));
        }

        return catalogue;
    }
}
