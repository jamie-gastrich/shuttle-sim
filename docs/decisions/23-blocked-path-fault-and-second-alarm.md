# Decision Record 23: The second alarm source — a shuttle-detected blocked path, reported on the status lane, published as a per-code alarm

**Date:** 2026-10-15
**Milestone:** M5 (Faults and recovery) — lesson 16: the blocked-path fault and the second alarm source.
**Status:** Accepted. Append-only. Supersedes nothing. Links back to records 07, 17, 19, 20, 21, 22.
**Builds on:** [record 21](21-alarms-with-severity.md) (§3 named `blockedPath` in the closed code set and §8 asked exactly this question: "the shuttle starts reporting its own faults … the question becomes whether the shuttle or the manager stamps the severity"; §4's latch is keyed `(shuttleId, code)` in anticipation of a second code), [record 22](22-serve-loop-and-alarm-publish.md) (the serve loop, its known-shuttle set, and the honest limits §3/§10), [record 20](20-liveness-heartbeats-and-timeouts.md) (`ShuttleHealth`, the liveness window), [record 17](17-occupancy-and-fault-reservations.md) (faulted shuttles hold their reservations; auto-release is forbidden — this record does not release anything), [record 19](19-run-loop-wiring-and-concurrency.md) (one lock per shared object; decision logic pure), [record 07](07-message-contract-v2-envelope-and-topics.md) (envelope and topics).
**Replaced by:** a later record that changes who publishes shuttle-detected alarms, where the blocked-path check runs, the evaluator's per-code shape, or the severities. Append-only.
**Wire contract:** **no change, no version bump.** The fault rides the existing `shuttleStatus` (v3): `State` can be `fault` and `FaultReason`'s own documented examples in `docs/contract/v3.md` line 190 include `blocked path`. The alarm rides the existing `alarm` kind (v3), which `docs/contract/v3.md` line 40 documents as "Sent by a fleet manager". No new kind, no new topic, no new QoS. `ContractVersion.Current` stays `3`. `docs/contract/` and `src/Contracts/` are untouched.

---

## 1. What we are deciding

Review 15 passed with `lostCommunication` flowing end to end. The README's M5 notes list what is left: `blockedPath` and `palletNotDetected` are defined in `AlarmCodes` but nothing raises them; a failed shuttle's work is never reassigned; recovery has no operator acknowledgement; and the done-when proof ("every injected fault leads to the right alarm and the right recovery, and the saved state never loses or duplicates a pallet") is unwritten.

This record decides six things:

**A.** Which M5 slice is next.
**B.** How a blocked path *happens* in the sim — where the fault comes from and who detects it.
**C.** Who turns that fault into an alarm on the wire.
**D.** How the pure evaluator takes a second alarm code.
**E.** Where the fault vocabulary lives, and which severities and configuration keys this slice adds.
**F.** What the shuttle's command handler does when a command makes it fault.

---

## 2. Decision A: build the shuttle-detected `blockedPath` fault next; reassignment and recovery come after

| | A: blocked-path fault first (chosen) | B: reassignment first | C: operator-acknowledged recovery first |
|---|---|---|---|
| Gives the next slices something to react to | **yes** — reassignment needs a "this shuttle failed" signal | no — it would invent its own failure signal | no — recovery needs an alarm to acknowledge |
| Needed by the done-when proof either way | **yes** — "every injected fault" needs an injection mechanism | partly | no |
| Proves the alarm path generalizes | **yes** — a second code, a second severity, the same lane | no | no |
| Depends on a slice that does not exist | no | the fault signal (this slice) | the alarm stream's second code (this slice) |

**Decision A: the second alarm source — a shuttle-detected blocked path — is the next slice.** The dependency order is one-way: reassignment asks "which shuttle can no longer take work?", and the answer must come from a real fault, not a test double. Recovery asks "which alarm is the operator acknowledging?", and today the only alarm is lost communication. The M5 done-when proof needs a fault that can be injected on demand; this slice builds the injection mechanism, so it is required no matter the order. M5's own line lists the faults before the recoveries: *"Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Rejected: **reassignment first** (it would have to fake a failed shuttle — throwaway work, and the fake would have to be replaced when real faults land); **recovery first** (there is only one alarm kind in the system; building an acknowledgement path for a single code and then generalizing it is the same rework in the wrong direction).

---

## 3. Decision B: the obstacle is rack model data, and the shuttle's `StartMove` detects it

"Blocked path" is a physical fact: there is something in the way. The sim has no physics and no sensors, so the fault must be *injected*. The question is where the injection lives.

| | A: an obstacle in the rack model, detected in `StartMove` (chosen) | B: a timer that injects the fault after N ms | C: a new `injectFault` wire command (contract bump) |
|---|---|---|---|
| It is a real detection in pure decision code | **yes** — the state machine refuses to drive into an obstacle | no — the clock fires regardless of anything physical | yes, but only after a new kind exists |
| Fires in relation to the thing it names (a path) | **yes** — the shuttle is commanded to move, then faults | no — it can fire while the shuttle is parked | yes |
| New wire traffic to invent | **none** | none | a whole message kind, a topic, a version bump |
| Later reuse | obstacle data is warehouse model data (M4 traffic could honor it later) | a throwaway timer | a test-only command on the production wire |

**Decision B: the rack model carries a set of blocked lanes, and `Shuttle.StartMove` checks the whole route before moving.** `Rack` gains `MarkBlocked(laneId)`, `IsBlocked(laneId)`, and `IsRouteBlocked(fromLane, toLane)`. `Shuttle.StartMove`, after its existing state and lane checks, calls `RaiseFault(FaultReasons.BlockedPath, now)` and returns without moving if any lane on the route is blocked. The blocked lanes come from configuration (`Shuttle:BlockedLanes`), so the "injection" is a config edit, not a code edit.

Rejected: **a timer-based injection** (a fault that fires because time passed is not a detection; nothing connects it to a path, and the owner could not defend "why did it fault *then*?" in an interview); **an `injectFault` command kind** (a contract bump and a new wire path for a feature whose whole job is testing — a test tool does not belong on the production wire when config can do it).

**The route rule: every lane between the current position and the destination, both ends included.** Not just the destination: a real shuttle senses an obstacle anywhere along the way, and a destination-only check would let the shuttle "drive through" a blocked intermediate lane. The rack already numbers lanes by ordinal (`A01-L01`=0, `A01-L02`=1, …); the route is the ordinal range between the two lanes, the same idea as `Track.Route`. Rejected: **destination-only** (semantically wrong the moment there are more than two lanes); **passing a `Track` into `Shuttle`** (a constructor change at every call site for one check, when the obstacle is rack data and the rack already owns the lane index). Honest limit: a real shuttle would re-plan around the obstacle; ours faults and waits — recovery and re-planning are later slices.

**Reject vs fault — the distinction this slice adds.** Today every refusal in the state machine throws `ShuttleRejectedException` and nothing changes: a command for an unknown lane is a *mistake in the command*. A blocked path is different: nothing is wrong with the command; the world is wrong. The shuttle's reaction is different too — it goes into the `Fault` state (which M1 defined and which nothing could reach from a move until now).

- `StartMove` to an unknown lane → still throws `ShuttleRejectedException`. No state change.
- `StartMove` across a blocked route → calls `RaiseFault`, returns normally with `State == Fault`, position unchanged, `FaultReason` set.

**The fault outcome is a normal return, not an exception.** In this codebase an exception means "nothing happened", and a caller that catches `ShuttleRejectedException` knows the shuttle is untouched. A fault *is* something happening: the state changed. The caller inspects `_shuttle.State == ShuttleState.Fault` after a successful (non-throwing) call. Rejected: **a distinct fault exception** (two signals for one event — a state change *and* a throw — and callers must catch to learn the outcome already happened); **reject-only, no state change** (then `State` is never `fault`, no fault status can ever be published, and there is no fault to alarm about — the whole slice collapses).

---

## 4. Decision C: the shuttle reports the fault on the status lane; the fleet manager publishes the alarm

Record 21 §8 left this open: "whether the shuttle or the manager stamps the severity, and whether a shuttle alarm and a manager alarm share one kind."

| | A: fault on the status lane, alarm published by the manager (chosen) | B: the shuttle publishes its own `AlarmMessage` | C: the manager detects the blockage itself |
|---|---|---|---|
| Wire contract change | **none** — v3 status already carries `State` + `FaultReason`; v3 says the alarm is "Sent by a fleet manager" | **yes** — `v3.md` line 40 becomes false; a doc edit is a contract change and needs a bump | none, but it cannot work |
| One severity authority | **yes** — manager config (record 21 §3) | no — severity would be stamped on the shuttle, config split across two apps | yes |
| One latch, one publisher, one stream for the dashboard | **yes** | no — a second latch on the shuttle, the same topic fed by two senders | yes |
| The manager has the facts it needs | **yes** — it already subscribes to `{root}/+/status` | yes | **no** — occupancy says who holds a lane, not what is broken; an obstacle is not in the manager's model at all |

**Decision C: the fault travels as a `shuttleStatus` with `State = "fault"` and `FaultReason = "blocked path"`; the serve loop reads it and publishes the `AlarmMessage`, exactly as it does for lost communication.** The status is a **level** — "here is what I am right now", repeated by every heartbeat. The alarm is an **edge** — "this just started", sent once per episode (record 21 §2/§4). That level/edge split is now load-bearing in both directions: the shuttle never sends an "event"; the manager's latch turns levels into events.

Rejected: **the shuttle publishes its own alarm** (breaks the documented direction of the `alarm` kind, splits the severity authority, puts a second latch on the weakest side of the link, and needs a version bump for no operational gain — the manager is alive, subscribed, and already holds the latch machinery); **the manager detects the blockage from traffic data** (it has no obstacle data — this would invent manager-side physics to avoid a two-line config key).

**The honest limits, written down.**

- If the faulted shuttle's radio then dies, the fault status stops arriving — but the shuttle is silent, so `lostCommunication` raises anyway (records 20/22). The two conditions are independent and both cover part of the failure.
- The manager does **not** stop routing other shuttles into the blocked lane. The alarm is knowledge, not action (record 17 §4). Once traffic control starts honoring blocked lanes, the severity question in Decision D is revisited.
- A fault reason this codebase does not map raises **no** alarm (see Decision D). The status still reaches any consumer; only the alarm lane stays silent.

---

## 5. Decision D: one evaluator, one latch keyed `(shuttleId, code)`, per-code severities, and a small pure mapper

`AlarmEvaluator` today takes one severity and one boolean. It must now handle a second condition.

| | A: one evaluator, `Evaluate(shuttleId, lostCommunication, faultCode)` (chosen) | B: a second evaluator instance for faults | C: `Evaluate(shuttleId, IReadOnlySet<string> activeConditions)` |
|---|---|---|---|
| Latch reuse | **yes** — already keyed `(shuttleId, code)`; record 21 built it for this | a second latch object | the latch keys become bare strings |
| One call per shuttle per sweep, 0–2 alarms | **yes** | the loop must merge two result sets | yes |
| Independent edges (faulted *and* silent = two alarms) | **yes** | yes | yes, but |
| Call sites stay readable | **yes** — the two conditions are named at the call | two objects wired in the loop and in `Program.cs` | a stringly-typed set; severities still need a per-code lookup inside |

**Decision D: `AlarmEvaluator` gains a second constructor parameter and a third argument.** `Evaluate(shuttleId, lostCommunication, faultCode)` checks both conditions under the one existing lock and returns zero, one, or two alarms. Each condition latches and clears independently on the same `(shuttleId, code)` set, so a faulted shuttle that also goes silent raises `blockedPath` *and* `lostCommunication`. `faultCode` must be `null` or `AlarmCodes.BlockedPath`; anything else throws `ArgumentOutOfRangeException` — the evaluator remains the authority on which codes it can raise (record 21's closed set). `palletNotDetected` later is one more mapper arm, one more severity parameter, one more switch arm, one more config key.

Rejected: **a second evaluator instance** (two latches to reason about, two objects in the loop and in `Program.cs`, and the sweep must interleave the results); **the generic condition set** (it erases the two named conditions for no gain and turns the severity lookup into a stringly dictionary).

**Two vocabularies, one pure mapper.** The status carries a human phrase in `FaultReason` (`"blocked path"` — the exact example `docs/contract/v3.md` line 190 documents). The alarm carries a machine-stable code in `Code` (`AlarmCodes.BlockedPath` = `"blockedPath"`). A tiny pure class maps one to the other: `FaultAlarmCodes.ForReason(string?) → string?`. Blank or unknown phrases map to `null` — no fault condition, no alarm. The shuttle and the manager both use the `FaultReasons.BlockedPath` constant, so the strings match by construction rather than by hand.

**Severities: `LostCommunicationSeverity` stays `critical`; the new `FleetManager:BlockedPathSeverity` is `warning`.** Record 21's taxonomy: `info` → log only, `warning` → dashboard highlight, `critical` → blocks work and needs attention. A blocked path is a *known* condition with a *known* fix (clear the lane); it stops one shuttle, and the operator knows exactly what to do. Lost communication is the opposite: state unknown, position uncertain, the worst thing a fleet can have. Two different problems deserve two different levels, and the M7 dashboard gets both to render. Rejected: `critical` (a fleet where everything is critical trains operators to ignore the board); `info` (a shuttle that will not move is not a log-only event). Would revisit: if a blocked lane can stall several shuttles at once — for example when traffic control starts honoring blocked lanes, or when reassignment finds no free shuttle.

---

## 6. Decision E: the fault vocabulary lives in `ShuttleCore`, the mapper in `FleetManager`, and both sides stay config-driven

`FaultReasons.BlockedPath = "blocked path"` is a wire value both apps need. Where it lives is a real structural choice.

- **`src/ShuttleCore/FaultReasons.cs` (new), next to `ShuttleStateWire`.** `ShuttleState.Fault`'s wire name already lives in `ShuttleCore` (`ToWireName`), because the shuttle's wire vocabulary travels with the shuttle's state machine. Putting `FaultReasons` beside it follows that precedent, keeps `ShuttleCore` dependency-free (the README's dependency diagram holds), and keeps `src/Contracts/` byte-untouched. Rejected: putting it in `Contracts` (it would give `ShuttleCore` its first dependency on another project for one constant — the dependency-graph change outweighs the tidiness); literals in both projects (two strings that must match by hand, and lesson 15's rule already says wire names are constants, not literals).
- **`src/FleetManager/FaultAlarmCodes.cs` (new), next to `AlarmEvaluator`.** Pure static, no clock, no I/O — the same purity bar as `ShuttleHealth` and `AlarmEvaluator`.
- **Configuration.** `Shuttle:BlockedLanes` is a JSON array of lane ids in `ShuttleSim/appsettings.json`, default `[]` (no injection, current behavior). `FleetManager:BlockedPathSeverity` is a string in `FleetManager/appsettings.json`, default `"warning"`. `Rack.MarkBlocked` throws `ArgumentException` on an unknown lane, so a typo fails at startup, not at 3 a.m.
- **`src/FleetManager/FleetManager.csproj` gains an explicit `<ProjectReference>` to `ShuttleCore`.** Today `FleetManager` compiles `ShuttleCore` types only through the transitive reference via `TrafficCore` (measured: `RunLoop.cs` already does `using ShuttleCore;` with no direct reference, and the baseline build is green). The serve loop now names `ShuttleCore` types too; the explicit reference makes the dependency honest instead of accidental.
- **No transport edits at all.** Both lanes the fault needs — `status` inbound, `alarm` outbound — exist since records 07 and 22. `MessageFamily`, `TopicScheme`, `QosOptions`, and both `Qos` config blocks are untouched. This is the first M5 slice that adds no wire plumbing.

---

## 7. Decision F: the command handler acks the faulted command as not accepted, then publishes the fault status

`ShuttleEndpoint.HandleCommandAsync` currently has two outcomes: rejected (ack `Accepted=false`, nothing published) and accepted (ack `Accepted=true`, status `moving`, then the simulated travel and status `idle`). A faulting move is a third outcome.

**The fault branch, in order:** after a non-throwing `StartMove`, if `_shuttle.State == ShuttleState.Fault` — (1) acknowledge the command with `Accepted = false` and `Reason = _shuttle.FaultReason`; (2) publish the current status through the existing public `PublishStatusAsync(now, cancellationToken)` (it already reads the shuttle's real state and `FaultReason`, so there are no literals); (3) return, skipping the travel and the `idle` status.

**Why `Accepted = false`:** the contract defines it as "the shuttle accepted the command *and applied its requested effect*". The shuttle never arrived at the lane, so the effect did not happen. **Why the ack is still sent:** `ReliableCommandSender` retries until an ack arrives or the attempt budget runs out (record 10); staying silent would turn a fault into a confusing "no acknowledgement" alarm. **Why the idempotency guard is NOT marked:** the guard's duplicate path acks `Accepted = true` (record 09), and a duplicate of a faulted command must not be told the move succeeded. A retried command id hits `StartMove` again, `EnsureState` refuses (the shuttle is faulted), and the retry gets an honest `Accepted = false` with that reason. **Why the fault status is published immediately:** the manager must learn now, not on the next heartbeat — though the heartbeat repeats the fault as a level either way.

Rejected: ack `Accepted = true` (a lie — the pallet did not move); no ack at all (retry storm, then a wrong alarm); marking the guard (a lying duplicate ack); publishing only on heartbeats (up to a full heartbeat interval of delay for no saved effort).

---

## 8. The exact shapes

```csharp
// src/ShuttleCore/FaultReasons.cs (new)
namespace ShuttleCore;

public static class FaultReasons
{
    public const string BlockedPath = "blocked path"; // the phrase docs/contract/v3.md already documents
}
```

```csharp
// src/ShuttleCore/Rack.cs (change) — obstacle data plus the route check
public void MarkBlocked(string laneId)   // unknown lane -> ArgumentException, like Place/Take
public bool IsBlocked(string laneId)
public bool IsRouteBlocked(string fromLane, toLane) // any lane between, both ends; unknown lane -> ArgumentException
```

```csharp
// src/ShuttleCore/Shuttle.cs (change) — inside StartMove, after the existing
// EnsureState and IsLane checks, before _moveToLane is set:
if (_rack.IsRouteBlocked(_position, toLane))
{
    RaiseFault(FaultReasons.BlockedPath, now);
    return; // State == Fault, position unchanged; the caller inspects State
}
```

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs (change) — after StartMove returns without throwing
if (_shuttle.State == ShuttleState.Fault)
{
    await AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, now, cancellationToken);
    await PublishStatusAsync(now, cancellationToken); // the existing public overload
    return; // guard NOT marked; the move did not happen
}
```

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

```csharp
// src/FleetManager/AlarmEvaluator.cs (change)
public AlarmEvaluator(string lostCommunicationSeverity, string blockedPathSeverity)
// both validated against AlarmSeverities, ArgumentOutOfRangeException, as today

public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication, string? faultCode)
// faultCode: null or AlarmCodes.BlockedPath, else ArgumentOutOfRangeException.
// Each condition latches/clears on (shuttleId, code) under the one existing lock.
// Returns 0..2 alarms: one per rising edge.
```

```csharp
// src/FleetManager/FleetServeLoop.cs (change)
// new field, guarded by the existing _knownLock:
private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);

