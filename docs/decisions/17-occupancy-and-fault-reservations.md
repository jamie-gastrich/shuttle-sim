# Decision Record 17: Occupancy vs reservation, release-on-departure, and fault behavior

**Date:** 2026-10-12
**Milestone:** M4 (Traffic control) — lesson 11: the run-loop gate with occupancy.
**Status:** Accepted. Append-only; does not replace [record 16](16-traffic-segments-and-deadlock-rule.md). Links back to records 15a, 16.
**Builds on:** [record 16](16-traffic-segments-and-deadlock-rule.md) (all-or-nothing whole-route reservation, no hold-and-wait), [record 15a](15a-task-dispatcher-and-db-test-fixtures.md) (dispatcher shape unchanged), [record 05](05-shuttle-core-and-rack-model.md) (rack model and lane as position).
**Replaced by:** a later record that changes occupancy scope, fault/recovery semantics, or heartbeat/timeout. Append-only.
**Wire contract:** no change to `docs/contract/v1.md` or `v2.md`. `ShuttleStatus.Position` already exists; no new message kinds. Heartbeats/timeouts are M5; this record only defines the in-memory occupancy view needed for M4's run-loop gate.

---

## 1. What we are deciding

Lesson 10 deferred the "parked shuttle" gap. The owner raised: a shuttle that has arrived and is working at its destination, or an idle/parked shuttle, holds nothing under record 16 §5's move-only gating, so another shuttle could be granted a route through that lane and cause a collision. Also fault behavior: declaring a shuttle lost is knowledge, not the metal; auto-release on timeout is unsafe.

We must settle four real choices:

**A.** Occupancy vs reservation: what does a shuttle *own* (physically occupies) vs what is *reserved* (may use in a move)?
**B.** When is a move reservation released? (on arrival vs on departure).
**C.** Faulted/lost shuttle behavior: is it an obstacle until physically cleared? Can we auto-release on timeout?
**D.** Fleet-side occupancy view and scope split: what does M4 (lesson 11, run loop) need now, vs M5 (heartbeats/timeouts/reconciliation)?

---

## 2. Decision A: occupancy means "I am physically here"; reservation means "I may use this"

**Definition (plain words):**

- **Occupancy** is the set of segments a shuttle physically occupies right now. The shuttle *owns* those segments for as long as it is present there. Another shuttle must never be granted a route that passes through occupied segments.
- **Reservation** is a temporary claim to a *transit route* for a move in progress. It is held only while moving along that route (or preparing to move) and is released when the move no longer needs it. Reservation is distinct from occupancy.

Record 16 already treats a granted route as reserved; this decision adds that a shuttle's current physical position (its occupied lane(s)) is also a block on others, regardless of whether it is moving or idle/working.

| Option | A: occupancy separate from reservation (chosen) | B: reservation is the only thing; parked means nothing held | C: occupancy equals "current position only" with no segment model |
|---|---|---|---|
| Prevents collision with working/idle shuttle | yes — occupied segments are blocked | **no** — allows another through a working/idle shuttle's lane | weak; cannot express a route |
| Matches real systems | yes — AGVs have physical occupancy + planned reservations | oversimplifies | no |
| Fits existing contract (`Position` is a lane) | yes | yes | yes but coarse |
| Preserves record 16's deadlock avoidance | yes — we still block on occupied + reserved; the all-or-nothing rule unchanged | breaks safety | unclear |

**Decision A:** A shuttle physically occupies the segment(s) it is in. For this serial-lane model (one lane = one segment), a shuttle at lane `P` occupies segment `P`. An idle or working shuttle at `P` occupies `P` — so no other shuttle may be granted a route through `P`. A move reservation is a *transit* claim on the route from `from` to `to` for the duration of that move; occupancy of the current position remains. The two are checked together: a route is grantable only if **no segment** in the route is occupied by a different shuttle **and** no segment is reserved by a different shuttle.

**Why rejected B:** record 16's note "It proves safety for moves, not parked shuttles" is a statement about that slice's scope, not a desired end state. The run-loop must enforce physical safety for all shuttles present.

**Why rejected C:** we already have the segment model (record 16); reusing it is simpler and testable.

---

## 3. Decision B: release a move's reservation on departure, not on arrival

