# Shuttle Sim

A simulation of an automated pallet shuttle system and the software that runs it: simulated shuttles, a fleet manager, traffic control, a mock WMS, and an operator dashboard.

This is a learning project. The owner writes the code. AI agents teach and review. See `AGENTS.md` for how the work happens and `spec/shuttle-spec.md` for the end product.

## Milestones

Only the reviewer changes this table, and only after a passing review.

| Milestone | Description | State |
|---|---|---|
| M0 | Repo and tooling | done |
| M1 | Warehouse model and one shuttle | done |
| M2 | Message contract and transport | done |
| M3 | Fleet manager core | done |
| M4 | Traffic control | done |
| M5 | Faults and recovery | in progress |
| M6 | WMS integration | not started |
| M7 | Operator dashboard and diagnostics | not started |
| M8 | Stretch | not started |

## Layout

```
AGENTS.md               how work happens in this repo
spec/shuttle-spec.md    the end product and milestones
docs/lessons/           lessons and hints
docs/decisions/         decision records (append-only)
docs/reviews/           reviews
docs/contract/          the versioned message contract
src/                    the code
docker/                 broker, database, and environment
```

## What ships in src/

Eight small projects: six libraries (pure decision logic, no I/O, so each can be tested alone) and two `.Exe` apps that wire the libraries to a real broker and database.

The two apps never call each other directly. They both connect to the MQTT broker and exchange messages on topics, so either side can be restarted without the other noticing:

```text
  ┌────────────────────┐                 ┌────────────────────┐
  │     ShuttleSim     │                 │    FleetManager    │
  │      (.Exe)        │                 │      (.Exe)        │
  │  one process per   │                 │  fleet manager:    │
  │  shuttle; emits    │                 │  liveness, alarms, │
  │  heartbeat+status  │                 │  commands          │
  └─────────┬──────────┘                 └─────┬────────┬─────┘
            │                                  │        │
            │      ┌────────────────────┐      │        │
            └─────►│    MQTT broker     │◄─────┘        │
                   │    (mosquitto)     │               │
                   └────────────────────┘               ▼
                                              ┌──────────────────┐
                                              │     Postgres     │
                                              │   tasks + board  │
                                              │  survive restart │
                                              └──────────────────┘

  messages on the broker:
    shuttle/{id}/command    manager ──► shuttle   (move / load / unload)
    shuttle/{id}/status     shuttle ──► manager   (position and state)
    shuttle/{id}/ack        shuttle ──► manager   (command confirmed)
    shuttle/{id}/alarm      manager ──► WMS       (something is wrong)
```

The projects are also built from one another. A project may only depend on projects below it:

```text
  ┌─ no dependencies ────────────────────────────────────────────────┐
  │   Contracts          ShuttleCore           FleetCore             │
  │   wire shapes        shuttle state         tasks + board         │
  └──────────────────────────────────────────────────────────────────┘
                                │
  ┌─ pure logic and the bus seam ────────────────────────────────────┐
  │   Messaging    ── uses Contracts                                 │
  │   TrafficCore  ── uses ShuttleCore                               │
  └──────────────────────────────────────────────────────────────────┘
                                │
  ┌─ transport ──────────────────────────────────────────────────────┐
  │   MqttMessaging ── uses Contracts, Messaging                     │
  └──────────────────────────────────────────────────────────────────┘
                                │
  ┌─ apps (.Exe) — the only projects that touch I/O ─────────────────┐
  │   FleetManager ── uses FleetCore, Contracts, Messaging,          │
  │                   MqttMessaging, TrafficCore                     │
  │   ShuttleSim   ── uses ShuttleCore, Contracts, Messaging,        │
  │                   MqttMessaging                                  │
  └──────────────────────────────────────────────────────────────────┘
```

The table gives the same picture project by project, plus what each one is:

| Folder (project) | Depends on (projectRefs) | Talks to | What it is |
|---|---|---|---|
| `Contracts` | — | wire shapes | Shared message types, envelope fields, `MessageTypes`, `ContractVersion` |
| `ShuttleCore` | — | nothing | Shuttle state machine (`idle/moving/...`), rack, track, timings, pure |
| `TrafficCore` | `ShuttleCore` | nothing | Lane-segment reservations, `CanMove`/`CommitMove` |
| `FleetCore` | — | nothing | Tasks, `TaskBoard`, repository interface (pure) |
| `Messaging` | `Contracts` | — | `IMessageBus`, `TopicScheme`, envelopes, retry, idempotency (seam only) |
| `MqttMessaging` | `Contracts`, `Messaging` | broker (MQTTnet) | Concrete MQTT bus: connect, pub/sub, QoS from config |
| `FleetManager` **(app)** | `FleetCore`, `Contracts`, `Messaging`, `MqttMessaging`, `TrafficCore` | broker + Postgres | Manager console. Today: `move` (send + retry a command) and `serve` (liveness + alarms). Assignment and traffic wiring arrive in later slices. |
| `ShuttleSim` **(app)** | `ShuttleCore`, `Contracts`, `Messaging`, `MqttMessaging` | broker | One simulated shuttle per process: accepts commands, moves, emits status + heartbeat |
| `* .Tests` | their project + a few deps | test frameworks | Direct test project for each project |

