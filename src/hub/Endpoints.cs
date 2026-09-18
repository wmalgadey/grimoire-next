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
            // Kestrel refuses a body over ~30 MB by default. A source has no maximum size (FR-029),
            // so this is the one endpoint whose body is not capped; nothing else here takes one.
            .WithMetadata(new DisableRequestSizeLimitAttribute())
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

        app.MapPost("/api/tasks/{taskId}/revert", RevertTask)
            .WithName("revertTask")
            .WithTags("Tasks")
            .WithSummary("Restore the wiki to its state before this task's run")
            .Produces<Contracts.TaskDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static IResult SubmitSource(
        Contracts.Submission submission,
        SqliteStore store,
        RunQueue queue,
        ILoggerFactory loggerFactory)
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

        // A URL is retrieved as the first step of its task's dispatch, not here: the task is visible
        // at once whatever the origin's speed (SC-001), and keeps its place in submission order
        // (FR-019). A run is still never given a source that does not exist (FR-003).
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

    private static IResult GetTask(
        string taskId, SqliteStore store, WikiMutation wiki, ILoggerFactory loggerFactory)
    {
        var task = store.GetTask(taskId);
        if (task is null)
        {
            return Problem("No such task.", $"There is no task '{taskId}'.", StatusCodes.Status404NotFound);
        }

        return Results.Ok(Detail(task, wiki, loggerFactory.CreateLogger("Grimoire.Hub.Endpoints")));
    }

    /// <summary>
    /// Restores the wiki to the content the task's run changed, as a new commit (FR-025), and
    /// marks the task reverted.
    /// </summary>
    /// <remarks>
    /// Eligibility is decided twice on purpose. The first decision answers the request and names
    /// the refusal; the second happens inside <see cref="WikiMutation.RevertIfStillTip"/> under the
    /// single-writer lock, where the tip cannot move underneath it. A double-click or a second
    /// browser tab therefore loses the race and is refused rather than reverting twice (FR-024,
    /// FR-026).
    /// </remarks>
    private static async Task<IResult> RevertTask(
        string taskId,
        SqliteStore store,
        WikiMutation wiki,
        RunQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Grimoire.Hub.Endpoints");

        var task = store.GetTask(taskId);
        if (task is null)
        {
            return Problem("No such task.", $"There is no task '{taskId}'.", StatusCodes.Status404NotFound);
        }

        // A run in flight holds the working tree, and its writes are uncommitted. Reverting now
        // would resolve the revert against that tree and sweep the running agent's half-finished
        // work into the revert commit — one commit carrying two runs' changes, which is the
        // property the single-writer lock cannot defend on its own: the lock serialises git
        // commands, not the agent writing files between them (FR-015, FR-019).
        if (queue.IsRunning)
        {
            return Problem(
                "This ingest cannot be reverted right now.",
                "A run is in progress and is writing into the wiki. Try again once it has ended.",
                StatusCodes.Status409Conflict);
        }

        var tip = wiki.Tip();
        var verdict = Eligibility(task, tip, logger);
        if (!verdict.Eligible)
        {
            return Problem("This ingest cannot be reverted.", Explain(verdict.Reason), StatusCodes.Status409Conflict);
        }

        string? revertCommitSha;
        try
        {
            revertCommitSha = await wiki.RevertIfStillTip(task.Run!.Commit!.Sha, tip, cancellationToken);
        }
        catch (WikiRevertFailedException exception)
        {
            // The wiki is back at the tip it had before the attempt; the task is unchanged and can
            // be reverted again once whatever stopped git is fixed.
            logger.LogError(exception, "The revert of task {TaskId} could not be completed.", taskId);
            return Problem(
                "The revert could not be completed.",
                $"The wiki is unchanged at {tip}, and the task can be reverted again. "
                + "The reason is in the hub's log.",
                StatusCodes.Status500InternalServerError);
        }

        if (revertCommitSha is null)
        {
            // The tip moved between the check and the lock. Same fact as `superseded`, reached a
            // few milliseconds later, and the reader is told the same thing. Recorded for its own
            // sake: the decision changed between the two reads, and that is what the row says.
            _ = Eligibility(task, wiki.Tip(), logger);
            return Problem("This ingest cannot be reverted.", Explain(RevertEligibility.Superseded),
                StatusCodes.Status409Conflict);
        }

        if (!RecordRevert(store, taskId, revertCommitSha, logger))
        {
            return Problem(
                "The revert landed in the wiki but could not be recorded on the task.",
                $"The wiki now has the revert commit {revertCommitSha}, but this task does not show it. "
                + "An operator needs to reconcile the task with wiki history.",
                StatusCodes.Status500InternalServerError);
        }

        logger.LogInformation("grimoire.wiki.reverted {TaskId} {RevertCommitSha}", taskId, revertCommitSha);
        logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", taskId, "reverted");

        return Results.Ok(Detail(store.GetTask(taskId)!, wiki, logger));
    }

    /// <summary>
    /// Records a revert commit on its task. The commit is already a fact of wiki history that
    /// nothing here can undo, so a failure to record it is retried and then said loudly, rather
    /// than leaving a commit no task accounts for in silence (FR-025) — the same containment the
    /// dispatcher applies to a run's commit.
    /// </summary>
    private static bool RecordRevert(SqliteStore store, string taskId, string revertCommitSha, ILogger logger)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (store.RecordRevert(taskId, new RevertRecord(revertCommitSha, DateTimeOffset.UtcNow)))
                {
                    return true;
                }

                logger.LogCritical(
                    "The revert commit {RevertCommitSha} exists in the wiki, but task {TaskId} was no longer "
                    + "a completed task when it was recorded. An operator needs to reconcile the task with "
                    + "wiki history by hand.",
                    revertCommitSha, taskId);
                return false;
            }
            catch (Exception exception) when (attempt < 3)
            {
                logger.LogWarning(exception,
                    "Retrying: the revert commit {RevertCommitSha} for task {TaskId} exists in the wiki but "
                    + "recording it failed on attempt {Attempt}.",
                    revertCommitSha, taskId, attempt);
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception,
                    "The revert commit {RevertCommitSha} exists in the wiki, but task {TaskId} could not be "
                    + "updated to reflect it after {Attempts} attempts. An operator needs to reconcile the "
                    + "task with wiki history by hand.",
                    revertCommitSha, taskId, attempt);
                return false;
            }
        }
    }

    /// <summary>
    /// Builds the task view's body: the task, its diff read back from history, and whether revert
    /// is offered.
    /// </summary>
    internal static Contracts.TaskDetail Detail(
        Grimoire.Tasks.Task task, WikiMutation wiki, ILogger logger)
    {
        // Derived from the commit on read, never stored, so the artifact cannot drift from what
        // the wiki actually contains (FR-022).
        var diffs = task.Run?.Commit is { } commit ? wiki.DiffOf(commit.Sha) : [];
        var verdict = Eligibility(task, wiki.Tip(), logger);
        return TaskProjection.ToDetail(
            task, diffs, new Contracts.RevertEligibilityDetail(verdict.Eligible, verdict.Reason));
    }

    /// <summary>
    /// Decides whether revert is offered and records the decision. One emission point, so the row
    /// and the field the surface renders cannot disagree (constitution IV).
    /// </summary>
    private static RevertEligibility.Verdict Eligibility(
        Grimoire.Tasks.Task task, string tip, ILogger logger)
    {
        var verdict = RevertEligibility.For(
            task.Run?.Commit?.Sha, task.State is TaskState.Reverted, tip);
        logger.LogInformation(
            "grimoire.wiki.revert_eligibility {TaskId} {Eligible} {Reason}",
            task.Id, verdict.Eligible, verdict.Reason);
        return verdict;
    }

    /// <summary>
    /// The refusal in the reader's words. `superseded` is the one that has to explain itself: it
    /// is not a failure, it is the boundary of what undo reaches (FR-027).
    /// </summary>
    private static string Explain(string? reason) => reason switch
    {
        RevertEligibility.NoCommit =>
            "This run produced no commit, so there is nothing to undo.",
        RevertEligibility.Superseded =>
            "This ingest was superseded by a later wiki commit. Undo reaches one ingest back, not further.",
        RevertEligibility.AlreadyReverted =>
            "This ingest has already been reverted.",
        _ => "Revert is not available for this task.",
    };

    private static IResult Problem(string title, string detail, int status) =>
        Results.Problem(detail: detail, title: title, statusCode: status);
}
