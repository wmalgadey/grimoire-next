// The egress proxy's composition root (ADR-0010, ADR-0012): the only route out of the hub
// container, and the only holder of the upstream credential. Two routes, two policies.
using System.Diagnostics;
using System.Text.Json;
using Grimoire.Egress;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

// Environment only, read once; a missing variable stops startup here, naming every one.
var configuration = EgressConfiguration.Read(Environment.GetEnvironmentVariable);

// Structured JSON on stdout, one event per line — the same transport as the hub. This is the
// access log contracts/deployment.md calls undeclared diagnostic logging, joined to a task by the
// X-Grimoire-Run header.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

builder.Services.AddSingleton(new CredentialProvider(configuration.Credential));
var (routes, clusters) = ModelRoute.Config(configuration);
builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddTransforms(ModelRoute.Transforms);
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton(_ => FetchRoute.CreateInvoker());

var app = builder.Build();
var access = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Grimoire.Egress.Access");

app.Use(async (context, next) =>
{
    var clock = Stopwatch.StartNew();
    await next();
    access.LogInformation(
        "egress {Method} {Path} {Status} run={Run} in {ElapsedMs} ms",
        context.Request.Method,
        context.Request.Path.Value,
        context.Response.StatusCode,
        context.Request.Headers[ModelRoute.RunHeader].ToString(),
        clock.ElapsedMilliseconds);
});

// This is not a forward proxy. A request that names its own destination — an absolute-form target,
// or CONNECT — is refused before routing, so no request is ever re-aimed at a host other than the
// one this process was configured with.
app.Use(async (context, next) =>
{
    var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "";
    if (HttpMethods.IsConnect(context.Request.Method) || !target.StartsWith('/'))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers[FetchRoute.ReasonHeader] = "This proxy does not forward to a destination the request names.";
        return;
    }

    await next();
});

app.MapGet(FetchRoute.Path, (HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker invoker) =>
    FetchRoute.Handle(context, forwarder, invoker));

app.MapReverseProxy(pipeline => pipeline.Use(ModelRoute.RequireInternalToken(configuration.InternalToken)));

app.Run();

// The assembly is anchored for tests by Grimoire.Egress.EgressMarker, not by a Program type:
// two top-level-statement apps in one test process would collide on the global `Program` name.
