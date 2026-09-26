# Decisions in force

<!-- DEC-NNN, in order of assignment, never renumbered, never reused. DEC is not a capability name.
     Every entry: decision, reason (a constraint or evidence, never just "owner decision"), made by. -->

## DEC-001 — Models are paid for through the owner's Claude subscription sign-in, never per token

**Decision**: Every model call goes through Claude Code's subscription sign-in. Locally the stored sign-in of the machine is used; Grimoire handles no credentials itself. An API key must never reach the agent process: `ANTHROPIC_API_KEY` is removed from its environment.

**Reason**: This project is meant to run on Claude Pro/Max subscription usage; per-token API billing is not acceptable for it. Subscription authentication is available only through Claude Code (the CLI, or the Agent SDK that wraps it), not through the API directly — a client calling the API with the subscription token is served Haiku only.

**Risk**: Anthropic changed its terms for this path several times in 2026 and may enforce them without notice. **Fallback**: an API-key adapter behind the same agent port; nothing outside that adapter may know which one is in use.

**Made by**: owner, before feature 001.

## DEC-002 — The stack is .NET 10, for the hub, the tools and all three test suites

**Decision**: ASP.NET Core on .NET 10 for the hub, the tools and every test suite. No TypeScript, no `npm`, no bundler anywhere in the tree.

**Reason**: Every requirement proven by `test` lands in the hub, so the hub's language is where the tests are. The two things that had required a second toolchain are gone — the agent is driven through the `claude` CLI rather than a TypeScript SDK (DEC-009), and the browser surface is two static files rather than a built front end (DEC-019). A toolchain with no requirement behind it is a mechanism with no consumer (II.1).

**Made by**: plan `001-first-ingest`.

## DEC-003 — A test carries its level as an xunit v3 trait

**Decision**: `[Trait("level", "fast" | "contract" | "e2e" | "deploy")]`, on the test class.

**Reason**: Native to the runner every suite uses, and readable from assembly metadata without running anything, which is what DEC-005 needs.

**Made by**: plan `001-first-ingest`.

## DEC-004 — A test carries its requirement id as an xunit v3 trait

**Decision**: `[Trait("req", "<CAPABILITY>-NNN")]`, repeated where a test proves more than one.

**Reason**: Same mechanism and same reader as DEC-003, and it repeats, which a single attribute property would not.

**Made by**: plan `001-first-ingest`.

## DEC-005 — `trace-check` reads the traits as metadata, never through a runner

**Decision**: `System.Reflection.MetadataLoadContext` over the built test assemblies. No test is executed and no runner is involved; the tree must be built before the check runs, and the check says so rather than guessing.

**Reason**: IV.3 demands one deterministic check that writes nothing. xunit v3's `-list json` is not exposed through the Microsoft.Testing.Platform entry point that .NET 10's `dotnet test` uses, so binding the gate to a runner flag would tie it to a configuration detail.

**Made by**: plan `001-first-ingest`.

## DEC-006 — How a test is named

**Decision**: A test class is `<Subject>Tests`, the subject an entity from the spec's vocabulary; a test method is `<Action>_<Result>[_<Scenario>]`, PascalCase parts separated by one underscore, the scenario opening with When / While / With / Without / After. Names use the spec's vocabulary, never the implementation's; the result is an observable outcome; a name that needs `And` is two tests; requirement ids live in the `req` trait and never in a name. Written down once in `tests/README.md`.

**Reason**: A test name is read in `docs/trace.md` and in CI failure output, where one glance has to show which behaviour broke and under which condition. Taking the subject from the spec rather than from a class means a rename in the code cannot make a name wrong, which is what keeps a test tied to the requirement it proves (III.1). CA1707 forbids the underscores and is switched off for the test projects alone, in `tests/.editorconfig`.

**Made by**: plan `001-first-ingest`.

## DEC-007 — The complexity ceiling is the SDK's own rule, as an absolute error

**Decision**: CA1502 as an **error** at a threshold of **15** for everything under `src/` and `tools/`; the suites are excluded. An absolute ceiling, not a delta against a baseline.

