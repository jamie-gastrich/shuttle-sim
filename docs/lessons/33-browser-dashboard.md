# Lesson 33: A real browser dashboard for the shuttle fleet

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET and Vue · **Prerequisite:** [lesson 32](32-m7-fault-scenario.md)

**Concepts this assignment requires:**

- [ ] **A browser client and a static build** — how Vue becomes files the Fleet Manager can serve → [§1](#1-the-browser-client)
- [ ] **An HTTP boundary in front of MQTT** — how dashboard requests read Fleet Manager data, including saved alarms, and publish commands without exposing the broker → [§2](#2-the-http-boundary)
- [ ] **Polling and operator feedback** — how the page refreshes its view and reports command results → [§3](#3-polling-and-operator-feedback)

Three new concepts. That is the cap.

**Not in this lesson:** changing the shuttle message contract, connecting the browser to MQTT, adding authentication, deploying the UI as a production site, or changing the WMS API.

---

## Where this fits

The existing Fleet Manager listens on its configured URL, currently `http://127.0.0.1:5080`, and exposes JSON APIs. It does not serve a browser dashboard. This assignment adds the missing Vue page, serves its built files from Fleet Manager, and adds a small set of dashboard-specific API routes.

The page and API share `http://127.0.0.1:5080`. Vite builds the Vue app into `src/FleetManager/wwwroot`; the .NET app serves the built page and its JSON endpoints from one origin. The browser never connects directly to the MQTT broker. The dashboard endpoint publishes the same `OperatorCommandMessage` on the existing operator topic; the Fleet Manager's current handler consumes it.

The dashboard will show configured rack lanes, shuttle positions and states, queued tasks, and alarms saved in SQL. Pause, Resume, and Acknowledge actions use the existing operator workflow.

**Important recovery limit:** `Shuttle.Recover()` clears the shuttle's fault state; it does not remove a blocked lane from the simulator's rack. A task retried against a still-blocked lane can fault again. This assignment makes the operator UI real, but it does not claim that the full M7 blocked-path scenario is recovered until the blockage itself has a defined, verified removal step.

**Important pause limit:** `PauseState` is local to the Fleet Manager. The simulator is a separate process, and the shuttle contract has no pause/hold command. Show the Fleet Manager's pause request as a request; do not label it as proof that an active shuttle has stopped.

**Important alarm data gap:** the current serve loop publishes alarms but does not save them, and `PostgresAlarmRepository` does not create the `alarms` table. The alarm route therefore cannot return saved alarms until this assignment adds that save path and table creation. The alarm route is not yet verified until that work is done.

---

## Verification table

Everything below was checked on **2026-10-28**. Scratch work went in `/tmp/opencode`. The broker and database were left down.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | Installed .NET SDK | `dotnet --version` | `10.0.113` |
| 2 | Baseline build | `cd src && dotnet build ShuttleFleet.slnx -v q` in a scratch copy | Build succeeded; `0 Warning(s)`, `0 Error(s)` |
| 3 | Baseline non-integration tests | `cd src && dotnet test ShuttleFleet.slnx -v q --filter "Category!=Integration"` in a complete scratch copy | `232` passed, `0` failed across 9 test projects: Contracts.Tests 8, ShuttleCore.Tests 31, MockWms.Tests 9, FleetCore.Tests 73, Messaging.Tests 10, TrafficCore.Tests 6, MqttMessaging.Tests 3, ShuttleSim.Tests 10, FleetManager.Tests 82 |
| 4 | Contract version | read `src/Contracts/MessageTypes.cs` | `ContractVersion.Current = 4` |
| 5 | Existing WMS routes | read `src/FleetCore/WmsRoutes.cs` | `/api/orders`, `/api/orders/{id}`, `/wms/orders/{id}/cancel`, `/wms/shuttles/{id}`, `/wms/task-queue`, `/wms/delivery-failures` |
| 6 | Static dashboard files today | grep `UseStaticFiles`, `UseDefaultFiles`, `wwwroot` in `src/FleetManager` | no matches |
| 7 | Operator command topic and handler | read `src/Messaging/TopicScheme.cs` and `src/FleetManager/Program.cs` | topic `shuttle/operator/command`; `HandleOperatorCommandAsync` subscribes at line 265 |
| 8 | Alarm save path | read `src/FleetManager/FleetServeLoop.cs` and `src/FleetManager/PostgresAlarmRepository.cs` | serve loop only publishes; repository has `SaveAsync` but no `CREATE TABLE alarms`, and no caller |
| 9 | Operator command JSON shape | read `src/Contracts/OperatorCommandMessage.cs` | `Kind` is an enum; the HTTP body uses a string, so the route must map it |
| 10 | Node toolchain | `node --version`; `npm --version` | both commands: `command not found`; frontend build not yet verified |
| 11 | Docker toolchain | `docker --version`; `docker compose -f docker/compose.yml config --services` | Docker `29.8.1`; services `broker`, `database` |
| 12 | Blocked-lane environment binding | scratch console with `Shuttle__BlockedLanes__0=A01-L02` | `A01-L02`; count `1` |
| 13 | Static-file serving model | scratch ASP.NET app with `ContentRootPath = AppContext.BaseDirectory` | `GET /` returned `200` with `wwwroot/index.html`; a toy `/ping` route also returned `200` |
| 14 | Current build Dockerfile | read `docker/Dockerfile.build` | .NET SDK stage only; builds and tests the solution; no Node stage |
| 15 | Current ignore rules | read `.gitignore` | no `src/Dashboard/node_modules/` or `src/FleetManager/wwwroot/` |
| 16 | Updated Docker build with Node stage | not run; Node is absent from this environment | **not yet verified** |

---

## The lecture

Today we are going to learn about **a browser client**, **an HTTP boundary**, and **polling with operator feedback**.

### 1 The browser client

**What it is.** Vue turns data from the Fleet Manager into a page an operator can read and use. Vite builds the Vue source into ordinary HTML, JavaScript, and CSS files.

**Why we need it.** A URL that returns JSON is useful to another program, but it is not an operator dashboard. The Vue page gives the operator a rack view, fleet status, alarms, and controls in one place.

**The URL.** The Fleet Manager will serve the page at `http://127.0.0.1:5080`. The same server will handle its `/api/dashboard/...` requests. There is one app process and one browser URL.

### 2 The HTTP boundary

**What it is.** The browser sends ordinary HTTP requests to dashboard routes on the Fleet Manager. The Fleet Manager reads fleet state or publishes an existing MQTT command.

**Why we need it.** The Fleet Manager owns the MQTT connection. The new HTTP route validates the operator command before publishing. Giving the browser direct broker access would add another way to control the fleet and require broker WebSocket configuration.

**The alarm data source.** The alarm route reads saved rows. The serve loop today only publishes alarms. Save each raised alarm and create the `alarms` table before the route can show history. This is the data source for the HTTP boundary, not a fourth browser concept.

**What the command response means.** A successful response means the Fleet Manager accepted the command for publishing. It does not mean the shuttle has already moved or recovered. The page gets the resulting state from the next data refresh. The HTTP body's `kind` is a string; map it to `OperatorCommandMessage.Command` before publishing.

### 3 Polling and operator feedback

**What it is.** The page periodically requests the latest shuttle, task, and alarm data. The refresh interval comes from dashboard configuration. While a command is being sent, its button is disabled; if the request fails, the page displays the error instead of showing a success message.

**Why we need it.** Polling is easy to understand and fits the same GET pattern as the existing WMS routes. The operator sees new state shortly after it changes without the browser subscribing to MQTT.

---

## Decision

The full design choice is [record 42](../decisions/42-browser-dashboard.md). Short version:

- Vue/Vite builds static assets into the Fleet Manager's `wwwroot`.
- The Fleet Manager serves the page and API from `http://127.0.0.1:5080`.
- The updated Docker build will run the frontend build before the .NET build.
- The Fleet Manager supplies dashboard data and publishes existing operator commands.
- The serve loop saves raised alarms so the dashboard's alarm route has data.
- The dashboard uses configured lane IDs and refresh interval.
- The message contract remains at version 4.

---

## The assignment

Build the browser page and its REST bridge. The assignment is complete only when an operator can open the page, see the fleet data, and issue Pause, Acknowledge, and Resume through the page. Do not mark the M7 fault-recovery done-when as proven solely because the page accepts an Acknowledge request.

### Files you may change or add

| File or folder | What changes |
|---|---|
| `src/Dashboard/` | New Vue/Vite app, Node 22 requirement, committed `package-lock.json`, and a short local run guide with build and startup commands. |
| `src/FleetManager/Program.cs` | Serve default/static files and map the dashboard read and command routes. |
| `src/FleetManager/ShuttleRegistry.cs` | Expose a safe snapshot of all currently known shuttles for the read route. |
| `src/FleetManager/FleetServeLoop.cs` | Save each raised alarm through the alarm repository. Keep existing constructors and tests compiling. |
| `src/FleetManager/PostgresAlarmRepository.cs` | Create the `alarms` table if it does not exist, mirroring the other Postgres repositories. |
| `src/FleetManager/appsettings.json` | Add dashboard lane IDs and a positive refresh interval. |
| `docker/Dockerfile.build` | Build the frontend assets in a Node stage and copy them into the .NET build stage. |
| `.gitignore` | Ignore `src/Dashboard/node_modules/` and generated dashboard assets under `src/FleetManager/wwwroot/`. |
| `src/FleetManager.Tests/` | Test dashboard API responses, validation, alarm persistence, and command publishing. |
| `src/FleetManager.Tests/FleetManager.Tests.csproj` | Add a test dependency only if the existing test project needs it for API testing. |

Do not edit other files unless you first stop and report why the assignment cannot be completed within this list. In particular, do not edit `src/Contracts/`, `docs/contract/`, `src/ShuttleSim/appsettings.json`, `docker/compose.yml`, or the top-level milestone table.

### Requirements

**R1 — the browser page.** Implement the Vue page with:

- A rack map rendered from the lane IDs returned by `GET /api/dashboard/config`.
- Shuttle markers showing shuttle ID, state, current lane, and whether a pause has been requested in the Fleet Manager.
- A task queue and alarms including severity and acknowledgement state.
- Pause, Resume, and Acknowledge controls for a selected shuttle.
- A visible loading state and a visible error state. Do not report a command as successful when the HTTP request fails.

**R2 — the data API.** Implement the routes from decision 42:

- `GET /api/dashboard/config` returns the configured lane IDs and refresh interval.
- `GET /api/dashboard/shuttles` returns the registry's known shuttle snapshots with a `pauseRequested` indicator from the shared `PauseState`.
- `GET /api/dashboard/tasks` returns the current queued tasks.
- `GET /api/dashboard/alarms` returns persisted alarms, including severity and acknowledgement time.
- `POST /api/dashboard/operator-command` accepts `{ "shuttleId": "SH-01", "kind": "Pause", "reason": null }`, where `kind` is exactly `Pause`, `Resume`, or `Acknowledge`.

Reject malformed or unsupported command requests, including a blank `shuttleId`, with a client error response. Do not silently drop bad input. The HTTP body's `kind` is a string; map it to `OperatorCommandMessage.Command` before publishing. Publish valid commands on the existing `topics.OperatorCommand` topic as `MessageFamily.Json`; create the current contract message and let the existing handler consume it. A successful response must not claim that the shuttle has completed the command.

**Alarm persistence:** the current serve loop publishes alarms but never calls `PostgresAlarmRepository.SaveAsync`, and the repository never creates the `alarms` table. Add both before the alarm route can return data. This is not yet verified.

**R3 — configuration.** Add `Dashboard:LaneIds` and `Dashboard:RefreshIntervalMs`. Validate that the lane list is present and non-empty and that the interval is positive. Do not hard-code lane names or the refresh interval in the UI.

**R4 — same-origin browser requests.** Serve the Vite build from Fleet Manager's `wwwroot` using the framework's default-file and static-file middleware. The page must use relative `/api/...` URLs. Do not add a CORS policy or expose MQTT to the browser.

**R5 — contract and existing behavior.** Keep `ContractVersion.Current` at `4`. Do not change any existing WMS route, operator command topic, shuttle message, or recovery behavior.

### Acceptance criteria

```bash
# Frontend first, because the backend tests may request the built page.
cd src/Dashboard
npm ci
npm run build
cd ../..

# Backend
cd src
dotnet build ShuttleFleet.slnx
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
cd ..

# Reproducible container build and test
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build
```

With the broker and database running, use separate terminals for the Fleet Manager, simulator, and mock WMS. Build the dashboard first with `npm ci` and `npm run build`; explain the commands in `src/Dashboard/README.md`.

```bash
# Terminal 1: start MQTT and PostgreSQL from the repository root.
docker compose -f docker/compose.yml up -d

# Terminal 2: build the dashboard, then start Fleet Manager from the repository root.
cd src/Dashboard
npm ci
npm run build
cd ../..
export SHUTTLE_DB_CONNECTION_STRING='<local PostgreSQL connection string>'
dotnet run --project src/FleetManager -- serve

# Terminal 3: start the mock WMS from the repository root.
dotnet run --project src/MockWms

# Terminal 4: start the simulator with one blocked lane.
Shuttle__BlockedLanes__0=A01-L02 dotnet run --project src/ShuttleSim -- serve
```

Submit an example order to the Fleet Manager from another terminal:

```bash
curl -X POST http://127.0.0.1:5080/api/orders \
  -H 'Content-Type: application/json' \
  -d '{"orderId":"DASH-001","kind":"store","palletId":"PALLET-001","laneId":"A01-L02"}'
```

Open `http://127.0.0.1:5080` and verify:

1. The browser displays the configured rack lanes and receives shuttle, task, and alarm data from same-origin API routes.
2. Pause and Resume requests reach the Fleet Manager and change the visible pause-request indicator. The UI does not claim the shuttle stopped.
3. With an alarm present, Pause, Acknowledge, and Resume requests are published through the existing operator topic; the saved acknowledgement is visible after refresh. The alarm must be saved before it can appear; if the alarm route returns an empty list, the save path is missing. If the shuttle faults again because the path remains blocked, show that state and report it; do not describe the physical fault as recovered.
4. Stopping the Fleet Manager makes the page unavailable; the UI does not replace missing data with an empty success state.
5. Invalid command values receive a client error and are not published.
6. `GET /` returns the built dashboard HTML, and its JavaScript and CSS assets load successfully.
7. No contract, Docker service, or Docker port changes are present. The build Dockerfile is updated only to include the Vue asset build.

Stop each foreground process with Ctrl+C and run `docker compose -f docker/compose.yml down` from the repository root when finished.

The full Pause → Acknowledge → Resume scenario with a persistent blocked lane is **not yet verified by this assignment**. Before claiming that M7's fault-recovery done-when is met, a later decision must define how the simulated blockage is removed and a real end-to-end run must verify it.

The pause request is also **not yet verified as a vehicle stop** across the separate Fleet Manager and ShuttleSim processes. Before claiming the dashboard can pause an active shuttle, a later contract decision must define and test that cross-process behavior.

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the `topics.OperatorCommand` publish from the REST command route | The command-publishing test or dashboard scenario fails; no operator action reaches the existing handler. |
| b | Accept an unknown command kind | The invalid-command test fails. |
| c | Omit the built `index.html` from the Fleet Manager static root | The browser page does not load. |
| d | Replace configured lane IDs with a literal list in Vue | The configuration test or review check fails because the page no longer follows the server-provided map. |
| e | Remove the alarm save call | The alarm route returns no rows and the alarm acceptance step fails. |

### Study first

1. The Fleet Manager's local `HandleOperatorCommandAsync` function — understand why publishing the existing message is safer than duplicating pause and recovery logic in a new HTTP handler.
2. `ShuttleRegistry`'s lock — the registry has no all-shuttle snapshot method today. The new snapshot method must take the lock and copy the dictionary so a status update cannot change it during enumeration.
3. Static-file hosting — trace a browser request for `/` to the built `index.html`, then a request for `/api/dashboard/shuttles` to the Fleet Manager route.

### Say it out loud

1. Why does the browser use HTTP to the Fleet Manager instead of connecting directly to MQTT?
2. What does a successful HTTP response to Pause promise, and what does it not promise?
3. How does the dashboard learn that a shuttle's state changed after an operator command?
4. What should the operator see if the Fleet Manager or its API is unavailable?

### What comes next

This assignment adds the missing browser surface for M7. Authentication, production hardening, push-based updates, cross-process vehicle pause, and verified removal of a simulated physical blockage remain separate future decisions.
