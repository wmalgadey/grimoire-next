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

**Amended after closing 004**: **Where** is a sixth condition word. It names a condition of the setting rather than of the moment — where the wiki is the vault, where a target leaves the wiki — which none of the five says as well; 004's tests had already used it eight times (test-audit.md §8).

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

## DEC-015 — Two ceilings, one stop, and cost means input-token equivalents

**Decision**: Elapsed time against `TimeProvider`; cost as the four token classes of **every** entry in the `result` message's `modelUsage` — all models and the CLI's own background calls included — **weighted 1 : 5 : 0.1 : 2** (input : output : cache read : cache write), counted live off `message_delta` and reconciled at the `result` by the same arithmetic. The weights are four named constants in `Ceilings.cs` and are not configurable; no absolute price is in the product code. At either ceiling the hub sends the same interrupt. Values 2 000 000 equivalents and 15 minutes; **the cost value is a placeholder** until the owner calibrates it after the acceptance run, and the four raw counts are kept per run so that it can be calibrated from what real runs actually caused rather than from an estimate.

**Reason**: The four classes are billed at those ratios, so **adding them up measures turns × context size and not cost**. Measured both ways round: ten million cache reads are 1 000 000 equivalents and reached the old raw ceiling of 2 000 000 five times over, while four hundred thousand output tokens are 2 000 000 equivalents — twice the money — and sat at a fifth of that same ceiling. The raw sum judged the first run twenty-five times the heavier where it in fact cost half as much: wrong by a factor of fifty, which is the ratio between the dearest class and the cheapest. So it stopped the cheap runs first and let the expensive ones run. It is a decent *thrash indicator*, because a run going round in circles re-reads its context every turn; `--max-turns` is the native form of that measure, and nothing asks for it today.

Cost cannot be counted in currency: `costUSD` exists only in the `result`, and a ceiling that can be checked only once the run is over is not a ceiling (`--max-budget-usd` was already rejected for being a client-side estimate). The weights are Anthropic's price *structure*, which every first-party model shares, so a weighted quantity is proportional to the money whatever model ran — which is what lets the tree hold no price. The proportion is held to the CLI by one sign-in contract test: measured on `claude-haiku-4-5-20251001`, 908 input / 121 output / 0 read / 6 753 written weighs to 15 019 equivalents, and 15 019 × the \$1 input list price per million is \$0.015019, which is the `costUSD` the CLI reported to the last digit. The cache write is weighted as a 1-hour write, the dearer of the two and the one the spike observed, so the weighting is uniformly conservative.

One mechanism for both ceilings because the agent loops model call → tool call → model call inside a turn, so nothing can prevent the next call without ending the one in flight. A background call the CLI makes is still the run's doing: a probe's `modelUsage` carried a Haiku entry the run never asked for beside its Opus one.

**Made by**: plan `001-first-ingest`; the weighting added 2026-09-26 (`001-first-ingest` research.md R-15).

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

**Decision**: The real `claude` CLI with the owner's real sign-in, at most four tests, marked `[Trait("requires", "signin")]`, run locally before the PR and excluded from CI with `--filter-not-trait "requires=signin"`.

**Reason**: III.4 puts a Contract suite against the real external thing, and the real external thing here is the CLI. CI has no subscription sign-in, so it cannot run them; a scripted endpoint would be a component we write and maintain that makes none of the CLI's observed behaviour more true (II.1). The cost — that their execution time is not measured in CI — is carried in the plan's Complexity Tracking.

**Made by**: plan `001-first-ingest`. Raised from three to four when GUARD-004's cost became weighted: the weights are ratios of Anthropic's prices, `costUSD` is the CLI's own reading of the same ratios, and it exists only on a real run — so nothing below this level can check them. A fourth cheap run is what that check costs.

## DEC-022 — Three generated project metrics, none of them a gate

