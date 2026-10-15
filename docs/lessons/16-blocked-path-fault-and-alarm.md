# Lesson 16: The blocked-path fault and the second alarm source

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 15: The serve loop and the alarm publish path](15-serve-loop-and-alarm-publish.md) · **Decision:** [record 23](../decisions/23-blocked-path-fault-and-second-alarm.md); builds on [record 21](../decisions/21-alarms-with-severity.md), [record 22](../decisions/22-serve-loop-and-alarm-publish.md), [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), [record 19](../decisions/19-run-loop-wiring-and-concurrency.md), and [record 17](../decisions/17-occupancy-and-fault-reservations.md)

**Concepts this assignment requires:**

- [ ] **The blocked-path fault (an obstacle in the rack, detected by the state machine)** — the rack carries a set of blocked lanes, and `Shuttle.StartMove` checks the whole route before moving; a blocked route means `RaiseFault("blocked path")` and no movement. Taught in [The blocked-path fault](#1-the-blocked-path-fault-an-obstacle-in-the-rack-and-a-state-machine-that-detects-it). Fixed by [record 23 §3](../decisions/23-blocked-path-fault-and-second-alarm.md).
- [ ] **A fault reported on the status lane (a level, not an event)** — the shuttle reports `State = "fault"` and `FaultReason = "blocked path"` on `shuttle/{id}/status`, and its heartbeats keep saying so; the manager's serve loop reads it. Taught in [A fault reported on the status lane](#2-a-fault-reported-on-the-status-lane-a-level-not-an-event). Fixed by [record 23 §4, §7](../decisions/23-blocked-path-fault-and-second-alarm.md).
- [ ] **The second alarm (per-code edges and per-code severity)** — the evaluator's latch is already keyed `(shuttleId, code)`; the loop feeds it the active fault code, the mapper turns a fault phrase into an alarm code, and each code carries its own configured severity. Taught in [The second alarm](#3-the-second-alarm-per-code-edges-and-per-code-severity). Fixed by [record 23 §5, §6](../decisions/23-blocked-path-fault-and-second-alarm.md).

Three new concepts. That is the cap.

Seven things this assignment needs are **assignment requirements, not concepts**. They change where code lives, what it is named, and how we run it, not the ideas above:

- the `FaultReasons` constants ([record 23 §6](../decisions/23-blocked-path-fault-and-second-alarm.md));
- the `Shuttle:BlockedLanes` and `FleetManager:BlockedPathSeverity` configuration keys ([record 23 §6, §8](../decisions/23-blocked-path-fault-and-second-alarm.md));
- the `FaultAlarmCodes` mapper ([record 23 §5, §8](../decisions/23-blocked-path-fault-and-second-alarm.md));
- the `FleetManager.csproj` reference to `ShuttleCore` ([record 23 §6](../decisions/23-blocked-path-fault-and-second-alarm.md));
- updating the existing `AlarmEvaluatorTests` call sites (7 constructor calls, 15 `Evaluate` calls) ([record 23 §11](../decisions/23-blocked-path-fault-and-second-alarm.md));
- the exact test names and the gated counts ([record 23 §11](../decisions/23-blocked-path-fault-and-second-alarm.md));
- the protected-file list and the no-contract-bump rule ([record 23 §9](../decisions/23-blocked-path-fault-and-second-alarm.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** `palletNotDetected` (there is no load or unload command on the wire yet — `moveCommand` is the only kind, so a "pallet not detected" fault cannot be exercised end to end; that is a later slice with its own record); reassigning a failed shuttle's work; operator-acknowledged recovery; restart reconciliation; a manager-side rule that stops routing other shuttles into a blocked lane (the alarm is knowledge, not action — [record 17 §4](../decisions/17-occupancy-and-fault-reservations.md)); persisting the fault codes or the latch; WMS orders (M6); the dashboard (M7); and any wire change — the fault rides the existing `status` lane and the alarm rides the existing `alarm` lane, so `ContractVersion.Current` stays `3` and `docs/contract/` and `src/Contracts/` are untouched.

---

## Where this fits

M5's spec line is: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lessons 13, 14, and 15 made **one** fault real: a silent shuttle raises a `lostCommunication` alarm on `shuttle/{id}/alarm`. The other two codes the contract names, `blockedPath` and `palletNotDetected`, are words in `src/Contracts/AlarmCodes.cs` that nothing in the running system can ever say.

This lesson gives the system a **second alarm source**: a shuttle that detects a blocked path. The shuttle detects it in its pure state machine, reports it as a status, and the fleet manager's serve loop turns that status into a `blockedPath` alarm — through the same loop, the same topic, and the same latch machinery that lesson 15 built.

```text
   built (lessons 13 + 14 + 15)              this lesson (16)                          later
   ────────────────────────────              ────────────────                          ─────
   lostCommunication: a silent shuttle
        │
        │                                     Shuttle:BlockedLanes  (config)
        │                                            │
        │                                     Rack.MarkBlocked(lane)
        │                                            │
        ▼                                     Shuttle.StartMove(toLane)
   FleetServeLoop                                   │  IsRouteBlocked?  ── yes ──► RaiseFault("blocked path")
        │  _health.IsUnresponsive                  │                              State = fault, no move
        │  _evaluator.Evaluate(id, lost)           ▼
        │                                     ShuttleEndpoint: ack Accepted=false
        │                                     + status: State=fault, FaultReason="blocked path"
        │                                            │
        │                                            ▼
        │                                     FleetServeLoop.HandleStatusAsync
        │                                       FaultAlarmCodes.ForReason("blocked path") = "blockedPath"
        │                                       _faultCodes[id] = "blockedPath"
        │                                            │  (every AlarmEvaluationIntervalMs)
        │                                            ▼
        │                                     _evaluator.Evaluate(id, lost, faultCode)
        │                                       latch keyed (shuttleId, code)
        │                                            │
        └──────────────►  AlarmMessage on shuttle/{id}/alarm  ◄──┘
                          lostCommunication (critical)  |  blockedPath (warning)
                                  │
                                  ▼
                          M7 dashboard: {root}/+/alarm
                          later: reassignment reacts; recovery acknowledges
```

M5's done-when is *"every injected fault leads to the right alarm and the right recovery"*. This lesson finishes the "right alarm" half for **two** faults, and it builds the first **injection mechanism**: set `Shuttle:BlockedLanes`, command a move, watch the alarm. Reassignment and recovery come next.

**The honest weak spots**, written down rather than hidden.

- **Our obstacle is model data, not a sensor.** A real shuttle sees an obstacle with a sensor, anywhere along the route, at any moment. Our rack carries a static set of blocked lanes, and the shuttle checks them when it plans a move. The route rule is real (every lane between the start and the destination), the sensor is not.
- **A blocked lane does not stop other shuttles.** The manager knows about the fault only after the alarm, and it does nothing with it yet. Traffic control still routes other shuttles through that lane. Fixing that reopens the severity question ([record 23 §10](../decisions/23-blocked-path-fault-and-second-alarm.md)).
- **A fault reason we do not recognise raises no alarm.** The mapper knows `"blocked path"`. Anything else maps to nothing, and the alarm lane stays silent. The status still reaches any consumer, and the gap is recorded.
- **A faulted shuttle never recovers itself.** `Shuttle.RaiseFault` has no clear. The toy at the end of this lesson shows what a future recovery status *would* do to the latch; the real recovery path is a later slice.
- **The sim has no real network.** A status can arrive late, twice, or not at all in a real warehouse. Here timing is perfect, and the manager trusts the latest status it sees. `MessageId` and `Timestamp` are there for a real consumer to dedupe and order; we do not use them yet.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."*

- Which lanes are blocked comes from `Shuttle:BlockedLanes`.
- The severity of a blocked-path alarm comes from `FleetManager:BlockedPathSeverity`; the lost-communication severity keeps coming from `FleetManager:LostCommunicationSeverity`.
- The topic root still comes from `Messaging:TopicRoot` through `TopicScheme`; the cadence and the window are unchanged from lessons 13 and 15.
- The wire values (`"blocked path"`, `"blockedPath"`, `"fault"`) are **contract constants**, not tunables: `FaultReasons.BlockedPath` in `ShuttleCore` beside `ShuttleStateWire`, and `AlarmCodes.BlockedPath` in `Contracts`. Lesson 15 set this rule: constants live in code; tunables live in config. No file holds a bare `"blocked path"` literal.

Test values are test fixtures and stay in the tests.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-15**. The baseline ran against a clean copy of `src/` at `/tmp/opencode/l16base` (no `bin/`/`obj/` copied in). The toy ran at `/tmp/opencode/l16toy`. Both scratch directories are deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no database | `/tmp/opencode/l16base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **109** tests; `FleetCore.Tests`=18, `Contracts.Tests`=7, `ShuttleCore.Tests`=25, `Messaging.Tests`=9, `MqttMessaging.Tests`=2, `ShuttleSim.Tests`=6, `TrafficCore.Tests`=6, `FleetManager.Tests`=36 |
| `AlarmEvaluator` raises only `lostCommunication` | read `src/FleetManager/AlarmEvaluator.cs` | one severity in the constructor; `Evaluate(string shuttleId, bool lostCommunication)`; latch keyed `(string ShuttleId, string Code)` |
| `AlarmCodes` already defines all three codes | read `src/Contracts/AlarmCodes.cs` | `LostCommunication`, `BlockedPath`, `PalletNotDetected` |
| Nothing raises `blockedPath` today | `grep -rn 'BlockedPath\|PalletNotDetected' src --include='*.cs'` | matches only `src/Contracts/AlarmCodes.cs` and tests that assert the constants |
| The contract already names `blocked path` as a `FaultReason` | read `docs/contract/v3.md:190` | "What is wrong. Meaningful only when `State` is `fault`; otherwise `null` or absent, e.g. `blocked path`, `lost communication`, `pallet not detected`" |
| The contract says the `alarm` kind is sent by the fleet manager | read `docs/contract/v3.md:40` | "Sent by a fleet manager to the bus on topic `{root}/{shuttleId}/alarm`" |
| `Shuttle.RaiseFault` exists and sets `FaultReason` | read `src/ShuttleCore/Shuttle.cs:104-112` | sets `_faultReason`, `State = Fault`; throws if already faulted |
| `Rack` has no obstacle data | read `src/ShuttleCore/Rack.cs` | lanes, slots, pallets only; `grep -rn 'MarkBlocked\|IsRouteBlocked' src` prints nothing |
| The wire name of `fault` has one home | read `src/ShuttleCore/ShuttleState.cs` | `ShuttleStateWire.ToWireName`, `Fault => "fault"` |
| The endpoint has no fault branch | read `src/ShuttleSim/ShuttleEndpoint.cs:63-80` | reject → ack `Accepted=false`; accept → ack `Accepted=true` + `moving` + travel + `idle` |
| The guard's duplicate path acks `Accepted=true` | read `src/ShuttleSim/ShuttleEndpoint.cs:57-61` | the duplicate ack — why a faulted command must not be marked completed |
| The serve loop holds no fault state | read `src/FleetManager/FleetServeLoop.cs` | `_knownShuttles` only; `_evaluator.Evaluate(id, lost)` |
| `FleetManager` uses `ShuttleCore` only transitively | read `src/FleetManager/FleetManager.csproj` (no `ShuttleCore` reference) and `src/FleetManager/RunLoop.cs:2` (`using ShuttleCore;`); baseline build green | compiles via `TrafficCore`; the explicit reference is the honest fix |
| `FleetManager.Tests` already references `ShuttleCore` | read `src/FleetManager.Tests/FleetManager.Tests.csproj:26` | `ProjectReference` present |
| The evaluator call sites that must be updated | `grep -c 'new AlarmEvaluator' src/FleetManager.Tests/AlarmEvaluatorTests.cs`; `grep -o 'Evaluate(' src/FleetManager.Tests/AlarmEvaluatorTests.cs \| wc -l` | 7 constructions; 15 `Evaluate(` calls |
| The new config keys do not exist yet | grep over `src` for `BlockedLanes` and `BlockedPathSeverity` | no matches |
| The existing test name style to match | read `src/ShuttleCore.Tests/RackTests.cs`, `src/ShuttleCore.Tests/StateTransitionTests.cs`, `src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, `src/FleetManager.Tests/AlarmEvaluatorTests.cs` | snake-case sentence names, e.g. `StartMove_from_idle_enters_moving`, `A_cleared_condition_can_raise_again` |
| The toy shows obstacle → fault → one alarm per edge, plus an independent second alarm | `/tmp/opencode/l16toy`: `dotnet run` | `FAULT` at `00:00:01` (reason `blocked path`, position unchanged); `PUBLISH blockedPath` warning once at `00:00:01`; `PUBLISH lostCommunication` critical once at `00:00:06`; two `CLEAR` lines at `00:00:07`; exit 0 |

Not yet verified: `FaultReasons`, the `Rack` and `Shuttle` changes, the endpoint's fault branch, `FaultAlarmCodes`, the evaluator and serve-loop changes, the two config keys, the `ShuttleCore` reference in `FleetManager.csproj`, the four new `ShuttleCore.Tests`, the one new `ShuttleSim.Tests` test, and the ten new `FleetManager.Tests` tests — this assignment specifies them and the reviewer measures them. The optional three-terminal broker demo is not yet verified either: no broker was available while this lesson was written.

---

## Options survey

The spec's learning method asks for options compared before a decision. Record 23 already decided all six; the surveys below are the record's, compressed, so you can see *why* before you build.

### Survey 1: Which M5 slice is next? (→ [record 23 §2](../decisions/23-blocked-path-fault-and-second-alarm.md))

| | A: the blocked-path fault (chosen) | B: reassignment | C: operator-acknowledged recovery |
|---|---|---|---|
| Gives the next slice a real failure signal | **yes** | no — it would invent one | no |
| Needed by the M5 done-when proof | **yes** — an injection mechanism | partly | no |
| Proves the alarm path generalizes | **yes** — a second code | no | no |

**Decision 1: the blocked-path fault is next.** Reassignment needs a real fault to react to; recovery needs a second alarm to acknowledge; the done-when proof needs an injectable fault no matter the order. Rejected: reassignment first (throwaway fake failure), recovery first (generalizing one alarm later).

### Survey 2: How does the fault happen? (→ [record 23 §3](../decisions/23-blocked-path-fault-and-second-alarm.md))

| | A: obstacle in the rack, detected in `StartMove` (chosen) | B: a timer injects it after N ms | C: a new `injectFault` command kind |
|---|---|---|---|
| A real detection in pure decision code | **yes** | no — fires while parked | yes |
| Related to the thing it names (a path) | **yes** | no | yes |
| Wire change | **none** | none | a kind, a topic, a version bump |

**Decision 2: `Rack` carries blocked lanes and `Shuttle.StartMove` checks the route.** `MarkBlocked`/`IsBlocked`/`IsRouteBlocked`; a blocked route means `RaiseFault` and no move. Rejected: a timer (not a detection — undefendable in an interview), an `injectFault` command (a test tool on the production wire plus a version bump).

### Survey 3: Who raises the alarm? (→ [record 23 §4](../decisions/23-blocked-path-fault-and-second-alarm.md))

| | A: fault on status, manager publishes (chosen) | B: shuttle publishes its own alarm | C: manager detects it |
|---|---|---|---|
| Contract change | **none** — v3 already carries both halves | **yes** — v3 says the alarm is sent by the manager | none, but impossible |
| One severity authority and one latch | **yes** | no — split across two apps | yes |
| The manager has the facts | **yes** — it already subscribes to `+/status` | yes | **no** — it has no obstacle data |

**Decision 3: the shuttle reports `State="fault"` + `FaultReason="blocked path"`; the serve loop publishes the alarm.** Rejected: shuttle-published alarms (contract direction change, split severity, a second latch on the weakest side of the link); manager-side detection (invents manager physics).

### Survey 4: How does the evaluator take a second code? (→ [record 23 §5](../decisions/23-blocked-path-fault-and-second-alarm.md))

| | A: one evaluator, `Evaluate(id, lost, faultCode)` (chosen) | B: a second evaluator instance | C: a generic condition set |
|---|---|---|---|
| Reuses the `(shuttleId, code)` latch | **yes** — record 21 built it for this | a second latch object | bare-string keys |
| One call per shuttle, 0–2 alarms | **yes** | the loop merges two results | yes |
| Call sites stay readable | **yes** | two objects wired everywhere | stringly-typed |

**Decision 4: extend `AlarmEvaluator`.** A third argument (`faultCode`, `null` or `AlarmCodes.BlockedPath`), a second severity in the constructor, and per-code edges on the existing latch. Severities: `LostCommunicationSeverity` stays `critical`; `BlockedPathSeverity` is `warning` — a known condition with a known fix, for one shuttle, versus a lost shuttle whose state is unknown. Rejected: a second evaluator (two latches, more wiring); a generic set (erases the named conditions; severities still need a lookup).

---

## The lecture

Today we are going to learn about three things: **the blocked-path fault**, **reporting a fault on the status lane**, and **the second alarm**.

### 1) The blocked-path fault (an obstacle in the rack and a state machine that detects it)

A **blocked path** is a physical fact: something is in the way, so the shuttle cannot go where it was told. It is the first fault in this project that the *shuttle itself* detects, instead of the manager noticing silence.

Real-world picture: a forklift driver is told to drive to bay 12. Before moving, the driver looks down the aisle and sees a pallet lying on the floor. The driver does not drive through it. The driver radios the supervisor: "aisle blocked". The pallet is the obstacle, the look is the check, and the radio call is the report.

Why do we need this now? M5 lists three faults. Two of them — `blockedPath` and `palletNotDetected` — are words in the code with nothing behind them. A fleet system that can only report silence cannot tell an operator what is wrong. It can only say "I have not heard from shuttle 3". The operator has to go and look.

**Where does an obstacle come from in a sim with no physics?** It is **injected**: the rack model carries a set of blocked lanes, and configuration decides which lanes they are. The rack is the right home for it. The rack already holds the warehouse's data — lanes, slots, pallets. An obstacle is lane data.

```csharp
// src/ShuttleCore/FaultReasons.cs (new)
namespace ShuttleCore;

public static class FaultReasons
{
    public const string BlockedPath = "blocked path";
}
```

- `FaultReasons` is a small class of constants for the phrases a shuttle puts in a status's `FaultReason`.
- `"blocked path"` is not a tunable. It is the exact phrase `docs/contract/v3.md` line 190 already documents as an example, so it is a contract constant and lives in code.
- Why `ShuttleCore` and not `Contracts`? Because the shuttle's wire vocabulary already lives here: `ShuttleStateWire.ToWireName()` is in this project. Putting the phrases beside it follows that precedent and keeps `ShuttleCore` dependency-free ([record 23 §6](../decisions/23-blocked-path-fault-and-second-alarm.md)).

**The rack learns about obstacles.** Two members store and read the set, and one walks a route.

```csharp
// src/ShuttleCore/Rack.cs (change) — names pinned by the assignment
public void MarkBlocked(string laneId)      // unknown lane -> ArgumentException, like Place/Take
public bool IsBlocked(string laneId)
public bool IsRouteBlocked(string fromLane, string toLane)
```

- `MarkBlocked` records a blocked lane. It throws `ArgumentException` for a lane the rack does not have, exactly like `Place` and `Take` do for an unknown slot.
- `IsBlocked` answers for one lane.
- `IsRouteBlocked` answers for a whole trip: it returns `true` if **any** lane between the two lanes is blocked, counting both ends. The rack already numbers every lane by ordinal (`A01-L01`=0, `A01-L02`=1, `A02-L01`=2, …), so the route is the range of ordinals from the smaller to the larger. `Track.Route` does the same thing for the manager; the shuttle has no `Track`, and the rack already owns the lane index.

**Why both ends?** Watch the difference with a real example. The shuttle sits in `A01-L01`. The obstacle is in `A01-L02`. The manager commands a move to `A02-L01`.

```text
   A01-L01     A01-L02     A02-L01     A02-L02
   shuttle     obstacle    destination
   └────────────┬───────────┘
        the route: both ends included
```

- A **destination-only** check would say "destination `A02-L01` is not blocked" and the shuttle would drive straight through the obstacle. Wrong.
- The **range** check sees `A01-L02` in the route and faults. Right.

This is the rule the assignment pins: **every lane between the current position and the destination, both ends included.**

**The state machine detects it.** `StartMove` already checks the clock, checks the state, and checks the lane. One more check goes in, after those, before the shuttle commits to moving:

```csharp
// src/ShuttleCore/Shuttle.cs (change) — inside StartMove, after the existing
// EnsureState and IsLane checks, before _moveToLane is set:
if (_rack.IsRouteBlocked(_position, toLane))
{
    RaiseFault(FaultReasons.BlockedPath, now);
    return; // State == Fault, position unchanged; the caller inspects State
}
```

- `_rack.IsRouteBlocked(_position, toLane)` asks the one question the sensor would ask: "is anything in my way?"
- If yes, `RaiseFault(FaultReasons.BlockedPath, now)` puts the shuttle in the `Fault` state with the reason set. `RaiseFault` is M1 code that nothing in a running system could reach until now.
- Then `StartMove` **returns normally**. It does not throw. The position has not changed. The caller must look at `State` to see what happened.

**Reject versus fault — the idea this section adds.** The state machine has two kinds of "no" now:

| | reject (today) | fault (this lesson) |
|---|---|---|
| Example | move to a lane that does not exist | move across a blocked lane |
| What is wrong | the command | the world |
| What the shuttle does | throws `ShuttleRejectedException`, nothing changes | `RaiseFault`, state becomes `fault`, position unchanged |
| How the caller learns | catches the exception | checks `_shuttle.State == ShuttleState.Fault` after a normal return |

Why not throw for a fault too? Because in this codebase an exception means "nothing happened". The endpoint catches `ShuttleRejectedException` and knows the shuttle is untouched. A fault is something happening — the state changed — so it is a normal return with a changed state. One call, two possible outcomes, and the caller can see which.

**Where this is used later.** The reassignment slice will ask "which shuttles are in `Fault`?" and move their work. Recovery will decide how a `Fault` shuttle is allowed back. `palletNotDetected` will join `FaultReasons` when the wire grows a load or unload command.

### 2) A fault reported on the status lane (a level, not an event)

The shuttle is now faulted. Nobody in another process knows. The manager sees the world only through messages, so the fault has to be **reported**.

Real-world picture: the forklift driver has stopped and radioed the supervisor "aisle blocked, I'm stopped". The supervisor writes it on the board. The driver's radio call is a **status**: "here is what I am right now". It is not an alarm. The alarm is the supervisor's decision to escalate it. Two different things.

Why do we need this now? The contract already has everything: `shuttleStatus` carries `State`, and `State` can already be `"fault"` (M1 defined it). `FaultReason` already carries the sentence, and `v3.md` line 190 already lists `blocked path` as an example. No new message kind, no version bump, no new topic.

**A level, not an event.** This vocabulary comes from [record 21 §2](../decisions/21-alarms-with-severity.md), and it is the whole reason this slice needs no contract change:

- A **status** is a level. It says what is true right now. It repeats. The heartbeat sends one every second whether or not anything changed.
- An **alarm** is an event. It says "this condition just started". It is sent once per episode, by the manager.

The shuttle never sends an event. It keeps telling the truth. The manager's latch (concept 3) turns the repeated truth into one event.

**The endpoint reports the fault.** `HandleCommandAsync` has two outcomes today: rejected and accepted. It gains a third. After `StartMove` returns without throwing, the endpoint looks at the shuttle's state:

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs (change) — after StartMove returns
if (_shuttle.State == ShuttleState.Fault)
{
    await AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, now, cancellationToken);
    await PublishStatusAsync(now, cancellationToken); // the existing public overload
    return; // the guard is NOT marked: the move did not happen
}
```

- `_shuttle.State == ShuttleState.Fault` — the fault outcome from `StartMove`. The endpoint branches on state, not on an exception.
- `AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, …)` — the contract defines `Accepted` as "the shuttle accepted the command *and applied its requested effect*". The shuttle never reached the lane, so the effect did not happen. The reason is the shuttle's own `FaultReason` (`"blocked path"`), not a literal.
- `PublishStatusAsync(now, cancellationToken)` — the existing public overload reads the shuttle's real state and `FaultReason`. No literals. The manager learns inside a heartbeat, not after one.
- `return` — the travel and the `idle` status are skipped. The shuttle is not moving.
- **The idempotency guard is not marked.** The guard's duplicate path acks `Accepted = true` (lesson 05). If a retried command hit that path, the manager would be told the move succeeded. Instead, a retry reaches `StartMove` again, `EnsureState` refuses because the shuttle is faulted, and the retry gets an honest `Accepted = false` with that reason.
- **Why ack at all?** `ReliableCommandSender` retries until it hears an ack (record 10). Staying silent would turn a fault into a confusing "no acknowledgement" failure at the manager.

**The heartbeat keeps repeating it.** `HeartbeatPublisher` calls `PublishStatusAsync(now, …)` every interval, and that overload publishes `_shuttle.State.ToWireName()` and `_shuttle.FaultReason`. So every heartbeat after the fault says `State = "fault"`, `FaultReason = "blocked path"`. That is the level the manager will read in concept 3. If the shuttle's radio then dies, the level stops arriving, and the liveness window from lesson 13 raises `lostCommunication` instead.

**Where this is used later.** The dashboard (M7) shows the same status. The recovery slice will make a healthy status possible again. Traffic control may later treat a blocked lane as unavailable ([record 23 §10](../decisions/23-blocked-path-fault-and-second-alarm.md)).

### 3) The second alarm (per-code edges and per-code severity)

Now the manager side. A `blockedPath` alarm has to leave on `shuttle/{id}/alarm`, once per fault episode, with a severity from configuration. Lesson 15 built the lane, the loop, and the latch. This section feeds the latch a second code.

Real-world picture: a hospital triage board. Each patient has their own row on the board. One patient can be both "needs isolation" and "needs surgery" — two separate flags, each set and cleared on its own. The board does not have one "something is wrong" flag per patient. It has one flag per *condition*.

Why do we need this now? `AlarmEvaluator` today takes one boolean and one severity. The latch inside it is already keyed on a **pair** — `(shuttleId, code)` — because record 21 built it for exactly this day. The pair is the patient's row: one entry per shuttle *per condition*. A second code needs no new latch, only a second condition in the method.

**The mapper: two vocabularies.** The status carries a human phrase (`"blocked path"`). The alarm carries a machine-stable code (`"blockedPath"`). They are different on purpose, and a tiny pure class translates between them.

```csharp
// src/FleetManager/FaultAlarmCodes.cs (new) — pure mapper, no clock, no I/O
using Contracts;
using ShuttleCore;

namespace FleetManager;

public static class FaultAlarmCodes
{
    public static string? ForReason(string? faultReason) => faultReason switch
    {
        FaultReasons.BlockedPath => AlarmCodes.BlockedPath,
        _ => null,
    };
}
```

- The `switch` maps one known phrase to one known code.
- `_ => null` covers everything else: a blank reason, an unknown reason, any string we have never seen. No alarm is raised for a reason we cannot name. That is the honest gap from the top of this lesson.
- It is pure: no clock, no ids, no I/O. It can be unit tested alone, like `ShuttleHealth` and `AlarmEvaluator`.

**The evaluator takes a second condition.** The constructor gains a second severity and validates it the same way. The method gains a third argument.

```csharp
// src/FleetManager/AlarmEvaluator.cs (change)
public AlarmEvaluator(string lostCommunicationSeverity, string blockedPathSeverity)

public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication, string? faultCode)
// faultCode: null, or AlarmCodes.BlockedPath. Anything else -> ArgumentOutOfRangeException.
// Each condition latches and clears on (shuttleId, code) under the one existing lock.
// Returns 0..2 alarms: one per rising edge.
```

- `faultCode` is `null` when the shuttle is not faulted, or `AlarmCodes.BlockedPath` when it is. The evaluator is the authority on which codes it can raise, so an unknown code is a bug, and it throws.
- **Both conditions are checked under the one existing lock.** Each one uses the same `Add`/`Remove` edge rule: `Add` returns `true` only on the rising edge, and `Remove` on the falling edge re-arms it.
- **The two conditions are independent.** A shuttle can be faulted *and* silent at the same time — for example, it faults and then its radio dies. Then `Evaluate` returns two alarms in one call: `blockedPath` and `lostCommunication`. Two rows on the triage board, both set.
- The returned alarms carry the configured severity per code: `_lostCommunicationSeverity` for one, `_blockedPathSeverity` for the other.

**The loop feeds it.** `FleetServeLoop` must know each shuttle's latest fault code. The intake handler computes it from the status, and the sweep passes it to the evaluator.

```csharp
// src/FleetManager/FleetServeLoop.cs (change) — new field, existing lock
private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);

