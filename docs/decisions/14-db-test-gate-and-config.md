# Decision 14: DB-backed tests, the fresh-container gate, and database configuration

**Date:** 2026-10-11
**Milestone:** M3 (Fleet manager core) — how a test that needs a real database stays out of the fresh-container gate, and how the database connection string is built from configuration. Written to fix lesson-08 defects **L1** and **L2** from [review 08](../reviews/08-sql-persistence-and-restart.md). It builds on [record 13](13-fleet-state-persistence.md) (the persistence design), [record 04](04-fresh-container-build.md) (the fresh-container build), and [record 03](03-database.md) (PostgreSQL and its dev-posture environment variables).
**Status:** Accepted.
**Replaced by:** any later record that changes where DB-backed tests live, how the container build excludes them, or how the connection string is configured. Append-only; records are never rewritten.
**Wire contract:** unchanged. This record is about tests and configuration only. No message kinds change; no version bump.

---

## 1. What we are deciding

Three things. They are one decision because they all come from the same failure: a test that needs a database broke the fresh-container gate.

1. **Where a DB-backed test lives.** Pure domain tests stay pure. The restart-survival test needs the fleet-manager assembly and a database, so it belongs with the app, not with the pure domain.
2. **How the fresh-container gate stays green with no database.** The container has no database. The gate must still exit 0, and it must not silently skip the DB test and call that a pass.
3. **How the connection string is configured.** No hard-coded host, port, user, or password. The literal values are defaults built from environment variables.

---

## 2. Context: what review 08 found

- **B1 (blocker).** The restart-survival test opened `127.0.0.1:5432` with no guard. `docker run --rm shuttle-fleet-build` exited **1** with no database present.
- **S1 (should-fix).** The test sat in `FleetCore.Tests` and dragged a `FleetManager` reference (and therefore Npgsql and MQTTnet) into the pure test project. The owner has since moved the test to `FleetManager.Tests` and dropped the reference from `FleetCore.Tests`; this record confirms that placement.
- **C2 / L2 (minor).** The lesson offered a hard-coded connection string literal, which conflicts with AGENTS.md's "no hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration." and with `docker/README.md`'s documented `DB_HOST_PORT` / `SHUTTLE_DB_*` overrides.
- **Owner's first fix attempt.** The owner added `EnsureDatabaseIsRunningAsync` / `StartDatabaseAsync`, which call `Process.Start("docker", …)` to auto-start the database from inside the test. This is impossible in the container: the build image is `mcr.microsoft.com/dotnet/sdk:10.0` and has no `docker` binary. Measured this session: `docker run --rm shuttle-fleet-build which docker` exits **1** (no path), while `which dotnet` prints `/usr/bin/dotnet` and exits 0. Auto-starting docker from inside the container is rejected, permanently.

---

## 3. Survey A: where the DB-backed test lives

| | A: `FleetManager.Tests` (chosen) | B: a separate `FleetManager.IntegrationTests` project | C: `FleetCore.Tests` (the original) |
|---|---|---|---|
| Keeps `FleetCore.Tests` pure (no Npgsql, no manager) | yes | yes | **no** — review 08 S1 |
| Project already references the code under test | yes (`FleetManager`) | needs a new reference | forced a wrong reference in |
| Number of new projects | none | one | none, but wrong |
| Matches record 13 §4 (I/O edge with the app) | yes | yes | no |

**Decision A: the restart-survival test lives in `FleetManager.Tests`.** That is where the Postgres implementation lives, it already references `FleetManager`, and it keeps `FleetCore.Tests` a pure domain suite. This confirms the owner's move and review 08's S1.

Rejected (C): putting an I/O test in the pure project is what created B1 and S1 in the first place.

Deferred (B): if integration tests grow past a handful, split them into their own project. Not now; one test does not justify a new project, and a separate project would still need a filter or an exclusion to keep the container green.

---

## 4. Survey B: how the fresh container stays green

The container has no database. We need the gate to exit 0 **and** to prove the DB test is not silently fake-green.

| | A: `[Trait("Category","Integration")]` + `--filter` (chosen) | B: runtime skip (early-return when the DB is unreachable) | C: run a database alongside the build |
|---|---|---|---|
| Container exits 0 with no DB | yes | yes | yes |
| Can tell "ran" from "skipped" | yes — the include-filter run shows `Passed`, not `Skipped` | **no** — a returned test reports `Passed` | yes |
| Needs a working dynamic skip | no | **yes, and xUnit 2.9.3 has none (see below)** | no |
| Honest about what ran | yes — the command names exactly what it excludes | no — a green suite hides a test that did nothing | yes |
| Extra infrastructure in the build | none | none | a DB service inside the build container |

**xUnit 2.9.3 has no working dynamic skip.** Its own XML documentation says so, for `Xunit.Sdk.SkipException.ForSkip`: *"Note that this only works in v3 and later of xUnit.net, as it requires runtime infrastructure changes."* There is no `Assert.Skip` member in 2.9.3. So option B would have to be an early `return`, which xUnit reports as `Passed`. That is exactly the "fake green" AGENTS.md forbids.

**Decision A: DB-backed tests carry `[Trait("Category", "Integration")]`, and the fresh-container command excludes that category.** The container is green and it says what it did not run. A second command, run with the database up, proves the test actually executed and asserted.

Rejected (C): dragging a database service into the build image changes the container's promise from "build and unit-test with no external service" to "build, unit-test, and run integration tests against a service". That is a bigger promise with more failure modes, and record 04's split does not ask for it.

---

## 5. Decision: the trait, the filter, and the two-gate proof

