# Review 09: Three shuttles finish a queue; state survives a restart

**Date:** 2026-10-12  
**Scope:** Lesson 09 assignment against its requirements and decisions 15/15a. This review includes the focused completions made at the owner's request.  
**Contract checked:** v1 remains version 1, v2 remains version 2, no v3; no task types leaked into `Contracts`.

**Verdict: implementation passes the lesson 09 technical acceptance.** No blockers found. M3 remains **in progress** pending the owner's explain-it answers; the README milestone table was not changed.

## Findings

No implementation findings. The dispatcher stays in `FleetManager`, delegates transition validation to `FleetTask`, and saves only after a successful transition. The pure tests now seed tasks and assert the six required behaviors, including that a rejected transition does not increase the save count. The end-to-end test completes nine tasks in round-robin order, persists each transition, holds one queued task, and reloads all ten rows into a new board through a new repository.

The shared database helper uses the required configuration precedence and resets through the sentinel `GetAsync` before deleting rows. Both DB-backed test classes use the non-parallel `Database` collection. `FleetCore`, `FleetCore.Tests`, `Program.cs`, the Dockerfile, and the message contract stayed within their lesson boundaries.

## Verification

| Check | Result |
|---|---|
| `dotnet build ShuttleFleet.slnx` | Passed |
| `dotnet test ShuttleFleet.slnx -v minimal --filter "Category!=Integration"` | Passed, 70 tests across 7 test assemblies |
| `dotnet test FleetManager.Tests/FleetManager.Tests.csproj --filter "Category=Integration"` with PostgreSQL running | Passed, both `EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart` and `RestartSurvivalTests.State_survives_restart_after_persisting_tasks` ran; 2 passed, 0 skipped |
| Fresh container: `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` and `docker run --rm shuttle-fleet-build` | Passed, all 7 suites (70 tests); no database in the container |
| Dispatcher purity, `FleetCore` database boundary, project references, required test discovery, fixture config/collection, Docker invocation, and contract version checks | Passed |
| Compose cleanup | Database stopped; `docker compose ps` returned no services |

## Explain-It Questions

Answer these before treating M3 as complete:

1. Why does `TaskDispatcher` belong in `FleetManager` rather than `FleetCore`, and where does each legal lifecycle transition get enforced?
2. Walk through `find → guard → save`. Which operation rejects a bad transition, and why can the repository save count not change?
3. Explain how `index % shuttleIds.Length` produces the repeating three-shuttle assignment, and why the end-to-end test adds `task-hold` after finishing the nine-task queue.
4. What does rebuilding from a new `TaskBoard` and a new repository prove that reusing the old board would not?
5. Why does the fixture reset call `GetAsync` for `__fixture_probe__` before `DELETE`, and how does the shared collection prevent the two DB tests from racing?

The owner may review or revise the focused code completions before deciding what to keep. The milestone table remains unchanged until the explain-it step is complete.