// HandleStatusAsync, inside the existing lock block (one lock acquisition):
//   var code = status.Status.State == ShuttleState.Fault.ToWireName()
//       ? FaultAlarmCodes.ForReason(status.Status.FaultReason) : null;
//   if (code is null) _faultCodes.Remove(shuttleId);
//   else _faultCodes[shuttleId] = code;

// EvaluateAndPublishAsync: snapshot the ids and copy _faultCodes under _knownLock,
// then, outside the lock:
//   foreach (var alarm in _evaluator.Evaluate(id, lost, faults.TryGetValue(id, out var c) ? c : null))
//       await PublishAlarmAsync(alarm, now, cancellationToken);
```

- `Dictionary<string, string> _faultCodes` holds each shuttle's **latest** fault code. An entry exists only while the shuttle is faulted; a healthy status removes it. Latest status wins, like `ShuttleHealth.RecordSeen`.
- `ShuttleState.Fault.ToWireName()` — the wire string `"fault"` already has one home in `ShuttleCore`. Do not retype the literal.
- `FaultAlarmCodes.ForReason(...)` — the mapper from above. An unknown reason yields `null`, and the shuttle is treated as not faulted for alarm purposes.
- The dictionary is guarded by the **existing** `_knownLock`. One lock guards the loop's intake state, both written only by `HandleStatusAsync` and read only by the sweep. A second lock would add ordering risk for no benefit ([record 19](../decisions/19-run-loop-wiring-and-concurrency.md) one-lock discipline).
- The sweep snapshots ids **and** the fault codes under the lock, then evaluates and publishes with the lock released — the same snapshot-then-work pattern lesson 15 established.
- `PublishAlarmAsync` is unchanged. The alarm already carries its own `ShuttleId`, `Severity`, and `Code`; the same envelope builder and the same `shuttle/{id}/alarm` topic serve both codes.

**Where the severity comes from.** `Program.cs` in the manager reads `FleetManager:LostCommunicationSeverity` (as today) and the new `FleetManager:BlockedPathSeverity`, and passes both into `new AlarmEvaluator(severity, blockedPathSeverity)`. No file holds a literal severity.

**Where this is used later.** `palletNotDetected` is one more mapper arm, one more constructor severity, one more switch arm, and one more config key. Recovery will clear the shuttle's fault, the status level will say `idle`, the sweep will pass `null`, and the latch will clear with no message at all — the edge rule absorbs the recovery. Reassignment reads the same fault signal to decide which shuttle's work must move.

### Recap in plain words

- **The blocked-path fault** lives in the rack (a set of blocked lanes, from configuration) and is detected by `Shuttle.StartMove`, which checks every lane between the shuttle and its destination. A blocked route means `RaiseFault("blocked path")` and no movement. It is a fault, not a rejection: the state changes, so the call returns normally and the caller reads `State`.
- **A fault is reported on the status lane.** The endpoint acks the command as not accepted, publishes a `fault` status with the reason, and the heartbeat keeps publishing it. A status is a level that repeats; an alarm is an event that happens once. No wire change is needed — v3 already carries both fields.
- **The second alarm** is the same latch with a second condition. The mapper turns the status phrase into an alarm code, the loop stores each shuttle's latest fault code, and `Evaluate(id, lost, faultCode)` raises up to two independent edges per sweep with a configured severity per code. The `blockedPath` alarm is `warning`; `lostCommunication` stays `critical`.

---

## Worked example (toy, not the assignment)

This toy shows the mechanism end to end without any MQTT: **an obstacle → a fault → a level on the status lane → one alarm per edge, and a second independent alarm when the link also dies**. It stands in for the rack check, the shuttle, the mapper, the evaluator, and the loop with plain variables, so you can watch the loop move. It is a **toy**. It does **not** use the real types, and it is **not** the assignment. Do not copy it into `src/`.

Everything below ran in `/tmp/opencode/l16toy` and was deleted afterwards. One console project. `L16Toy.csproj` (complete, from `dotnet new console`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`Program.cs`, part 1 of 4 (concatenate the four parts in order; they are one file):

```csharp
// Worked-example toy for lesson 16: an injected blocked lane, a shuttle that
// faults on its route, and a manager that turns the fault status into one
// blockedPath alarm per edge (plus a lostCommunication alarm when the link
// also dies). This is a TOY, clearly not the assignment. It stands in for
// Rack / Shuttle / FaultAlarmCodes / AlarmEvaluator / FleetServeLoop without
// using the real types, and it does not touch anything under src/.

