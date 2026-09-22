# Grimoire.Trace

The `trace-check` gate of Constitution IV.3, the single documented command that writes
`docs/trace.md` (IV.4), and the counts behind the requirements badge. Three verbs, one pair of
inputs, no state of its own.

```
Grimoire.Trace check [--complete] [--configuration <Debug|Release>]
Grimoire.Trace write            [--configuration <Debug|Release>]
Grimoire.Trace summary          [--configuration <Debug|Release>]
```

## What it reads

| Input | Where from | Read by |
| --- | --- | --- |
| Requirement ids and their proof kinds | the tables in `docs/capabilities/*.md` | `CapabilityRegistry` |
| The `level` and `req` traits on each test | the **built** test assemblies under `tests/*/bin/` | `TestCatalogue` |

Traits are read as **metadata**, with `System.Reflection.MetadataLoadContext`: no test is executed
and no runner is involved, which is what makes the check deterministic. Reading a runner's test
list was the obvious alternative and was rejected — xunit v3's `-list json` is not reachable
through the Microsoft.Testing.Platform entry point that .NET 10's `dotnet test` uses, so binding
the gate to a runner flag would tie it to a configuration detail (research.md R-05).

The check therefore needs the tree **built** before it runs. It says so rather than guessing when
it is not.

## `check` — the gate

Writes nothing. Constitution IV.3 names four conditions, and this check has those four and no
others:

| # | Condition | Where it runs |
| --- | --- | --- |
| 1 | a `test` requirement with no test carrying its id | `--complete` only |
| 2 | a test carrying an unknown, retired or reserved id (`OUT-*` and `DEC-*` are reserved by I.2) | every push |
| 3 | a test carrying no level | every push |
| 4 | an E2E or Deploy test carrying no requirement id | every push |

**Why the first one is split off.** IV.2 registers a requirement in `docs/capabilities/` *before*
its test is written, so "a `test` requirement with no test" is red by construction for as long as
a feature is in flight, and green only where the feature is done. IV.3 therefore applies it where
a feature lands on main, which is also where I.9 says a feature is done. `.github/workflows/ci.yml`
calls `check` on every push and `check --complete` only on a pull request whose base is `main`.
Running the fourth condition on every push would mean a permanently red pipeline nobody reads.

**It fails on what it cannot read, rather than skipping it.** A row in a capability file that
opens like a requirement and does not read as one in full — a lower-case or mis-numbered id, a
missing proof column, text after the last pipe — fails the read naming file, line and row. A
skipped row would drop its requirement out of the gate silently, which is the one thing IV.3 says
a check must not do. The same rule covers the command line: an argument the tool does not
understand ends the run, so a mistyped `--complet` cannot quietly run the weaker check and report
success.

| Exit code | Means |
| --- | --- |
| `0` | no violations |
| `1` | violations, listed on stderr |
| `2` | the tool could not read its input, or was called with arguments it does not understand |

## `write` — `docs/trace.md`

The single documented command of Constitution IV.4: requirement, proof kind, tests, level, status.
The file is committed when a feature closes, together with the outcome status and spec reference in
`docs/product.md` — the only two edits an agent makes to that file.

## `summary` — the counts behind the badge

JSON on stdout, nothing written, nothing judged:

```json
{
  "requirements": 16,
  "byProof": { "test": 15, "eval": 0, "review": 1 },
  "testRequirementsWithATest": 15
}
```

The same two readers as the gate, so the badge and the gate cannot disagree about what is
registered. Retired requirements are not counted: they keep their id (IV.2) and are no longer
requirements of the system. `scripts/metrics.sh` and the `metrics` job of
`.github/workflows/ci.yml` are what read this.

No count makes the verb fail, which is what keeps it a measurement and not a second gate (II.2).
It exits `2` on input it cannot read, like the other two verbs.

## The files

| File | What it is |
| --- | --- |
| `Program.cs` | the three verbs, the argument reading, the exit codes |
| `RepositoryLayout.cs` | where the repository keeps the two inputs; finds the root by `Grimoire.slnx` |
| `CapabilityRegistry.cs` | reads `docs/capabilities/`, fail-closed |
| `TestCatalogue.cs` | reads the traits off the built assemblies |
| `TraceCheck.cs` | the four conditions. Pure: it is handed the two lists and decides |
| `TraceDocument.cs` | renders `docs/trace.md` |
| `TraceSummary.cs` | counts the registered requirements. Pure, like the check |

`TraceCheck.Run` and `CapabilityRegistry.Read` are proven by the Fast suite, which feeds them
small in-memory lists — one test per condition above, and one per way a row can fail to read. The
gate is code we wrote, not framework behaviour, so III.8 does not exclude it from testing. It has
also been shown failing once on a real violation, which is what makes it count (II.2); the run is
linked from the PR that introduced it (#23).
