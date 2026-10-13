# Decision Record 20: Liveness — heartbeats as periodic status, `lastSeenAt`, and the timeout window

**Date:** 2026-10-13
**Milestone:** M5 (Faults and recovery) — lesson 13: heartbeats and timeouts.
**Status:** Accepted. Append-only. Supersedes nothing. Links back to records 07, 08, 09, 10, 17, 18, 19.
**Builds on:** [record 17](17-occupancy-and-fault-reservations.md) (occupancy vs reservation; §5 explicitly deferred heartbeats, `lastSeenAt`, timeout detection, alarms, recovery, and restart reconciliation to M5), [record 18](18-deferred-dispatch-and-storage-policies.md) (closest-eligible dispatch depends on health), [record 19](19-run-loop-wiring-and-concurrency.md) (the run loop and the `lock` discipline), [record 07](07-message-contract-v2-envelope-and-topics.md) (envelope and topics), [record 08](08-mqtt-client-qos-and-configuration.md) (MQTT QoS and configuration).
**Replaced by:** a later record that changes the liveness signal, the clock used for `lastSeenAt`, or the timeout rule. Append-only.
**Wire contract:** **no change** to `docs/contract/v1.md` or `v2.md`. No new message kind; the heartbeat reuses `shuttleStatus`. No version bump.

---

## 1. What we are deciding

Record 17 Decision D ended M4 with an in-memory occupancy map and no time-based expiry. It named exactly what M5 must add: heartbeats, `lastSeenAt`, timeout detection, alarms, operator-acknowledged recovery, and restart reconciliation.

Lesson 13 takes the first slice of M5: **heartbeats and timeouts**. It decides three things:

**A.** How does a shuttle prove it is alive? A dedicated heartbeat message kind, a periodic status, or the broker's own connection monitoring?
**B.** Whose clock stamps the time we last heard from each shuttle — the shuttle's `Timestamp`/`SentAt`, or the fleet manager's clock at receipt?
**C.** Is "unresponsive" a stored flag or a derived fact, and where does the timeout window come from?

The lesson that follows this record decides alarms; a later one decides recovery and reassignment. This record stops at detection.

---

## 2. Decision A: a heartbeat is a periodic `shuttleStatus`, not a new message kind

The contract today carries three kinds: `moveCommand`, `commandAck`, and `shuttleStatus` (`docs/contract/v2.md`). There is no heartbeat kind. A heartbeat must make silence mean something, so someone has to send something on a regular cadence.

| | A: periodic `shuttleStatus` (chosen) | B: a dedicated `heartbeat` kind | C: broker keep-alive + Last Will only |
|---|---|---|---|
| New wire kind and version bump | **no** — stays v2 | **yes** — a fifth kind, `v3.md` | no |
| Carries state and position for free | **yes** — `Status.State`, `Status.Position`, `Status.Timestamp` | **no** — an empty ping | no |
| Detects a hung shuttle process | yes | yes | **no** — it only sees the connection drop |
| One source of truth for liveness | yes — any inbound message is liveness | yes for heartbeats, but statuses and heartbeats are separate | no — connection-level, not per message |
| New work | small | new kind, parser, topic, tests | will config, retained/will behavior to verify, and still not enough |

**Decision A: the heartbeat is a periodic `shuttleStatus` message.** The shuttle publishes its current status on a fixed interval even when nothing changes. The fleet manager treats *any* inbound message from a shuttle as proof of liveness. Rejected: a dedicated `heartbeat` kind (a fifth message kind and a contract version bump for something the status already carries — and a missed heartbeat is *also* a missed state update, which is useful to see); broker keep-alive + Last Will (it tells us the broker lost the connection, not that a connected shuttle process is alive but stuck; it also needs will/retained configuration we do not need yet). The installed MQTTnet 5.2.0.1603 does expose `MqttClientOptions.WillTopic`, `WillDelayInterval`, and `KeepAlivePeriod` (verified in the package XML), so option C is available; it is simply the wrong layer for this job.

