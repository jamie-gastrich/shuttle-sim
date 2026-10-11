# Decision 13: Fleet manager state persistence in SQL

**Date:** 2026-10-11
**Milestone:** M3 (Fleet manager core) — the second M3 lesson: save fleet state in SQL so it survives a restart.
**Status:** Accepted. Written for lesson 08, which adds a repository abstraction, a persistence shape for the task board, and a restart-survival proof. It builds on [record 03](03-database.md) (PostgreSQL 17 server, service in Docker), [record 11](11-task-lifecycle-and-queue.md) (the pure `FleetCore` model and in-memory `TaskBoard`), and [record 12](12-task-blank-inputs-and-exception-types.md) (blank inputs and exception categories). Links back to [record 10 §11](10-command-retry-and-ack-timeout.md) which notes retry state is in-memory and becomes persistence input.
**Replaced by:** any later record that changes the persisted shape, the repository split, or the reload semantics. Append-only.
**Wire contract:** unchanged. This is persistence of internal fleet state only. No new message kinds; no version bump. `docs/contract/v1.md` and `v2.md` remain as-is.

---

## 1. What we are deciding

The spec’s M3 says: *"Assign tasks to one shuttle, then several. Save state in SQL. Done when: a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

Lesson 07 built the pure half. This record decides the persistence half so the restart is real, testable, and keeps the separation of concerns:

1. **What is persisted** — which parts of the fleet manager’s state survive restart (the task queue/state), and what is not persisted now.
2. **The persistence shape** — the database schema (table/columns/types) for tasks, including ordering semantics (FIFO must survive restart).
3. **The repository abstraction** — the interface boundary that keeps decision logic free of I/O, and who owns implementation (pure contracts in FleetCore or a new persistence project).
4. **Reload semantics** — how a fresh manager rebuilds the in-memory `TaskBoard` from the store so invariants hold (same order, same states, same ids).
5. **Restart-survival proof** — what constitutes a valid proof (a process restart, not just in-memory copy).
6. **What is deferred** — assignment policy, multiple shuttles, retry state beyond noting it as future input, traffic, WMS, dashboard.

---

## 2. Why this is lesson 08

M3 is split deliberately:

| M3 piece | Lesson | Why |
|---|---|---|
| Task model, lifecycle, pure queue | 07 (this built) | The shape everything stands on. |
| Assignment, repository, SQL save/load (this piece) | **08** | Wraps the pure model with a store and sets up restart semantics. The assignment policy for “which shuttle” is introduced lightly but the policy details (load balancing) are deferred; the core is persistence + separation. |
| Three shuttles finish a queue; state survives restart (end-to-end M3 done) | 09 | Proves M3 done-when. |

This order keeps each lesson ≤ 3 concepts and the restart claim measurable.

---

## 3. Survey A: What to persist

We need the minimum set that lets the manager resume after a restart without losing or corrupting work.

| Candidate | A: Persist tasks (the `FleetTask` board) — chosen | B: Persist events (event sourcing) | C: Persist the whole in-memory snapshot |
|---|---|---|---|
| Survives restart | yes | yes | yes |
| Rebuilds to same FIFO order | easy (by `CreatedAt` + insertion order) | needs projection | trivial |
| Testable in pure way | repository abstraction over rows | more machinery | simple but less explicit |
| Matches the pure board model | maps directly to rows | changes model | matches but couples |
| Complexity for M3 | small | more than 3 concepts | small but less educational |
| Future-proof (replay) | good enough; easy to evolve | strong | weak for history |

**Decision A: Persist the task state (the board contents).** Specifically persist each task’s fields: `TaskId`, `ToLane`, `CreatedAt`, `State` (as wire/state value), `AssignedShuttleId` (nullable), `FailureReason` (nullable). The queue order is the order tasks were enqueued; on reload we reconstruct the board in **insertion order by `CreatedAt` ascending**, then by insertion order if times tie (defensive). This preserves FIFO for `NextQueued()`.

Eventsourcing (B) is powerful but adds projection/replay machinery — likely pushes beyond 3 concepts. Snapshotting (C) is easy but blurs semantics. We keep it simple and explicit.

Not persisted now: MQTT session state, retry-attempt counts, command envelopes in flight, shuttle connection state, metrics, logs. Those are deferred (record 10 §11 notes retry state becomes persistence input later).

---

