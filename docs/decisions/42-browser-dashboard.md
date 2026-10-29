# Decision 42: Browser dashboard client and Fleet Manager API

**Date:** 2026-10-26

## Status

Chosen: **A. A Vue/Vite dashboard built into Fleet Manager's static web root.**

The dashboard and its API share the Fleet Manager's configured URL, currently `http://127.0.0.1:5080`.

## Problem

The Fleet Manager currently exposes JSON endpoints and listens on port 5080, but the repository has no browser UI. The M7 dashboard workflow is therefore not available as a page an operator can open.

The dashboard needs to show configured rack lanes, shuttle state, queued tasks, and alarms. Its pause, resume, and acknowledge controls must use the existing operator command contract and Fleet Manager MQTT handling.

There is a recovery limit the page must not hide: `Shuttle.Recover()` clears the shuttle's fault state, but it does not remove a blocked lane from the simulator's rack. If the task is retried while that lane remains blocked, the shuttle can fault again. This UI assignment must not claim that acknowledging the alarm alone clears the physical blockage or proves the complete M7 fault scenario.

There is also a pause limit: the current `PauseState` is inside the Fleet Manager, while `ShuttleSim` is a separate process and the shuttle contract has no pause/hold command. The UI can show that the Fleet Manager recorded a pause request, but it must not present that as proof that an active shuttle stopped.

## Options considered

| Option | Meaning | Trade-off |
|---|---|---|
| **A. Bundle Vue into Fleet Manager static files** (chosen) | Build the frontend and serve it from the same host and port as the API. | One browser URL and same-origin API calls; the Docker build needs a Node build stage. |
| **B. Vue/Vite development server with an HTTP proxy** | Run the dashboard at port 5173; Vite forwards its `/api` requests to the Fleet Manager at port 5080. | Easy frontend iteration, but adds a second server and a second URL to the local operator workflow. |
| **C. Browser connects to MQTT directly** | The page subscribes and publishes through a broker WebSocket connection. | Requires broker WebSocket configuration and exposes the broker to browser clients; it bypasses the Fleet Manager's HTTP boundary. |

## Decision and reasons

- **A.** Use Vue with Vite to produce static assets in `src/FleetManager/wwwroot`. Fleet Manager serves those assets and its API from the same origin. The browser URL is `http://127.0.0.1:5080`.
- **B.** Use Node 22 in a build stage in `docker/Dockerfile.build` so the Vue assets are built reproducibly before the .NET build. For local development, build the assets with `npm ci` and `npm run build` before starting `FleetManager serve`.
- **C.** The browser uses relative `/api/...` URLs. Same-origin requests need no CORS configuration.
- **D.** The browser does not connect to MQTT. Add a small REST surface to the Fleet Manager for dashboard reads and operator commands. The command endpoint publishes the existing `OperatorCommandMessage` on the existing operator topic; the current handler remains responsible for pause, resume, and recovery.
- **E.** Add dashboard configuration for the displayed lane IDs and refresh interval. The UI must render configured lanes rather than inventing lane names or dimensions.
- **F.** Keep contract version 4 and its message shapes unchanged. Do not add a broker WebSocket listener or change Docker service ports.

The Fleet Manager API surface for the assignment is:

| Method and route | Purpose |
|---|---|
| `GET /api/dashboard/config` | Return configured lane IDs and the UI refresh interval. |
| `GET /api/dashboard/shuttles` | Return the shuttle snapshots currently known to the registry, with the Fleet Manager's pause-request state. |
| `GET /api/dashboard/tasks` | Return the current task queue. |
| `GET /api/dashboard/alarms` | Return persisted alarms. |
| `POST /api/dashboard/operator-command` | Validate a dashboard request and publish the corresponding existing operator command. |

The command request uses `shuttleId`, `kind` (`Pause`, `Resume`, or `Acknowledge`), and optional `reason`. A successful HTTP response means the command was accepted for publishing; it does not claim the shuttle has completed recovery. The dashboard refreshes its state from the read endpoints.

## Rejected options

- **B. A separate Vite development server** is rejected for the operator workflow because it would require the operator to start and remember a second server and URL.
- **C. Direct browser-to-MQTT communication** is rejected because it requires broker WebSocket exposure and duplicates the Fleet Manager's existing operator-command boundary in the browser.

## What would change the decision

- If frontend iteration becomes cumbersome, use Vite's development server locally while preserving the built static files and same-origin Fleet Manager deployment for the operator workflow.
- If live updates need lower latency than configured HTTP polling, evaluate server-sent events or WebSockets before exposing MQTT to the browser.
- If the browser must operate outside the local trusted development environment, add an authentication and authorization decision before exposing operator controls.
- If M7's fault scenario must complete without external intervention, first decide how the simulation represents removal of a physical blockage. Do not silently make `Acknowledge` remove it.
- If Pause must stop a shuttle in the separate `ShuttleSim` process, add a separate contract and transport decision. Do not treat the Fleet Manager's local pause flag as a vehicle confirmation.

## Scope and compatibility

- The lesson adds the browser UI, Fleet Manager static-file hosting, a reproducible frontend build stage, and the REST bridge.
- Existing WMS routes and their response shapes remain unchanged.
- Existing MQTT operator commands continue to work.
- The UI displays the result it receives; it does not claim that alarm acknowledgement removes a physical fault.
- The UI labels the Fleet Manager's pause request separately from confirmed shuttle motion state.
- No message contract, contract version, broker configuration, or Docker port changes are required.

## References

- [End-product spec](../../spec/shuttle-spec.md), M7
- [Decision 35: Dashboard diagnostics](./35-dashboard-diagnostics.md)
- [Decision 39: Shared pause state](./39-shared-pause-state.md)
- [Decision 40: Dashboard-driven fault recovery](./40-dashboard-fault-recovery.md)
- [Decision 41: M7 end-to-end fault scenario](./41-m7-fault-scenario.md)