**Reason**: An absolute ceiling needs no baseline file and no tooling of our own to keep one (II.3); it is four lines of configuration the analyzer already reads. Set while the code is small it never needs an exception list. It is not a gate in the constitution's sense (II.2) and gets no CI job: the build already fails on analyzer errors.

**Made by**: plan `001-first-ingest`.

## DEC-008 — `time-budget` is the test platform's own session timeout

**Decision**: Microsoft.Testing.Platform's `--timeout`: `15s` on Fast, `90s` on Contract.

**Reason**: III.7 asks for the simplest means the stack offers. `--timeout` is a first-class platform option, it measures the test session rather than the build, and it fails the run. No tooling of our own measures this.

**Made by**: plan `001-first-ingest`.

## DEC-009 — The model is reached by spawning the `claude` CLI per run

**Decision**: The `claude` CLI in headless mode, spawned per run as a child process of the hub by `HarnessProcess`, speaking newline-delimited JSON over stdin and stdout.

**Reason**: DEC-001 requires the owner's subscription sign-in and rules out API-token billing, and the CLI is the sign-in path — `apiKeySource: "none"` observed, with no API key in the environment. The TypeScript SDK is itself a wrapper that spawns this same binary, so it would add a toolchain without adding a capability (II.1).

**Made by**: plan `001-first-ingest`.

## DEC-010 — A run names a pinned model id and runs without an API key

**Decision**: `--model` with a pinned model id, never an alias and never the default; `ANTHROPIC_API_KEY` removed from the child process's environment.

**Reason**: A run is reproducible and its cost attributable only if the model is fixed: an alias follows whatever it is pointed at, and the default follows the owner's own `model` setting — a machine setting of exactly the kind DEC-012 exists to keep out. Observed: with a pinned id and the key unset, `modelUsage` came back keyed by that id and `apiKeySource` was `none`, so the run was on the subscription. Leaving the key set would bill per token through an API key, which DEC-001 rules out.

**Made by**: plan `001-first-ingest`.

## DEC-011 — Tools are deny-by-default by construction, not by an allow-list of names

**Decision**: `--tools ""` plus `--mcp-config` and `--strict-mcp-config`, with `--allowed-tools "mcp__wiki__*"` and `--permission-mode dontAsk`.

**Reason**: Observed: with these flags `system/init` reported a tool array holding only the granted MCP names — the whole tool surface is the grant. An allow-list of names is not a grant; the CLI reference says of `--allowedTools`, "To restrict which tools are available, use `--tools` instead". This is deny-by-default by construction rather than an enumeration of built-ins that rots (V.3).

**Made by**: plan `001-first-ingest`.

## DEC-012 — No setting from the machine reaches a run

**Decision**: `--setting-sources ""`, `--strict-mcp-config`, and a working directory Grimoire owns rather than the wiki. **Not** `--bare`.

**Reason**: V.1 allows only the instruction and the purpose description into the agent's prompt. The documentation states that without such flags `claude -p` loads the same context an interactive session would, including anything configured in the working directory, and a probe confirmed a `CLAUDE.md` leaking in without the flag and staying out with it. `--bare` would also close it but "doesn't use your subscription login", which DEC-001 forbids.

**Made by**: plan `001-first-ingest`.

## DEC-013 — The wiki tools are served by the hub over MCP, at a per-run endpoint

**Decision**: The hub serves them over streamable HTTP (`ModelContextProtocol.AspNetCore`) at `/mcp/runs/{runId}`; the CLI is pointed at it.

**Reason**: Puts provenance stamping, the grant, the grant record and both ceilings in one language where Fast tests prove them, and leaves `trace-check` one test format to read rather than two.

**Made by**: plan `001-first-ingest`.

## DEC-014 — The per-run tool endpoint is unauthenticated

**Decision**: `/mcp/runs/{runId}`, bound to loopback, no token.

**Reason**: `docs/product.md` §2 puts Grimoire inside a network the user trusts and gives it no access control of its own. A per-run token would be half an access-control story with no consumer (II.1); the run identifier in the path is addressing, not authorisation. The first feature that puts Grimoire on an untrusted network (OUT-10, OUT-12) must revisit this.

