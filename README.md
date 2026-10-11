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
| M3 | Fleet manager core | in progress |
*Verification (review 07 re-review, 2026-10-11): `dotnet build` clean; `dotnet test` → 7 Passed! (64/64); fresh Docker build/run → 7 Passed!; Phase 3b gate passes (wire-name test asserts all five names via ToWireName); mutations (Complete guard, EnsureInOrder, NextQueued skip) red when broken. M3 NOT done — SQL persistence and restart proof deferred to lessons 08–09.*
| M4 | Traffic control | not started |
| M5 | Faults and recovery | not started |
| M6 | WMS integration | not started |
| M7 | Operator dashboard and diagnostics | not started |
| M8 | Stretch | not started |

### Verification notes
Commands the reviewer ran to confirm each `done` milestone go here.

**M0** — verified 2026-10-08, re-review in `docs/reviews/03-docker-services.md`. From repo root: `docker compose -f docker/compose.yml config --services` prints `broker` and `database`; `up -d`, `ps -q | wc -l` = 2, `docker run --rm --network shuttle-fleet_default eclipse-mosquitto:2.0 mosquitto_pub -h broker -t acceptance/probe -m ok` and `docker run --rm --network shuttle-fleet_default postgres:17 pg_isready -h database -U shuttle -d shuttle` both exit 0, `down`; `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0; `docker run --rm shuttle-fleet-build` exit 0 with `grep -c 'Passed!'` = 2; from `src/`, `dotnet build` and `dotnet test` exit 0. Fresh-container build and suite green; gates mutation-proven able to fail.

**M2** — verified 2026-10-10, review in `docs/reviews/06-ack-timeout-and-retry.md`. From `src/`, `dotnet build` exit 0 (0 warnings / 0 errors) and `dotnet test` exit 0 with six `Passed!` lines (22 / 4 / 8 / 5 / 2 / 5 = 46 tests); the anchored name walk over the seven new sender/policy test names prints nothing. Over the real broker (`docker compose -f docker/compose.yml up -d broker`, no pre-sleep) `move SH-01 A01-L02` exits 0 with a correlated `commandAck` (`"Accepted":true`) and one `moving` + one `idle` status in the manager output, and the lost-command retry (manager started before the shuttle, `AckTimeoutMs=1000 MaxAttempts=6`) also exits 0 with `"Accepted":true`; the give-up path (no shuttle, `MaxAttempts=2`) exits 1 with `No acknowledgement for shuttle SH-01, command <id>, after 2 attempts.`; a rejected command (`A99-L99`) exits 1 in one attempt with the reason on stderr. The id mutation (fresh `MessageId` per attempt) turns `Lost_acknowledgement_is_retried_with_the_same_message_id` red, and the two lesson-05 idempotency mutations still go red. Contract untouched: `v1.md` `Version: 1`, `v2.md` `Version: 2`, no `v3.md`. Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0 and `docker run --rm shuttle-fleet-build` exit 0 with six `Passed!` lines.

**M1** — verified 2026-10-09, second re-review in `docs/reviews/04-warehouse-model-and-one-shuttle.md`. From `src/`, `dotnet build` exit 0 (0 warnings / 0 errors) and `dotnet test` exit 0 with four `Passed!` lines and **22** passed in `ShuttleCore.Tests`; the Phase 4 anchored name walk (`grep -qE "\.$t\$"` over all 23 transition/rejection/scenario test names) prints nothing. Every state transition is covered 10/10 per record 06: the ten rows map to `StartMove_from_idle_enters_moving`, `CompleteMove_from_moving_enters_idle`, `StartLoad_from_idle_enters_loading`, `CompleteLoad_from_loading_enters_idle`, `StartUnload_from_idle_enters_unloading`, `CompleteUnload_from_unloading_enters_idle`, and `RaiseFault_from_{idle,moving,loading,unloading}_enters_fault`; the fault rows are mutation-proven (making `RaiseFault` reject non-`Idle` turns all four red), and removing the `StartUnload` occupancy guard turns `StartUnload_into_an_occupied_slot_is_rejected` red. The scripted sequence `Scripted_sequence_moves_a_pallet_from_one_slot_to_another` passes, and `dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0 prints 7 status lines moving `PLT-001` from `A01-L01-S01` to `A02-L02-S02` (`00:00:17Z` arrival, `00:00:19Z` done). Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0 and `docker run --rm shuttle-fleet-build` exit 0 with four `Passed!` lines and 22 in `ShuttleCore.Tests`; contract untouched.

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

## Build and test

```
cd src
dotnet build
dotnet test
```

Setup details for the broker and database arrive with M0.