using System.Text.Json;

var root = "shuttle";                       // Messaging:TopicRoot
var window = TimeSpan.FromSeconds(3);       // FleetManager:LivenessTimeoutMs
var cadence = TimeSpan.FromSeconds(1);      // FleetManager:AlarmEvaluationIntervalMs

// --- concept 1: the injected obstacle, as rack model data -----------------
// A 2x2 rack: A01-L01=0, A01-L02=1, A02-L01=2, A02-L02=3.
var laneOrdinals = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["A01-L01"] = 0, ["A01-L02"] = 1, ["A02-L01"] = 2, ["A02-L02"] = 3,
};
var blockedLanes = new HashSet<string>(StringComparer.Ordinal) { "A02-L02" }; // Shuttle:BlockedLanes

// The shuttle's route check: every lane between the current position and the
// destination, both ends included. This is the shuttle "looking before driving".
bool RouteBlocked(string from, string to)
{
    var start = Math.Min(laneOrdinals[from], laneOrdinals[to]);
    var end = Math.Max(laneOrdinals[from], laneOrdinals[to]);
    for (var o = start; o <= end; o++)
        if (blockedLanes.Contains(laneOrdinals.First(kv => kv.Value == o).Key))
            return true;
    return false;
}
```

`Program.cs`, part 2 of 4:

```csharp
var shuttleState = "idle";
var shuttlePosition = "A01-L01";
string? faultReason = null;

