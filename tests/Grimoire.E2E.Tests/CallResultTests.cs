using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// What a call returned, as a run's page shows it: in the entry of the call it answers, folded until
/// the user opens it, then whole — and one segment however its own lines read (RUNS-009, ACCESS-006).
/// </summary>
/// <remarks>
/// Both tests read one run, recorded once by <see cref="OneRecordedResult"/>, and differ only in what
/// they assert about it.
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-006")]
public sealed class CallResultTests(CallResultTests.OneRecordedResult run)
    : PageTest, IClassFixture<CallResultTests.OneRecordedResult>
{
    /// <summary>
    /// A result long enough that folding it is the point, and holding a fence and a heading of its
    /// own — which is what a run reading wiki pages full of code returns, and what the record's fence
    /// rule exists for (research.md R-04).
    /// </summary>
    private const string ALongResult =
        "## Ada Lovelace\n\n```\nthe first program\n```\n\nShe wrote the first program.";

    [Fact]
    [Trait("req", "RUNS-009")]
    public async Task Result_IsShownWhole_OnceTheUserOpensIt()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — run.js builds the fold, and
        // "folded" is the details element's open state, which only a browser has.
        await Page.GotoAsync(run.Page);

        // The result sits in the entry of the call it answers, folded under its one line.
        var result = Page.Locator("#record li").Nth(0).Locator("details.result");

        // Folded: the user can follow what the run did without reading the results in full.
        await Expect(result.Locator("pre")).Not.ToBeVisibleAsync();

        await result.Locator("summary").ClickAsync();

        // And whole once they reach for it — the fence and the heading inside it included, because
        // nothing of a result is cut and nothing is escaped (RUNS-009).
        await Expect(result.Locator("pre")).ToBeVisibleAsync();
        await Expect(result.Locator("pre")).ToHaveTextAsync(ALongResult);
    }

    [Fact]
    public async Task Result_IsOneSegment_WhenItHoldsALineStartingWithTwoHashes()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — the record's fence rule is
        // applied by run.js, in the page.
        await Page.GotoAsync(run.Page);

        // The result holds `## Ada Lovelace` at column one. A reader finds the fence first and skips
        // to its close, so that line is no boundary and the result stays one moment
        // (contracts/run-record.md, rule 4). Two entries: the call with its answer, and the tail.
        var segments = Page.Locator("#record li");

        await Expect(segments).ToHaveCountAsync(2);
        await Expect(segments.Nth(0).Locator("> .heading")).ToContainTextAsync("called read_page");
        await Expect(segments.Nth(0).Locator("details.result")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// One run that called one tool and was given <see cref="ALongResult"/>, recorded once for both
    /// tests above.
    /// </summary>
    /// <remarks>
    /// Constitution III.4 says two tests with the same setup that differ only in their assertion are
    /// one test. That "one test" is read as <b>one setup</b>: where joining the two would give a name
    /// that needs <c>And</c> — which <c>tests/README.md</c> says is two tests — they stay two methods,
    /// and share the setup through this fixture instead of making it twice. <c>tests/README.md</c>
    /// holds unchanged.
    /// </remarks>
    public sealed class OneRecordedResult : IAsyncLifetime
    {
        private HubUnderTest? hub;

        /// <summary>The run's page, for the submission that caused it.</summary>
        public string Page { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            var token = TestContext.Current.CancellationToken;
            hub = await HubUnderTest.StartAsync(token);

            var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
            hub.Agent.ReportIn(submission);
            hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
            hub.Agent.Returned(submission, "read_page", ALongResult);
            hub.Agent.End(submission, RunOutcome.Done);

            Page = $"{hub.Address}/run.html?submission={submission}";
        }

        public async ValueTask DisposeAsync()
        {
            if (hub is not null)
            {
                await hub.DisposeAsync();
            }
        }
    }
}