## 4. Survey B: Where the repository lives (abstraction + implementation)

AGENTS.md requires decision logic separated from I/O. The pure `FleetCore` has no DB. We need an interface and an implementation.

| | A: `IFleetTaskRepository` in `FleetCore`; Postgres impl in `FleetManager` (or new `FleetManager.Persistence`) — chosen | B: Repository only in `FleetManager`, no interface in `FleetCore` | C: Put both in a new `FleetPersistence` project; interface there |
|---|---|---|---|
| Keeps `FleetCore` pure (no I/O refs) | interface is a contract type but no implementation refs; impl lives outside | no interface, couples manager to DB | pure project for interface/impl split |
| Testable without DB | yes, mock/fake the interface | harder to isolate | yes |
| Matches precedent (`ShuttleCore` pure, apps drive it) | `FleetCore` stays domain-only; apps depend on abstraction | less clean | also clean but more projects |
| Easy for lesson scope | 3 concepts fit | fewer seams | one more project |

**Decision B-like-but-A: Define `IFleetTaskRepository` in `FleetCore` (the domain boundary) and implement it in `FleetManager` (the app boundary) using Npgsql against PostgreSQL.** The interface is a simple contract with no DB types. The implementation lives where config/connection strings and DB client live. Alternatively we could create `FleetManager.Persistence`, but one new concept project is fine; keeping impl in `FleetManager` matches how `ReliableCommandSender` lives with the app. The key is: `FleetCore` has **no** `Npgsql`/DB references; the repository is an abstraction consumed by the app.

Rejected (C): adding a whole persistence project is doable but not necessary for 3 concepts. Rejected (B): skipping the interface blurs the I/O boundary.

Interface shape (minimal for M3):

```csharp
// in FleetCore
public interface IFleetTaskRepository
{
    Task SaveAsync(FleetTask task, CancellationToken ct);
    Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken ct);
    Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken ct);
    Task<FleetTask?> GetAsync(string taskId, CancellationToken ct);
}
```

Notes:
- `SaveAllAsync` supports saving the whole board in one operation (useful for restart/export and for atomicity of a batch). For M3 we can start with Save per task or SaveAll; both are fine.
- LoadAllAsync returns tasks in the order they were enqueued (by `CreatedAt` asc). The board reconstructs by that order.
- The repository works with `FleetTask` domain objects (or can map). Mapping stays in the impl.

---

## 5. Survey C: Schema and how to map

Tasks map directly to columns:

| Column | Type | Constraints | Notes |
|---|---|---|---|
| task_id | text | PRIMARY KEY | `FleetTask.TaskId` |
| to_lane | text | NOT NULL | `FleetTask.ToLane` |
| created_at | timestamptz | NOT NULL | UTC; preserves FIFO when sorted |
| state | text | NOT NULL | store wire name `queued/assigned/in_progress/done/failed` (or the enum value as text) — consistent with `TaskStateWire.ToWireName` |
| assigned_shuttle_id | text | NULL | `FleetTask.AssignedShuttleId` |
| failure_reason | text | NULL | `FleetTask.FailureReason` |

Why `timestamptz` (timestamp with time zone): stores UTC clearly; comparisons are unambiguous. Ordering is `ORDER BY created_at ASC, task_id ASC` as a tie-breaker if two tasks share the same timestamp (defensive; caller passes CreatedAt in).

We do **not** store `_lastEventAt` (it’s an in-memory guard). It’s reconstructed implicitly (not needed after load). We also don’t store the list structure separately — the board’s order is recovered by `created_at`.

No schema migrations framework introduced now; we use `CREATE TABLE IF NOT EXISTS` for the lesson (keeps scope small). A real project would add migrations later — deferred.

---

## 6. Decision: Reload semantics (restart survival)

When loading from DB after a restart:
1. Read all tasks ordered by `created_at ASC, task_id ASC`.
2. Reconstruct `FleetTask` instances in that order and add them to a fresh `TaskBoard` in the same order. This preserves FIFO (`NextQueued()` returns the oldest queued task by created_at).
3. The tasks’ states/fields are exactly as saved. The board’s internal list order matches enqueue order.
4. The in-memory `_lastEventAt` is **not** persisted; on a fresh process load, there are no prior events in that process. This is fine because the task’s lifecycle history is encoded in its current state. Guards against backwards time apply to *subsequent* events in the new process lifetime.