// HandleStatusAsync, inside the existing lock block (one lock acquisition):
//   var code = status.Status.State == ShuttleState.Fault.ToWireName()
//       ? FaultAlarmCodes.ForReason(status.Status.FaultReason) : null;
//   if (code is null) _faultCodes.Remove(shuttleId); else _faultCodes[shuttleId] = code;

// EvaluateAndPublishAsync: snapshot ids AND a copy of _faultCodes under _knownLock,
// then, outside the lock:
//   foreach alarm in _evaluator.Evaluate(id, lost, faults.TryGet(id) -> string?)
```

```jsonc
// src/ShuttleSim/appsettings.json (change) — the injection, default empty
"Shuttle": { "Id": "SH-01", "StartLane": "A01-L01", "HeartbeatIntervalMs": 1000, "BlockedLanes": [] }
```

```jsonc
// src/FleetManager/appsettings.json (change) — the second severity
"BlockedPathSeverity": "warning"
```

`Program.cs` on the shuttle side reads `Shuttle:BlockedLanes` (a `string[]`, default empty) and calls `rack.MarkBlocked(lane)` for each entry before building the endpoint. `Program.cs` on the manager side reads `FleetManager:BlockedPathSeverity` (required, like `LostCommunicationSeverity`) and passes both severities into `new AlarmEvaluator(lostSeverity, blockedPathSeverity)`. `Shuttle:BlockedLanes` has no default entry today; the key is added with an empty array so the next reader sees it.

---

## 9. Where the new code lives

- **`src/ShuttleCore/FaultReasons.cs`** (new) — the wire phrase constants, beside `ShuttleStateWire`.
- **`src/ShuttleCore/Rack.cs`, `src/ShuttleCore/Shuttle.cs`** (change) — the obstacle set, the route check, and the fault branch in `StartMove`.
- **`src/ShuttleSim/ShuttleEndpoint.cs`, `src/ShuttleSim/Program.cs`** (change) — the fault branch in the command handler; the config read.
- **`src/ShuttleSim/appsettings.json`** (change) — `Shuttle:BlockedLanes`.
- **`src/FleetManager/FaultAlarmCodes.cs`** (new) — the pure phrase-to-code mapper.
- **`src/FleetManager/AlarmEvaluator.cs`** (change) — the second severity, the third argument, per-code edges.
- **`src/FleetManager/FleetServeLoop.cs`** (change) — the fault-code intake and the sweep input.
- **`src/FleetManager/Program.cs`** (change) — read `BlockedPathSeverity`; the `serve` command passes both severities. The `move` command keeps its behavior.
- **`src/FleetManager/appsettings.json`** (change) — `BlockedPathSeverity`.
- **`src/FleetManager/FleetManager.csproj`** (change) — the explicit `ShuttleCore` reference.
- **Tests:** `src/ShuttleCore.Tests/RackTests.cs`, `StateTransitionTests.cs` (or `RejectionTests.cs`), `src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, `src/FleetManager.Tests/AlarmEvaluatorTests.cs`, `FleetServeLoopTests.cs`, and a new `FaultAlarmCodesTests.cs`.

