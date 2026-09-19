using System.Text;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// T044 / TS-04 (FR-029). The source is handed to the run <b>whole</b>: no size limit, no
/// truncation, no summarisation. Asserted against the bytes the scripted model actually received,
/// through a real spawned runner and the real SDK loop.
/// </summary>
/// <remarks>
/// "No maximum source size" is a clarified decision, not an oversight: a source too large for the
/// run surfaces as a failed run with that run's own reason, never as a rejected submission or a
/// quietly shortened prompt.
/// </remarks>
public sealed class SourcePassthroughTests
{
    [Theory]
    [InlineData(1_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public async Task SendsEveryByteOfTheSubmittedTextToTheRun(int byteLength)
    {
        var source = new string('s', byteLength);
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("echo-length");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText(source, TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // The source's recorded byte length is the submitted length, unreduced.
        Assert.Equal(byteLength, task.GetProperty("source").GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task ReachesTheModelWithTheSourceItselfRatherThanASummaryOfIt()
    {
        // A marker at the very end: a truncating implementation keeps the beginning and loses this.
        const string marker = "THE-LAST-THING-THE-SOURCE-SAYS";
        var source = new string('s', 250_000) + marker;

        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("echo-length");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText(source, TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var received = await model.RequestBodies(TestContext.Current.CancellationToken);
        Assert.NotEmpty(received);
        Assert.Contains(received, body => body.Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PassesMultiByteCharactersThroughUnmangled()
    {
        const string source = "日本語のメモ — Straße, naïve, 🜲 alchemical sulfur";

        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("echo-length");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText(source, TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Byte length, not character count: the recorded size is of the text handed to the run.
        Assert.Equal(Encoding.UTF8.GetByteCount(source),
            task.GetProperty("source").GetProperty("byteLength").GetInt32());
        Assert.Equal(source, task.GetProperty("source").GetProperty("submittedValue").GetString());
    }

    [Fact]
    public async Task AcceptsAnOversizedSourceRatherThanRejectingTheSubmission()
    {
        var source = new string('s', 5_000_000);
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("echo-length");
        using var hub = GrimoireHub.Start(wiki, model);

        var response = await hub.Submit("text", source, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }
}
