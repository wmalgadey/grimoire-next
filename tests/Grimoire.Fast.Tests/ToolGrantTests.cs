using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a run may reach, and what is recorded about it (GUARD-002, GUARD-003).
/// </summary>
/// <remarks>
/// The grant is the agent's entire tool surface, not a permission list over a larger one, which is
/// why <see cref="ToolGrant.IsTheSurface"/> is equality rather than containment. That the real CLI
/// actually reports this surface is the Contract suite's business (T025) — and the
/// <c>mcp__wiki__</c> spelling with it, since only <c>HarnessProcess</c> knows the prefix.
/// </remarks>
[Trait("level", "fast")]
public sealed class ToolGrantTests
{
    private readonly ToolGrant grant = ToolGrant.Ingest(FastSuite.Clock());

    [Fact]
    [Trait("req", "GUARD-002")]
    public void Grant_AllowsReadingAndWritingPagesIndexesAndTheLog() =>
        Assert.Equal(
            ["list_pages", "read_page", "write_page", "write_index", "append_log"],
            grant.ToolNames);

    [Theory]
    [InlineData("delete_page")]
    [InlineData("move_page")]
    [InlineData("rename_page")]
    [Trait("req", "GUARD-002")]
    public void Grant_LeavesOutDeletingAndMoving(string name) =>
        Assert.DoesNotContain(name, grant.ToolNames);

    [Fact]
    [Trait("req", "GUARD-003")]
    public void Grant_IsRecordedWithTheRun() =>
        Assert.Equal(FastSuite.Start, grant.RecordedAt);

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Surface_IsTheGrant_WhenItHoldsExactlyTheGrantedNames() =>
        Assert.True(grant.IsTheSurface(["append_log", "write_index", "write_page", "read_page", "list_pages"]));

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Surface_IsNotTheGrant_WithAToolOutsideIt() =>
        Assert.False(grant.IsTheSurface([.. grant.ToolNames, "delete_page"]));

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Surface_IsNotTheGrant_WithoutOneOfTheGrantedNames() =>
        Assert.False(grant.IsTheSurface(grant.ToolNames.Take(4)));
}
