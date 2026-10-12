# Decision Record 16: Traffic segments, reservations, and the deadlock rule

**Date:** 2026-10-12
**Milestone:** M4 (Traffic control) — lesson 10: the track as segments, the segment reservation, and the deadlock rule.
**Status:** Accepted. Written for lesson 10, the first M4 lesson. This record also answers the question record 05 §4 deliberately left open: *"M4 needs to say where in a lane a shuttle is, or reserve a segment rather than a whole lane → position granularity and distance move to a new record that links here."*
**Builds on:** [record 05](05-shuttle-core-and-rack-model.md) (the rack model, its lane ids, and `DistanceInLanes`), [record 11](11-task-lifecycle-and-queue.md) (the task), [record 15](15-m3-end-to-end-assignment-and-restart.md) and [record 15a](15a-task-dispatcher-and-db-test-fixtures.md) (the dispatcher and its caller-side assignment policy). Record 15a §6 said: *"If the dispatcher grows past assignment (for example M4 lane reservation), split traffic decisions into their own class; do not let `TaskDispatcher` become a catch-all."*
**Replaced by:** a later record that changes the segment granularity, the reservation rule, or where traffic logic lives. Records are append-only; a replacement links back here.
**Wire contract:** unchanged. Traffic control is internal to the fleet manager. No message kind changes; no version bump. `docs/contract/v1.md` and `v2.md` remain as-is.

---

## 1. What we are deciding

The spec's M4 line (`spec/shuttle-spec.md:50-51`): *"Reserve lane segments so two shuttles never occupy the same space. Detect or avoid deadlock."* Its done-when: *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

That sentence holds three unanswered questions, and this record answers all three:

- **A: Where does the traffic decision logic live?** (a pure project, so it is unit-testable alone)
- **B: What is a segment, and how is a route built from the warehouse model?** (segment granularity)
- **C: What is the deadlock rule?** (the spec says "detect or avoid"; we pick one and name it)

A fourth question — *when* the run loop calls the gate, before `TaskDispatcher.AssignNextAsync` — was already answered by review 09's End-product check. This record confirms it and leaves the wiring to the next M4 lesson.

---

## 2. Decision A: traffic logic lives in a new pure project, `TrafficCore`

| | A: new `TrafficCore` class library, referencing `ShuttleCore` (chosen) | B: inside `FleetCore` | C: a class inside `FleetManager` |
|---|---|---|---|
| Keeps `FleetCore` unaware of shuttles | yes | **no** — a reservation names a shuttle id | yes |
| Testable without a broker or DB | yes — a plain library with no packages | yes, but the core grows a shuttle concept | yes, but the test project drags the app |
| Matches record 15 §3 (assignment names shuttles → app layer) | yes — traffic names shuttles too, so it is its own pure layer, not `FleetCore` | breaks it | yes, but traffic and assignment share one project |
| Matches the spec's system overview (Warehouse model and Traffic control are separate rows) | yes | no | partly |
| New projects | two (`TrafficCore`, `TrafficCore.Tests`) | none | none |

**Decision A: a new, package-free class library `TrafficCore`, with a `ProjectReference` to `ShuttleCore` only.** The reservation table and the traffic decision live there, in plain classes with no broker, no database, no clock, and no ids. `TrafficCore.Tests` holds the pure tests. `FleetManager` will reference `TrafficCore` in the next M4 lesson, when the run-loop gate is wired.

**Why not `FleetCore`:** record 15 §3 keeps `FleetCore` unaware that shuttles exist. A reservation is keyed by a shuttle id, so traffic cannot live there without breaking that boundary. **Why not a class in `FleetManager`:** the spec lists *Traffic control* as its own area, and record 15a §6 asked for traffic to be split from the dispatcher. A package-free library makes the compiler — not a reviewer — enforce that traffic cannot reach the broker or the database. **Why `ShuttleCore` for the track, not `TrafficCore`:** the track answers *"which lanes exist, and in what order?"* That is warehouse geometry, which `ShuttleCore` already owns (`Rack`). `TrafficCore` answers *"who may use which piece right now?"* Two different questions, two projects.

---

## 3. Decision B: a segment is one lane, and a route is every segment between two lanes

Record 05 §3 fixed the id scheme: lanes are `A{nn}-L{nn}` and slots append `-S{nn}`, and validation is membership in the set the rack built. Record 05 §4 fixed position as a lane and distance as the aisle-major ordinal difference.

| | A: one lane = one segment (chosen) | B: fixed-length segments inside each lane | C: one slot = one segment |
|---|---|---|---|
| Answers "two shuttles never share space" | yes — a lane is the unit `Shuttle.Position` already uses | yes, but needs a within-lane position the model does not have | yes, but slots are storage, not track |
| Builds on the existing model | directly (`Rack` lanes, `DistanceInLanes`) | needs new geometry (metres, within-lane offsets) | needs the shuttle to be at a slot, not a lane |
| Testable today | yes | needs a length input with no source yet | awkward |
| Cost | coarse: a whole lane is blocked | fine-grained | wrong unit |

