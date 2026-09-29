using Grimoire.Agent.Adapters;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs.Adapters;
using Grimoire.Wiki.Adapters;
using Microsoft.AspNetCore.Builder;

// The hub's entry point, and the only place the three real adapters are put at their ports: the
// filesystem wiki store, the `claude` process, and the SQLite submission store (Constitution V.2).
// Every suite builds the same application through `HubApplication.Build` with in-memory adapters
// (III.9), which is why nothing here is covered by a test: dependency wiring is not tested (III.8).
// What the arguments are refused for is a decision and lives in StartUp.cs, where the Fast suite
// reads it. What exercises this file is the owner's acceptance run (quickstart.md).

var startUp = StartUp.Read(args);

if (startUp is null)
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

    // The records live in `runs/` inside the same directory, which is already guarded against sitting
    // inside the wiki (contracts/run-record.md, RUNS-007).
    new MarkdownRunRecord(startUp.StateDirectory),
    TimeProvider.System);

app.Run();

return 0;