// --- concept 2: the status lane carries the fault as a level --------------
// The shuttle's latest published status, as the manager sees it.
var lastSeenAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
var faultCodes = new Dictionary<string, string>(StringComparer.Ordinal);

// --- concept 3: the evaluator's rising-edge latch, keyed (shuttleId, code) -
var latched = new HashSet<(string ShuttleId, string Code)>();
var severities = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["lostCommunication"] = "critical",  // FleetManager:LostCommunicationSeverity
    ["blockedPath"] = "warning",         // FleetManager:BlockedPathSeverity
};

void Publish(string shuttleId, string code, DateTime now)
{
    var alarm = new { ShuttleId = shuttleId, Severity = severities[code], Code = code, Detail = (string?)null };
    Console.WriteLine($"{now:HH:mm:ss}  PUBLISH  {root}/{shuttleId}/alarm  severity={severities[code]} code={code}");
    Console.WriteLine($"                    {JsonSerializer.Serialize(alarm)}");
}

void Intake(string state, string? reason, DateTime now)
{
    lastSeenAt["SH-01"] = now;
    // The manager maps the fault phrase to an alarm code; anything it does
    // not know maps to "no fault condition".
    var code = state == "fault"
        ? (reason == "blocked path" ? "blockedPath" : null)
        : null;
    if (code is null) faultCodes.Remove("SH-01");
    else faultCodes["SH-01"] = code;
}
```

`Program.cs`, part 3 of 4:

```csharp
void Sweep(DateTime now)
{
    var lost = !lastSeenAt.TryGetValue("SH-01", out var seen) || now - seen > window;
    faultCodes.TryGetValue("SH-01", out var faultCode);

    foreach (var (condition, code) in new[] { (lost, "lostCommunication"), (faultCode is not null, faultCode ?? "blockedPath") })
    {
        var key = ("SH-01", code);
        if (condition && latched.Add(key)) { Publish("SH-01", code, now); }
        else if (!condition) { if (latched.Remove(key)) Console.WriteLine($"{now:HH:mm:ss}  CLEAR    latch ({code}) re-armed"); }
    }
}