**Decision B: one lane is one segment.** A segment id **is** the lane id (for example `A01-L03`). `Track` builds its ordered `SegmentIds` from the rack's lanes, in the same aisle-major order `Rack.DistanceInLanes` already uses. `Track.Route(from, to)` returns every segment from the lower lane to the higher lane, **inclusive of both ends**. A move to the same lane returns that one segment.

This leaves the "within-lane travel" record 05 §4 deferred still deferred. That is deliberate: the model cannot yet say *where inside a lane* a shuttle is, so it cannot reserve less than a whole lane. When it can, the change is local to `Track.Route` (which would return sub-lane segment ids); the reservation table and the rule do not change. A later record links here.

**The numbers, and where they come from.** There is no lane list in code: `Track` takes a `Rack` and asks it for its lanes (`grep` for a literal `"A01-L01"` in `src/TrafficCore` finds nothing). Segment length is one lane, derived from the model. A station (a lane a shuttle works in) consumes its own single segment, derived. The reservation scope (route inclusive of both ends) is the rule in Decision D, not a number. There is **no timeout, retry count, or size** in this slice; when M5 adds reservation expiry, that is a configured duration, not a literal.

---

## 4. Decision C: one reservation table owns "who holds which segment"

**Decision C: `SegmentReservations` is the single owner of the reservation state, and a reservation is atomic and all-or-nothing.**

- It holds one mapping: segment id → owner id (a shuttle id).
- `TryReserveAll(ownerId, segmentIds)` checks **every** requested segment first. If any is held by a different owner, it returns `false` and writes **nothing**. Otherwise it writes them all and returns `true`.
- `Release(ownerId)` frees every segment the owner holds, in one call.
- `OwnerOf(segmentId)` and `SegmentsOwnedBy(ownerId)` read the state.

The two-pass shape (check all, then write all) is what makes the grant atomic from the caller's point of view. Writing while checking would leave a half-reserved route on a refusal, and a half-reserved route is exactly the "holds a piece while waiting" state that Decision D forbids.

This is the fleet-control rule *"never let two shuttles claim the same lane segment at once"* and the AGENTS rule *"protect shared state; prefer a single owner"*. One table, one answer to "who is where". The run loop is the intended single caller; M5 owns what to do when a shuttle stops answering.

**Rejected:** a reservation per `TrafficController` call with no shared table (two callers would disagree); tracking occupancy on `Shuttle` (the shuttle simulator is on the other side of the contract and must not be a second source of truth for fleet state); a lock per segment (locks are harder to reason about and to unit test than a plain table with one caller).

---

## 5. Decision D: the deadlock rule — all-or-nothing whole-route reservation

A deadlock needs **all four** of these at the same time (the classic four ingredients):

1. **Mutual exclusion** — a resource is used by one at a time.
2. **Hold and wait** — a holder keeps what it has and waits for more.
3. **No preemption** — nothing can be taken away; only the holder releases.
4. **Circular wait** — a cycle of holders each waiting for the next.

Real systems in the AGV/AMR literature divide the guidepath into zones/segments and give one vehicle exclusive access to a region (mutual exclusion is wanted). To stop deadlock they either **prevent** it with a rule that removes an ingredient, or **detect** it and **resolve** it (roll one vehicle back). Two named kinds recur: **head-on deadlock** (two vehicles face each other on one track) and **loop deadlock** (a ring of vehicles). The literature also reports the trade-off: avoidance avoids rollback but can make vehicles wait; detection and resolution can wait less on long aisles but needs a rollback policy.

**Our rule, named: "All-or-nothing whole-route reservation (no hold-and-wait)."**

- A shuttle that wants to move asks the controller to reserve **the whole route** from its current lane to the destination lane.
- The controller reserves **all** of the route in one atomic step, or grants **nothing**.
- A refused shuttle waits **holding no segment**. It is not queued by the controller; the caller decides how to retry (next lesson).
- The reservation is held until the caller calls `Release`. The run loop will release it when the move ends; M5 will release it when a shuttle is declared lost or faulted.
- While a shuttle is moving through or parked on its reserved route, it is not waiting for another segment. It holds but does not wait.

**Why this avoids deadlock, structurally.** It removes ingredient 2 (hold and wait): a waiting shuttle holds nothing. Ingredient 4 (circular wait) cannot form, because a cycle needs each waiter to hold something the next needs. Deadlock needs all four ingredients, so deadlock is impossible. This is **avoidance**, not detection: there is no cycle detector, no timeout, and no victim rollback. It is a rule, not a guess.

**What the rule costs (honest trade-offs).**