**Where the heartbeat goes on the wire.** Nowhere new. It uses the existing `shuttle/{id}/status` topic and the `MessageFamily.Status` QoS from configuration. No topic change, no contract change.

**Honest limit.** A heartbeat proves the heartbeat loop is running. It does not prove the shuttle's whole job is healthy. A shuttle can ping while stuck mid-command. Real systems put a small diagnostic in the heartbeat for this reason; we do not yet.

---

## 3. Decision B: `lastSeenAt` is stamped by the fleet manager's clock at receipt, and never moves backwards

`lastSeenAt` is the moment the manager decided it heard from a shuttle. Two different clocks are in play: the shuttle stamps `ShuttleStatus.Timestamp`, and the manager stamps its own receive time.

| | A: the manager's clock at receipt (chosen) | B: the shuttle's `Timestamp` | C: the later of the two |
|---|---|---|---|
| Immune to shuttle clock skew | **yes** | **no** — a fast or slow shuttle clock distorts the window | partly |
| Immune to network delay | **yes** — a late message simply counts as seen now | partly | partly |
| Matches the question being asked ("have *I* heard from it?") | **yes** | no | close but muddier |
| Trivial to explain | **yes** | yes | less so |

**Decision B: stamp `lastSeenAt` with the manager's clock at receipt.** The question liveness answers is *"has the fleet manager heard from this shuttle recently?"* That is a fact about the manager's clock. The shuttle's `Timestamp` is kept for order and diagnostics, not for the window. A late message counts as seen *now*, which is correct: it just arrived.

**Monotonicity.** `RecordSeen` must never move `lastSeenAt` backwards. Two real reasons: MQTT "at least once" delivery can repeat a message, and a retained status can replay an old one. An older stamp must be ignored, not applied. This mirrors the `EnsureInOrder` guard already used by `FleetTask` and `Shuttle`.

---

## 4. Decision C: "unresponsive" is derived on demand, and the window is configured

| | A: store only `lastSeenAt`, derive the verdict (chosen) | B: store a per-shuttle `IsUnresponsive` flag |
|---|---|---|
| Cannot go stale | **yes** — recomputed against `now` every call | **no** — a flag must be cleared by a second code path |
| A late heartbeat flips it back | **yes**, automatically | only if the clear path runs |
| Simple state | **yes** — one timestamp per shuttle | two pieces of state to keep in sync |
| Testable as a pure function | **yes** | harder |

**Decision C: store `lastSeenAt` only; compute `IsUnresponsive(shuttleId, now)` from it.** The rule is `never seen` **or** `now - lastSeenAt > window`. A shuttle silent for *exactly* the window is still responsive; we call it lost only once it is past the window. This is a choice, and the lesson pins it with a boundary test. Do not store a flag: a flag needs a second code path to clear, and a late heartbeat must clear it instantly.

**The window comes from configuration** (`FleetManager:LivenessTimeoutMs`), never a literal (AGENTS.md: no hard-coded timeouts). `ShuttleHealth` takes the window in its constructor and validates it is positive, in the same way `RetryPolicy` does. `ShuttleHealth` itself is pure: it reads no clock, opens no socket, and touches no database. Each call takes `now`.

**Thread safety.** The status path (a later lesson) will call `RecordSeen` from an MQTT handler while queries run elsewhere. `ShuttleHealth` therefore guards its dictionary with a `lock`, exactly like `OccupancyMap` and `SegmentReservations` from record 19. The lock covers the read-modify-write in `RecordSeen`, so the monotonic check and the store are one atomic step.

---

## 5. Where the new code lives

- **`src/FleetManager/ShuttleHealth.cs`** — the pure liveness decision. It lives in `FleetManager` next to `OccupancyMap`, not in `FleetCore`. Reason: it is fleet-side runtime state, the same kind of thing as the occupancy map, and `FleetManager.Tests` already tests pure fleet-manager classes (`RunLoopGate`). `FleetCore` stays the pure task core with no shuttle or health concepts.
- **`src/ShuttleSim/HeartbeatPublisher.cs`** — the producer. It owns a `PeriodicTimer` and calls the endpoint's publish method each tick. It takes its interval in the constructor; the interval comes from `Shuttle:HeartbeatIntervalMs`.