var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

Console.WriteLine("=== Blocked-path toy: obstacle -> fault status -> one alarm per edge ===");
Console.WriteLine($"window={window.TotalSeconds}s cadence={cadence.TotalSeconds}s blocked=[{string.Join(",", blockedLanes)}]");
Console.WriteLine();

for (var tick = 0; tick <= 8; tick++)
{
    // Heartbeats publish the shuttle's current status while it is alive.
    var talking = tick <= 2; // after tick 2 the shuttle's radio dies too
    if (talking) { Intake(shuttleState, faultReason, clock); Console.WriteLine($"{clock:HH:mm:ss}  STATUS   state={shuttleState} reason={faultReason ?? "null"}"); }

    // Tick 1: the manager commands a move whose route crosses the obstacle.
    if (tick == 1)
    {
        var to = "A02-L02";
        var route = string.Join(" ",
            laneOrdinals.Where(kv => kv.Value >= Math.Min(laneOrdinals[shuttlePosition], laneOrdinals[to])
                                   && kv.Value <= Math.Max(laneOrdinals[shuttlePosition], laneOrdinals[to]))
                        .OrderBy(kv => kv.Value).Select(kv => kv.Key));
        Console.WriteLine($"{clock:HH:mm:ss}  MOVE     SH-01 -> {to}  route=[{route}]");
        if (RouteBlocked(shuttlePosition, to))
        {
            shuttleState = "fault";            // the state machine faults instead of moving
            faultReason = "blocked path";
            Console.WriteLine($"{clock:HH:mm:ss}  FAULT    SH-01 stays at {shuttlePosition}, reason=\"{faultReason}\"");
            Intake(shuttleState, faultReason, clock); // the fault status leaves immediately
        }
    }
```

`Program.cs`, part 4 of 4:

```csharp
    // Tick 7: a future recovery would publish a healthy status again. The
    // recovery slice does not exist yet; the toy shows only what the latch
    // would do with that status.
    if (tick == 7)
    {
        shuttleState = "idle";
        faultReason = null;
        Intake(shuttleState, faultReason, clock);
        Console.WriteLine($"{clock:HH:mm:ss}  STATUS   state={shuttleState} reason=null  (a future recovery)");
    }

    Sweep(clock);
    clock += cadence;
}

// The toy's stand-ins for the real types.
// Rack route check        -> Rack.IsRouteBlocked (real: ShuttleCore/Rack.cs)
// Shuttle fault on route  -> Shuttle.StartMove  (real: ShuttleCore/Shuttle.cs)
// Intake mapping          -> FaultAlarmCodes.ForReason + FleetServeLoop.HandleStatusAsync
// Latch keyed (id, code)  -> AlarmEvaluator._raised (real: FleetManager/AlarmEvaluator.cs)
```

Run it:

```bash
cd /tmp/opencode/l16toy
dotnet run
```

Observed output (this is a real run from `/tmp/opencode/l16toy` on 2026-10-15):

```text
=== Blocked-path toy: obstacle -> fault status -> one alarm per edge ===
window=3s cadence=1s blocked=[A02-L02]

00:00:00  STATUS   state=idle reason=null
00:00:01  STATUS   state=idle reason=null
00:00:01  MOVE     SH-01 -> A02-L02  route=[A01-L01 A01-L02 A02-L01 A02-L02]
00:00:01  FAULT    SH-01 stays at A01-L01, reason="blocked path"
00:00:01  PUBLISH  shuttle/SH-01/alarm  severity=warning code=blockedPath
                    {"ShuttleId":"SH-01","Severity":"warning","Code":"blockedPath","Detail":null}
00:00:02  STATUS   state=fault reason=blocked path
00:00:06  PUBLISH  shuttle/SH-01/alarm  severity=critical code=lostCommunication
                    {"ShuttleId":"SH-01","Severity":"critical","Code":"lostCommunication","Detail":null}
