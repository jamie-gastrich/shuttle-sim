# Shuttle Sim

A simulation of an automated pallet shuttle system and the software that runs it: simulated shuttles, a fleet manager, traffic control, a mock WMS, and an operator dashboard.

This is a learning project. The owner writes the code. AI agents teach and review. See `AGENTS.md` for how the work happens and `spec/shuttle-spec.md` for the end product.

## Milestones

Only the reviewer changes this table, and only after a passing review.

| Milestone | Description | State |
|---|---|---|
| M0 | Repo and tooling | not started |
| M1 | Warehouse model and one shuttle | not started |
| M2 | Message contract and transport | not started |
| M3 | Fleet manager core | not started |
| M4 | Traffic control | not started |
| M5 | Faults and recovery | not started |
| M6 | WMS integration | not started |
| M7 | Operator dashboard and diagnostics | not started |
| M8 | Stretch | not started |

### Verification notes
Commands the reviewer ran to confirm each `done` milestone go here.

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
