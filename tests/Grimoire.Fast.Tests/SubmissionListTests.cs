using System.Text.Json;
using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the list response carries about a run's cost: the figure, and the ceiling it is written
/// against (ACCESS-005, GUARD-004).
/// </summary>
/// <remarks>
/// In process, through <c>HubApplication.Build</c>, because the ceiling is put on the response by
/// the endpoint and by nothing the domain holds. What the browser then <em>draws</em> — the two
/// numbers as one cell, and the row not moving as the left one grows — is the E2E half of
/// ACCESS-005 and is geometry only a real browser has.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "ACCESS-005")]
public sealed class SubmissionListTests
{
    [Fact]
    public async Task List_SaysWhatARunHasSpentAndTheCeilingItIsHeldTo()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);
        hub.Agent.Spend(submission, 148_233);

        var listed = await hub.GetAsync("/api/submissions");
        using var body = JsonDocument.Parse(
            await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The ceiling is on the list and not on each row: it is the hub's value and the same for
        // every run in it.
        Assert.Equal(Ceilings.Fixed.Cost, body.RootElement.GetProperty("costCeiling").GetInt64());

        // And the figure beside it is what the run has spent — the same quantity, so that the two
        // can be read as one against the other. A figure with no ceiling beside it says nothing:
        // input-token equivalents have no unit and no scale a reader carries in their head.
        var row = body.RootElement.GetProperty("submissions").EnumerateArray().Single();
        Assert.Equal(148_233, row.GetProperty("costSpent").GetInt64());
    }
}