1. The restart-survival test gets `[Trait("Category", "Integration")]`.
2. `docker/Dockerfile.build` line 6 changes from
   `CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m"]`
   to
   `CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m", "--filter", "Category!=Integration"]`.
   This is part of the lesson-08 assignment. It keeps record 04's split — `RUN` builds, `CMD` tests — and only the `CMD` gains a filter. Record 04's rule "no `RUN dotnet test` layer" is untouched.
3. The gate is a pair, and both halves must pass:
   - **Container green, no DB:** `docker build -f docker/Dockerfile.build -t shuttle-fleet-build . && docker run --rm shuttle-fleet-build` exits 0 and all suites print `Passed!`. Because the trait filter is the only reason the DB-less suite can be green, this *is* the proof the filter works: if the filter were wrong, the integration test would run and fail.
   - **DB test truly ran:** with the database up, `dotnet test src/FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"` prints `Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks` and `Passed: 1`. A test that skipped everywhere would print `Skipped <name>` (or would never appear), so this half cannot be faked.

The `!` in `Category!=Integration` is safe in the Dockerfile: the `CMD` is the JSON exec form, not a shell, so there is no history expansion.

---

## 6. Decision: the connection string is configuration, never a literal

The connection string is built at run time, in this precedence order:

1. `SHUTTLE_DB_CONNECTION_STRING` — if set and non-blank, it is used whole. This is the single escape hatch for a remote or non-standard database.
2. Otherwise, `NpgsqlConnectionStringBuilder` assembled from:
   - `SHUTTLE_DB_HOST` (default `127.0.0.1`)
   - `DB_HOST_PORT` (default `5432`, Npgsql's own default; `docker/README.md` documents this name)
   - `SHUTTLE_DB_USER` (default `shuttle`)
   - `SHUTTLE_DB_PASSWORD` (default `shuttle`)
   - `SHUTTLE_DB_NAME` (default `shuttle`)

These are exactly the variables `docker/compose.yml` and `docker/README.md` already expose. That satisfies AGENTS.md's "use configuration" rule and record 03 §4's dev-time posture. A non-integer `DB_HOST_PORT` fails fast with a clear message instead of silently using 5432.

**Why `DB_HOST_PORT` and not a new `SHUTTLE_DB_PORT`:** the compose file already publishes the database on `${DB_HOST_PORT:-5432}` and `docker/README.md` already documents that name. Adding a second port variable would be two sources of truth for one value.

Rejected: a literal `Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle` in test or app source. It works on one laptop only, and changing it means editing code.

---

## 7. Rejected options, in one list

- **Auto-start docker from inside the test/container.** Impossible: no `docker` binary in the build image (measured). Never do this.
- **Runtime skip via early return.** xUnit 2.9.3 has no working dynamic skip; the test would report `Passed` while doing nothing. Fake green.
- **A dedicated integration-test project, for now.** More project for one test; can be revisited when integration tests grow.
- **A database service inside the build container.** Changes the container's promise and adds failure modes; not asked for.

---

## 8. Verification table (measured)

Experiments ran in `/tmp/opencode` (scratch xUnit projects, deleted afterwards) and against the existing build image; no file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| The build image has no `docker` binary | `docker run --rm shuttle-fleet-build which docker` | exit 1, no path printed |
| The build image does have `dotnet` | `docker run --rm shuttle-fleet-build which dotnet` | `/usr/bin/dotnet`, exit 0 |
| xUnit 2.9.3 has no working dynamic skip | read `~/.nuget/packages/xunit.assert/2.9.3/lib/netstandard1.1/xunit.assert.xml`, `SkipException.ForSkip` doc | "only works in v3 and later of xUnit.net" |
| The exclude filter drops a trait test | scratch xUnit project, `dotnet test --filter "Category!=Integration"` | exit 0, `Passed: 1`, the integration test not run |
| The include filter selects only the trait test | same scratch project, `dotnet test --filter "Category=Integration"` | the deliberately-failing integration test ran; exit 1 |
| The filter works at solution level (the container's shape) | scratch 2-project solution, `dotnet test SolProbe.slnx -v m --filter "Category!=Integration"` | exit 0, both pure tests `Passed` |
| The owner's connection string is config-driven | read `src/FleetManager.Tests/RestartSurvivalTests.cs` `BuildConnectionString` | uses `SHUTTLE_DB_CONNECTION_STRING` then `SHUTTLE_DB_HOST` / `DB_HOST_PORT` / `SHUTTLE_DB_USER` / `SHUTTLE_DB_PASSWORD` / `SHUTTLE_DB_NAME` |

**Not yet verified:** the owner's actual rebuilt container (`docker build … && docker run --rm shuttle-fleet-build` exit 0 with the filter and the trait in place), and the with-DB include run against the real restart test. Those are the assignment and belong to the reviewer's measurement.

---

## 9. What would change this choice

- If integration tests grow past a handful, move them to their own project and give that project its own gate. New record, links here.
- If CI runs the database as a service, CI can run the `Category=Integration` half too; that is a CI record and links here.
- If database configuration moves into `appsettings.json` / the generic host, this record's variables become the defaults behind that loader. New record.

---

## 10. What this record unblocks

- Lesson 08's acceptance can require the trait, the filtered container `CMD`, and the config-driven connection string, with an honest two-part gate.
- Lesson 09 (assignment persistence) and M5 (fault recovery) reuse the same mechanism: DB-backed tests carry the `Integration` trait, the container excludes it, and a with-DB run proves the test executed.
- AGENTS.md's fresh-container "tests pass" definition of done is true again, without silently skipping a test.
