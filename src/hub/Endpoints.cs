using System.Text;
using Grimoire.Dispatch;
using Grimoire.Ingest;
using Grimoire.Ingest.Adapters;
using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Microsoft.AspNetCore.Mvc;
using Task = System.Threading.Tasks.Task;

namespace Grimoire.Hub;

/// <summary>
/// The three task endpoints (FR-001, FR-020, FR-021, FR-022, FR-030), each deriving its shape from
/// <c>contracts/hub-api.openapi.yaml</c> and diffed against it in CI (ADR-0008).
/// </summary>
public static class Endpoints
{
    /// <summary>The task list's default and largest page, as the contract fixes them.</summary>
    private const int DefaultLimit = 50;

    private const int MaxLimit = 200;

    /// <summary>Maps the task surfaces.</summary>
    public static WebApplication MapTaskEndpoints(this WebApplication app)
    {
        app.MapPost("/api/tasks", SubmitSource)
            .WithName("submitSource")
            .WithTags("Tasks")
            .WithSummary("Submit a source and get back exactly one task")
            .Produces<Contracts.TaskSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/tasks", ListTasks)
            .WithName("listTasks")
            .WithTags("Tasks")
            .WithSummary("List every retained task, newest first")
            .Produces<Contracts.TaskList>(StatusCodes.Status200OK);

        app.MapGet("/api/tasks/{taskId}", GetTask)
            .WithName("getTask")
            .WithTags("Tasks")
            .WithSummary("Open a task in any of its five states")
            .Produces<Contracts.TaskDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> SubmitSource(
        Contracts.Submission submission,
        SqliteStore store,
        RunQueue queue,
        UrlFetch urlFetch,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Grimoire.Hub.Endpoints");

        if (submission.Kind is not ("text" or "url"))
        {
            return Problem("A submission is either pasted text or a URL.",
                $"'{submission.Kind}' is not a submission kind.", StatusCodes.Status400BadRequest);
        }

        var kind = submission.Kind is "text" ? SourceKind.Text : SourceKind.Url;
        var accepted = Submission.Accept(kind, submission.Value);
        if (!accepted.IsAccepted)
        {
            // No task is created: the submission never became work (FR-001).
            return Problem("The submission was empty.", accepted.Rejection!, StatusCodes.Status400BadRequest);
        }

        var task = TaskCreation.Create(accepted.Source!, DateTimeOffset.UtcNow);
        store.AddTask(task);
        logger.LogInformation("grimoire.task.created {TaskId} {Kind}", task.Id, submission.Kind);

        if (kind is SourceKind.Url)
        {
            // Retrieved before dispatch, so a run is never given a source that does not exist.
            // Deliberately awaited: the task is already created and openable, and the caller is
            // told about a refusal in the same breath as the task (FR-003).
            await RetrieveBeforeDispatch(task, store, urlFetch, logger, cancellationToken);
            var retrieved = store.GetTask(task.Id)!;
            if (retrieved.State is TaskState.Failed)
            {
                return Results.Created($"/api/tasks/{task.Id}", TaskProjection.ToSummary(retrieved));
            }
        }

        queue.Enqueue(task.Id);
        return Results.Created($"/api/tasks/{task.Id}", TaskProjection.ToSummary(store.GetTask(task.Id)!));
    }

    private static IResult ListTasks(SqliteStore store, [FromQuery] int? limit, [FromQuery] string? cursor)
    {
        var page = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        try
        {
            var tasks = store.ListTasks(page, cursor);
            return Results.Ok(new Contracts.TaskList(
                [.. tasks.Tasks.Select(TaskProjection.ToSummary)],
                tasks.NextCursor));
        }
        catch (ArgumentException)
        {
            return Problem("The cursor is not one this hub issued.",
                "Start again without a cursor.", StatusCodes.Status400BadRequest);
        }
    }

    private static IResult GetTask(string taskId, SqliteStore store, WikiMutation wiki)
    {
        var task = store.GetTask(taskId);
        if (task is null)
        {
            return Problem("No such task.", $"There is no task '{taskId}'.", StatusCodes.Status404NotFound);
        }

        return Results.Ok(Detail(task, wiki));
    }

    /// <summary>
    /// Builds the task view's body: the task, its diff read back from history, and whether revert
    /// is offered.
    /// </summary>
    internal static Contracts.TaskDetail Detail(Grimoire.Tasks.Task task, WikiMutation wiki)
    {
        // Derived from the commit on read, never stored, so the artifact cannot drift from what
        // the wiki actually contains (FR-022).
        var diffs = task.Run?.Commit is { } commit ? wiki.DiffOf(commit.Sha) : [];
        var verdict = RevertEligibility.For(
            task.Run?.Commit?.Sha, task.State is TaskState.Reverted, wiki.Tip());
        return TaskProjection.ToDetail(
            task, diffs, new Contracts.RevertEligibilityDetail(verdict.Eligible, verdict.Reason));
    }

    private static async Task RetrieveBeforeDispatch(
        Grimoire.Tasks.Task task,
        SqliteStore store,
        UrlFetch urlFetch,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = await urlFetch.Retrieve(task.Source.SubmittedValue, cancellationToken);

        if (!result.Succeeded)
        {
            // The task fails with the reason and no run is dispatched. `retrievedText` stays
            // permanently null (FR-003).
            store.FailTask(task.Id, result.Failure!, DateTimeOffset.UtcNow);
            logger.LogInformation(
                "grimoire.task.state_changed {TaskId} {State} {FailureReason}", task.Id, "failed", result.Failure);
            return;
        }

        store.AttachRetrievedText(
            task.Id, result.Text!, DateTimeOffset.UtcNow, Encoding.UTF8.GetByteCount(result.Text!));
    }

    private static IResult Problem(string title, string detail, int status) =>
        Results.Problem(detail: detail, title: title, statusCode: status);
}
