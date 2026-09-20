// The hub's entry point.
//
// It stays a host and nothing more until T031, the task that writes `HarnessProcess` — the adapter
// at `IAgentHarness` that runs the real `claude` CLI. `HubApplication.Build` takes that adapter as
// a parameter, so composing the hub for a real run is that task's business and the acceptance run's
// (quickstart.md). In this phase user story 1 is exercised by the E2E suite, which builds the same
// application with the in-memory adapter at the same port, exactly as the story's Independent Test
// describes (Constitution III.9).
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