The move's transit reservation is held for the *transit*. When the shuttle arrives at destination, it becomes physically present there (occupies that destination lane). Releasing the transit reservation **at arrival** frees the middle segments while the shuttle is now standing in the destination — but the key is: the shuttle continues to occupy its position. The "release" we mean is releasing the *move's reserved route*. Equivalently, better to say: **a move reservation is released as the shuttle departs the segments** (or simply released once the transit is no longer needed). Practically, for an end-to-end move along a route, the reservation on segments behind the shuttle can be released as it moves; but we do not have within-lane position yet.

With one lane = one segment and no sub-lane travel model (record 16 §3, record 05 §4 deferred), the cleanest, explainable rule is:

**Decision B:** A shuttle's **move reservation** (the route granted for `TryReserveRoute(from, to)`) is **released on departure** — that is, when the shuttle begins executing that move and leaves its previous position, or more concretely, at the moment the transit claim is no longer required. For this lesson's run-loop (discrete steps), the reservation is released **after the shuttle has left the source segments** and before/when starting the next action, or simply: when the move completes its transit to destination, the transit reservation is released, but the **destination remains occupied** by the shuttle. The physical occupancy is what persists.

But more precisely: the reservation is the claim on the *transit* path. The shuttle occupies its current lane at all times (from status/position). So when it arrives at destination `D`, it occupies `D`. The reserved route included `from ... D`; releasing the reservation at arrival frees `from ... (D-?)` but `D` was both end and now occupied. This is safe if occupancy blocks others.

**Chosen rule (clear and testable):** 
- **Physical occupancy** is authoritative: a shuttle at position `P` occupies segment `P`. This is true whether idle, working, or just arrived. Occupancy is driven by the shuttle's reported position (from `ShuttleStatus.Position`) and is owned by the fleet manager's occupancy view.
- **Transit reservation** is the set of segments granted by `TrafficController.TryReserveRoute(from, to)` for a specific move. It exists only while that move is in progress. **Release the transit reservation on departure / when the move ends its transit phase** — i.e. when the shuttle has reached its destination and the move is complete, call `TrafficController.Release(shuttleId)` to free the transit route. The destination remains occupied by physical position.
- **Idle/parked shuttle:** occupies its current lane. It holds **no transit reservation**. The traffic gate must not grant any route through an occupied segment.

This matches the review's note: "the run loop asks `TrafficController.TryReserveRoute` **before** `TaskDispatcher.AssignNextAsync`, so the dispatcher only ever gets a task whose route is already safe." Combined with occupancy, "safe" means the route does not intersect any occupied segment.

**Why not release on arrival only?** The phrase is ambiguous. Releasing "on departure" means the reservation is tied to the transit, not to the parked state. Also it pairs well with the next move: to start moving again, you need a new route reservation from current position.

---

## 4. Decision C: faulted/lost shuttle is an obstacle until physically cleared; Release is post-recovery, not automatic on timeout

The owner's challenge to record 16 §5: "declaring a shuttle lost/unresponsive is a statement about our knowledge, not the metal; the shuttle is still physically in its segment." Auto-release on timeout is unsafe because the shuttle may still be alive and moving, or it may be stopped in place.

**Principles:**

- **Knowledge vs reality.** Loss of communication does not mean the physical shuttle vanished. It may be stopped, it may be blocked, it may still be executing its last command.
- **Safety first.** Never assume a lost shuttle freed space. Assume it occupies the space it was last known to occupy until an operator or recovery action confirms the obstruction is cleared.
- **Release is post-recovery.** `TrafficController.Release(shuttleId)` frees transit/reserved segments. That is an *action* taken during/after recovery (e.g. after operator acknowledges and the shuttle is confirmed cleared, or after the shuttle is safely recovered). It must not happen automatically just because a timeout fired.
- **M5 scope.** Heartbeats, timeouts, last-seen timestamps, and operator-acknowledged recovery belong to M5. But M4 must not bake in unsafe auto-release semantics.

| Option | A: treat last-known position as occupied until recovery clears it (chosen) | B: auto-release on timeout/declaration of loss | C: drop all occupancy on any fault |
|---|---|---|---|
| Safety | safe — conservative | **unsafe** (could grant route through live/stuck shuttle) | unsafe |
| Matches "operator acknowledges recovery" (M5) | yes — clearance requires action | no, automatic | no |
| Fits M4/M5 split | M4 tracks last-known occupancy; M5 adds timeouts/lastSeen | forces M5 behavior now | no |
| Matches real ops | yes — lost comm means assume blocked until verified | no | no |

**Decision C:** A shuttle's **last-known occupied position** remains occupied as long as we consider it present. Declaring a shuttle lost/faulted does **not** automatically call `Release`. The segments it occupies stay blocked (held as occupied) until the obstruction is physically cleared and recovery completes — which includes operator acknowledgement in M5. `Release` is a post-recovery action, never an automatic side effect of detecting "no update".