Not changed: `src/Contracts/`, `docs/contract/`, `src/Messaging/`, `src/MqttMessaging/`, `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager/RunLoop.cs`, `src/TrafficCore/`, `src/FleetCore/`, `docker/Dockerfile.build`. `ContractVersion.Current` stays `3`.

---

## 10. What would change these choices

- **`palletNotDetected` arrives** (needs load/unload traffic, which the wire does not carry yet — `moveCommand` is the only command kind) → the pattern repeats: one `FaultReasons` constant, one mapper arm, one evaluator severity parameter, one config key. Reopen D's constructor shape only if a third code makes the parameter list unreadable.
- **Traffic control starts honoring blocked lanes** (the manager marks the lane unavailable, reroutes, or holds assignments) → the severity decision (D) and record 17's reservation rules reopen together; the fault *detection* (B) does not change.
- **The shuttle recovers** (operator clears the obstacle; the shuttle leaves `Fault`) → the status level goes back to `idle`, the evaluator's latch clears on the next sweep, and nothing needs a "clear" message — the edge rule from record 21 §4 absorbs it. Recovery *authorization* (who may clear a fault, and how it is confirmed) is its own slice with its own record.
- **The shuttle publishes its own alarms** (for example when the manager is unreachable and the shuttle must speak directly to the WMS) → reopen C; it is a contract direction change and a version bump.
- **A real sensor model appears** (obstacles can appear mid-route, or the shuttle re-plans instead of faulting) → reopen B's route rule; the reject/fault distinction in §3 stands.
- **A fault reason arrives that the mapper does not know** → today: no alarm (D). If that gap ever costs an operator, the fallback becomes a decision: a generic code, a logged warning, or a contract close on `FaultReason`.
- **The known-shuttle set or the latch must survive a manager restart** → record 22 §10 already owns that question; this slice adds `_faultCodes` to the same in-memory state, and the same answer covers it.

