# Lesson 11: Physical occupancy, the fleet-side occupancy view, and the run-loop gate

**Milestone:** M4 (Traffic control) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 10: The track as segments, the segment reservation, and the deadlock rule](10-track-segments-and-reservation.md) · **Decision:** [record 17](../decisions/17-occupancy-and-fault-reservations.md); builds on [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md), [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md), and [record 05](../decisions/05-shuttle-core-and-rack-model.md)

**Concepts this assignment requires:**

- [ ] **Physical occupancy vs transit reservation** — a shuttle physically occupies its lane (whether idle, working, or moving); a transit reservation is a temporary claim on a route for a move. Both must be checked before granting a move. Taught in [Physical occupancy vs transit reservation](#1-physical-occupancy-vs-transit-reservation). Fixed by [record 17 §2](../decisions/17-occupancy-and-fault-reservations.md).
- [ ] **Fleet-side occupancy view** — the fleet manager owns an in-memory map from shuttle id to its last-known occupied lane. Taught in [Fleet-side occupancy view](#2-fleet-side-occupancy-view). Fixed by [record 17 §5](../decisions/17-occupancy-and-fault-reservations.md).
- [ ] **The run-loop gate** — before assigning a move, the run loop checks both occupancy and transit reservation, then calls `TaskDispatcher.AssignNextAsync`. Taught in [The run-loop gate](#3-the-run-loop-gate). Fixed by [record 17 §3](../decisions/17-occupancy-and-fault-reservations.md).

Three new concepts. That is the cap.

Four things this assignment needs are **assignment requirements, not concepts**. They change where code lives and how the tests run, not the ideas above:

- an in-memory `OccupancyMap` in `FleetManager` ([record 17 §5](../decisions/17-occupancy-and-fault-reservations.md));
- the gate as a pure helper that the run loop would call before `TaskDispatcher.AssignNextAsync` ([record 17 §3](../decisions/17-occupancy-and-fault-reservations.md));
- the dispatcher's signature and behavior unchanged ([record 15a §2](../decisions/15a-task-dispatcher-and-db-test-fixtures.md));
- project references: `FleetManager` gains `TrafficCore`; `FleetManager.Tests` gains `TrafficCore` and `ShuttleCore` ([record 17 §5](../decisions/17-occupancy-and-fault-reservations.md));
- a unit test proving the gate blocks a route through an occupied lane.

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** wiring the gate into a real `Program.cs` run loop (a later lesson; the gate is proven by unit tests here), heartbeats, `lastSeenAt`, timeout detection, alarms, reconciliation on restart, auto-release on fault (all M5), WMS orders (M6), the dashboard (M7), real shuttle movement or animation, and any wire-contract change.

---

## Where this fits

M4's done-when requires: *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

Lesson 10 built the pure traffic core: `Track`, `SegmentReservations`, and `TrafficController` with the all-or-nothing rule. It proves collision-free routing and deadlock avoidance. But it has **no idea where a shuttle physically is** — it only knows what has been reserved. A parked or working shuttle holds no reservation, so another shuttle could be granted a route through its lane. That is the gap this lesson closes.

```text
    run loop (this lesson)                     lesson 10 (built)
    ──────────────────────                     ───────────────────
    need to move SH-01 to A01-L04?
         │
         ▼
    OccupancyMap check ──────────────► occupancy map          "who is physically here?"
         │                              (shuttleId → lane)
         ▼
    TrafficController.TryReserveRoute(…) ─► Track.Route(…)     "what is the route?"
         │                                  SegmentReservations "who holds each segment?"
         │  both clear?
         ├── yes ─► TaskDispatcher.AssignNextAsync(…)   (M3, unchanged)
         └── no  ─► try another shuttle, or wait
```

**The honest weak spot.** There is **no running shuttle movement yet.** The run-loop gate is proven by a unit test that drives the gate logic directly, not by a live shuttle moving through a lane. The gate is the real M4 wiring: it is what a production run loop would call. Say this plainly to a reviewer rather than overclaiming a live traffic demo.

There is a second requirement from AGENTS.md: *"Decision logic is separated from input and output."* The occupancy map and the gate are decision logic. The occupancy map is a simple in-memory data structure; the gate is a pure check. Neither touches a broker, a database, or a network.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* The occupancy map stores whatever lane the shuttle reports — no lane list is typed into the code. The gate checks whatever route `Track.Route` returns. There is no timeout or retry count in this slice; when M5 adds heartbeat timeouts, that duration comes from configuration, not a literal.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-12**. The baseline build and suite ran against the real `src/`; the toy ran at `/tmp/opencode/l11toy`. The toy directory is deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; `25+18+4+8+2+11+6+5` = **79** tests |
| The solution currently has 16 projects | `grep -c '<Project Path' src/ShuttleFleet.slnx` | `16` |
| `TrafficController` has `TryReserveRoute` and `Release` | read `src/TrafficCore/TrafficController.cs` | `:16` `TryReserveRoute`, `:38` `Release` |
| `ShuttleStatus.Position` exists in the contract | read `src/Contracts/ShuttleStatus.cs` | `:6` `string Position` |
| `TaskDispatcher` shape matches record 15a | read `src/FleetManager/TaskDispatcher.cs` | `:5` class; `AssignNextAsync`, `StartAsync`, `CompleteAsync`, `FailAsync` |
| No occupancy code exists yet | `grep -rn "OccupancyMap\|occupancy\|lastSeen\|LastSeen" src --include='*.cs'` | no matches in source files |
| `FleetManager` does NOT yet reference `TrafficCore` | read `src/FleetManager/FleetManager.csproj` | 4 `ProjectReference` lines: FleetCore, Contracts, Messaging, MqttMessaging |
| `FleetManager.Tests` does NOT yet reference `TrafficCore` or `ShuttleCore` | read `src/FleetManager.Tests/FleetManager.Tests.csproj` | 3 `ProjectReference` lines: FleetManager, Contracts, Messaging |
| The toy runs and shows occupancy blocking, then unblocking | toy at `/tmp/opencode/l11toy`, `dotnet run` | 4 scenarios; gate refuses twice, allows twice; exit 0 |
| Contract is still v2 | `grep -qE '^Version: 2$' docs/contract/v2.md` | match |

Not yet verified: the `OccupancyMap`, the run-loop gate, and the gate unit test — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for up to three options per real choice, compared, before a decision record picks one. There are two real choices in this slice.

### Survey 1: Where does the occupancy map live? (→ [record 17 §5](../decisions/17-occupancy-and-fault-reservations.md))

| | A: in-memory map in `FleetManager` (chosen) | B: inside `TrafficCore` | C: inside `FleetCore` |
|---|---|---|---|
| Knows shuttle ids | yes — FleetManager already names shuttles | **no** — TrafficCore is shuttle-agnostic (record 16 §2) | **no** — FleetCore must not learn about shuttles (record 15 §3) |
| Testable without a broker or DB | yes | yes | yes |
| Matches the spec's area boundaries | yes — fleet-side view is fleet manager state | breaks the TrafficCore boundary | breaks the FleetCore boundary |
| Run loop can access it easily | yes — same project | yes — FleetManager references TrafficCore | yes |

**Decision 1: an in-memory `OccupancyMap` in `FleetManager`.** The occupancy view is fleet-manager state: it maps shuttle ids to lanes, and only the fleet manager knows shuttle ids. Rejected: putting it in `TrafficCore` (record 16 §2 keeps TrafficCore shuttle-agnostic); putting it in `FleetCore` (record 15 §3 forbids FleetCore learning about shuttles).

### Survey 2: Where does the gate live? (→ [record 17 §3](../decisions/17-occupancy-and-fault-reservations.md))

| | A: gate before `AssignNextAsync` in the run loop (chosen) | B: gate inside `TaskDispatcher.AssignNextAsync` | C: gate inside `TrafficController.TryReserveRoute` |
|---|---|---|---|
| Dispatcher shape unchanged | yes | **no** — dispatcher gains traffic/occupancy deps | yes |
| Dispatcher stays a pure task-board operation | yes | **no** | yes |
| Run loop controls retry/wait policy | yes | **no** — hidden inside dispatcher | yes |
| Matches record 15a §2 | yes | breaks it | yes |

**Decision 2: the gate sits in the run loop, before `TaskDispatcher.AssignNextAsync`.** The dispatcher keeps its exact M3 shape. The run loop asks the occupancy map and the traffic controller for a safe route, and only then calls the dispatcher. Rejected: putting the gate inside the dispatcher (record 15a §2 freezes the dispatcher shape); putting it inside `TrafficController` (the controller answers "is this route reserved?" — it does not know physical occupancy).

---

## The lecture

Today we are going to learn about three things: **physical occupancy vs transit reservation**, **the fleet-side occupancy view**, and **the run-loop gate**.

### 1) Physical occupancy vs transit reservation

A **segment** is one lane's worth of track (record 16 §3).

A **transit reservation** means "I may use these segments for this move right now." Record 16 gave us `SegmentReservations` and `TrafficController.TryReserveRoute`: reserve the whole route or nothing; a waiting shuttle holds nothing. That removed hold-and-wait and prevents deadlock.

**Physical occupancy** means "I am physically here." A shuttle at position `P` occupies segment `P`. This is true whether it is idle, working, loading, or just arrived. The metal does not disappear just because a move finished.

Real-world picture: a forklift parks in lane 3 to load a pallet. The forklift is physically in lane 3 — you cannot drive another truck through lane 3 while it is there, even if it is not "moving." The planned route for the next move is separate; the physical space is occupied.

**The two ideas together:**

- **Occupancy** is long-lived — for as long as the shuttle is present in that space. It comes from the shuttle's last known position.
- **Transit reservation** is short-lived — for a specific move. It covers the route from `from` to `to` while moving. It is released when the move's transit ends (record 17 §3). The destination remains occupied by physical position after arrival.

**Why both?** If we only checked transit reservations, an idle or working shuttle holds nothing and another shuttle could be granted a route through its lane — causing a collision. If we only checked occupancy, we could not plan safe transit routes. Both must be true for a grant.

**The safety rule (from record 17 §2):** before granting a route `R = {s1,...,sk}`, check: for every segment in `R`, no other shuttle occupies it; **and** `TrafficController.TryReserveRoute` checks no other shuttle has a transit reservation on it. The full safety test is "no segment is double-booked by occupancy and no segment is double-booked by transit reservation."

Where this appears later: M5 will treat last-known occupancy as an obstacle until physically cleared (record 17 §4); auto-release on timeout is unsafe and is not done.

---

### 2) Fleet-side occupancy view

A **fleet-side occupancy view** is a simple map from shuttle id to the lane it occupies (its last known position). The fleet manager owns this view. It is not the same as the transit reservation table.

**Why the fleet needs it.** The shuttle reports `Position` in `ShuttleStatus` (the contract already has it). But until now the fleet manager kept no authoritative occupancy registry. The run loop needs an authoritative answer to: "which shuttle is in segment X right now?"

**M4 scope (now):**

- In-memory map: `shuttleId → occupiedLane` (last known). Update it when the shuttle's position changes.
- When gating a move, reject any route that intersects a segment occupied by a different shuttle.
- Idle/parked shuttles remain in the map at their position.
- No heartbeat/`lastSeen` expiry yet; no timeout-based release. This is conservative and safe (record 17 §4).

**M5 scope (deferred):**

- Heartbeats, `lastSeenAt`, timeout detection, alarms.
- Reconciliation on restart; operator-acknowledged recovery. A lost shuttle stays an obstacle until physically cleared; `Release` is post-recovery, never automatic on timeout (record 17 §4).

Real-world picture: the traffic controller at a warehouse has a whiteboard showing where each forklift is right now (last reported). If comms are spotty, you do not erase the forklift from the floor just because you have not heard from it in the last second — you assume it is still there until someone confirms it is clear.

This is internal state in the fleet manager process (in-memory for M4). It does not require a contract change; `Position` already exists in `ShuttleStatus`.

---

### 3) The run-loop gate

The **run loop** decides what to do next for each shuttle (assign work, start a move, etc.). The **gate** is: before assigning a new move to a shuttle, first ensure the route is safe (no occupancy conflict, no transit reservation conflict). Only then call `TaskDispatcher.AssignNextAsync`.

**The rule (from record 17 §3 + record 16 §5):**

1. Know the shuttle's current position `P` (from the occupancy view / last known position).
2. To move from `P` to `dest`, get the route via `track.Route(P, dest)`.
3. **Check occupancy:** for every segment `s` in the route, if any other shuttle occupies `s`, refuse.
4. **Check transit reservation:** call `TrafficController.TryReserveRoute(shuttleId, P, dest)`. It checks all segments are free of other transit reservations and atomically reserves the whole route (or nothing).
5. If both are clear, proceed: call `TaskDispatcher.AssignNextAsync(shuttleId, now, ct)`. The transit reservation remains held until the move's transit ends.
6. If refused, try another shuttle or wait; **do not** hold anything (record 16 §5). The dispatcher shape is unchanged.

**Crucial:** `TaskDispatcher` is **not** modified. It still takes `(shuttleId, now, ct)` and assigns the next queued task. The traffic decision is made **before** calling it. That matches record 15a §2.

**Worked example intuition.** Shuttle A is parked at `A01-L02` (occupied). Shuttle B wants to move through `A01-L02`. Occupancy check fails → route refused. Shuttle A finishes working and moves away; its position updates → occupancy changes; Shuttle B can try again later.

**What "release on departure" means in practice.** The transit reservation is the claim for the move. When the shuttle arrives at destination, the transit reservation is released (freeing the route) — but the shuttle now occupies the destination. The gate for the next move will check against current occupancy.

### Recap in plain words

- **Occupancy = physically here.** A shuttle at `P` occupies `P` even if idle/working. This blocks others from routing through `P`.
- **Transit reservation = may use this route for a move.** Granted by `TrafficController.TryReserveRoute`; released when the move's transit ends. A waiting shuttle holds nothing.
- **Fleet-side occupancy view** stores last-known position per shuttle (M4). No auto-timeout yet (M5). Treat a lost shuttle as an obstacle until cleared (record 17 §4).
- **Run-loop gate:** check occupancy + transit reservation **before** `TaskDispatcher.AssignNextAsync`. Dispatcher shape unchanged.

---

## Worked example (toy, not the assignment)

This toy shows the key idea: a parked shuttle blocks a route via occupancy; a transit reservation also blocks; after the shuttle moves or releases, the route becomes possible. It uses the **real** `Track`, `SegmentReservations`, and `TrafficController` from `src/`. Run in `/tmp/opencode/l11toy`, delete after. It is **not** the assignment. The assignment uses the real `OccupancyMap` and gate in `FleetManager`, with unit tests. Do not copy this into `src/`.

Everything below ran in `/tmp/opencode/l11toy`, compiled against the real `src/ShuttleCore` and `src/TrafficCore` source files, and was deleted afterwards. It is one console project. `L11Toy.csproj` (complete):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="/home/jamie/shuttle-sim/src/ShuttleCore/*.cs" />
    <Compile Include="/home/jamie/shuttle-sim/src/TrafficCore/*.cs" />
  </ItemGroup>

</Project>
```

`Program.cs` (complete):

```csharp
// Worked-example toy for lesson 11: occupancy + transit reservation gate.
// This is a TOY, clearly not the assignment. It compiles the real ShuttleCore
// and TrafficCore sources; nothing under src/ is changed.

using ShuttleCore;
using TrafficCore;

var rack = new Rack(1, 4, 1);
var track = new Track(rack);
var reservations = new SegmentReservations();
var traffic = new TrafficController(track, reservations);

// Toy occupancy map: shuttleId -> last known occupied lane.
var occupancy = new Dictionary<string, string>
{
    ["SH-A"] = "A01-L02",
    ["SH-B"] = "A01-L01",
};

bool GateAllows(string shuttleId, string from, string to)
{
    var route = track.Route(from, to);

    // 1) Occupancy check: no segment occupied by a different shuttle.
    foreach (var segment in route)
    {
        foreach (var (otherShuttle, occupiedLane) in occupancy)
        {
            if (otherShuttle != shuttleId && occupiedLane == segment)
            {
                Console.WriteLine($"  gate refuses: {segment} occupied by {otherShuttle}");
                return false;
            }
        }
    }

    // 2) Transit reservation check.
    var decision = traffic.TryReserveRoute(shuttleId, from, to);
    if (!decision.Granted)
    {
        Console.WriteLine($"  gate refuses: {decision.Reason}");
        return false;
    }

    Console.WriteLine($"  gate allows: route [{string.Join(",", route)}]");
    return true;
}

// ── Scenario 1: parked shuttle blocks a route through its lane ──
Console.WriteLine("=== Scenario 1: SH-A parked at A01-L02 ===");
Console.WriteLine($"occupancy: SH-A={occupancy["SH-A"]}, SH-B={occupancy["SH-B"]}");
Console.WriteLine("SH-B wants A01-L01 -> A01-L03 (passes through A01-L02):");
GateAllows("SH-B", "A01-L01", "A01-L03");

// ── Scenario 2: parked shuttle moves away, gate allows ──
occupancy["SH-A"] = "A01-L04";
Console.WriteLine();
Console.WriteLine("=== Scenario 2: SH-A moves to A01-L04 ===");
Console.WriteLine($"occupancy: SH-A={occupancy["SH-A"]}, SH-B={occupancy["SH-B"]}");
Console.WriteLine("SH-B wants A01-L01 -> A01-L03:");
GateAllows("SH-B", "A01-L01", "A01-L03");

// ── Scenario 3: transit reservation blocks ──
Console.WriteLine();
Console.WriteLine("=== Scenario 3: SH-B holds transit reservation ===");
Console.WriteLine("SH-B holds transit on A01-L01..A01-L03 (from scenario 2)");
Console.WriteLine("SH-A at A01-L04 wants A01-L02 -> A01-L04 (overlaps at A01-L03):");
GateAllows("SH-A", "A01-L02", "A01-L04");

// ── Scenario 4: release transit reservation, gate allows ──
traffic.Release("SH-B");
Console.WriteLine();
Console.WriteLine("=== Scenario 4: SH-B releases transit reservation ===");
Console.WriteLine("SH-A at A01-L04 wants A01-L02 -> A01-L04:");
GateAllows("SH-A", "A01-L02", "A01-L04");
```

Run it:

```bash
cd /tmp/opencode/l11toy
dotnet run
```

Observed output (exit 0):

```
=== Scenario 1: SH-A parked at A01-L02 ===
occupancy: SH-A=A01-L02, SH-B=A01-L01
SH-B wants A01-L01 -> A01-L03 (passes through A01-L02):
  gate refuses: A01-L02 occupied by SH-A

=== Scenario 2: SH-A moves to A01-L04 ===
occupancy: SH-A=A01-L04, SH-B=A01-L01
SH-B wants A01-L01 -> A01-L03:
  gate allows: route [A01-L01,A01-L02,A01-L03]

=== Scenario 3: SH-B holds transit reservation ===
SH-B holds transit on A01-L01..A01-L03 (from scenario 2)
SH-A at A01-L04 wants A01-L02 -> A01-L04 (overlaps at A01-L03):
  gate refuses: A01-L02 held by SH-B

=== Scenario 4: SH-B releases transit reservation ===
SH-A at A01-L04 wants A01-L02 -> A01-L04:
  gate allows: route [A01-L02,A01-L03,A01-L04]
```

Read the output.

- `gate refuses: A01-L02 occupied by SH-A` — SH-B's route passes through L02, which SH-A occupies. The gate refuses. This is the **occupancy** check firing.
- `gate allows: route [A01-L01,A01-L02,A01-L03]` — SH-A moved to L04. The route is clear. The gate allows and **reserves** the route for SH-B.
- `gate refuses: A01-L02 held by SH-B` — SH-A wants a route that overlaps SH-B's transit reservation. The gate refuses. This is the **transit reservation** check firing.
- `gate allows: route [A01-L02,A01-L03,A01-L04]` — SH-B released its transit reservation. The route is clear again.

Your assignment is this toy with the real names and the real plumbing: an `OccupancyMap` in `FleetManager`, a gate that checks occupancy and calls `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, and a unit test proving the gate.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. What is the difference between **occupancy** and a **transit reservation**?
2. Why must an idle/parked shuttle block routes through its lane (why is not "holds nothing" enough)?
3. When is a transit reservation released, and what happens to occupancy at the destination?
4. Why is auto-release on timeout unsafe (record 17 §4)?
5. The run loop puts `TrafficController.TryReserveRoute` **before** `TaskDispatcher.AssignNextAsync`. How does this preserve the dispatcher shape, and what does "safe" mean now?

---

## The assignment

Build the M4 run-loop gate: an in-memory `OccupancyMap` in `FleetManager`, and a gate that checks both occupancy and transit reservation before calling `TaskDispatcher.AssignNextAsync`. Prove it with a unit test. Do **not** change `TaskDispatcher`.

### Requirements

1. **Add the occupancy map: `src/FleetManager/OccupancyMap.cs`.**
   - `public sealed class OccupancyMap`.
   - `public string? GetOccupiedLane(string shuttleId)` — the last-known occupied lane, or `null` if unknown.
   - `public void SetOccupiedLane(string shuttleId, string lane)` — record the shuttle's current physical position.
   - `public bool IsOccupiedByOthers(IEnumerable<string> route, string requesterId)` — `true` if any segment in `route` is occupied by a shuttle other than `requesterId`.
   - The file must contain **no** hard-coded lane list, **no** I/O, **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It is a plain in-memory data structure.

2. **Add the gate as a pure helper: `src/FleetManager/RunLoopGate.cs`.**
   - `public sealed class RunLoopGate` with constructor `RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)`.
   - `public bool CanMove(string shuttleId, string destinationLane)` — returns `true` if the move may proceed.
   - It gets the shuttle's current position `P` from `occupancy.GetOccupiedLane(shuttleId)`. If `null`, the shuttle's position is unknown; refuse.
   - It gets the route via `track.Route(P, destinationLane)`.
   - It checks occupancy: if `occupancy.IsOccupiedByOthers(route, shuttleId)` is `true`, the gate refuses.
   - It checks transit reservation: if `traffic.TryReserveRoute(shuttleId, P, destinationLane)` is not granted, the gate refuses (and holds nothing).
   - If both are clear, the gate returns `true` (the caller may now call `TaskDispatcher.AssignNextAsync`).
   - The gate does **not** call `TaskDispatcher` itself — it only answers "may this move proceed?"
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It references `TrafficCore` and `ShuttleCore` for `Track` and `TrafficController`.

3. **Add the unit test: `src/FleetManager.Tests/RunLoopGateTests.cs`.**
   - Build a real `Track` from a `Rack` (for example `new Rack(1, 6, 1)`), a real `TrafficController`, and an `OccupancyMap`.
   - `Parked_shuttle_blocks_route_through_its_lane`: place shuttle A at a lane; attempt a route for shuttle B that passes through that lane; assert the gate refuses.
   - `Route_is_clear_after_occupied_shuttle_moves`: same setup; move shuttle A away; assert the gate now allows the route.
   - `Gate_allows_route_when_no_occupancy_conflict`: shuttle A at a lane not on shuttle B's route; assert the gate allows.
   - `Gate_refuses_when_transit_reservation_conflicts`: reserve a route for shuttle A; attempt an overlapping route for shuttle B; assert the gate refuses.
   - These tests are **pure**: no `Integration` trait, no database, no broker.

4. **Add project references.**
   - `src/FleetManager/FleetManager.csproj` gains a `ProjectReference` to `..\TrafficCore\TrafficCore.csproj`. `RunLoopGate` needs `TrafficController` and `Track`.
   - `src/FleetManager.Tests/FleetManager.Tests.csproj` gains `ProjectReference`s to `..\TrafficCore\TrafficCore.csproj` and `..\ShuttleCore\ShuttleCore.csproj`. The tests need `Track`, `Rack`, `SegmentReservations`, and `TrafficController`.

5. **Keep dispatcher shape intact.** `src/FleetManager/TaskDispatcher.cs` is unchanged. `AssignNextAsync` keeps its exact signature and behavior (record 15a §2). The gate is called **before** the dispatcher, never inside it.

6. **Release on departure semantics.** When a move's transit ends, call `TrafficController.Release(shuttleId)` to free the transit reservation. The destination remains occupied by physical position (update the occupancy map when the shuttle arrives). Document this in a comment or a short note in the test.

7. **Faulted/lost shuttle is an obstacle.** Do not auto-release on timeout. Last-known occupancy remains until recovery clears it (record 17 §4). No `lastSeenAt`, no timeout, no heartbeat logic.

8. **M5 items are not present.** No heartbeats, no `lastSeenAt`, no timeout detection, no alarms, no reconciliation persistence. The occupancy map is in-memory only.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the occupancy map and gate exist, with the pinned names
test -f src/FleetManager/OccupancyMap.cs || echo "missing OccupancyMap.cs"
test -f src/FleetManager/RunLoopGate.cs || echo "missing RunLoopGate.cs"
test -f src/FleetManager.Tests/RunLoopGateTests.cs || echo "missing RunLoopGateTests.cs"
grep -qE 'class OccupancyMap' src/FleetManager/OccupancyMap.cs
grep -qE 'class RunLoopGate' src/FleetManager/RunLoopGate.cs
for m in GetOccupiedLane SetOccupiedLane IsOccupiedByOthers; do grep -qE "\b$m\b" src/FleetManager/OccupancyMap.cs || echo "missing OccupancyMap member: $m"; done
for m in CanMove; do grep -qE "\b$m\b" src/FleetManager/RunLoopGate.cs || echo "missing RunLoopGate member: $m"; done
grep -qE 'OccupancyMap.*Track.*TrafficController' src/FleetManager/RunLoopGate.cs || echo "RunLoopGate must take OccupancyMap, Track, TrafficController"
```
```bash
# Phase 2 — purity: no clock, no ids, no I/O types in the new files
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs --include='*.cs'  # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres' src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs --include='*.cs'  # nothing
grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs --include='*.cs'  # nothing
```
```bash
# Phase 3 — project references are wired
grep -q 'TrafficCore' src/FleetManager/FleetManager.csproj || echo "FAIL: FleetManager must reference TrafficCore"
grep -q 'TrafficCore' src/FleetManager.Tests/FleetManager.Tests.csproj || echo "FAIL: FleetManager.Tests must reference TrafficCore"
grep -q 'ShuttleCore' src/FleetManager.Tests/FleetManager.Tests.csproj || echo "FAIL: FleetManager.Tests must reference ShuttleCore"
grep -c 'ProjectReference' src/FleetManager/FleetManager.csproj   # prints 5
grep -c 'ProjectReference' src/FleetManager.Tests/FleetManager.Tests.csproj   # prints 5
```
```bash
# Phase 4 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 83 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 83 tests"
grep -E 'FleetManager\.Tests\.dll.*Passed: +15,' "$T" && echo "FleetManager.Tests=15 OK" || echo "FAIL: FleetManager.Tests should show 15"
rm -f "$T"

# Phase 4b — the four new pure tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in Parked_shuttle_blocks_route_through_its_lane \
         Route_is_clear_after_occupied_shuttle_moves \
         Gate_allows_route_when_no_occupancy_conflict \
         Gate_refuses_when_transit_reservation_conflicts; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                            # nothing
rm -f "$L"
cd ..
```
```bash
# Phase 5 — no test side-file launches docker, and the gate tests are pure.
# Scan EVERY test project, not one file.
grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs' \
  && echo "FAIL: a test must not start docker" || echo "OK: no test starts docker"
grep -rn 'Trait' src/FleetManager.Tests/RunLoopGateTests.cs --include='*.cs'                         # nothing
grep -rniE 'Npgsql|Postgres' src/FleetManager.Tests/RunLoopGateTests.cs --include='*.cs'              # nothing
```
```bash
# Phase 6 — protected files unchanged, dispatcher shape intact, contract still v2
git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build \
  && echo "OK: dispatcher, Program.cs, contract and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v3.md
for m in AssignNextAsync StartAsync CompleteAsync FailAsync; do grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo "missing dispatcher method: $m"; done
```

**Proof details.** The assignment adds two product files (`OccupancyMap.cs`, `RunLoopGate.cs`), one test file, and project-reference edits; the pure test total goes from **79 to 83** (`FleetManager.Tests` gains four tests, 11 → 15). `FleetManager` gains one `ProjectReference` (4 → 5); `FleetManager.Tests` gains two (3 → 5). The gate must refuse a route through an occupied lane and allow it after the shuttle moves. If a reviewer makes the gate skip the occupancy check, `Parked_shuttle_blocks_route_through_its_lane` must go red; if the gate calls `TaskDispatcher` itself, the dispatcher-shape check fails; if the gate checks occupancy **after** `AssignNextAsync`, the ordering is wrong.

### Stretch goal

Prove the fault case in a test: place shuttle A at a lane; simulate a fault (do **not** call `Release`); assert that shuttle B's route through that lane is still refused. Comment that this is record 17 §4: a lost shuttle is an obstacle until physically cleared.

### Not in this assignment

- **Wiring the gate into a real `Program.cs` run loop** — a later lesson. The gate is proven by unit tests here.
- **Heartbeats, `lastSeenAt`, timeout detection, alarms, reconciliation** — M5.
- **Auto-release on fault** — unsafe and not done (record 17 §4).
- **WMS orders** — M6. **Dashboard** — M7.
- **Real shuttle movement, animation, or wire messages.** The contract stays v2.

---

## Closing

Read the spec's M4 line once more: *"Reserve lane segments so two shuttles never occupy the same space. Detect or avoid deadlock. Done when: a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

Lesson 10 built the pure traffic core. Lesson 11 closes the parked-shuttle gap: the fleet manager now knows where every shuttle physically is, and the run loop checks both occupancy and reservation before a move is granted. The dispatcher is untouched. The gate is the real M4 wiring.

Before you call this done, be ready to explain without notes:

- what occupancy is and why it is separate from a transit reservation;
- why an idle shuttle blocks a lane even though it holds no reservation;
- why the occupancy map lives in `FleetManager` and not in `TrafficCore` or `FleetCore`;
- why the gate sits before `AssignNextAsync` and not inside the dispatcher;
- what happens to a lost shuttle's occupancy, and why we never auto-release on timeout.

When the reviewer passes this, M4 has its run-loop gate. The next lesson wires the gate into a real run loop, or M5 adds heartbeats and recovery.