---

## 6. What would change these choices

- **The contract gains an alarm/recovery kind (next lessons)** → the heartbeat can stay a status; alarms get their own kind and version bump. This record is unaffected.
- **A heartbeat must carry richer diagnostics** (queue depth, battery, error code) → a dedicated `heartbeat` kind may become worth its own version bump. Revisit A with a new record.
- **The fleet manager becomes multi-instance** → `lastSeenAt` in one process's memory is not enough; it must be shared (database or distributed cache). Revisit B's storage location, not the clock.
- **The run loop's simulated clock and the status path's wall clock must agree** → the window must be defined in one clock domain. Today the run loop uses `startTime + ticks` (record 19) and the status path will use `DateTime.UtcNow`. This is a known sim/reality seam; a new record settles it when the status path is wired.
- **The shuttle hosts both the heartbeat loop and the command handler** → both touch the one `Shuttle` object from different threads; a single owner or a lock is needed. Revisit in the wiring lesson.

---

## 7. Verification table (measured)

Everything below was checked on **2026-10-13** by a command or a file read. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l13base` (no `bin/`/`obj/` copied in). The toy ran at `/tmp/opencode/l13toy`. Both scratch directories were deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `/tmp/opencode/l13base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; `25+18+8+4+5+6+2+16` = **84** tests; `FleetManager.Tests` = **16** |
| No liveness code exists yet | `grep -rniE 'heartbeat\|lastSeen\|health\|liveness' src --include='*.cs' --include='*.json'` | no matches |
| The shuttle publishes status only on a move | read `src/ShuttleSim/ShuttleEndpoint.cs` | `PublishStatusAsync` called at `:75` (`moving`) and `:79` (`idle`); nowhere else |
| The contract has no heartbeat/alarm/recovery kind | read `docs/contract/v2.md` | three kinds: `moveCommand`, `commandAck`, `shuttleStatus`; `Version: 2` |
| `ShuttleStatus.Timestamp` exists | read `docs/contract/v2.md` | `Timestamp` field, ISO 8601 UTC |
| MQTTnet offers will + keep-alive | read `~/.nuget/packages/mqttnet/5.2.0.1603/lib/net10.0/MQTTnet.xml` | `MqttClientOptions.WillTopic`, `WillDelayInterval`, `KeepAlivePeriod` present |
| The fleet manager has no long-running status path | read `src/FleetManager/Program.cs` | status handled only inside `RunMoveAsync`, for one move round trip |
| The toy shows `lastSeenAt` and the timeout window | `/tmp/opencode/l13toy`, `dotnet run` | `SH-02` silent `3.0s` → `ok`, `4.0s` → `UNRESPONSIVE` at `00:00:06`; exit 0 |
| Contract is still v2 | `grep -qE '^Version: 2$' docs/contract/v2.md` | match; `test ! -f docs/contract/v3.md` also true |

Not yet verified: `ShuttleHealth`, `HeartbeatPublisher`, the two config keys, and the new tests — this assignment specifies them and the reviewer measures them.

---

## 8. What this record unblocks

- Lesson 13 can assign a pure `ShuttleHealth` with a fixed shape, a `HeartbeatPublisher`, both config keys, and the tests, with no unguided choices.
- The next M5 lesson can wire the fleet-side status path that calls `RecordSeen` and raise an alarm when `IsUnresponsive` turns true. Record 17 §4 already forbids auto-release on timeout; the alarm is knowledge, not action.
- Record 18's closest-eligible dispatch has a health input (`IsUnresponsive`) once this is wired: a shuttle that is unresponsive is not eligible.
- The contract stays v2. The next contract change is the alarm/recovery kind, which belongs to a later record.