---

## 11. Verification table (measured)

Everything below was checked on **2026-10-15** by a command or a file read. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l16base` (no `bin/` or `obj/` copied in). The toy ran at `/tmp/opencode/l16toy`. Both scratch directories are deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no database | `/tmp/opencode/l16base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **109** tests; `FleetCore.Tests`=18, `Contracts.Tests`=7, `ShuttleCore.Tests`=25, `Messaging.Tests`=9, `MqttMessaging.Tests`=2, `ShuttleSim.Tests`=6, `TrafficCore.Tests`=6, `FleetManager.Tests`=36 |
| `AlarmEvaluator` raises only `lostCommunication`, one severity | read `src/FleetManager/AlarmEvaluator.cs` | constructor takes one severity; `Evaluate(shuttleId, lostCommunication)`; latch keyed `(shuttleId, code)` |
| `AlarmCodes` already defines all three codes | read `src/Contracts/AlarmCodes.cs` | `LostCommunication`, `BlockedPath`, `palletNotDetected` |
| The contract's `FaultReason` example already names `blocked path` | read `docs/contract/v3.md:190` | "e.g. `blocked path`, `lost communication`, `pallet not detected`" |
| The contract says the alarm is sent by the fleet manager | read `docs/contract/v3.md:40` | "Sent by a fleet manager to the bus on topic `{root}/{shuttleId}/alarm`" |
| `Shuttle.RaiseFault` exists and is reachable from any state but `Fault` | read `src/ShuttleCore/Shuttle.cs:104-112` | sets `_faultReason`, `State = Fault`; throws if already faulted |
| Nothing raises `blockedPath` or `palletNotDetected` | `grep -rn 'BlockedPath\|PalletNotDetected' src --include='*.cs'` | matches only `src/Contracts/AlarmCodes.cs` and test assertions on the constants |
| `Rack` has no obstacle data today | read `src/ShuttleCore/Rack.cs` | lanes, slots, pallets only; no `MarkBlocked`/`IsBlocked`/`IsRouteBlocked` anywhere in `src` |
| `FaultReasons` does not exist yet | `grep -rn FaultReasons src` | no matches |
| The wire name of `fault` already has one home | read `src/ShuttleCore/ShuttleState.cs` | `ShuttleStateWire.ToWireName`, `Fault => "fault"` |
| The endpoint has two outcomes today | read `src/ShuttleSim/ShuttleEndpoint.cs:63-80` | reject → ack false; accept → ack true + `moving` + travel + `idle`; no fault branch |
| The endpoint's duplicate path acks `Accepted = true` | read `src/ShuttleSim/ShuttleEndpoint.cs:57-61` | the guard's duplicate ack — why a faulted command must not be marked |
| The serve loop holds no fault state today | read `src/FleetManager/FleetServeLoop.cs` | `_knownShuttles` only; `Evaluate(id, lost)` two-argument call |
| `FleetManager` uses `ShuttleCore` types without a direct reference | read `src/FleetManager/FleetManager.csproj` (three refs + TrafficCore) and `RunLoop.cs:2` (`using ShuttleCore;`); baseline build green | transitive via `TrafficCore`; the direct reference is the honest fix |
| `FleetManager.Tests` already references `ShuttleCore` | read `src/FleetManager.Tests/FleetManager.Tests.csproj` | `ProjectReference` to `ShuttleCore.csproj` present |
| Seven `AlarmEvaluator` constructions and fifteen `Evaluate` calls will need updating | `grep -c 'new AlarmEvaluator'` and `grep -o 'Evaluate(' \| wc -l` in `AlarmEvaluatorTests.cs` | 7 constructions; 15 `Evaluate(` call sites |
| `Shuttle:BlockedLanes` and `BlockedPathSeverity` do not exist yet | grep over `src` for both keys | no matches |
| Both config files' current relevant blocks | read both `appsettings.json` files | shuttle: `Id`/`StartLane`/`HeartbeatIntervalMs`; manager: the M5 keys through `AlarmEvaluationIntervalMs` |
| The toy shows obstacle → fault status → one alarm per edge, then a second independent alarm | `/tmp/opencode/l16toy`: `dotnet run` | `FAULT` at `00:00:01` (reason `blocked path`, position unchanged); `PUBLISH blockedPath` warning once at `00:00:01`; `PUBLISH lostCommunication` critical once at `00:00:06`; `CLEAR` ×2 at `00:00:07`; exit 0 |

Not yet verified: `FaultReasons`, the `Rack`/`Shuttle` changes, the endpoint fault branch, `FaultAlarmCodes`, the evaluator and loop changes, the two config keys, the `ShuttleCore` project reference, and the ten new FleetManager / four new ShuttleCore / one new ShuttleSim tests — this assignment specifies them and the reviewer measures them. The three-terminal broker demo below is also not yet verified in this environment (no broker available while authoring).

---

## 12. What this record unblocks

- Lesson 16 can assign the obstacle model, the `StartMove` fault, the endpoint's fault branch, the mapper, the evaluator generalization, the loop's fault intake, the two config keys, and the tests — with no unguided product choice.
- Reassignment (next slice) has a real failure signal to react to: a shuttle in `Fault`, visible in status and in the `blockedPath` alarm.
- Recovery (slice after) has a second alarm code to acknowledge, and the record's own §10 says the latch will absorb a status-level clear without a new wire kind.
- The M5 done-when proof gains its first injectable fault: set `Shuttle:BlockedLanes`, command a move, watch the alarm.
