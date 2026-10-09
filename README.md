# Shuttle Sim

A simulation of an automated pallet shuttle system and the software that runs it: simulated shuttles, a fleet manager, traffic control, a mock WMS, and an operator dashboard.

This is a learning project. The owner writes the code. AI agents teach and review. See `AGENTS.md` for how the work happens and `spec/shuttle-spec.md` for the end product.

## Milestones

Only the reviewer changes this table, and only after a passing review.

| Milestone | Description | State |
|---|---|---|
| M0 | Repo and tooling | done |
| M1 | Warehouse model and one shuttle | done |
| M2 | Message contract and transport | not started |
| M3 | Fleet manager core | not started |
| M4 | Traffic control | not started |
| M5 | Faults and recovery | not started |
| M6 | WMS integration | not started |
| M7 | Operator dashboard and diagnostics | not started |
| M8 | Stretch | not started |

### Verification notes
Commands the reviewer ran to confirm each `done` milestone go here.

**M0** — verified 2026-10-08, re-review in `docs/reviews/03-docker-services.md`. From repo root: `docker compose -f docker/compose.yml config --services` prints `broker` and `database`; `up -d`, `ps -q | wc -l` = 2, `docker run --rm --network shuttle-fleet_default eclipse-mosquitto:2.0 mosquitto_pub -h broker -t acceptance/probe -m ok` and `docker run --rm --network shuttle-fleet_default postgres:17 pg_isready -h database -U shuttle -d shuttle` both exit 0, `down`; `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0; `docker run --rm shuttle-fleet-build` exit 0 with `grep -c 'Passed!'` = 2; from `src/`, `dotnet build` and `dotnet test` exit 0. Fresh-container build and suite green; gates mutation-proven able to fail.

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