00:00:07  STATUS   state=idle reason=null  (a future recovery)
00:00:07  CLEAR    latch (lostCommunication) re-armed
00:00:07  CLEAR    latch (blockedPath) re-armed
```

Read the output like an operator:

- **The obstacle is in the route, not at the destination.** The route line prints all four lanes from `A01-L01` to `A02-L02`. `A02-L02` is blocked, so `RouteBlocked` is `true`. If the obstacle had been only in `A01-L02`, the route check would still catch it — the check looks at every lane in the printed range.
- **A fault is not a move.** At `00:00:01` the shuttle is still at `A01-L01`. It faulted with reason `"blocked path"`.
- **One alarm per edge.** `PUBLISH ... code=blockedPath` appears exactly once, at `00:00:01`. The next status, at `00:00:02`, repeats the fault level, and nothing is published again. That is the latch: the status repeats, the alarm does not.
- **A second, independent alarm.** From `00:00:02` the shuttle's radio is dead, so the manager sees no more statuses. The window is 3 seconds, and the last status was at `00:00:02`; at `00:00:06` it is past the window, so `lostCommunication` raises at `critical`. Same shuttle, same topic, a different code and a different severity. This is the triage board with two flags set.
- **The clear sends nothing.** At `00:00:07` a future recovery's healthy status arrives. The toy prints two `CLEAR` lines: both latches re-arm. No message is sent, because a cleared condition is not an event in this slice — the recovery slice will decide what, if anything, is published.

The toy inlines the three pieces. The real code splits them the same way:

| Toy | Real code |
|---|---|
| `laneOrdinals` + `blockedLanes` + `RouteBlocked` | `Rack` lane ordinals + `MarkBlocked`/`IsBlocked`/`IsRouteBlocked` |
| the `MOVE` / `FAULT` block | `Shuttle.StartMove` → `RaiseFault(FaultReasons.BlockedPath, now)` |
| the immediate `Intake(shuttleState, faultReason, clock)` | `ShuttleEndpoint`'s fault branch: ack `Accepted=false` + `PublishStatusAsync` |
| the `code` calculation in `Intake` | `FaultAlarmCodes.ForReason` + `FleetServeLoop.HandleStatusAsync` |
| `faultCodes` | `FleetServeLoop._faultCodes` |
| `latched` keyed `(ShuttleId, Code)` + `severities` | `AlarmEvaluator._raised` + the two constructor severities |
| `Publish` | `FleetServeLoop.PublishAlarmAsync` → `_bus.PublishAsync(_topics.Alarm(id), …)` |
| the `CLEAR` lines | `Evaluate(id, lost: false, faultCode: null)` returning nothing |

Your assignment is this toy with the real names and the real plumbing: the rack obstacle set, the `StartMove` fault, the endpoint's fault branch, the mapper, the evaluator's second code, and the loop's fault intake.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. `Shuttle.StartMove` throws `ShuttleRejectedException` for an unknown lane but calls `RaiseFault` and returns normally for a blocked one. Why the two different behaviors? What is the caller's contract in each case?
2. Why does `IsRouteBlocked` check every lane between the shuttle and the destination, both ends included, instead of just the destination? What would a destination-only check let the shuttle do wrongly?
3. The shuttle is faulted and its heartbeats keep reporting `State="fault"`. Why does the manager publish only one `blockedPath` alarm and not one per heartbeat? Which part of the system decides that?
4. The `AlarmEvaluator` latch was already keyed `(shuttleId, code)` before this lesson. Why was that key shape chosen back in record 21, and what does this lesson get for free because of it?
5. A shuttle faults on a blocked path and then its radio dies. Trace the two conditions through `HandleStatusAsync` and `Evaluate` to the two published alarms. Why do they not interfere with each other?
6. `FaultAlarmCodes.ForReason` returns `null` for any phrase it does not know. What does the manager do with that `null`, and what is the operational cost of not recognising a fault reason?

---

## The assignment

Build M5's fourth slice: **the blocked-path fault and the second alarm source**. A shuttle whose route is blocked enters `Fault` and reports it on `shuttle/{id}/status`. The fleet manager's serve loop turns that level into one `blockedPath` alarm per episode, on the existing `shuttle/{id}/alarm` topic, at a configured severity. The contract does not change: `status` and `alarm` are already v3.

### Requirements

1. **The fault vocabulary: `src/ShuttleCore/FaultReasons.cs` (new).**
   - `public static class FaultReasons` with `public const string BlockedPath = "blocked path";`.
   - Namespace `ShuttleCore`. No other members.

2. **The obstacle data: `src/ShuttleCore/Rack.cs`.**
   - `public void MarkBlocked(string laneId)` — records the lane as blocked. An unknown lane throws `ArgumentException`, the same convention as `Place`/`Take`.
   - `public bool IsBlocked(string laneId)` — `true` when the lane is marked.
   - `public bool IsRouteBlocked(string fromLane, string toLane)` — `true` if **any** lane from `fromLane` to `toLane` inclusive (by the rack's lane order) is blocked. An unknown lane throws `ArgumentException`.
   - Store the blocked set in a field; guard nothing (the rack is single-threaded model data, like its pallets today).

3. **The detection: `src/ShuttleCore/Shuttle.cs`.**
   - In `StartMove`, after the existing `EnsureInOrder`, `EnsureState(Idle)`, and lane check, and before `_moveToLane` is set: if `_rack.IsRouteBlocked(_position, toLane)`, call `RaiseFault(FaultReasons.BlockedPath, now)` and `return`.
   - Nothing else changes. An unknown lane still throws `ShuttleRejectedException`. A blocked route leaves the position unchanged and the state `Fault`.
   - `using` nothing new: `FaultReasons` is in the same namespace.

4. **The command handler: `src/ShuttleSim/ShuttleEndpoint.cs`.**
   - After `_shuttle.StartMove(command.ToLane, now)` returns without throwing, branch: if `_shuttle.State == ShuttleState.Fault`, then (a) `AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, now, cancellationToken)`, then (b) `PublishStatusAsync(now, cancellationToken)`, then return.
   - Do **not** call `_guard.MarkCompleted(messageId)` on this path. Do not publish the `moving` or `idle` statuses.
   - The rejected (exception) path and the accepted path keep their current behavior exactly.

5. **The injection configuration: `src/ShuttleSim/appsettings.json` and `src/ShuttleSim/Program.cs`.**
   - Add `"BlockedLanes": []` to the `Shuttle` object, so the key exists and defaults to no injection.
   - In `RunServerAsync`, read `Shuttle:BlockedLanes` (a `string[]`, default empty) and call `rack.MarkBlocked(lane)` for each entry, **before** the endpoint is built. A bad lane id fails at startup.
   - No literal lane id in the code.

6. **The mapper: `src/FleetManager/FaultAlarmCodes.cs` (new).**
   - `public static class FaultAlarmCodes` in namespace `FleetManager`, with `public static string? ForReason(string? faultReason)`.
   - `FaultReasons.BlockedPath → AlarmCodes.BlockedPath`; everything else, including `null` and blank, → `null`.
   - Pure: no `DateTime.UtcNow`, no `Guid.NewGuid`, no I/O, no `Messaging`.

7. **The evaluator: `src/FleetManager/AlarmEvaluator.cs`.**
   - Constructor becomes `AlarmEvaluator(string lostCommunicationSeverity, string blockedPathSeverity)`. Validate **both** against `AlarmSeverities`; a bad value throws `ArgumentOutOfRangeException`, as today.
   - `public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication, string? faultCode)`. A `faultCode` that is neither `null` nor `AlarmCodes.BlockedPath` throws `ArgumentOutOfRangeException`.
   - Keep the one lock and the `(shuttleId, code)` latch. Each condition uses the same `Add`/`Remove` edge rule. Return 0, 1, or 2 alarms; the two conditions are independent.
   - The blocked-path alarm uses `_blockedPathSeverity`, `AlarmCodes.BlockedPath`, and `Detail = null`.

8. **The serve loop: `src/FleetManager/FleetServeLoop.cs`.**
   - Add `private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);`, guarded by the existing `_knownLock`.
   - In `HandleStatusAsync`, inside the existing lock block, compute `var code = status.Status.State == ShuttleState.Fault.ToWireName() ? FaultAlarmCodes.ForReason(status.Status.FaultReason) : null;` and then `if (code is null) _faultCodes.Remove(shuttleId); else _faultCodes[shuttleId] = code;`.
   - In `EvaluateAndPublishAsync`, snapshot the ids **and** a copy of `_faultCodes` under `_knownLock` (one lock acquisition), then evaluate and publish outside the lock. Pass each id's fault code (`null` when absent) as the third `Evaluate` argument.
   - The class takes no new constructor parameter. `PublishAlarmAsync` is unchanged.
   - The file must still contain no `DateTime.UtcNow`, no `Guid.NewGuid`, no `Thread.Sleep`, and no `MqttMessaging`/`Npgsql` reference. It now names `ShuttleCore` types (`ShuttleState`, `FaultReasons` via `FaultAlarmCodes`), so add `using ShuttleCore;`.

9. **The manager's configuration and wiring: `src/FleetManager/appsettings.json`, `src/FleetManager/Program.cs`, `src/FleetManager/FleetManager.csproj`.**
   - `appsettings.json`: add `"BlockedPathSeverity": "warning"` beside `LostCommunicationSeverity`.
   - `Program.cs` `RunServeAsync`: read `FleetManager:BlockedPathSeverity` (required, like `LostCommunicationSeverity`) and pass both severities into `new AlarmEvaluator(lostSeverity, blockedPathSeverity)`. The `move` command keeps its behavior exactly.
   - `FleetManager.csproj`: add `<ProjectReference Include="..\ShuttleCore\ShuttleCore.csproj" />`. `FleetManager` code already names `ShuttleCore` types (`RunLoop.cs`, and now `FleetServeLoop.cs`); the explicit reference replaces the accidental transitive one.

10. **Tests — `ShuttleCore.Tests` (four new, total 25 → 29).**
    - `src/ShuttleCore.Tests/StateTransitionTests.cs` (or a new file of your choice):
      - `StartMove_across_a_blocked_lane_enters_fault_with_the_blocked_path_reason` — the destination is blocked; assert `State == Fault`, `Position` unchanged, and `FaultReason == FaultReasons.BlockedPath`.
      - `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault` — the obstacle is in an intermediate lane, not the destination; assert the fault. This is the test that pins the route rule.
    - `src/ShuttleCore.Tests/RackTests.cs`:
      - `IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked` — mark one lane, check a route that does not include it.
      - `MarkBlocked_for_an_unknown_lane_is_rejected` — assert `ArgumentException`.

11. **Tests — `ShuttleSim.Tests` (one new, total 6 → 7).**
    - `src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, name `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status`.
    - Arrange a rack with the destination lane (or a lane on the route) marked blocked and a shuttle at `A01-L01`. Feed a real `moveCommand` payload. Assert: exactly one ack with `Accepted == false` and `Reason == FaultReasons.BlockedPath`; one status with `State == "fault"` and `FaultReason == FaultReasons.BlockedPath`; the shuttle's position is unchanged; no `moving`/`idle` statuses. The existing `FakeBus` in that file is reusable, but the helper that creates the shuttle must let the test reach the rack.