**Implication for M4 scope:** the fleet-side occupancy view stores `shuttleId → lane` (last known). It does not expire based on time yet (no `lastSeen` threshold/timeout logic in M4). M5 adds heartbeats, `lastSeenAt`, timeout detection, alarms, and the recovery path that may call `Release` after operator confirmation.

---

## 5. Decision D: fleet-side occupancy view — scope split M4-now vs M5

Today the contract carries `Position` (a lane) and shuttles know where they are; the fleet manager has **no authoritative occupancy registry** and **no heartbeat/lastSeen** (as confirmed in codebase inspection). The run loop needs enough to gate moves safely without over-scoping.

**What M4 (lesson 11) must maintain now:**

- A **fleet-side occupancy map**: `shuttleId → occupiedLane` (the last known physical position). This is updated when the shuttle's position changes (from status updates or as we track the shuttle's state). It represents "this shuttle occupies this lane now."
- When considering `TryReserveRoute(from, to)`, the gate must **reject** any route that passes through a segment occupied by a **different** shuttle (by current occupancy). The transit reservation is checked via `TrafficController` (which checks its `SegmentReservations` table). So the full block check is: (no segment in route reserved by another) AND (no segment in route occupied by another).
- An idle/parked shuttle remains in occupancy at its position; it holds no transit reservation.
- No time-based expiry yet. No heartbeat/timeout yet. No reconciliation on restart beyond what M3 persistence already gives (tasks persist; occupancy of shuttles is runtime state derived from last known positions). This is conservative and safe.

**What belongs to M5 (deferred):**

- Heartbeats and `lastSeenAt` timestamp per shuttle.
- Timeout detection ("lost communication") and alarms with severity.
- Reconciliation on fleet manager restart: restore occupancy from persisted state or reconstruct from known shuttle states; "the saved state never loses or duplicates a pallet" (M5 done-when).
- The recovery flow where a lost shuttle's occupied segments remain blocked until operator acknowledges and physical clearance occurs; only then call `Release` (post-recovery). Auto-release on timeout is **not** done.

**Decision D:** M4 adds an in-memory occupancy registry (last-known position per shuttle). The run-loop gate checks both occupancy and transit reservations before granting a move. Full heartbeat/timeout/reconciliation is explicitly M5. This split keeps lesson 11 to ≤3 concepts and avoids over-scoping while closing the parked-shuttle safety gap.

---

## 6. What would change these choices

- **Sub-lane position/granularity** becomes available (record 05 §4 resolved) → occupancy can be finer than a whole lane; `Track`/route logic may change but A-D remain valid.
- **Shuttles can be in multiple segments simultaneously** (multi-segment vehicles, two-deep lanes) → occupancy becomes a set; this decision would be revised.
- **Throughput demands preemption policy** → would require revisiting C (safe preemption rules) and adding rollback semantics; new record.
- **Operator can force-clear an obstruction** → M5 recovery flow formalizes when `Release` is allowed (post-operator-ack); this decision already forbids auto-release.
- **Restart must reconstruct authoritative occupancy** → M5 adds persisted occupancy/lastSeen; M4's in-memory map is sufficient for the run loop during a live run.

---

## 7. Verification table (measured)

Claims below verified by reading files and commands on 2026-10-12. No code under `src/` changed.

| Claim | How checked | Result |
|---|---|---|
| No existing occupancy/lastSeen code | `grep -rn "lastSeen\|LastSeen\|occupancy" src 2>&1 \| head -5` | only binary matches in bin/ dirs; no source references |
| `ShuttleStatus.Position` exists | `cat src/Contracts/ShuttleStatus.cs` | `string Position` present (line 6) |
| Shuttle sim publishes Position | `grep -rn "Position" src/ShuttleSim/ShuttleEndpoint.cs` | uses `_shuttle.Position` |
| FleetManager Program reads status | `grep -rn "ShuttleStatus" src/FleetManager/Program.cs` | deserializes status message (M2 demo) |
| Dispatcher unchanged | `cat src/FleetManager/TaskDispatcher.cs` | shape matches record 15a (AssignNextAsync/StartAsync/CompleteAsync/FailAsync) |
| Record 16's trade-off quoted | `sed -n '95,115p' docs/decisions/16-traffic-segments-and-deadlock-rule.md` | contains "It proves safety for moves, not parked shuttles..." text as stated |