**Made by**: plan `001-first-ingest`.

## DEC-015 — Two ceilings, one stop, and cost means every token the run causes

**Decision**: Elapsed time against `TimeProvider`; cost as the four token fields of **every** entry in the `result` message's `modelUsage`, all models and the CLI's own background calls included, counted live off `message_delta` and reconciled at the `result`. At either ceiling the hub sends the same interrupt. Initial values 2 000 000 tokens and 15 minutes, revised by the owner after the acceptance run.

**Reason**: GUARD-004 counts model tokens, and a background call the CLI makes is the run's doing: a probe's `modelUsage` carried a Haiku entry the run never asked for beside its Opus one. One mechanism for both ceilings because the agent loops model call → tool call → model call inside a turn, so nothing can prevent the next call without ending the one in flight. `--max-budget-usd` is currency from a client-side estimate the documentation says can differ from the bill, and `--max-turns` is the wrong quantity.

**Made by**: plan `001-first-ingest`.

## DEC-016 — A run is stopped by an interrupt; killing the process is the backstop

**Decision**: `control_request` / `interrupt` on stdin, with killing the process as the backstop.

**Reason**: GUARD-004 requires the run stopped at once at either ceiling, a model call in flight included. Observed: an interrupt ended a response in flight in about 0.9 s with `terminal_reason: "aborted_streaming"`, leaving the process able to serve a further message; SIGTERM is documented to leave the turn unfinished, so it is the backstop, not the mechanism.

**Made by**: plan `001-first-ingest`.

## DEC-017 — The nudge is a second user message inside the same run

**Decision**: Written on the CLI's stdin under `--input-format stream-json`.

**Reason**: RUNS-005 needs the agent told once and allowed to continue inside the same run. Observed: a second message after the first `result` continued the same `session_id`, and the agent's answer referred to its own earlier turn.

**Made by**: plan `001-first-ingest`.

## DEC-018 — Elapsed time in tests comes from `TimeProvider`