12. **Tests — `FleetManager.Tests` (ten new, total 36 → 46).**
    - New file `src/FleetManager.Tests/FaultAlarmCodesTests.cs` (2 tests):
      - `A_blocked_path_reason_maps_to_the_blocked_path_alarm_code`;
      - `An_unknown_or_blank_reason_maps_to_no_alarm_code` (cover `null`, `""`, and a phrase you do not know).
    - `src/FleetManager.Tests/AlarmEvaluatorTests.cs` (5 new tests) plus updating the 7 existing constructions (two severities) and the 15 existing `Evaluate` calls (add `, null`):
      - `A_blocked_path_fault_raises_one_alarm_with_the_blocked_path_severity`;
      - `A_blocked_path_fault_that_persists_does_not_raise_again`;
      - `A_cleared_blocked_path_fault_can_raise_again`;
      - `A_faulted_shuttle_that_goes_silent_raises_two_alarms` — one `Evaluate(id, true, AlarmCodes.BlockedPath)` call returns two alarms with distinct codes;
      - `An_unknown_fault_code_is_rejected`.
      - Also extend `An_unknown_severity_is_rejected` to assert **both** constructor severities are validated.
    - `src/FleetManager.Tests/FleetServeLoopTests.cs` (3 new tests), using the existing `RecordingBus` and a fixed clock:
      - `A_fault_status_publishes_a_blocked_path_alarm_with_the_configured_severity` — feed a fault status through the captured handler, sweep, assert one publish: topic `shuttle/SH-01/alarm`, family `MessageFamily.Alarm`, body `Code == AlarmCodes.BlockedPath`, `Severity ==` the configured blocked-path severity, envelope `MessageType == MessageTypes.Alarm`.
      - `A_healthy_status_clears_the_blocked_path_latch` — fault status → sweep publishes one; healthy status → sweep publishes nothing; fault status again → sweep publishes one again.
      - `A_faulted_shuttle_that_goes_silent_publishes_both_alarms` — fault status, then advance the clock past the liveness window, sweep once: assert two publishes with codes `blockedPath` and `lostCommunication` (order-insensitive).
      - The existing 8 loop tests keep passing; only the evaluator construction changes.
    - These tests are **pure**: no `Integration` trait, no database, no broker.

13. **Keep the contract and the transport unchanged.**
    - Do not touch `src/Contracts/` or `docs/contract/`. `ContractVersion.Current` stays `3`. Do not add a `v4.md`.
    - Do not touch `src/Messaging/` or `src/MqttMessaging/`. No new family, topic, or QoS. The fault rides `status`; the alarm rides `alarm`.

14. **Do not touch these:** `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager/RunLoop.cs`, `src/FleetManager/RunLoopGate.cs`, `src/FleetManager/OccupancyMap.cs`, `src/FleetManager/TaskDispatcher.cs`, `src/TrafficCore/`, `src/FleetCore/`, `src/FleetManager/ReliableCommandSender.cs`, `docs/contract/v2.md`, `docker/Dockerfile.build`. The `move` command in `Program.cs` keeps its behavior.

15. **M5 later items are not present.** No reassignment, no recovery, no acknowledgement, no restart reconciliation, no auto-release on timeout, no `palletNotDetected` raised anywhere, no manager-side rule that avoids blocked lanes, no roster, no WMS, no dashboard. The fault codes and the latch stay in memory.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the new pieces exist, with the pinned names
test -f src/ShuttleCore/FaultReasons.cs || echo "missing FaultReasons.cs"
test -f src/FleetManager/FaultAlarmCodes.cs || echo "missing FaultAlarmCodes.cs"
test -f src/FleetManager.Tests/FaultAlarmCodesTests.cs || echo "missing FaultAlarmCodesTests.cs"
grep -qE 'class FaultReasons' src/ShuttleCore/FaultReasons.cs
grep -qE 'BlockedPath = "blocked path"' src/ShuttleCore/FaultReasons.cs
for m in MarkBlocked IsBlocked IsRouteBlocked; do grep -qE "\b$m\b" src/ShuttleCore/Rack.cs || echo "missing Rack member: $m"; done
grep -qE 'IsRouteBlocked' src/ShuttleCore/Shuttle.cs
grep -qE 'RaiseFault\(FaultReasons\.BlockedPath' src/ShuttleCore/Shuttle.cs
grep -qE 'ShuttleState\.Fault' src/ShuttleSim/ShuttleEndpoint.cs
grep -qE 'class FaultAlarmCodes' src/FleetManager/FaultAlarmCodes.cs
grep -qE 'FaultAlarmCodes\.ForReason' src/FleetManager/FleetServeLoop.cs
grep -qE '_faultCodes' src/FleetManager/FleetServeLoop.cs
grep -qE '_blockedPathSeverity' src/FleetManager/AlarmEvaluator.cs
grep -qE 'string\? faultCode' src/FleetManager/AlarmEvaluator.cs
```

```bash
# Phase 2 — configuration carries the injection and the severity, no literal
grep -qE '"BlockedLanes"' src/ShuttleSim/appsettings.json
grep -qE '"BlockedPathSeverity": *"warning"' src/FleetManager/appsettings.json
grep -qE 'BlockedPathSeverity' src/FleetManager/Program.cs
grep -qE 'new AlarmEvaluator\(' src/FleetManager/Program.cs
grep -qE 'BlockedLanes' src/ShuttleSim/Program.cs
```

```bash
# Phase 3 — the pure files stay pure, and the loop keeps its discipline
grep -nE 'DateTime\.UtcNow|Guid\.NewGuid|Thread\.Sleep|MqttMessaging|Npgsql' src/FleetManager/FaultAlarmCodes.cs   # nothing
grep -nE 'DateTime\.UtcNow|MqttMessaging|Npgsql' src/FleetManager/FleetServeLoop.cs   # nothing
grep -qE 'private readonly object _knownLock' src/FleetManager/FleetServeLoop.cs
grep -qE 'lock *\(_knownLock\)' src/FleetManager/FleetServeLoop.cs || echo "FAIL: the intake state must be guarded"
grep -qE 'using ShuttleCore;' src/FleetManager/FleetServeLoop.cs
grep -qE 'ShuttleCore' src/FleetManager/FleetManager.csproj
```

```bash
# Phase 4 — no new transport, no contract bump
grep -qE 'Current = 3' src/Contracts/MessageTypes.cs
grep -qE '^Version: 3$' docs/contract/v3.md
test ! -f docs/contract/v4.md
git diff --quiet HEAD -- src/Contracts docs/contract src/Messaging src/MqttMessaging docker/Dockerfile.build \
  && echo "OK: contracts, contract pages and the transport are unchanged" \
  || echo "FAIL: a protected file changed"