- **Starvation, not deadlock.** A long route can keep losing to short routes under heavy load. The controller is fair by turn only if the caller is. We accept this for a correct, explainable rule (the spec's non-goal says *"A correct, explainable approach beats a clever one"*), and note that a fairness rule or reservation expiry can be added.
- **A dead shuttle blocks its route.** Until `Release` is called, its segments stay held. M5's recovery calls `Release`; this record guarantees the hook exists.
- **Lower throughput than incremental reservation.** Whole-route grants hold more space for longer than segment-by-segment. That is the price of removing hold-and-wait completely.
- **It proves safety for moves, not parked shuttles.** This slice gates *moves*. An idle shuttle holds nothing. The run-loop lesson says how an idle or working shuttle's station is handled.

**Rejected option B: incremental reservation with cycle detection and victim rollback.** Reserve the next segment as you move; build a waits-for graph; if a grant would close a cycle, either refuse it (avoidance by cycle prevention) or detect the cycle and roll a victim back. More throughput, but it needs the graph, a rollback policy, and a way to move a victim backwards safely. Rejected for M4: more code, more failure modes, and the spec lets us avoid instead of detect.

**Rejected option C: one move at a time (a global token / central scheduler).** Let only one shuttle move in the whole system at a time, or hand out a token. Collisions and deadlock are trivially impossible, but throughput is very low and it is a global design, not *"reserve lane segments"* as the spec words it. Rejected.

**Rejected option D: reserve the destination station but not the transit route.** A shuttle could be hit from behind on the way. Rejected: the route is the space it actually passes through, so the route must be reserved.

---

## 6. What would change these choices

- **Finer segment granularity** (within-lane travel) becomes possible → `Track.Route` returns sub-lane segment ids; Decision B is superseded by a record that links here. Decisions C and D do not change.
- **Starvation becomes a measured problem** → add a fairness rule (grant in wait-age order) or reservation expiry; new record.
- **Throughput matters more than simplicity** → replace Decision D with incremental reservation plus cycle prevention or detection and rollback; new record.
- **A second caller of `TrafficCore` appears** (for example the operator dashboard pausing a lane) → the controller needs an owner or a queue for "who is the single caller"; new record.
- **The rack model gains two-deep lanes or unequal lane lengths** → segment geometry changes; a new record links to record 05.

---

## 7. Verification table (measured)

Experiments ran on 2026-10-12. The baseline suite ran against a throwaway copy of `src/` at `/tmp/opencode/l10verify`; the new-project shape was measured in the same copy; the toy ran at `/tmp/opencode/l10toy`. All are deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite (no DB) | copy of `src/`, `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **7** `Passed!` lines, 70 tests (`22+18+4+8+2+11+5`) |
| The rack builds lane ids and orders them by ordinal | read `src/ShuttleCore/Rack.cs` (`:27`, `:45`, `:50-51`) | lanes built from dimensions; `IsLane` is membership; `DistanceInLanes` is ordinal difference |
| No lane id is hard-coded in the warehouse model | `grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/ShuttleCore --include='*.cs'` | no matches (exit 1) |
| The `dotnet` templates match the project's versions | `dotnet new xunit` | xunit 2.9.3, runner 3.1.4, test SDK 17.14.1, coverlet 6.0.4 — same as the existing test projects |
| A package-free library can reference `ShuttleCore` | `dotnet new classlib`, `dotnet add reference ShuttleCore` | csproj gets exactly one `ProjectReference`, no `PackageReference` |
| Adding two projects changes the solution count 14 → 16 | `dotnet sln ShuttleFleet.slnx add …`; `grep -c '<Project Path'` | prints `16` |
| Adding the new test project changes the pure run to 8 `Passed!` lines | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | 8 `Passed!` lines (the template adds one placeholder test) |
| The all-or-nothing rule drains a 4-shuttle scenario with zero shared segments | toy at `/tmp/opencode/l10toy`, `dotnet run` | 4 shuttles finish in 5 rounds; collisions 0 |
| The same head-on case deadlocks under hold-and-wait | same toy, contrast section | step 2: each waits for a segment the other holds; no progress |
| A segment id equals a lane id, so the model is reused | toy output line `track segments: A01-L01 … A01-L06` built from a real `Rack` | confirmed |

**Not yet verified:** `Track`, `SegmentReservations`, `TrafficController`, `TrafficDecision`, the three track tests, the six traffic tests, the run-loop gate, and the fresh container with the two new projects — lesson 10 specifies them and the reviewer measures them.

---

## 8. What this record unblocks

- Lesson 10 can assign a pure `TrafficCore` with fixed names and a stress test, and a `Track` in the warehouse model, with no unguided choices.
- The M4 done-when's *"the rule used is written down and explained in a decision record"* is satisfied by this record, and its *"no collisions and no permanent deadlock"* is provable by a pure test today.
- The next M4 lesson wires `TrafficCore` into the run loop in front of `TaskDispatcher.AssignNextAsync`; the dispatcher's shape (record 15a §2) does not change.
- M5 has a named hook (`Release`) to free a lost shuttle's segments, and M7 has a read model (`OwnerOf`, `SegmentsOwnedBy`) for the dashboard.