`FleetManager` and `ShuttleSim` are the only programs that touch I/O. Everything under them (state machines, traffic, tasks, messaging contracts) is pure C# with no broker or database, so it unit-tests in milliseconds with no services running.

### Folder-by-folder READMEs

Each `src/*/README.md` has a short **Overview** section: what the project is, what it depends on, what depends on it, and how messages flow in and out (for the I/O projects) or who calls it and what comes back (for the pure projects). Start reading in `Contracts`, then `Messaging`, then either `ShuttleCore`→`ShuttleSim` (the shuttle side) or `FleetCore`/`TrafficCore`→`FleetManager` (the manager side). Then you can read top-down from either entry point.

## Build and test

Start the supporting services (broker + database) needed for integration tests:

```
docker compose -f docker/compose.yml up -d
```
```
cd src
dotnet build
dotnet test
cd ..
```
```
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build
```

Stop the services when done:

```
docker compose -f docker/compose.yml down
```

OR run all except for integration tests
```
cd src
dotnet build
dotnet test --filter "Category!=Integration"
cd ..
```
### Verification notes
Commands the reviewer ran to confirm each `done` milestone go here.

**M5** (in progress) — slices reviewed so far: lesson 13 (heartbeats, lastSeenAt, timeout window) `docs/reviews/13-heartbeats-and-timeouts.md` ([record 20](docs/decisions/20-liveness-heartbeats-and-timeouts.md)); lesson 14 (alarms with severity, v3 `alarm` kind, rising-edge latch) `docs/reviews/14-alarms-with-severity.md` ([record 21](docs/decisions/21-alarms-with-severity.md)); lesson 15 (serve loop and alarm publish, `Alarm` transport lane, `serve` command) `docs/reviews/15-serve-loop-and-alarm-publish.md` ([record 22](docs/decisions/22-serve-loop-and-alarm-publish.md)). From `src/`: `cd src && dotnet build` → exit 0, 0 warnings/errors; `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → RC 0, 8 `Passed!`, 0 `Failed!`, 109 tests (Contracts.Tests 7, Messaging.Tests 9, FleetManager.Tests 36); with broker+database up `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → RC 0, 3 `Passed!`, 0 `Failed!`. Mutations (a)-(i) all turned red, including the three key fixes (d) distinct fixture id, (g) version/MessageType/blank-id/blank-MessageId variants, and (i) the new clock-domain sweep test. `AlarmEvaluator` currently raises only `lostCommunication`; `blockedPath` and `palletNotDetected` are defined in `src/Contracts/AlarmCodes.cs` but not raised. Not yet done: fault injection/raising for `blockedPath`/`palletNotDetected`; reassigning a failed shuttle’s work; operator-acknowledged recovery; and the M5 done-when proof that every injected fault leads to the right alarm and right recovery and saved state never loses or duplicates a pallet (recovery/reconciliation are later slices).

**M4** — lesson 12 wiring slice reviewed 2026-10-13, `docs/reviews/12-run-loop-wiring.md`. Verified M4 done-when: a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock. From `src/`: `cd src && dotnet test ShuttleFleet.slnx --filter "Category!=Integration"` → RC 0, 8 `Passed!`, 0 `Failed!`, 84 tests, `FleetManager.Tests` 16. Stress test `Four_or_more_shuttles_finish_with_no_collisions_and_no_deadlock` (4+ shuttles, overlapping routes) drives tick-by-tick and calls `AssertNoSharedLanes` after every tick; mutations confirmed the stress/collision checks are load-bearing. Rule is documented in `docs/decisions/19-run-loop-wiring-and-concurrency.md`. Known limitation recorded in review 12: the gate is not wired into `FleetManager/Program.cs` (deferred to a later wiring slice); the delivered slice demonstrates the run loop behavior under test.