```

```bash
# Phase 5 — protected M5/M4/M3 files unchanged
git diff --quiet HEAD -- src/FleetManager/ShuttleHealth.cs src/FleetManager/RunLoop.cs src/FleetManager/RunLoopGate.cs \
  src/FleetManager/OccupancyMap.cs src/FleetManager/TaskDispatcher.cs src/FleetManager/ReliableCommandSender.cs \
  src/TrafficCore src/FleetCore \
  && echo "OK: protected files unchanged" \
  || echo "FAIL: a protected file changed"
```

```bash
# Phase 6 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 124 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 124 tests"
grep -E 'Passed: +46,.*FleetManager\.Tests\.dll' "$T" && echo "FleetManager.Tests=46 OK" || echo "FAIL: FleetManager.Tests should show 46"
grep -E 'Passed: +29,.*ShuttleCore\.Tests\.dll' "$T" && echo "ShuttleCore.Tests=29 OK" || echo "FAIL: ShuttleCore.Tests should show 29"
grep -E 'Passed: +7,.*ShuttleSim\.Tests\.dll' "$T" && echo "ShuttleSim.Tests=7 OK" || echo "FAIL: ShuttleSim.Tests should show 7"
rm -f "$T"

# Phase 6b — the new tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in StartMove_across_a_blocked_lane_enters_fault_with_the_blocked_path_reason StartMove_through_a_lane_blocked_between_two_lanes_enters_fault IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked MarkBlocked_for_an_unknown_lane_is_rejected A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status A_blocked_path_reason_maps_to_the_blocked_path_alarm_code An_unknown_or_blank_reason_maps_to_no_alarm_code A_blocked_path_fault_raises_one_alarm_with_the_blocked_path_severity A_blocked_path_fault_that_persists_does_not_raise_again A_cleared_blocked_path_fault_can_raise_again A_faulted_shuttle_that_goes_silent_raises_two_alarms An_unknown_fault_code_is_rejected A_fault_status_publishes_a_blocked_path_alarm_with_the_configured_severity A_healthy_status_clears_the_blocked_path_latch A_faulted_shuttle_that_goes_silent_publishes_both_alarms; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                             # nothing
rm -f "$L"
cd ..
```

```bash
# Phase 7 — no test launches docker
grep -rniE 'docker' src/*.Tests --include='*.cs' && echo "FAIL: docker referenced in a test" || echo "OK: no docker in tests"
```

```bash
# Phase 8 — no later-M5 scope leaks
grep -rniE 'palletNotDetected|reassign|acknowledg|recover' src --include='*.cs' \
  | grep -vE 'src/Contracts/AlarmCodes\.cs|src/[^/]+\.Tests/' || true
```

```bash
# Optional — the three-terminal broker demo, needs the broker up.
# docker compose -f docker/compose.yml up -d
# Terminal 1 (shuttle, injection on): edit src/ShuttleSim/appsettings.json "BlockedLanes": ["A02-L02"], then
#   cd src && dotnet run --project ShuttleSim/ShuttleSim.csproj serve
# Terminal 2 (manager): cd src && dotnet run --project FleetManager/FleetManager.csproj serve
# Terminal 3 (watch the alarm lane): docker exec <broker-container> mosquitto_sub -t 'shuttle/+/alarm' -v
# Terminal 4 (the bad command): cd src && dotnet run --project FleetManager/FleetManager.csproj move SH-01 A02-L02
#   expect exit 1 and "blocked path" on stderr, and one blockedPath alarm in terminal 3
# docker compose -f docker/compose.yml down
```

**Proof details.** The assignment adds three product files (`src/ShuttleCore/FaultReasons.cs`, `src/FleetManager/FaultAlarmCodes.cs`, `src/FleetManager.Tests/FaultAlarmCodesTests.cs`); edits eight product files (`src/ShuttleCore/Rack.cs`, `src/ShuttleCore/Shuttle.cs`, `src/ShuttleSim/ShuttleEndpoint.cs`, `src/ShuttleSim/Program.cs`, `src/FleetManager/AlarmEvaluator.cs`, `src/FleetManager/FleetServeLoop.cs`, `src/FleetManager/Program.cs`, `src/FleetManager/FleetManager.csproj`); edits two `appsettings.json` files; edits three test files (`ShuttleEndpointTests.cs`, `AlarmEvaluatorTests.cs`, `FleetServeLoopTests.cs`) and two more test files gain tests (`StateTransitionTests.cs` or a new file, `RackTests.cs`). The pure test total goes from **109 to 124**: `ShuttleCore.Tests` 25 → **29** (+4), `ShuttleSim.Tests` 6 → **7** (+1), `FleetManager.Tests` 36 → **46** (+10). The gate is `RC 0`, 8 `Passed!`, 0 `Failed!`, 124 total.

What the reviewer will mutate to prove the tests are load-bearing:

- delete the `IsRouteBlocked` check in `StartMove` → `StartMove_across_a_blocked_lane_...` and `StartMove_through_a_lane_blocked_between_two_lanes_...` go red;
- check only the destination instead of the route → `StartMove_through_a_lane_blocked_between_two_lanes_...` goes red (the destination is clear but an intermediate lane is blocked);
- raise the fault with a different reason string → the endpoint test's `Reason`/`FaultReason` assertions and the mapper/loop alarm tests go red;
- make `FaultAlarmCodes.ForReason` return `null` for `"blocked path"` → the mapper test and both loop fault tests go red;
- pass the fault code as always-`null` in the sweep → `A_fault_status_publishes_...` and `A_faulted_shuttle_that_goes_silent_publishes_both_alarms` go red;
- key the evaluator latch on `shuttleId` alone → the two-alarm test goes red (one condition erases the other's latch);
- return the wrong severity for the blocked-path alarm → `A_blocked_path_fault_raises_one_alarm_with_the_blocked_path_severity` goes red;
- skip `PublishStatusAsync` in the endpoint's fault branch → `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status` goes red (no fault status);
- ack `Accepted = true` in the fault branch → the same test goes red;
- mark the guard in the fault branch → a duplicate-command variant of the endpoint test would go red (if you write one); otherwise this is a code-review item;
- accept any string as `faultCode` in `Evaluate` → `An_unknown_fault_code_is_rejected` goes red.

### Stretch goal

Give the manager a way to see that an obstacle is real, not just one shuttle's story. Add a pure `Rack`-level or manager-level check that answers "is any lane on the route from A to B blocked?" using the **manager's** warehouse model, and one test that pins the same route rule the shuttle uses. Do **not** wire it into traffic control (no routing change, no release) — that is a later slice with its own record. The point is to show that the manager's model and the shuttle's model can agree about an obstacle without sharing memory, and to name the seam where traffic control will someday honor blocked lanes.

### Not in this assignment

- **`palletNotDetected`** — there is no load or unload command on the wire yet; it is a later slice.
- **Reassignment, recovery, operator acknowledgement** — later M5 slices. A faulted shuttle stays faulted.
- **Traffic control honoring blocked lanes** ([record 23 §10](../decisions/23-blocked-path-fault-and-second-alarm.md)) — the alarm is knowledge, not action (record 17 §4).
- **Persisting the fault codes or the latch** — in-memory only; a manager restart forgets both.
- **WMS orders (M6)** and **the dashboard (M7)**.
- **A contract version bump.** No new kind, topic, or family. `docs/contract/` and `src/Contracts/` are untouched.

---

## Closing

Read the spec's M5 line once more: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lessons 13 to 15 gave the system one fault: silence, reported by the manager. This lesson gives it a second: a fault the shuttle detects itself. The rack carries an injected obstacle; `StartMove` checks the whole route; a blocked route puts the shuttle in `Fault` with a reason. The shuttle reports the reason as a **level** on the existing status lane, the manager's mapper turns the phrase into an alarm **code**, and the evaluator's `(shuttleId, code)` latch turns the repeated level into one **edge** — one `blockedPath` alarm at a configured severity, independent of the lost-communication latch sitting beside it. No wire change: v3 already carried both halves.

Before you call this done, be ready to explain without notes:

- why a blocked path is a fault (state changes, normal return) and not a rejection (exception, nothing changes);
- why the route check covers every lane between the shuttle and its destination;
- why the fault travels as a status level and the alarm as an edge, and why that needs no version bump;
- why the mapper exists at all, and what happens to a fault reason it does not know;
- why the evaluator's latch key was already `(shuttleId, code)`, and what a shuttle that is both faulted and silent publishes.

When the reviewer passes this, M5 has two alarm sources end to end and its first injectable fault. Reassignment — moving a faulted shuttle's work — is next.