**Restart-survival proof (required):** Create tasks, persist them, then simulate a restart by creating a **new** `TaskBoard` (or new repository-backed board instance) and loading from the database. Assert that the loaded board contains the same tasks in the same order, with identical `TaskId`, `ToLane`, `CreatedAt`, `State`, `AssignedShuttleId`, `FailureReason`. Also verify `NextQueued()` behavior matches (oldest queued is first). This is a process-level restart in spirit (new instances), not just copying references.

The spec states "fleet state survives a fleet manager restart". The lesson’s acceptance must include this concrete proof using the real DB (docker service). The worked example demonstrates save+load; the assignment requires a restart-survival test.

---

## 7. Decision: Repository pattern and testability

- `FleetCore` defines `IFleetTaskRepository` (no implementation details, no DB types). This keeps pure logic decoupled from I/O.
- The Postgres implementation lives in the app layer (e.g. `FleetManager.Persistence.PostgresFleetTaskRepository` or just `FleetManager.PostgresTaskRepository`). It maps between `FleetTask` and DB rows. All DB access is async with `CancellationToken`.
- The board itself stays pure. A higher-level service (in FleetManager) orchestrates: loads board from repo on startup, then drives assignment using board, and saves changes after state transitions.
- Tests: pure logic tests (FleetCore.Tests) need no DB; repository tests can use the real DB (docker) or be integration-style. The lesson focuses on the core mechanics and provides a small toy; the assignment includes the restart-survival test against a real DB instance (started via docker compose for that test context as appropriate).

This matches record 11’s "pure board, SQL is a later layer" — the layer is the repository.

---

## 8. Decision: What’s NOT in this lesson

Explicitly deferred:

- **Assignment to SEVERAL shuttles / assignment POLICY** (which shuttle gets which task, load balancing) — later M3 lesson (lesson 09). This lesson introduces the shape but does not implement a policy beyond the mechanics.
- **Reassignment of failed work** — M5 (terminal states remain terminal at M3).
- **Traffic control** — M4.
- **WMS integration** — M6.
- **Dashboard** — M7.
- **Retry state persistence** — noted as input for future (record 10 §11); not persisted now.
- **Any wire contract change** — v2 stays. Tasks are internal at M3.

---

## 9. Verification table (measured)

Checked this session (2026-10-11):

| Claim | How checked | Result |
|---|---|---|
| PostgreSQL 17 service runs in compose | `docker compose -f docker/compose.yml up -d database; docker compose ps` | started, port 5432 mapped to localhost |
| Postgres accepts connections with defaults | `docker exec shuttle-fleet-database-1 pg_isready -U shuttle -d shuttle` | accepting connections |
| Npgsql connects and can write/read rows | toy probe in `/tmp/opencode/npgsqlprobe` wrote bike-1 (queued) and mower-2 (failed), read them back | loaded 2 row(s), data matches |
| Schema can be created with `CREATE TABLE IF NOT EXISTS` | probe created `probe_tasks` table | succeeded |
| FIFO ordering by created_at is explicit | probe ordered `ORDER BY created_at ASC` | returned bike-1 before mower-2 |
| `FleetCore` has no DB refs | `grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs'` and csproj | no matches |
| Pure board model unchanged | read `FleetTask`, `TaskBoard`, `TaskState` | as per record 11/12 |
| Contract unchanged | `grep -qE '^Version: 2$' docs/contract/v2.md; test ! -f docs/contract/v3.md` | v2, no v3 |

Not yet verified: the actual repository implementation and the restart-survival test in the assignment — they are specified, not measured.

---

## 10. What would change this choice

- If we need history/replay (audit), move toward event sourcing (B) — new record.
- If we need richer queries or EF Core, switch driver/ORM — new record, links here.
- If assignment policy needs transactional updates across tasks/shuttles, adjust SaveAll semantics — new record.
- If M3’s restart proof needs to survive an actual process restart (separate processes), the acceptance gate must be explicit; this record allows process-level (new instances) which is equivalent for state.

---

## 11. What this record unblocks

- Lesson 08 can define `IFleetTaskRepository` in `FleetCore`, its Postgres implementation in `FleetManager`, the schema, and the restart-survival acceptance gate.
- The assignment can build a real carry-forward piece (persistence) that survives restart.
- Lesson 09 can build on this to finish M3’s done-when with three shuttles.

(End of file - 199 lines)
