# ADR-0006: Operational state store

## Status

Accepted

## Context

Beyond the wiki, the system holds a second body of durable state: the record of what it did. The
constitution requires every operation performed on the user's behalf to produce an inspectable task
artifact recording at minimum the instruction-file version dispatched and the tool set granted
(I.4), and it makes those artifacts an observability surface (IV.5). They are therefore not logs —
they are queried, listed, opened individually, and appended to while a run is in progress.

The shape that state has, independent of any particular capability:

- **Append-mostly with an ordered child collection.** An operation has a sequence of recorded events
  whose order is part of the meaning, and which grow while the operation is running.
- **Immutable once finished.** A completed record's contents do not change; a small, well-defined set
  of later transitions is the only exception.
- **Queried by recency and by identity**: the most recent operations, and one operation in full.
- **Durable across restarts**, because recovery after an interrupted process is part of the system's
  correctness, not an optimisation.
- **Retained indefinitely**, because the value of the record is comparison over time — this run
  against that one, under this instruction revision versus that one.

It is deliberately *not* the wiki. A failed operation must record precisely what happened while
leaving wiki content untouched, so the two stores answer different questions and fail independently.

Two forward-looking forces: the system is single-node today and its write path is serialised because
concurrent agents editing the same content conflict at the content level, not because of any storage
choice — but the read path is not serialised, and surfaces, plus eventually concurrent readers, will
query these records freely. And the schema will grow as new kinds of operation are recorded.

## Decision

Operational state lives in **SQLite**, accessed through the ADO.NET provider directly, confined to
one adapter inside the slice that owns the artifacts. Write-ahead logging is enabled so readers do
not block on the writer.

Structural invariants are expressed as **database constraints** rather than as application
convention: in particular, the relationship that guarantees an operation cannot acquire a second
execution record is a uniqueness constraint, not a code path.

## Consequences

- Correctness properties that would otherwise be races become constraint violations. "This operation
  has at most one execution" is enforced by the store even if two code paths disagree, including
  across a restart.
- Ordering and recency queries are indexes rather than directory scans, and they stay cheap as the
  record count grows without bound.
- Tests use the same engine as production with a file per test: real infrastructure, hermetic, and
  parallel-safe with no server process and no shared state.
- The store is a file on local block storage. It travels with a volume, is copied by copying a file,
  and needs no separate backup mechanism or operational surface.
- **It does not survive a move to multiple nodes.** SQLite is a single-writer, single-host store, and
  a horizontally scaled orchestrator would need a networked database. That boundary is known and the
  migration is contained in one adapter; it is not a hidden cliff.
- **It must not be placed on a network filesystem.** SQLite's locking is not reliable over NFS or
  SMB, and doing so corrupts the file rather than failing loudly. This is a deployment constraint
  that has to be written down because it is invisible until it has already gone wrong.
- Writing SQL by hand is more verbose than a mapper, and every schema change is an explicit
  migration. Accepted while the schema is small; the trigger to revisit is schema churn, not
  aesthetics.

## Alternatives Considered

**PostgreSQL** — **why not:** *a second server process to run, secure, back up and upgrade, and it
makes hermetic parallel tests materially harder — paid continuously for concurrency this system's
domain does not have.*

The straightforward answer if the store is expected to outgrow one node: real concurrency, a
migration ecosystem, richer types for semi-structured payloads, and operational tooling that exists
everywhere. It is a better database in most respects that matter at scale. Before that scale arrives
it costs a container per test run with schema setup, or a shared instance with exactly the cross-test
coupling the testing rules forbid. The right time to adopt it is when a second node is a real
requirement.

**JSON or Markdown files, one per operation** — **why not:** *listing by recency becomes a directory
scan that degrades without bound, and structural guarantees become a hand-written locking protocol.*

Maximally transparent and debuggable: readable in a text editor, diffable, no library at all. It
fails on the operations that define this state — appending to an ordered collection while a run is in
progress means rewriting a file repeatedly or fragmenting into many, and "an operation cannot acquire
a second execution record" becomes the class of problem a database exists to have already solved.

**Storing the record in the wiki repository alongside the content** — **why not:** *a failed
operation must record what happened while leaving content byte-identical — if the record lives in the
content, recording is itself a mutation and both cannot hold.*

Appealingly unifying: one durable store, one backup, and the record versioned with what it describes.
It would also put high-frequency, in-progress writes into a repository whose entire design is one
commit per completed operation.

**An embedded key-value store** — **why not:** *ordering, cross-record queries and relational
constraints would all be rebuilt on top, reimplementing the parts of a relational engine this state
actually uses.*

Lighter than SQLite in library terms, fast, and adequate for storing a record by identity. The trade
is removing a dependency that is already ubiquitous and present on nearly every base image, in
exchange for writing secondary indexes and application-level invariants by hand.

**An ORM over the same SQLite file** — **why not:** *indirection with no caller needing it while the
schema is small and static.*

Not an alternative store but an alternative access layer, and a reasonable one: migrations as
first-class artifacts, change tracking, typed queries instead of hand-written SQL. It is a change
inside one adapter, and sustained schema churn is the trigger that would justify it.
