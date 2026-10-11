# Lesson 09: Three shuttles finish a queue; state survives a restart

**Milestone:** M3 (Fleet manager core) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 08: SQL persistence so fleet state survives a restart](08-sql-persistence-and-restart.md) · **Decision:** [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md) · **Builds on:** record 11 (task lifecycle), record 13 (fleet state persistence), record 14 (db test gate)

**Concepts this assignment requires:**

- [ ] **Assignment to several shuttles** — the fleet manager takes the next queued task from the board and assigns it to an available shuttle. For three shuttles, the manager dequeues three tasks and assigns each to a different shuttle ID. Taught in [Assignment to several shuttles](#assignment-to-several-shuttles).
- [ ] **End-to-end queue completion** — three shuttles each complete their assigned task in sequence. The board's `NextQueued()` advances past completed tasks. Taught in [End-to-end queue completion](#end-to-end-queue-completion).
- [ ] **Restart survival proof end-to-end** — after three shuttles finish, persist state, simulate restart, load into a fresh board, and verify the queue is still correct. Taught in [Restart survival proof end-to-end](#restart-survival-proof-end-to-end).

Three new concepts. That is the cap.

---

### Where this fits

M3's done-when requires: *"a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."* Lesson 07 gave the pure model and board. Lesson 08 added SQL persistence so state survives restart. Lesson 09 completes the end-to-end proof: three shuttles finish a queue and restart survival is confirmed.

The key invariant from lesson 08 persists: **decision logic stays separate from input/output.** `FleetCore` remains pure (no broker, no DB, no network). The fleet manager consumes the repository and board, drives shuttles, and saves state.

There is a second, harder requirement here. The spec's definition of done says the project *"builds in the Docker environment, tests pass"*, and the M0 gate is `docker run --rm shuttle-fleet-build` in a container with **no database**. The restart-survival test needs a real database, so the test carries an `Integration` trait, the container runs everything **except** that trait, and a separate command run with the database up proves the test really executed. That mechanism was settled in [record 14](../decisions/14-db-test-gate-and-config.md).

---

### 1) Assignment to several shuttles

The fleet manager's job is to take tasks from the board and give them to shuttles. At M3, a task is a move to a target lane. The assignment loop is:

1. Call `board.NextQueued()` to get the oldest queued task
2. If a task is returned, call `Assign(shuttleId, now)` on it
3. Repeat for as many shuttles as are available

For three shuttles, the manager assigns three tasks in round-robin order: SH-01 gets the first, SH-02 the second, SH-03 the third, then SH-01 gets the fourth, etc. The round-robin order is a simple policy that keeps the proof concrete without over-specifying load balancing.

Read the assignment loop in `FleetManager/Program.cs` (or the lesson's toy project) to see how it consumes the board and repo.

**Why round-robin?** It is deterministic, easy to test, and sufficient for the M3 done-when. Load-balancing by distance or by shuttle state can come later (M4, M5). The spec says *"assign tasks to one shuttle, then several"* — several means "more than one," and round-robin is the smallest correct policy.

---

### 2) End-to-end queue completion

Once three shuttles have been assigned three tasks each, the manager waits for each shuttle to complete its work. The completion flow is:

1. Each shuttle finishes its move and reports status
2. The manager calls `Complete(now)` on the task, transitioning it from `InProgress` to `Done`
3. The manager saves the updated task state via the repository
4. The manager again calls `board.NextQueued()` — this now returns the next oldest queued task (since the first three are now `Done`)
5. Steps 1-4 repeat until the board has no more queued tasks

The critical invariant: `NextQueued()` always skips tasks that are not `Queued`. A task that is `Done` or `Failed` still lives on the board for tracking, but it no longer competes for assignment. This is why the board's `NextQueued()` checks `task.State == TaskState.Queued` rather than simply returning the first task.

A real-world picture: at a repair shop's front desk, three mechanics work from a ticket queue. Each takes the next ticket when free. Tickets that are being worked on or are finished don't block the others.

---

### 3) Restart survival proof end-to-end

After three shuttles have completed their tasks, the proof that state survives a restart is:

1. **Persist:** Save all tasks (queued, assigned, in-progress, done, failed) to PostgreSQL via `SaveAllAsync`
2. **Restart:** Simulate a restart by creating a **new** `TaskBoard` and loading all tasks from the repository via `LoadAllAsync`, preserving FIFO order by `created_at ASC, task_id ASC`
3. **Assert:**
   - All tasks are present with identical `TaskId`, `ToLane`, `CreatedAt`, `State`, `AssignedShuttleId`, `FailureReason`
   - Order is preserved (oldest queued task is first by `CreatedAt`)
   - `NextQueued()` returns the correct oldest queued task
   - State transitions still behave correctly on loaded tasks (e.g., a `Queued` task can still be `Assigned`)

This is a process-level restart in spirit: new `TaskBoard` instances, new repository connections, but the same durable state in PostgreSQL.

Real-world: power loss mid-shift; the manager restarts and resumes correctly because all task state survived in the database.

---

### What's not in this lesson

- **Assignment policy beyond round-robin** — which shuttle gets which task at scale is deferred to M4/M5
- **Fault injection and recovery** — M5. `Done` and `Failed` remain terminal at M3
- **Traffic control** — M4. No lane segment reservation needed for this proof
- **WMS integration** — M6. Tasks are internal fleet work
- **Dashboard** — M7. No operator UI needed
- **Any wire contract change** — v2 stays. Tasks are internal at M3.

---

### Say it out loud

1. Why does `NextQueued()` skip tasks that are not `Queued`? What would break if it didn't?
2. Why round-robin assignment and not "all tasks to the first available shuttle"? What fairness issue does round-robin solve?
3. Why is the restart-survival proof a *process* restart (new instances) rather than just copying references?
4. What is NOT persisted across restart (per record 13)? Why defer it?
5. How does the end-to-end proof differ from the lesson 08 restart test? (Lesson 08 persists + reloads; lesson 09 adds three shuttles completing work first.)

---

### The assignment

Build the end-to-end M3 proof: three shuttles finish a queue and state survives restart.

### Requirements

1. **Add assignment loop to `FleetManager`**: in `src/FleetManager/Program.cs` or a new method, implement the assignment loop that takes the next queued task from the board and assigns it to a shuttle. For three shuttles, use round-robin order (SH-01, SH-02, SH-03, SH-01, SH-02, SH-03...). The loop should persist task state after each `Assign` via the repository.

2. **Add end-to-end test in `FleetManager.Tests`**: the test is named `Three_shuttles_finish_queue_and_restart_survives` and must:
   - carry `[Trait("Category", "Integration")]` so the DB-less container can exclude it;
   - build its connection string from configuration, never a literal (same pattern as `RestartSurvivalTests.BuildConnectionString()`);
   - **not** start Docker itself. The test assumes the database is reachable and fails loudly if it is not;
   - create a queue of tasks (at least 9 tasks, 3 per shuttle), with mixed states (queued, assigned, in-progress, done, failed);
   - run the assignment loop so three shuttles each get 3 tasks and complete them;
   - persist all tasks via `SaveAllAsync`;
   - simulate a restart by creating a new `TaskBoard` and loading via the repo;
   - assert all 9 tasks are present with identical fields;
   - assert FIFO order is preserved (oldest queued first by `CreatedAt`);
   - assert `NextQueued()` returns the correct oldest queued task.

3. **Keep purity**: `FleetCore` must contain no `DateTime.UtcNow`, no `Guid.NewGuid`, no `Task.Delay`, no `Thread.Sleep`, and no references to `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. Ids and times are inputs.

4. **Keep the fresh-container gate green**. Do not change `docker/Dockerfile.build` line 6 — it already has `--filter "Category!=Integration"`.

### Acceptance criteria

Run from repo root unless stated. Exit codes read directly; printed numbers are asserted, not admired.

```bash
# Phase 1 — assignment loop exists
grep -q 'AssignNextQueued\|Assign.*shuttleId' src/FleetManager/Program.cs || echo "missing assignment loop"

# Phase 2 — pure suite still passes (no DB needed)
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"; rm -f "$T"
# expect RC 0, 7 "Passed!" lines (FleetCore.Tests 18 + FleetManager pure tests), no "Failed!"

# Phase 3 — with database, the integration test runs and passes
docker compose -f docker/compose.yml up -d database
cd src
T=$(mktemp); dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_restart_survives|Passed:|Skipped:' "$T"; rm -f "$T"
cd ..
docker compose -f docker/compose.yml down
# expect RC 0 and a line like:
#   Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_restart_survives [.. ms]
# plus "Passed: 1". A skipped test would print "Skipped <name>" instead, not "Passed <name>".

# Phase 4 — contract unchanged
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v3.md
```

### Stretch goal

Add fault injection to the end-to-end test: after two shuttles complete, inject a `Fail` on the third task's state, then verify restart survival still works (the failed task is preserved correctly).

### Not in this assignment

- **Assignment policy beyond round-robin** (which shuttle at scale, load balancing) — lesson 09 introduces the concept only.
- **Reassignment of failed work** — M5. `Done` and `Failed` are terminal at M3.
- **Traffic reservations** — M4.
- **WMS orders** — M6.
- **Dashboard** — M7.
- **Any new wire message.** A task is internal at M3; the contract stays v2.

---

### Closing

Read the spec's M3 line once more: *"A task queue with a clear lifecycle (queued, assigned, in progress, done, failed). Assign tasks to one shuttle, then several. Save state in SQL. Done when: a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

This lesson completes M3. Lessons 07, 08, and 09 together give the owner the full picture: the pure task model, the SQL persistence layer, and the end-to-end proof that three shuttles can finish a queue and that state survives restart. The owner should be able to explain, without notes, why `NextQueued()` skips non-queued tasks, why round-robin assignment is the simplest correct policy, and how the restart-survival proof uses new instances (not reference copying) to demonstrate durability.