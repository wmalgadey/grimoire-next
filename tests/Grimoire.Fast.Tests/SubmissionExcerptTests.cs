using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The opening of a submitted text, which is what a user tells one submission from another by
/// (ACCESS-004).
/// </summary>
/// <remarks>
/// The same length for every submission is what the requirement asks; 120 characters is this
/// plan's answer to it, and collapsing whitespace is what makes the first 120 characters of a
/// pasted document a sentence rather than an indented fragment (research.md R-07). The text
/// itself is kept whole and untidied — the agent receives what the user pasted.
/// </remarks>
[Trait("level", "fast")]
public sealed class SubmissionExcerptTests
{
    private readonly FastHub hub = new();

    private async Task<Submission> Of(string text) => await hub.AcceptedAsync(text);

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_IsTheWholeText_WhenItIsShorterThanTheCut()
    {
        var submission = await Of("Ada Lovelace wrote the first program.");

        // Nothing appended and nothing padded: a short text is shown whole.
        Assert.Equal("Ada Lovelace wrote the first program.", submission.Excerpt);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_IsTheWholeText_WhenItIsExactlyTheCut()
    {
        var text = new string('a', 120);

        var submission = await Of(text);

        Assert.Equal(text, submission.Excerpt);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_IsCutAndMarked_WhenTheTextIsLonger()
    {
        var submission = await Of(new string('a', 121));

        Assert.Equal(new string('a', 120) + "…", submission.Excerpt);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_IsTheSameLength_ForEveryLongSubmission()
    {
        var one = await Of(new string('a', 400));
        var other = await Of(string.Join(' ', Enumerable.Repeat("word", 200)));

        // "cut to the same length for every submission" — a list whose rows are one line each.
        Assert.Equal(one.Excerpt.Length, other.Excerpt.Length);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_CollapsesWhitespace()
    {
        var submission = await Of("  # A heading\n\n   Ada   Lovelace\twrote\r\nthe first program.  ");

        // A pasted document opens with its own indentation and blank lines; run together, its
        // first line reads as a sentence.
        Assert.Equal("# A heading Ada Lovelace wrote the first program.", submission.Excerpt);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_LeavesTheTextWhole()
    {
        const string Text = "  Ada   Lovelace\nwrote the first program.  ";

        var submission = await Of(Text);

        // Every run receives what the user pasted (INGEST-002); the excerpt is a reading of it,
        // not a replacement for it.
        Assert.Equal(Text, submission.Text);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task Excerpt_KeepsACharacterWhole_WhenTheCutFallsInsideOne()
    {
        // A character outside the basic plane is two chars wide, and the cut lands between its
        // halves. Half of one is not the opening of anything, so it is left out.
        var submission = await Of(new string('a', 119) + "𝄞" + "trailing");

        Assert.Equal(new string('a', 119) + "…", submission.Excerpt);
    }
}
