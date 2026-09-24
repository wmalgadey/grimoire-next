using Grimoire.Agent.Adapters;
using Grimoire.Hub;
using Grimoire.Runs.Adapters;
using Grimoire.Wiki.Adapters;
using Microsoft.AspNetCore.Builder;

// The hub's entry point, and the only place the three real adapters are put at their ports: the
// filesystem wiki store, the `claude` process, and the SQLite submission store (Constitution V.2).
// Every suite builds the same application through `HubApplication.Build` with in-memory adapters
// (III.9), which is why nothing here is covered by a test: dependency wiring and argument reading
// are not tested (III.8). What exercises this file is the owner's acceptance run (quickstart.md).

if (StartUp.Read(args) is not { } startUp)
{
    Console.Error.WriteLine(StartUp.Usage);
    return 1;
}

// The address is settled before the application is built, because the agent has to be told where
// this hub serves its run's tools and a port the operating system picks would not be known until
// after the server was already listening.
var app = HubApplication.Build(
    ["--urls", startUp.Address.ToString()],
    startUp.Options,
    new HarnessProcess(HarnessSettings.Default(startUp.Address)),
    new FileSystemWikiStore(startUp.Options.WikiRoot),
    new SqliteSubmissionStore(startUp.StateDirectory),
    TimeProvider.System);

app.Run();

return 0;

/// <summary>
/// What the owner starts the hub with. Three inputs are theirs — the wiki, the purpose description
/// and the model — and the instruction is Grimoire's own, versioned in this repository, so it has a
/// default and changing it is an owner decision named in the PR (Constitution V.1).
/// </summary>
internal sealed record StartUp(HubOptions Options, Uri Address, string StateDirectory)
{
    /// <summary>
    /// Where the queue is kept so that it survives a stop (RUNS-004). Beside the hub rather than
    /// inside the wiki: the queue writes nothing into the wiki, and Grimoire's bookkeeping in the
    /// user's repository would show up in their version history (contracts/submission-store.md).
    /// </summary>
    public static string DefaultStateDirectory => Path.Combine(AppContext.BaseDirectory, "state");

    /// <summary>
    /// Loopback, because the run's tool endpoint carries no token: the hub sits inside a network
    /// the user trusts and has no access control of its own (`docs/product.md` §2, DEC-014).
    /// </summary>
    public const string DefaultAddress = "http://127.0.0.1:5057";

    public const string Usage = """
        Grimoire — the hub.

          --wiki <path>         the wiki this Grimoire writes into                     (required)
          --purpose <path>      the hand-written description of what the wiki is for   (required)
          --model <id>          a pinned model id, never an alias                      (required)
          --instruction <path>  Grimoire's own instruction  (default: instructions/ingest.md)
          --state <path>        where the queue is kept, so that it survives a stop
                                (default: state/ beside the hub)
          --urls <url>          where the hub listens; loopback only
                                (default: http://127.0.0.1:5057)
        """;

    public static StartUp? Read(string[] arguments)
    {
        var given = Named(arguments);

        if (given.GetValueOrDefault("wiki") is not { } wiki
            || given.GetValueOrDefault("purpose") is not { } purpose
            || given.GetValueOrDefault("model") is not { } model)
        {
            return null;
        }

        var instruction = given.GetValueOrDefault("instruction")
            ?? Path.Combine(AppContext.BaseDirectory, "instructions", "ingest.md");

        if (!Uri.TryCreate(given.GetValueOrDefault("urls") ?? DefaultAddress, UriKind.Absolute, out var address)
            || !address.IsLoopback)
        {
            // Refused rather than obeyed: the run's tool endpoint grants writing into the wiki and
            // asks for no token, so serving it anywhere but loopback would hand that grant to the
            // network. The first outcome that puts Grimoire on an untrusted network revisits this.
            Console.Error.WriteLine("The hub serves an unauthenticated tool endpoint and listens on loopback only.");
            return null;
        }

        if (address.Port == 0)
        {
            // Port 0 asks the operating system to pick, and it picks while the server starts —
            // after the agent has already been told where its run's tools are served. The run
            // would come up unable to reach a single one of them.
            Console.Error.WriteLine("The hub needs a port of its own: the agent is told where its tools are before the server starts.");
            return null;
        }

        var state = given.GetValueOrDefault("state") ?? DefaultStateDirectory;

        if (IsInside(state, wiki))
        {
            // Refused rather than obeyed, because both consequences are the user's to live with:
            // the wiki store lists every non-hidden file it finds, so the queue would be served to
            // the agent as a page — and `run-hub.sh --fresh` deletes the wiki, which would take
            // every submission the user ever made with it (contracts/submission-store.md).
            Console.Error.WriteLine("The queue is Grimoire's own bookkeeping and is not kept inside the wiki. Give --state a directory outside it.");
            return null;
        }

        return new StartUp(
            new HubOptions(instruction, purpose, wiki, model),
            address,
            state);
    }

    /// <summary>
    /// Whether one path lies within the other, the wiki itself counting as inside.
    /// </summary>
    /// <remarks>
    /// Compared as <em>real</em> paths, not as text. <c>GetFullPath</c> collapses <c>..</c> and
    /// nothing else, so a state directory reached through a symbolic link would read as outside the
    /// wiki while sitting in it — which is how `submissions.db` would end up where the wiki store
    /// lists it and `--fresh` deletes it. <c>FileSystemWikiStore</c> refuses a path that leaves
    /// through a link for the same reason, on the same kind of check.
    /// <para>
    /// Compared without case, because the filesystems Grimoire is developed and run on do not
    /// distinguish it and .NET offers no portable way to ask. That errs towards refusing, which is
    /// the safe direction for a guard: the cost of a wrong refusal is one start-up argument.
    /// </para>
    /// </remarks>
    private static bool IsInside(string path, string directory)
    {
        var inside = RealPathOf(path);
        var outer = RealPathOf(directory);

        return string.Equals(inside, outer, StringComparison.OrdinalIgnoreCase)
            || inside.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A path with every link along it resolved. Neither directory need exist yet — the state
    /// directory usually does not on a first start — so the nearest ancestor that does is resolved
    /// and what was below it is put back on.
    /// </summary>
    private static string RealPathOf(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var below = new Stack<string>();
        var at = full;

        while (!Directory.Exists(at) && !File.Exists(at))
        {
            if (Path.GetDirectoryName(at) is not { } parent || parent == at)
            {
                return full;
            }

            below.Push(Path.GetFileName(at));
            at = parent;
        }

        var resolved = new DirectoryInfo(at).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? at;

        return Path.TrimEndingDirectorySeparator(Path.Combine([resolved, .. below]));
    }

    /// <summary>
    /// `--name value` pairs, which is the whole of what this entry point understands. An argument
    /// it does not understand is left out rather than guessed at, and a required one missing is
    /// what makes the run refuse to start.
    /// </summary>
    private static Dictionary<string, string> Named(string[] arguments)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i + 1 < arguments.Length; i++)
        {
            // A blank value is no value: `--wiki ""` is the input missing, not a wiki at the empty
            // path, and it is refused below with the usage rather than crashing the host on its
            // way up.
            if (arguments[i].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(arguments[i + 1]))
            {
                named[arguments[i][2..]] = arguments[i + 1];
            }
        }

        return named;
    }
}
