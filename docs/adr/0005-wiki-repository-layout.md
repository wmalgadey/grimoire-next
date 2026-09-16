# ADR-0005: Wiki repository layout and mutation path

## Status

Accepted

## Context

The wiki is markdown in git, and the constitution fixes the mutation semantics: every wiki mutation
is a commit the system can revert without manual intervention (II.1), and no filesystem write may
bypass that commit path (II.2). What is left to decide is the repository's physical arrangement and
how an agent's writes reach a commit.

Properties any arrangement has to provide, independent of which agent is running or what it was
asked to do:

- A run contributes **exactly one commit or none**. Never a partial series, never two.
- A run that fails, is abandoned, or is killed leaves **history untouched** and prior content intact.
- The previous state is restorable **without manual intervention**, as a new commit, leaving earlier
  history readable rather than rewritten.
- What a run changed is **derivable from history**, so the record shown to an operator cannot drift
  from what the repository actually contains.
- Agents need a real directory to read and write; the mechanism cannot depend on the agent
  cooperating with it.

Two forward-looking forces matter more than any current constraint:

- **Multiple agent kinds.** A read-only agent will eventually want to consult the wiki while other
  work is in flight. Whatever any agent reads must be a *consistent* view, never another run's
  half-finished writes.
- **Concurrency of writers is a content problem before it is a git problem.** Two agents editing
  prose in the same wiki at once produce a conflict no merge strategy resolves, so serialised writing
  is a property of the domain rather than a limitation of a particular layout. What could change is
  the desire to *stage* a run — produce its result in isolation and decide afterwards whether to keep
  it — which is a different requirement and is called out in the alternatives below.

## Decision

The wiki is **one ordinary git repository**, checked out on its main branch, with a clean working
tree between runs.

- A run's working directory is that repository; its granted tools read and write inside it.
- Run end, content changed: `git add -A` then `git commit` — one commit, harness-authored, message
  supplied by the agent.
- Run end, failed / abandoned / killed / limit reached: `git reset --hard HEAD && git clean -fd`,
  no commit. The same reset runs at startup, as the backstop for a process that died mid-run.
- Undo is `git revert --no-edit <sha>` in that working tree; eligibility is a `git rev-parse HEAD`
  comparison.
- **Reads that must be consistent come from committed state** (`git show <ref>:<path>`), not from the
  working tree. A write-capable run reads its own working tree because it must see its own edits; any
  agent that is not the writing run reads the commit.

git is invoked as a CLI child process, confined to one adapter.

## Consequences

- The mutation path is four git commands. Every failure path is short enough to hold in mind, and
  there is no lifecycle object whose cleanup can itself fail.
- `git revert` needs a working tree, and this arrangement has one.
- Because a single process writes and runs are serialised, tip checking is a plain `rev-parse`; no
  compare-and-swap, no lock file.
- **Containment is compensating rather than structural.** A failed run's writes are removed after the
  fact rather than having been unreachable all along. The cleanup is on both the run-end path and the
  startup path, so a dying agent process and a dying orchestrator are both covered — but it is
  cleanup, and that is the honest cost of this arrangement.
- Uncommitted writes are invisible to everything that reads committed state, which is every surface
  and every non-writing agent. An operator inspecting the volume directly between a crash and the
  next startup would see them.
- Diffs are derived from history on demand, so the record cannot drift from the repository.
- **Trigger to revisit**: wanting to stage or preview a run's result before accepting it, or to run
  write-capable agents concurrently. Both need per-run isolation, which this arrangement does not
  have and the first alternative below does. The change is contained in one adapter.

## Alternatives Considered

**Bare repository with a per-run worktree, and a compare-and-swap ref update** — **why not:** *pays a
lifecycle tax on every run and every failure path to buy isolation that nothing in the system can
currently observe.*

The architecturally stronger option, and what this should become if isolation is ever needed. Each
run gets its own checkout of the tip, so a partial write is not merely uncommitted but unreachable
from any ref — containment becomes structural, with no cleanup path that could fail, and "prior
content is intact after a failure" holds by construction rather than by remembering to reset. The
compare-and-swap makes a concurrent tip move a detected conflict rather than a silent overwrite, so
it extends to concurrent runs and to staging a result without publishing it. What it costs is a
lifecycle: creating and removing worktrees on every path including those taken by a crashing process,
a directory root and a volume to hold them, bare-repository handling for every read, and — because a
bare repository has no working tree — a temporary worktree created solely to perform a revert. With
serialised writes and consistent reads taken from commits, nothing can currently see the difference.
It is the right answer the moment per-run isolation has a caller.

**A branch per run, fast-forwarded into the main branch on success** — **why not:** *gives no
isolation between concurrent runs — the thing isolation is actually for — while still adding branch
lifecycle.*

A middle path needing no worktree: the run commits onto `run/<id>`, success fast-forwards the main
branch, failure deletes the branch, and an abandoned run leaves a nameable, inspectable artifact
rather than nothing. But a single working tree can only be on one branch at a time, so concurrency is
no better off, and a garbage-collection question about abandoned branches appears. A run that commits
incrementally to its branch then needs squashing to satisfy the one-commit rule, inheriting the next
option's problem.

**A full clone per run** — **why not:** *copies object storage per run and still needs the integration
step, which is the worktree option with more disk and more moving parts.*

True isolation with no worktree mechanics and no bare-repository handling, using only the most
ordinary git commands. Its one genuine advantage over a worktree — a clone can live anywhere,
including another machine — matters only for a topology this system does not have.

**Write to a temporary directory, copy into the repository on success** — **why not:** *the copy is a
second write path into the wiki, and reconciling deletions and renames reimplements what git already
does.*

Keeps the repository pristine during a run using no git features at all, which is appealingly simple
to reason about. It ends up as the worktree's overhead with none of git's help, and it introduces
exactly the bypassing write path the mutation rules exist to prevent.

**Commit after every tool write, squash at run end** — **why not:** *the squash is a history rewrite,
and a process dying before it leaves precisely the partial series the mutation semantics exclude.*

Makes a run's progress inspectable as it happens and loses nothing if the process dies mid-run, which
is genuinely attractive for a surface that watches work live. That need is better served by the run's
event stream than by publishing intermediate commits.

**A git library binding instead of the CLI** — **why not:** *the behaviours relied on are defined by
the CLI's implementation, and a binding tracks them at its own pace — a divergence would be a subtle
correctness bug in the one path that must not have any.*

In-process, no subprocess per operation, typed results instead of parsed output, and no dependency on
a `git` binary in the image. For high-volume repository access that would be the better engineering.
Here the exact semantics of revert's conflict handling, clean's rules and add's treatment of renames
and deletions are the product, and the constitution asks for real child processes in tests anyway.