**Decision**: `TimeProvider` everywhere time is read, with `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) in the Fast suite.

**Reason**: The Fast budget is 15 s for the whole suite (III.7), and GUARD-004's ceiling is the one requirement that would otherwise make a test wait for real seconds. `TimeProvider` is the framework's own abstraction, so no interface of ours is created for it (II.4) and the provider itself is not tested (III.8).

**Made by**: plan `001-first-ingest`.

## DEC-019 — The browser surface is static files the hub serves, with no build step

**Decision**: One HTML page and one script, served from `wwwroot/`. No Vite, no `npm`, no bundler.

**Reason**: The browser surface is one form (ACCESS-001) and one list of states (ACCESS-002); a bundler for two files is a mechanism with no consumer (II.1). Static content is not tested (III.8), and both requirements are browser-observable, so the E2E suite proves them.

**Made by**: plan `001-first-ingest`.

## DEC-020 — The E2E driver is Playwright for .NET

**Decision**: `Microsoft.Playwright.Xunit.v3`, a real browser against the running hub.

**Reason**: Keeps every test in one format, so `trace-check` has one reader rather than two (DEC-005).

**Made by**: plan `001-first-ingest`.

## DEC-021 — The agent adapter's Contract suite runs against the real CLI, outside CI

**Decision**: The real `claude` CLI with the owner's real sign-in, at most three tests, marked `[Trait("requires", "signin")]`, run locally before the PR and excluded from CI with `--filter-not-trait "requires=signin"`.

**Reason**: III.4 puts a Contract suite against the real external thing, and the real external thing here is the CLI. CI has no subscription sign-in, so it cannot run them; a scripted endpoint would be a component we write and maintain that makes none of the CLI's observed behaviour more true (II.1). The cost — that their execution time is not measured in CI — is carried in the plan's Complexity Tracking.

**Made by**: plan `001-first-ingest`.

## DEC-022 — Three generated project metrics, none of them a gate

**Decision**: CI shows three measurements of this repository, as shields.io endpoint badges read from an orphan badges branch that a metrics job force-pushes on every push to main: requirements proven (Grimoire.Trace summary, from docs/capabilities/ and the test traits), line coverage of the Fast suite over the projects that hold decisions of ours (Grimoire.Runs, Grimoire.Agent, Grimoire.Wiki without their adapters, Grimoire.Trace), and the time to read src/ (cloc's non-blank non-comment count at 20 lines per minute, rounded to 5 minutes). All three use one flat colour, no value is a threshold, and no number is written into README.md — only badge URLs are.

**Reason**: A number that can fail a build is a target, and a target is optimised rather than read (Goodhart); II.2 also makes a new gate an amendment, which these are not. The scope exclusions are what make the numbers legible: Grimoire.Hub is the composition root and the adapters are the ports to the outside, and III.8 tests neither, so their coverage is low by design and including it would only lower the number without saying anything about untested decisions. Every measurement comes from an existing tool (II.3) — Microsoft.Testing.Platform's coverage extension with ReportGenerator, and cloc — and the requirements count reuses the readers trace-check already has, so the badge and the gate cannot disagree. The mutation score gets no badge: scripts/mutation.sh is run by hand, one Stryker run per project, and a badge for a number nothing produces automatically would go stale silently, which is worse than no badge. It becomes a candidate for a fourth badge if and when its run is automated.

**Made by**: owner, after feature 001.

## DEC-023 — The submissions live in SQLite, behind a port of the RUNS context

**Decision**: `Microsoft.Data.Sqlite`, raw SQL over two tables in one file inside a directory Grimoire owns (`--state <path>`, defaulting to `state/` beside the hub), behind `ISubmissionStore` declared by the RUNS context with `SqliteSubmissionStore` under `Grimoire.Runs/Adapters/`. No ORM and no migration framework. The port's members are synchronous, unlike every other port in the tree.

**Reason**: RUNS-004 covers *any* stop — a crash, a forced kill, a power cut — so nothing may depend on a shutdown step having run: every change has to be on disk before Grimoire answers for it. Getting that right by hand means fsync ordering and torn-record recovery, which are decisions a dependency has already made, and III.8 says we test our decisions rather than a dependency's. `Microsoft.Data.Sqlite` is first-party, with no native install step and no server. An ORM was rejected because it brings a migration mechanism for two tables that never change shape here, which is a mechanism with no consumer (II.1); a journal of our own was rejected as storage tooling where an existing thing does the job (II.3). The port is synchronous because the board writes each change under the one lock it decides the queue rule with, and a lock cannot be held across an await — and because SQLite's provider writes synchronously underneath, so an async signature would promise a yielding call that never yields. The file is not inside the wiki: the queue writes nothing into the wiki, and Grimoire's bookkeeping in the user's repository would show up in the version history that is their only undo.

**Made by**: plan `002-ingest-queue` (research.md R-01, R-02).

## DEC-024 — A run's agent is recognised by its process identifier *and* its start time

**Decision**: The pair is recorded with the run as soon as the child exists, and at start-up a process is terminated only where a live process carries that identifier **and** that start time. Both halves come from `System.Diagnostics.Process`; the act reuses the tree kill `HarnessProcess` already performs, and it sits at the agent port because only that adapter knows what a process is.

**Reason**: RUNS-006 has Grimoire terminate an agent that outlived a stop it could not act on, and an identifier alone is not an identity: operating systems reuse those numbers, and after a reboot one almost certainly belongs to something else — terminating it would kill an unrelated program on the owner's machine, which is the one failure in this feature that does damage outside Grimoire. Two processes sharing an identifier *and* a start time to the tick do not occur, and a reboot changes every start time, so the pair handles the reboot case with no rule of its own. Matching the process name as well would work but would make the proof need a real `claude` and therefore a sign-in (DEC-021), putting it outside CI; the pair is already exact.

**Made by**: plan `002-ingest-queue` (research.md R-11).

## DEC-025 — Every PR's second reviewer is GitHub Copilot code review, requested by the repository

**Decision**: Copilot code review is requested automatically by a repository rule, once per pull request: when a PR is opened ready for review, or when a draft is marked ready. It does not run again on a push. Every further round is requested by the implementing agent, by re-requesting the reviewer on the PR. No CI job of ours starts the review; the repository setting does.

**Reason**: Constitution I.11 requires a reviewer other than the agent that wrote the PR and ties the start of review to leaving draft. The implementing agent is a Claude session, so a review by a different vendor's model is a distinct reviewer with no memory of the change, which is what "other than" means here. Observed on PR #42: the `copilot-pull-request-reviewer` check run appeared the moment the PR left draft, with no workflow of ours involved. Configuring it as a repository setting rather than a workflow means no code of ours (II.1) and no gate (II.2); its findings are classified per Governance 3 and never fail CI on their own.

**Made by**: owner, on amending the constitution to 2.0.0.

## DEC-026 — A run's record is a port of the RUNS context, written to `<state>/runs/<runId>.md`

**Decision**: `IRunRecord`, declared by the RUNS context, with one adapter `MarkdownRunRecord` writing one Markdown file per run into `runs/` inside the directory `--state` names. The Fast suite has an in-memory adapter at the same port.

**Reason**: the record is what a run *did*, so it belongs to the context that owns what a run is; the filesystem is an external system and so appears only inside that context's adapter (V.2), which is the shape DEC-023 gave `ISubmissionStore`. It sits beside the SQLite file rather than in the wiki because Grimoire's bookkeeping in the user's repository would turn up in the version history that is their only undo — and `Program.cs` already refuses a `--state` inside the wiki, so the guard is inherited rather than written twice (research.md R-01). Writing it from the Agent context, where the stream is read, was rejected: that adapter reports and decides nothing, and which moments are worth recording is the hub's.

**Made by**: plan `003-live-run-record` (research.md R-01).

## DEC-027 — A record is a head at the start, moments appended, and a tail at the end; never rewritten

**Decision**: the frame is split. What is known when the run begins is written then, the narrative is appended as the run proceeds, and what only the ending knows is appended when it ends. No byte already on disk is ever moved.

**Reason**: RUNS-007 has the record appended and never rewritten, and an append is the cheapest write there is to make survive a stop — the concern DEC-023 settled for the queue. A frame patched in place would need the file rewritten at the end, which is what would make a run cut off by a stop unreadable: the one case the record matters most. It is also what makes "a live run and a run from last month look the same" true — the live one has no tail yet, and that *is* the difference (research.md R-02).

**Made by**: plan `003-live-run-record` (research.md R-02).

## DEC-028 — What a run did is read from the CLI's complete messages, in `AgentTranscript`

**Decision**: every `tool_use` and `text` block of a complete `assistant` message, and every `tool_result` block of a complete `user` message, read in `AgentTranscript` and nowhere else. `thinking` blocks are not read. The nudge is appended by the hub, which knows it nudged, and never read back off the stream.

**Reason**: `contracts/agent-cli-protocol.md` specified the `assistant` message's `tool_use` blocks in `001-first-ingest` and only now has a consumer. Measured on `claude` 2.1.283: the complete message arrives for every block, so nothing has to be assembled from the partial stream, which stays the cost ceiling's alone; and stdin messages are not echoed on stdout, so a `user` line is a tool result and the nudge cannot appear twice. A `thinking` block carries an empty string and a signature blob, which is nothing a person reads (research.md R-03, R-05).

**One correction from implementation**: one message can carry several `tool_use` blocks, with their results arriving together in the next `user` message. The outstanding tool names are kept as a queue and each result takes the oldest, so order alone still attributes them and no `tool_use_id` reaches the record.

**Made by**: plan `003-live-run-record` (research.md R-03).

## DEC-029 — A tool result is kept whole, in a fence one backtick longer than anything inside it

**Decision**: the result goes into the record byte for byte, inside a fenced block whose fence is a run of backticks one longer than the longest run of backticks in the content, and at least three.

**Reason**: the owner decided nothing is cut — a record that silently loses part of a result is not a record of what the run did. That leaves the reader to protect, and two of them have to be: a person in an editor and `run.js`. A fixed fence breaks on a result containing a fence, which a run reading wiki pages full of code will produce; CommonMark's own longer-fence rule makes the block unambiguous for any content, alters nothing, and gives the browser a deterministic segmentation rule. Escaping was rejected because it changes what the tool returned, which is the one thing a record must not do (research.md R-04).

**Made by**: plan `003-live-run-record` (research.md R-04).

## DEC-030 — The run's figures are columns on the run row, never parsed back out of the record

**Decision**: the tokens spent, the tool calls made and the entries the record could not hold are three columns on the `runs` table, written whenever one of them changes and never derived from the Markdown.

**Reason**: the list polls once a second, and parsing prose Grimoire has just written to recover a number it already had is a seam. The figures are state, so they live where the state lives (DEC-023), and both they and the narrative are written from the same event, so they cannot disagree. Deriving them from the record at start-up was rejected: it would make the Markdown a data format, which DEC-027 kept it from being, and a record that could not be written would take the figures with it (research.md R-06).

**Made by**: plan `003-live-run-record` (research.md R-06).

## DEC-031 — An existing state file gains columns through `PRAGMA table_info` and `ALTER TABLE`

**Decision**: after `CREATE TABLE IF NOT EXISTS`, the store reads `PRAGMA table_info(runs)` and issues one `ALTER TABLE runs ADD COLUMN` for each column it does not find. No version table, no ordered scripts, no ORM.

**Reason**: DEC-023 rejected a migration *framework* as a mechanism with no consumer, and that still holds — but a consumer for bringing an existing file up to date exists now: the owner's own `submissions.db`, holding the ingests they have already made. The alternative is asking them to delete it, which throws their list away to save eight lines. `IF NOT EXISTS` is already this file's idempotent-schema idiom, and `ADD COLUMN` is a metadata-only change. That a committed `ALTER TABLE` survives is SQLite's decision and is not tested; that an older file comes back with its submissions intact and its figures at zero is ours, and the Contract suite proves it (research.md R-07).

**Departs from**: DEC-023's "two tables that do not change shape" — stated here rather than silently broken.

**Made by**: plan `003-live-run-record` (research.md R-07).

## DEC-032 — Reading a run is a second static page, polled and appended to, with no Markdown renderer

**Decision**: `run.html` and `run.js`, reached from the row by a link carrying the submission's identifier. It polls `GET /api/submissions/{id}/record`, which answers with the record's bytes, and renders one element per moment, appending only what is not already on the page and never replacing an element that is.

**Reason**: reading a run is a second job, which is when `docs/ux.md` allows a second page; it gives the back button and a shareable URL for nothing. Appending rather than re-rendering is what makes "arriving lines must not move what the user is reading" true without measuring anything: a segment already on the page is never touched, so the scroll holds and a folded result the user has opened stays open. DEC-019 rules out a bundler and npm, so there is no renderer to reach for, and `docs/ux.md` asks for monospace wherever the content is a log or a file. Range requests were weighed and left out: the poll is over loopback and a record in the low hundreds of kilobytes costs nothing there, so the mechanism has no consumer yet (research.md R-08).

**Made by**: plan `003-live-run-record` (research.md R-08).

## DEC-033 — A record that cannot be written costs the run nothing; the gap is counted and shown

**Decision**: no member of `IRunRecord` throws. The adapter catches its own IO failures, counts them, and the count travels with the run's figures; the record says how many entries were lost once a write succeeds again, and the browser says lines are missing wherever the count is above zero.

**Reason**: the owner decided the run goes on and the gap is made visible. A throw out of the port would end the run, which is the opposite; a silent failure would leave the user unable to tell an unwritten record from an agent that did nothing. One count, written by the same call that writes the other two figures, makes the gap visible in both places the user looks (research.md R-10).

**One correction from implementation**: a run ends once, whether or not its tail reached the disk. Marking it ended only on a successful write was tried so that a lost tail could be written later — nothing writes it later, because a run has no moments after its end, and it left the record able to take a late moment with no tail behind it. A lost tail is one more entry counted.

**Made by**: plan `003-live-run-record` (research.md R-10).

## Superseded
