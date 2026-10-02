using Grimoire.Hub;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the hub refuses to start with, and what it derives from what it was given: the address it
/// may listen on (DEC-014), where its own bookkeeping may sit (RUNS-007), and where the wiki sits
/// inside the vault a reference is opened in (ACCESS-009).
/// </summary>
/// <remarks>
/// <para>
/// These are decisions rather than argument parsing (Constitution III.8): each refusal keeps a start
/// from doing something the owner would have to live with — a write grant served to the network, a
/// run told to reach tools at a port nobody knows yet, the queue deleted with the wiki, links that
/// address a place that is not there.
/// </para>
/// <para>
/// The paths are real ones under a directory of this test's own, because the guard compares paths as
/// the filesystem resolves them. None of the wiki, the vault or the state directory needs to exist —
/// on a first start the state directory usually does not — and on macOS the temporary directory is
/// itself reached through a link, so every comparison here is made on resolved paths.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class StartUpTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("grimoire-fast-startup-").FullName;

    private string Vault => Path.Combine(root, "vault");

    private string Wiki => Path.Combine(Vault, "wiki");

    private string State => Path.Combine(root, "state");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Start_IsAccepted_WithTheStateDirectoryOutsideTheWiki()
    {
        // The case every refusal below is told apart from: the same three inputs, a loopback address
        // with a port of its own, and the bookkeeping beside the wiki rather than in it.
        var startUp = StartUp.Read(With("--urls", "http://127.0.0.1:5057"));

        Assert.NotNull(startUp);
        Assert.Equal(new Uri("http://127.0.0.1:5057"), startUp.Address);
        Assert.Equal(State, startUp.StateDirectory);
    }

    [Theory]
    [InlineData("http://0.0.0.0:5057")]
    [InlineData("http://192.168.1.20:5057")]
    [InlineData("http://example.org:5057")]
    public void Start_IsRefused_WhenTheAddressIsNotLoopback(string address) =>
        // The run's tool endpoint grants writing into the wiki and asks for no token, so serving it
        // anywhere but loopback would hand that grant to the network (DEC-014).
        Assert.Null(StartUp.Read(With("--urls", address)));

    [Fact]
    public void Start_IsRefused_WhenThePortIsLeftToTheSystem() =>
        // Port 0 is picked while the server starts — after the agent has been told where its run's
        // tools are served — so every run would come up unable to reach one of them.
        Assert.Null(StartUp.Read(With("--urls", "http://127.0.0.1:0")));

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsRefused_WhenTheStateDirectoryIsTheWiki() =>
        Assert.Null(StartUp.Read(With("--state", Wiki)));

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsRefused_WhenTheStateDirectoryIsInsideTheWiki() =>
        // The records live in `runs/` under it, and a directory Grimoire owns is never inside the
        // wiki (RUNS-007) — the wiki store would list it and `--fresh` would delete it.
        Assert.Null(StartUp.Read(With("--state", Path.Combine(Wiki, "state"))));

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsRefused_WhenTheStateDirectoryReachesIntoTheWikiFromOutsideIt() =>
        // Spelled from beside the wiki and landing in it: compared as text, this would pass.
        Assert.Null(StartUp.Read(With("--state", Path.Combine(root, "elsewhere", "..", "vault", "wiki", "state"))));

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsRefused_WhenTheStateDirectoryReachesIntoTheWikiThroughALink()
    {
        // Spelled beside the wiki, through a link that exists and is a directory, not a file — and
        // lands in the wiki. Only resolving the link shows it: compared as spelled, `link/state` is
        // outside `vault/wiki`, and the queue would sit where the wiki store lists it.
        Directory.CreateDirectory(Wiki);
        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, Wiki);

        Assert.Null(StartUp.Read(With("--state", Path.Combine(link, "state"))));
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsRefused_WhenTheStateDirectoryReachesIntoTheWikiThroughALinkAboveIt()
    {
        // The link is not on the end but further up: `vlink` is the vault, and `vlink/wiki/state`
        // lies in the wiki although nothing below the link is one. Resolving only the deepest
        // directory that exists would leave the link spelled as it is and let the start through.
        Directory.CreateDirectory(Wiki);
        var link = Path.Combine(root, "vlink");
        Directory.CreateSymbolicLink(link, Vault);

        Assert.Null(StartUp.Read(With("--state", Path.Combine(link, "wiki", "state"))));
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public void Start_IsAccepted_WhenTheStateDirectoryOnlySharesTheWikisName()
    {
        // `wiki-state` begins with the wiki's path and is not inside it. A comparison that forgot the
        // separator would refuse a start the owner did nothing wrong with.
        var beside = Wiki + "-state";

        Assert.Equal(beside, StartUp.Read(With("--state", beside))?.StateDirectory);
    }

    [Fact]
    public void Start_IsRefused_WhenTheWikiIsOutsideTheVaultRoot() =>
        // A wiki outside the vault has no path inside it, so every link would address a place that is
        // not there. Refused rather than drawn silently wrong.
        Assert.Null(StartUp.Read(With("--vault", "Notes", "--vault-root", Path.Combine(root, "other"))));

    [Fact]
    [Trait("req", "ACCESS-009")]
    public void Start_FindsTheWikiInsideTheVaultRoot_WhenBothVaultSettingsAreGiven()
    {
        // What the owner gives is the directory they have open in Obsidian; what a link needs is where
        // the wiki sits **inside** it — `wiki`, not the absolute path the owner typed.
        var options = StartUp.Read(With("--vault", "Notes", "--vault-root", Vault))?.Options;

        Assert.NotNull(options);
        Assert.Equal("Notes", options.VaultName);
        Assert.Equal("wiki", options.WikiPathInVault);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public void Start_DerivesNoVault_WhenOnlyTheVaultRootIsGiven()
    {
        // Half the setting is none of it, and a setting that is not there refuses nothing: the start
        // goes ahead, and the browser says opening a page is not set up.
        var options = StartUp.Read(With("--vault-root", Path.Combine(root, "other")))?.Options;

        Assert.NotNull(options);
        Assert.Null(options.WikiPathInVault);
    }

    /// <summary>The three required inputs and a state directory beside the wiki, then <paramref name="more"/>.</summary>
    /// <remarks>A later <c>--name</c> replaces an earlier one, so a test overrides by appending.</remarks>
    private string[] With(params string[] more) =>
    [
        "--wiki", Wiki,
        "--purpose", Path.Combine(root, "purpose.md"),
        "--model", FastHub.Model,
        "--state", State,
        .. more,
    ];
}
