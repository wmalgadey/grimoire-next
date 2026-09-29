using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The vault the wiki is read in, and where the wiki sits inside it, as the chat stream opens with it (ACCESS-009).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class VaultTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Stream_OpensWithTheVaultTheWikiIsReadIn_WhenGrimoireWasToldBoth()
    {
        await using var hub = new HostedHub(vaultName: "Notes", wikiPathInVault: "wiki");

        var opening = await SnapshotAsync(hub);

        // Both settings as the owner gave them, so that the browser can build the link itself: the
        // vault to open, and the wiki's own path inside it, which a reference's target hangs off
        // (ACCESS-009).
        Assert.Equal(new VaultView("Notes", "wiki"), opening.Vault);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Stream_OpensWithNoVault_WhereGrimoireWasNotToldBoth()
    {
        await using var hub = new HostedHub();

        // The field is **absent**, not null: the browser then shows a page's name as plain text and
        // says opening is not set up, and a key that was there holding nothing would be a setting it
        // had to interpret (ACCESS-009).
        Assert.DoesNotContain("vault", await SnapshotFieldsAsync(hub));
    }

    [Theory]
    [Trait("req", "ACCESS-009")]
    [InlineData("/home/me/Vault", "/home/me/Vault/wiki", "wiki")]
    [InlineData("/home/me/Vault", "/home/me/Vault/notes/wiki", "notes/wiki")]
    [InlineData("/home/me/Vault", "/home/me/Vault", "")]
    [InlineData("/home/me/Vault/", "/home/me/Vault/wiki/", "wiki")]
    public void Vault_CarriesWhereTheWikiSitsInsideIt(string vaultRoot, string wiki, string inside)
    {
        // What the owner gives is the directory they have open in Obsidian; what a link needs is the
        // wiki's path **within** it. Passed straight through, an absolute filesystem path went into a
        // link that addresses a place inside a vault, and every reference pointed at nothing
        // (ACCESS-009, quickstart.md).
        Assert.Equal(inside, VaultView.InVaultPathOf(vaultRoot, wiki));
    }

    [Theory]
    [InlineData("/home/me/Vault", "/home/me/elsewhere/wiki")]
    [InlineData("/home/me/Vault/wiki", "/home/me/Vault")]
    [InlineData("/home/me/Vault", "/etc/wiki")]
    public void Vault_IsNothing_WhereTheWikiIsNotInsideIt(string vaultRoot, string wiki)
    {
        // A wiki outside the vault has no path inside it, so there is nothing to build a link from.
        // The entry point refuses such a start rather than drawing links that address a place that is
        // not there — silently drawing none would leave the owner wondering why a setting they gave
        // does nothing.
        Assert.Null(VaultView.InVaultPathOf(vaultRoot, wiki));
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public void Vault_IsCarried_WhereTheWikiIsTheVaultItself()
    {
        // An empty path is a **value**, not a missing one: it is what the wiki being the vault makes,
        // and a reference's target then stands alone. Read as blank, that ordinary setup would draw no
        // links and say opening was not set up (ACCESS-009).
        Assert.Equal(new VaultView("Notes", string.Empty), VaultView.FromStartUp("Notes", string.Empty));
    }

    [Theory]
    [Trait("req", "ACCESS-009")]
    [InlineData("Notes", null)]
    [InlineData(null, "wiki")]
    [InlineData(null, null)]
    [InlineData("  ", "wiki")]
    public void Vault_IsNothing_WhereOnlyOneOfTheTwoWasGiven(string? name, string? wikiPath)
    {
        // **Both or neither.** Half the setting is the same as none of it: a vault with no path inside
        // it addresses the wrong place, and a path inside a vault nobody named addresses nothing.
        //
        // Read without a server, because this is which inputs make a vault at all — a decision of ours.
        // That what it decides then reaches the browser, present or absent, is the two tests above it,
        // and that is the boundary (Constitution III.6).
        Assert.Null(VaultView.FromStartUp(name, wikiPath));
    }

    /// <summary>
    /// Which fields the snapshot carries, read off the JSON itself: <c>vault</c> is written only when
    /// there is one, and a deserialised null cannot tell a field that is absent from one sent as null.
    /// </summary>
    private static async Task<IReadOnlyList<string>> SnapshotFieldsAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        return FieldsOf(data);
    }
}