**Decision**: CI shows three measurements of this repository, as shields.io endpoint badges read from an orphan badges branch that a metrics job force-pushes on every push to main: requirements proven (Grimoire.Trace summary, from docs/capabilities/ and the test traits), line coverage of the Fast suite over the projects that hold decisions of ours (Grimoire.Runs, Grimoire.Agent, Grimoire.Wiki without their adapters, Grimoire.Trace), and the time to read src/ (cloc's non-blank non-comment count at 20 lines per minute, rounded to 5 minutes). All three use one flat colour, no value is a threshold, and no number is written into README.md — only badge URLs are.

**Reason**: A number that can fail a build is a target, and a target is optimised rather than read (Goodhart); II.2 also makes a new gate an amendment, which these are not. The scope exclusions are what make the numbers legible: Grimoire.Hub is the composition root and the adapters are the ports to the outside, and III.8 tests neither, so their coverage is low by design and including it would only lower the number without saying anything about untested decisions. Every measurement comes from an existing tool (II.3) — Microsoft.Testing.Platform's coverage extension with ReportGenerator, and cloc — and the requirements count reuses the readers trace-check already has, so the badge and the gate cannot disagree. The mutation score gets no badge (superseded by the amendment below): scripts/mutation.sh is run by hand, one Stryker run per project, and a badge for a number nothing produces automatically would go stale silently, which is worse than no badge. It becomes a candidate for a fourth badge if and when its run is automated.

**Made by**: owner, after feature 001.

**Amended by `004-ask-the-wiki`**: the mutation run is no longer manual only. CI runs it as its own job and publishes a fourth badge; both existed since 002 without this entry, which is the drift the 004 test audit found. The job runs on push to main and dispatch, not on pull requests — with `Grimoire.Hub` in scope it takes about half an hour (31 min 36 s, measured on the first such run, 36951410197), and its score is a metric to read, not a gate to wait for. The three metrics of the original decision still run on every push to main; this one reports.

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

**Decision**: what the run cost, the four raw token counts behind that figure, the tool calls made and the entries the record could not hold are columns on the `runs` table, written whenever one of them changes and never derived from the Markdown.

**Reason**: the list polls once a second, and parsing prose Grimoire has just written to recover a number it already had is a seam. The figures are state, so they live where the state lives (DEC-023), and both they and the narrative are written from the same event, so they cannot disagree. Deriving them from the record at start-up was rejected: it would make the Markdown a data format, which DEC-027 kept it from being, and a record that could not be written would take the figures with it (research.md R-06).

**Made by**: plan `003-live-run-record` (research.md R-06). The four raw counts joined the cost on 2026-09-26, when the cost became a weighted quantity that cannot be unweighted (DEC-015); they are written in the same statement as it, so a restart cannot come back with one from a different moment than the other.

## DEC-031 — An existing state file gains columns through `PRAGMA table_info` and `ALTER TABLE`

**Decision**: after `CREATE TABLE IF NOT EXISTS`, the store reads `PRAGMA table_info(runs)` and issues one `ALTER TABLE runs ADD COLUMN` for each column it does not find. No version table, no ordered scripts, no ORM.

**Reason**: DEC-023 rejected a migration *framework* as a mechanism with no consumer, and that still holds — but a consumer for bringing an existing file up to date exists now: the owner's own `submissions.db`, holding the ingests they have already made. The alternative is asking them to delete it, which throws their list away to save eight lines. `IF NOT EXISTS` is already this file's idempotent-schema idiom, and `ADD COLUMN` is a metadata-only change. That a committed `ALTER TABLE` survives is SQLite's decision and is not tested; that an older file comes back with its submissions intact and its figures at zero is ours, and the Contract suite proves it (research.md R-07).

**Departs from**: DEC-023's "two tables that do not change shape" — stated here rather than silently broken.

**Made by**: plan `003-live-run-record` (research.md R-07).

**Amended by `004-ask-the-wiki`**: a `runs` table that declares `submission_id NOT NULL` — every file 002 and 003 wrote — cannot take a question's run, and `ADD COLUMN` cannot lift a constraint. Such a table is rebuilt once, by SQLite's own procedure (create `runs_new`, copy every row, drop, rename) inside one transaction, and only when `pragma_table_info` reports the constraint; the column additions then run as before. Before the rebuild the file is copied to `submissions.db.before-rebuild` beside it; if the rebuild fails, the error names the copy. After it, `PRAGMA user_version` is set to 1, and from here on the store tells one schema from another by that number, not by inspecting columns — the next feature that changes the schema raises it and adds its step.

Refusing the file was tried in `7a5aff0` and is reverted: it threw away exactly the list this decision exists to keep, the check that refused was as long as the rebuild that keeps, and the "owner decision" it cited was never recorded here. The two real files — the 002 schema and the 003 schema — are the fixtures that prove this; a file state no commit ever wrote is not.

**Departs from**: research.md R-04's "no table is rebuilt", which asked for something SQLite cannot do.

## DEC-033 — A record that cannot be written costs the run nothing; the gap is counted and shown

**Decision**: no member of `IRunRecord` throws. The adapter catches its own IO failures, counts them, and the count travels with the run's figures; the record says how many entries were lost once a write succeeds again, and the browser says lines are missing wherever the count is above zero.

**Reason**: the owner decided the run goes on and the gap is made visible. A throw out of the port would end the run, which is the opposite; a silent failure would leave the user unable to tell an unwritten record from an agent that did nothing. One count, written by the same call that writes the other two figures, makes the gap visible in both places the user looks (research.md R-10).

**One correction from implementation**: a run ends once, whether or not its tail reached the disk. Marking it ended only on a successful write was tried so that a lost tail could be written later — nothing writes it later, because a run has no moments after its end, and it left the record able to take a late moment with no tail behind it. A lost tail is one more entry counted.

**Made by**: plan `003-live-run-record` (research.md R-10).

## DEC-034 — The hub reads no configuration file, and does not watch for one

**Decision**: the hub's options come from its command line alone (`StartUp.Read`). The host is built without configuration files and without the file watcher the default builder installs for them.

**Reason**: `WebApplication.CreateBuilder` adds `appsettings.json` with reload-on-change, which installs a directory watcher on every start whether or not the file exists. The hub has no such file and no code that reads one; the watcher has no consumer (II.1). Measured on macOS it costs about 200 ms and most of the kernel time of every hub start — in the Fast suite, which builds a hub per test, 11 of 12 seconds of system time and half the wall-clock. Turning it off is removing machinery, not tuning tests: the suite merely made the cost visible.

**Consequence**: whichever mechanism is chosen must be explicit in `HubApplication.Build` — a reader must see that no configuration file is read — and must not silently drop anything the hub uses (console logging, Kestrel on loopback, routing).

**Made by**: owner, in `004-ask-the-wiki`.

## DEC-035 — The browser is sent what happens, over Server-Sent Events, and nothing polls

**Decision**: one `text/event-stream` per view, served by ASP.NET Core 10's own `TypedResults.ServerSentEvents` and read with `EventSource`. Each stream opens with a full snapshot and then carries increments; every event's `data` is one line of JSON. Polling is gone from `app.js` and `run.js` as well as from the chat.

**Reason**: the direction is one-way, which is the shape `EventSource` has. SignalR's browser client means npm or a vendored script, which DEC-019 rules out, and its duplex, transports and stateful reconnect have no consumer (II.1). Opening with a snapshot is what answers ACCESS-007's reconnect clause without a replay buffer. The server side is framework (`Microsoft.AspNetCore.App.Ref` 10), so no package is added and none of it is tested (III.8).

**Made by**: plan `004-ask-the-wiki` (research.md R-01). Supersedes DEC-032.

## DEC-036 — There is one chat, an object in the composition root, held in memory only

**Decision**: one `Chat` in the composition root for as long as the hub runs, replaced whole by a new chat. Nothing of it on disk, in the wiki or anywhere else. No port and no interface.

**Reason**: QUERY-005 makes "gone after a restart" a requirement, so a persistent implementation would contradict the spec rather than serve it. Nothing outside the process is involved and no second implementation exists, so II.4 allows no interface. Exactly one, so "a new chat empties both tabs" falls out rather than being built (research.md R-02).

**Made by**: plan `004-ask-the-wiki`.

## DEC-037 — Submissions and questions wait in one ordered list on `RunBoard`

**Decision**: `SubmissionBoard` became `RunBoard`, holding one ordered list of `Queued`, of which `Submission` and `Question` are the two kinds. The queue rule reads that one list; `RunBoard.All` still answers with the submissions alone.

**Reason**: RUNS-002 orders waiting work by when it was made across both kinds, and a list carries that order intrinsically — the same argument `TakeNext` already makes for list position over a clock that is not monotonic. Two lists would need a sequence number of our own beside the ordering the list already is. Two real implementations exist, which is when II.4 allows the abstraction. The board is named for what it holds, because a name naming one of its two kinds would be a comment that lies. Declined: *keeping the name* — no rename diff, at the cost of the one class that decides what may run being named after half of what it holds; *two lists and a counter* — no rename and no base class, at the cost of a second ordering mechanism beside the list that already is one (research.md R-03).

**Made by**: plan `004-ask-the-wiki`.

## DEC-038 — A question's run keeps its row on disk; the question does not

**Decision**: the `runs` table holds a question's run — identifier, start, grant, the agent's process identity, the figures — with `StoredRun.QueuedId` null where a question caused it, and `ended_at` marking that such a run has ended. The question's text, its answer and its steps are written nowhere.

**Reason**: RUNS-006 has a start-up terminate the agent of every run that was in progress before anything else runs; a run with nothing on disk would leave an orphaned `claude` holding the granted tools with no ceiling on it. The row is also where the answer's cost comes from (DEC-030), so nothing counts twice. How an older file is brought to this schema is DEC-031 as amended (research.md R-04).

**Made by**: plan `004-ask-the-wiki`.

## DEC-039 — The streams are fed by one hub-owned `LiveUpdates`, signalled by delegates

**Decision**: one `LiveUpdates`, a channel per subscriber bounded at one signal with `DropWrite`. `RunBoard` raises one `Changed` delegate the composition root supplies; `RunConductor` publishes a record's growth and the chat's. The record stream sends the bytes past a per-subscriber offset, read through `IRunRecord.Read`.

**Reason**: `RunConductor.NextRunMayStart` is already a delegate the root supplies so that a context can say something happened without knowing who listens — one precedent followed rather than a second mechanism (II.1). A signal carries no payload and the reader reads the current state when it wakes, so one pending signal says what a hundred would, and the write never blocks the board's lock. Reading through `IRunRecord.Read` keeps `MarkdownRunRecord` the only renderer of a record — two renderers of one record can disagree, the seam DEC-030 named (research.md R-05).

**Made by**: plan `004-ask-the-wiki`.

## DEC-040 — A question's run is served the two read tools at a door of its own

**Decision**: a question's run is dispatched at `/mcp/questions/{runId}`, where a session's tool catalogue is built from `WikiReadToolsServer` alone — `list_pages` and `read_page` — chosen per session through `HttpServerTransportOptions.ConfigureSessionOptions`. An ingest run keeps `/mcp/runs/{runId}` and its five tools. `ToolGrant` carries the door's segment beside the names.

**Reason**: DEC-011 makes tools deny-by-default by construction, not by an allow-list of names; naming two of five in `--allowed-tools` would leave `write_page` served at that run's endpoint, one flag away. Two `MapMcp` patterns do not give two catalogues — the library serves one collection at every pattern, measured — so the catalogue is chosen per session. GUARD-001's equality check then guards it for free, and no fresh signed-in probe is needed with DEC-021's budget spent (research.md R-06).

**Made by**: plan `004-ask-the-wiki`.

## DEC-041 — Every piece of the agent's own text is the answer; the steps are folded under it

**Decision**: the agent's text is appended to the answer as it arrives; the tool calls and what they returned are the steps, folded shut under it. Nothing new is parsed — `AgentTranscript` already reports the three moments (DEC-028).

**Reason**: ACCESS-007 binds three things at once, and the third decides it: content arriving must not move what the user is already reading. "The final turn's prose" cannot stream, and "the newest prose, demoted when a call follows" moves text the user has read. Of the rules that survive that, this is the one with no exception in it. *Only the final turn's prose* reads closer to the brief's walkthrough, but it cannot stream, which is the whole of US1; *everything but the opening block*, folded away as a preamble, is also decidable live and reads closer to the walkthrough, and was declined for the rule with no exception in it (research.md R-08).

**Made by**: plan `004-ask-the-wiki`.

## DEC-042 — A follow-up's run is given the whole chat so far, untrimmed, without the steps

**Decision**: `InstructionLoader` renders each earlier question and the answer it produced into the prompt. The steps are not included. Nothing is trimmed and there is no cap.

**Reason**: a chat too large for one dispatch ends that run failed and the chat says so (QUERY-006) — the path every failed run takes, with a remedy that exists (a new chat) — while dropping the oldest turns would answer a follow-up in the light of less than the chat shows, silently. A cap, window or summary has no consumer until a real chat reaches the limit (II.1). V.1 keeps one thing putting text into a prompt. The steps are for the user to check, not context the next run needs, and a run's tool results are the largest thing in a chat. Declined beside dropping the oldest turns: *refusing the question* — honest, but it needs a fourth refusal in QUERY-003 and so a requirement the spec does not have (research.md R-07).

**Amended after closing 004**: a question whose run got no answer goes into the next question's prompt as well — as asked, with "got no answer because <reason>" where its answer would stand. An agent that does not know the question was already asked and failed walks the same way again. The steps stay out, as before.

**Made by**: plan `004-ask-the-wiki`.

## DEC-043 — A page an answer names opens in Obsidian through a link the browser builds

**Decision**: two optional start-up inputs, `--vault <name>` and `--vault-root <directory>`; the link is `obsidian://open?vault=<name>&file=<path relative to --vault-root>`. The agent writes ordinary relative Markdown links whose target is the page's path relative to the wiki's root, and the browser makes them followable. Without the two inputs nothing is refused: the page's name stays readable and the page says opening is not set up.

**Reason**: nothing new goes into a wiki page, and the link form lives in one place. OKF §6.1 writes a link relative to the page it sits on and an answer sits on no page, so the wiki's root is the one anchor it has. The absolute-path form was rejected because it would take the directory the paths hang off away from the owner. The answer is an answer without the click, so a missing setting refuses nothing (research.md R-09).

**Made by**: plan `004-ask-the-wiki`.

## DEC-044 — The question instruction is Grimoire's own file, assembled by `InstructionLoader`

**Decision**: `instructions/question.md`, reached by `--question-instruction <path>` with a default and assembled by `InstructionLoader`. `StartUpInputs` carries a third flag: a question is refused on the question instruction and the purpose description, a submission on the ingest instruction and the purpose description, each refusal naming exactly one thing. Where the wiki holds nothing about the question, the instruction has the agent say so plainly, name what it looked at, and stop.

**Reason**: the same shape `--instruction` has, because both instructions are Grimoire's own and versioned here; V.1 keeps `InstructionLoader` the only thing that puts text into a prompt. An answer drawn from the model's own knowledge would not rest on the wiki, which QUERY-004 asks of it; the no-coverage clause refines that clause and adds no requirement id (IV.8). Declined: *answering from the model's own knowledge, marked as not from the wiki* — useful in the moment, but the answer would no longer rest on the wiki, and a marked sentence is a source with no page behind it (research.md R-10).

**Made by**: plan `004-ask-the-wiki`.

## DEC-045 — Asking the wiki is a third static page, and the three are joined by a line of links

**Decision**: `chat.html` and `chat.js` in `wwwroot/`, and a line of links on each of the three pages.

**Reason**: `docs/ux.md` withholds navigation chrome until a second job exists, and a third exists now — ACCESS-010 is its consumer. It stays what the pages already are: text-first, a line of links, no bar and no menu. DEC-019 is untouched: static files, no bundler and no npm (research.md R-14).

**Made by**: plan `004-ask-the-wiki`.

## Superseded

## DEC-032 — Reading a run is a second static page, polled and appended to, with no Markdown renderer

**Decision**: `run.html` and `run.js`, reached from the row by a link carrying the submission's identifier. It polls `GET /api/submissions/{id}/record`, which answers with the record's bytes, and renders one element per moment, appending only what is not already on the page and never replacing an element that is.

**Reason**: reading a run is a second job, which is when `docs/ux.md` allows a second page; it gives the back button and a shareable URL for nothing. Appending rather than re-rendering is what makes "arriving lines must not move what the user is reading" true without measuring anything: a segment already on the page is never touched, so the scroll holds and a folded result the user has opened stays open. DEC-019 rules out a bundler and npm, so there is no renderer to reach for, and `docs/ux.md` asks for monospace wherever the content is a log or a file. Range requests were weighed and left out: the poll is over loopback and a record in the low hundreds of kilobytes costs nothing there, so the mechanism has no consumer yet (research.md R-08).

**Made by**: plan `003-live-run-record` (research.md R-08).

**Superseded by**: DEC-035, in `004-ask-the-wiki`. Polling is gone from the run page and the submissions list as well as from the chat; the run page is still a second static page that appends and never replaces, and that part stands in DEC-035 and DEC-039.