**M3** — verified 2026-10-12, review in `docs/reviews/09-three-shuttles-finish-queue.md`. From `src/`: `dotnet build` exit 0 (0 warnings / 0 errors); `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` exit 0 with 7 `Passed!` lines and 70 tests (`FleetManager.Tests` 11 = 5 + 6 new dispatcher tests); the six pinned dispatcher test names are present via `dotnet test ShuttleFleet.slnx --list-tests`. With the database up: `dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"` exit 0, with `Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart` and `Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks`, `Passed: 2`, zero skipped; `psql` shows ten rows — nine `done` in the round-robin SH-01/02/03 pattern and `task-hold` `queued` — and the whole suite is 72 tests with `FleetManager.Tests` 13. Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0 and `docker run --rm shuttle-fleet-build` exit 0 with 7 `Passed!` lines and `FleetManager.Tests` 11 (the `Integration` trait excluded; there is no database in the container). Nine mutation rows in a throwaway copy: eight turned the relevant test red — missing `CompleteAsync` save, flipped `created_at` load order, dropped `task-hold` save (product-side and test-side), the `LoadAllAsync` fixture with a stale `state='legacy_state'` row planted, a `ToString()` state binding, missing `FailAsync` save, and an ignored shuttle id — and the ninth (the sentinel `GetAsync` reset, same stale row) stayed green and deleted the row, closing review 08 C3. Contract unchanged: `v1.md` `Version: 1`, `v2.md` `Version: 2`, no `v3.md`; `FleetCore`/`FleetCore.Tests` pure and untouched; `Program.cs` and the Dockerfile untouched.

**M2** — verified 2026-10-10, review in `docs/reviews/06-ack-timeout-and-retry.md`. From `src/`, `dotnet build` exit 0 (0 warnings / 0 errors) and `dotnet test` exit 0 with six `Passed!` lines (22 / 4 / 8 / 5 / 2 / 5 = 46 tests); the anchored name walk over the seven new sender/policy test names prints nothing. Over the real broker (`docker compose -f docker/compose.yml up -d broker`, no pre-sleep) `move SH-01 A01-L02` exits 0 with a correlated `commandAck` (`"Accepted":true`) and one `moving` + one `idle` status in the manager output, and the lost-command retry (manager started before the shuttle, `AckTimeoutMs=1000 MaxAttempts=6`) also exits 0 with `"Accepted":true`; the give-up path (no shuttle, `MaxAttempts=2`) exits 1 with `No acknowledgement for shuttle SH-01, command <id>, after 2 attempts.`; a rejected command (`A99-L99`) exits 1 in one attempt with the reason on stderr. The id mutation (fresh `MessageId` per attempt) turns `Lost_acknowledgement_is_retried_with_the_same_message_id` red, and the two lesson-05 idempotency mutations still go red. Contract untouched: `v1.md` `Version: 1`, `v2.md` `Version: 2`, no `v3.md`. Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0 and `docker run --rm shuttle-fleet-build` exit 0 with six `Passed!` lines.

**M1** — verified 2026-10-09, second re-review in `docs/reviews/04-warehouse-model-and-one-shuttle.md`. From `src/`, `dotnet build` exit 0 (0 warnings / 0 errors) and `dotnet test` exit 0 with four `Passed!` lines and **22** passed in `ShuttleCore.Tests`; the Phase 4 anchored name walk (`grep -qE "\.$t\$"` over all 23 transition/rejection/scenario test names) prints nothing. Every state transition is covered 10/10 per record 06: the ten rows map to `StartMove_from_idle_enters_moving`, `CompleteMove_from_moving_enters_idle`, `StartLoad_from_idle_enters_loading`, `CompleteLoad_from_loading_enters_idle`, `StartUnload_from_idle_enters_unloading`, `CompleteUnload_from_unloading_enters_idle`, and `RaiseFault_from_{idle,moving,loading,unloading}_enters_fault`; the fault rows are mutation-proven (making `RaiseFault` reject non-`Idle` turns all four red), and removing the `StartUnload` occupancy guard turns `StartUnload_into_an_occupied_slot_is_rejected` red. The scripted sequence `Scripted_sequence_moves_a_pallet_from_one_slot_to_another` passes, and `dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0 prints 7 status lines moving `PLT-001` from `A01-L01-S01` to `A02-L02-S02` (`00:00:17Z` arrival, `00:00:19Z` done). Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0 and `docker run --rm shuttle-fleet-build` exit 0 with four `Passed!` lines and 22 in `ShuttleCore.Tests`; contract untouched.

**M0** — verified 2026-10-08, re-review in `docs/reviews/03-docker-services.md`. From repo root: `docker compose -f docker/compose.yml config --services` prints `broker` and `database`; `up -d`, `ps -q | wc -l` = 2, `docker run --rm --network shuttle-fleet_default eclipse-mosquitto:2.0 mosquitto_pub -h broker -t acceptance/probe -m ok` and `docker run --rm --network shuttle-fleet_default postgres:17 pg_isready -h database -U shuttle -d shuttle` both exit 0, `down`; `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0; `docker run --rm shuttle-fleet-build` exit 0 with `grep -c 'Passed!'` = 2; from `src/`, `dotnet build` and `dotnet test` exit 0. Fresh-container build and suite green; gates mutation-proven able to fail.
