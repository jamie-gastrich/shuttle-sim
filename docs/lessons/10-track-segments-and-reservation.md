# Lesson 10: The track as segments, the segment reservation, and the deadlock rule

**Milestone:** M4 (Traffic control) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 09: Three shuttles finish a queue; state survives a restart](09-three-shuttles-finish-queue.md) · **Decision:** [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md); builds on [record 05](../decisions/05-shuttle-core-and-rack-model.md), [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md) and [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md)

**Concepts this assignment requires:**

- [ ] **The track as segments** — cut the warehouse into small pieces of track, one piece at a time, so the software can name the space two shuttles might share. Taught in [The track as segments](#1-the-track-as-segments). Fixed by [record 16 §3](../decisions/16-traffic-segments-and-deadlock-rule.md).
- [ ] **The segment reservation** — a segment has at most one owner; reserve the whole route before you move, release it after. Taught in [The segment reservation](#2-the-segment-reservation). Fixed by [record 16 §2, §4](../decisions/16-traffic-segments-and-deadlock-rule.md).
- [ ] **The deadlock rule** — the four ingredients of a deadlock, and the one rule this project uses to break them. Taught in [The deadlock rule](#3-the-deadlock-rule). Fixed by [record 16 §5](../decisions/16-traffic-segments-and-deadlock-rule.md).

Three new concepts. That is the cap.

Four things this assignment needs are **assignment requirements, not concepts**. They change where code lives and how the tests run, not the ideas above:

- a new pure project `TrafficCore` and its test project `TrafficCore.Tests` ([record 16 §2](../decisions/16-traffic-segments-and-deadlock-rule.md));
- the track model added to the warehouse project `ShuttleCore` ([record 16 §3](../decisions/16-traffic-segments-and-deadlock-rule.md));
- the rule's numbers are derived from the model, not written into the code ([record 16 §3](../decisions/16-traffic-segments-and-deadlock-rule.md));
- the 4+ shuttle stress test, which is the M4 done-when at the decision level ([record 16 §1](../decisions/16-traffic-segments-and-deadlock-rule.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** wiring the traffic gate into a run loop in front of `TaskDispatcher.AssignNextAsync` (the next M4 lesson), reservation expiry and fault recovery (M5), WMS orders (M6), the dashboard (M7), real shuttle movement or animation, and any wire-contract change.

---

## Where this fits

M4's done-when requires: *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

Lesson 09 ended M3 with a dispatcher that hands tasks to shuttles. It has **no idea where a shuttle is**. Two shuttles could be told to use the same lane. M4 is the milestone that stops that. It has two halves:

1. **Decide who may use which space, and when.** That is today. It is pure decision logic, so it can be tested without a broker, a database, or a moving shuttle.
2. **Put that decision in front of the dispatcher.** The run loop asks the traffic controller for the route, and only then calls `TaskDispatcher.AssignNextAsync`. That is the next M4 lesson. The dispatcher's shape does not change ([record 15a §2](../decisions/15a-task-dispatcher-and-db-test-fixtures.md); review 09's End-product check).

```text
   run loop (next lesson)                     this lesson
   ──────────────────────                     ───────────
   need to move SH-01 to A01-L04?
        │
        ▼
   TrafficController.TryReserveRoute(…)  ────► Track.Route(…)          "what is the route?"
        │                                     SegmentReservations      "who holds each segment?"
        │  granted?
        ├── yes ─► TaskDispatcher.AssignNextAsync(…)   (M3, unchanged)
        └── no  ─► try another shuttle, or wait
```

**The honest weak spot.** There is **no running shuttle movement yet.** M3 finishes a queue logically; the run loop that turns a task into a `moveCommand` and drives a real shuttle is still deferred (review 09 "What later milestones will demand of this slice"). So today's traffic core decides collision-free routes and is proven by tests, but nothing yet breathes. That is fine and it is not throwaway work: the traffic core is exactly what the run loop will call, and the 4-shuttle stress test is the M4 done-when's collision and deadlock claim. Say this plainly to a reviewer rather than overclaiming a live traffic demo.

There is a second requirement from AGENTS.md: *"Decision logic is separated from input and output."* Traffic is decision logic. It must be pure — no broker, no database, no clock, no generated ids. That is why it lands in its own package-free project.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* Traffic has a temptation to cheat here: write a list of lanes, or a list of segment ids, into the code. Do not. The track is **derived from the rack**: `Track` takes a `Rack` and asks it for its lanes. One lane is one segment (record 16 §3), so a segment id is a lane id — no separate name table. There is no timeout or retry count in this slice; when M5 adds reservation expiry, that duration comes from configuration, not a literal.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-12**. The baseline build and suite, and the new-project shape, ran against a throwaway copy of `src/` at `/tmp/opencode/l10verify`; the toy ran at `/tmp/opencode/l10toy`. Both are deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | copy of `src/`, `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **7** `Passed!` lines, 0 `Failed!`; `22+18+4+8+2+11+5` = **70** tests |
| The rack builds every lane id from its dimensions and orders lanes by ordinal | read `src/ShuttleCore/Rack.cs` | `:27` builds `$"A{aisle:00}-L{lane:00}"`; `:45` `IsLane` is membership; `:50-51` `DistanceInLanes` is the ordinal difference |
| No lane id is hard-coded in the warehouse model | `grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/ShuttleCore --include='*.cs'` | no matches (exit 1) |
| The xUnit template versions match the existing test projects | `dotnet new xunit` in the copy | xunit 2.9.3, runner 3.1.4, test SDK 17.14.1, coverlet 6.0.4 — identical to `FleetManager.Tests` |
| A package-free library can reference `ShuttleCore` | `dotnet new classlib`; `dotnet add reference ShuttleCore/ShuttleCore.csproj` | one `ProjectReference`, no `PackageReference` |
| The solution currently has 14 projects | `grep -c '<Project Path' src/ShuttleFleet.slnx` | `14` |
| Adding `TrafficCore` + `TrafficCore.Tests` makes it 16 | `dotnet sln ShuttleFleet.slnx add …`; `grep -c '<Project Path'` | prints `16` |
| The new project adds an 8th `Passed!` line | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` in the copy | **8** `Passed!` lines (the template's one placeholder test ran) |
| An all-or-nothing rule drains a 4-shuttle scenario with zero shared segments | toy at `/tmp/opencode/l10toy`, `dotnet run` | 4 shuttles finish in 5 rounds; `collisions …: 0` |
| The same head-on case deadlocks under hold-and-wait | same toy, contrast section | step 2: each waits for a segment the other holds; no progress |

Not yet verified: `Track`, `SegmentReservations`, `TrafficController`, `TrafficDecision`, the three track tests, the six traffic tests, the run-loop gate, and the fresh container built with the two new projects — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for up to three options per real choice, compared, before a decision record picks one. There are three real choices in this slice.

### Survey 1: Where does traffic logic live? (→ [record 16 §2](../decisions/16-traffic-segments-and-deadlock-rule.md))

| | A: new pure `TrafficCore` (chosen) | B: inside `FleetCore` | C: a class inside `FleetManager` |
|---|---|---|---|
| Keeps `FleetCore` unaware of shuttles | yes | **no** — a reservation names a shuttle id | yes |
| Testable with no broker or DB | yes — a package-free library | yes, but the core grows a shuttle concept | yes, but the tests drag the app |
| Matches record 15 §3 (naming shuttles is the app layer) | yes | breaks it | yes |
| Matches the spec's separate "Traffic control" area | yes | no | partly |

**Decision 1: a new package-free `TrafficCore`, referencing `ShuttleCore` only.** `FleetCore` stays the pure task world. `FleetManager` will reference `TrafficCore` in the next lesson, when the run-loop gate is wired. Rejected: putting it in `FleetCore` (record 15 §3 forbids `FleetCore` learning about shuttles); a class in `FleetManager` (the spec lists Traffic as its own area, and a package-free library lets the compiler enforce "no broker, no DB").

### Survey 2: What is a segment? (→ [record 16 §3](../decisions/16-traffic-segments-and-deadlock-rule.md))

| | A: one lane = one segment (chosen) | B: fixed-length segments inside a lane | C: one slot = one segment |
|---|---|---|---|
| Answers "two shuttles never share space" | yes — a lane is what `Shuttle.Position` already is | yes, but the model has no within-lane position | yes, but a slot is storage, not track |
| Builds on the existing rack model | directly (`Rack` lanes, `DistanceInLanes`) | needs metres and offsets that do not exist | needs the shuttle to be at a slot, not a lane |
| Testable today | yes | needs a length input with no source | awkward |

**Decision 2: one lane is one segment, and a segment id is the lane id.** `Track.Route(from, to)` returns every segment between the two lanes, inclusive. Record 05 §4 deferred within-lane travel to M4, but the model still cannot say *where inside a lane* a shuttle is, so a whole lane is the smallest honest unit. Finer granularity is a local change to `Track.Route` later. Rejected: sub-lane segments (no geometry to place them) and slots-as-segments (wrong unit).

### Survey 3: Which deadlock rule? (→ [record 16 §5](../decisions/16-traffic-segments-and-deadlock-rule.md))

| | A: all-or-nothing whole route, no hold-and-wait (chosen) | B: incremental + cycle detection and rollback | C: one move at a time (global token) |
|---|---|---|---|
| Collisions impossible | yes — one owner per segment | yes | yes |
| Deadlock impossible | yes, by construction — a waiter holds nothing | yes, if the graph check or the rollback is correct | yes, trivially |
| Code and failure modes | one table, one atomic call | a waits-for graph, a cycle rule, and a way to move a victim back | a global lock; almost no concurrency |
| Throughput | medium | higher | very low |
| How real fleet systems do it | zone/segment reservation with deadlock **avoidance** is a common simple choice; "reserve a lane atomically to pass from one place to another" is a reported pattern | the literature also uses **detection and resolution** (roll a victim back) and reports it can wait less on long aisles | central scheduling exists, but usually as a planner, not a single global token |

**Decision 3: all-or-nothing whole-route reservation, and hold nothing while waiting.** It removes the *hold-and-wait* ingredient of deadlock, so deadlock cannot form. The spec allows "detect **or** avoid"; avoiding is simpler, has no rollback policy, and is easier to explain. Rejected: incremental reservation with cycle detection and rollback (more code, more failure modes); one move at a time (very low throughput, and it is not "reserve lane segments" as the spec words it).

---

## The lecture

Today we are going to learn about three things: **the track as segments**, **the segment reservation**, and **the deadlock rule**.

### 1) The track as segments

A **segment** is one small piece of track that only one shuttle may use at a time. The **track** is all the segments in order.

Real-world picture: a single-track railway. The track is cut into **blocks**. A train may enter a block only if no other train is in it. The signal box knows each block by a name, and it knows which blocks lie between two stations. Cutting the track into blocks is what makes "keep two trains apart" a rule a machine can follow.

Why do we need this now? Because "two shuttles must not use the same space" is not a rule until the software can name the small pieces of space. Today the model already has the right unit: a **lane**. A shuttle's `Position` is a lane (`A01-L03`), and distance is counted in lanes. So we reuse it. One lane is one segment, and the segment id is the lane id.

The existing warehouse model already gives us the lanes and their order. Read the real lines:

```csharp
// src/ShuttleCore/Rack.cs (already there)
var laneId = $"A{aisle:00}-L{lane:00}";                                  // :27
...
public bool IsLane(string laneId) => _laneOrdinals.ContainsKey(laneId);  // :45
public int DistanceInLanes(string fromLane, string toLane) =>
    Math.Abs(Ordinal(fromLane) - Ordinal(toLane));                       // :50-51
```

Line by line:

- `:27` builds every lane id from the rack's dimensions. There is no hand-written list of lanes; the rack knows them all. Our `Track` will ask the rack for that same list, so it cannot drift from the warehouse.
- `:45` answers "is this a real lane?" by looking it up, not by parsing the text. `Track` can use the same idea to reject an unknown lane.
- `:50-51` gives the **order**: the ordinal of a lane. `Track` uses that order to know which segments lie *between* two lanes.

Now the new type, in the warehouse project (`ShuttleCore`), shown as a shape — you write the body:

```csharp
// src/ShuttleCore/Track.cs — shape only; you write the body
namespace ShuttleCore;

public sealed class Track
{
    public Track(Rack rack);
    public IReadOnlyList<string> SegmentIds { get; }   // ordered; one per lane; the same strings as lanes
    public bool IsSegment(string segmentId);
    public IReadOnlyList<string> Route(string fromLane, string toLane);   // every segment between, inclusive
}
```

Line by line:

- `Track(Rack rack)` **derives** the segments from the rack. The file contains no lane list, so the track always matches the warehouse.
- `SegmentIds` is **ordered**. That order is what makes a route a continuous run of segments rather than a random bag.
- `Route(fromLane, toLane)` returns every segment from the lower lane to the higher lane, **inclusive of both ends**. A move from `A01-L03` to `A01-L05` returns `A01-L03, A01-L04, A01-L05`. A move to the same lane returns that one segment.
- `IsSegment` is just validation, used by `Route` so an unknown lane is rejected instead of silently ignored.

**Why the whole route and not just the destination?** A shuttle does not teleport. It passes *through* the segments between where it is and where it goes. If we reserved only the destination, two shuttles could meet in the middle. The route is the space the shuttle actually uses, so the route is what we reserve.

Grammar note, so you can defend it: *today* one lane is one segment. Record 05 §4 always expected M4 might want a finer unit ("a segment rather than a whole lane"). When the model can say where inside a lane the shuttle is, only `Track.Route` changes — it would return sub-lane segment ids. The reservation table and the rule do not change. That is the seam we are deliberately leaving.

Where you will meet this again: the traffic controller asks `Track.Route` to get the set of segments to reserve. The run loop (next lesson) asks before it sends a move. A finer track later changes only this method.

---

### 2) The segment reservation

A **reservation** means "this segment belongs to this shuttle right now". A segment has **at most one** owner. The **reservation table** is the single owner of that fact.

Real-world picture: the sign-in sheet for a set of meeting rooms. One row per room. You write your name; only one name fits per row. Before you book a room for a long meeting, you check it is free; when you leave, you cross your name out. Everyone reads the same sheet, so nobody disagrees about who has which room.

This is two rules from the project standards at once:

- fleet-control: *"Never let two shuttles claim the same lane segment at once."*
- AGENTS.md: *"protect shared state; prefer a single owner."* One table, one answer to "who is where". If two places tracked it, they would sooner or later disagree.

The reservation is **atomic and all-or-nothing**: the table reserves the *whole route in one step*, or it reserves nothing. That detail is both the safety rule and the deadlock rule (next section).

```csharp
// src/TrafficCore/SegmentReservations.cs — shape only; you write the body
namespace TrafficCore;

public sealed class SegmentReservations
{
    public bool TryReserveAll(string ownerId, IEnumerable<string> segmentIds);   // all, or none
    public void Release(string ownerId);                                          // free everything this owner holds
    public string? OwnerOf(string segmentId);
    public IReadOnlyCollection<string> SegmentsOwnedBy(string ownerId);
}
```

Line by line:

- `TryReserveAll` checks **every** requested segment first. If any segment has a *different* owner, it returns `false` and writes **nothing**. Otherwise it writes them all and returns `true`. The two passes are what make the grant atomic: if you wrote as you checked, a refusal halfway along would leave the earlier segments reserved — a half-held route, which is exactly what we must never create.
- `Release(ownerId)` frees every segment that owner holds, in one call. This is how a finished move releases its route, and this is the hook M5 will call when a shuttle is declared lost so a dead shuttle cannot block a lane forever.
- `OwnerOf(segmentId)` answers "who holds this segment?" — the question the tests and a future operator view ask.
- `SegmentsOwnedBy(ownerId)` answers "what does this shuttle hold?" — the read side of the table.

**The single-owner idea.** `SegmentReservations` holds one mapping, segment id → owner id. Only this class decides who holds what. The controller asks it; the run loop will ask it; the tests read it. Nothing else keeps a copy. That is why a collision is impossible rather than merely unlikely: `TryReserveAll` refuses when a segment already has another owner, so two shuttles can never be recorded on the same segment.

Where you will meet this again: `TrafficController` calls `TryReserveAll`; the run loop and M5 call `Release`. A dashboard (M7) can read `OwnerOf` and `SegmentsOwnedBy` to draw which lane is taken.

---

### 3) The deadlock rule

A **deadlock** is a standstill where every mover is waiting for something another mover holds, forever. It is not a crash — the system is alive and doing nothing.

A deadlock needs **all four** of these at the same time. These are the classic four ingredients:

1. **Mutual exclusion** — the resource is used by one at a time.
2. **Hold and wait** — a mover keeps what it holds and waits for more.
3. **No preemption** — nothing can be taken away; only the holder lets go.
4. **Circular wait** — the waiters form a circle, each waiting for the next.

Break any one, and deadlock cannot happen.

Real-world picture: two cars meet on a one-lane bridge. Each car is partway on the bridge (it **holds** the part behind it) and needs the middle, which the other car has (it **waits**). Neither can reverse (no preemption). They wait in a circle. Add a rule — "you may enter only if the whole bridge is clear, and otherwise you wait off the bridge" — and the circle cannot form.

Our four ingredients, and what we do with them:

| Ingredient | In our system | Do we keep it? |
|---|---|---|
| Mutual exclusion | one shuttle per segment | **keep** — this is safety |
| Hold and wait | a shuttle keeps segments and waits for more | **break it** |
| No preemption | only the holder releases | keep (it no longer matters) |
| Circular wait | waiters form a circle | cannot form once hold-and-wait is gone |

**The rule, named in [record 16 §5](../decisions/16-traffic-segments-and-deadlock-rule.md): "All-or-nothing whole-route reservation (no hold-and-wait)."**

- A shuttle asks to reserve the **whole route** from its current lane to its destination.
- The controller reserves **all** of it in one atomic step, or grants **nothing**.
- A refused shuttle waits **holding no segment**. It is not queued by the controller; the caller decides how to retry (next lesson).
- The reservation is held until the caller calls `Release` (the move ends; M5 calls it on a lost shuttle).

**Why this avoids deadlock, structurally.** A waiting shuttle holds nothing, so *hold and wait* is gone. And *circular wait* needs each waiter to hold something the next one needs — with nothing held by waiters, no circle can form. Deadlock needs all four ingredients, so deadlock is impossible. This is **avoidance**, not detection: there is no cycle detector, no timeout, and no "pick a victim" logic. It is a rule, not a hope.

```csharp
// src/TrafficCore/TrafficController.cs — shape only; you write the body
namespace TrafficCore;

using ShuttleCore;

public sealed class TrafficController
{
    public TrafficController(Track track, SegmentReservations reservations);
    public TrafficDecision TryReserveRoute(string shuttleId, string fromLane, string toLane);
    public void Release(string shuttleId);
}

// src/TrafficCore/TrafficDecision.cs
public sealed record TrafficDecision(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason);
```

Line by line:

- The controller **holds** the `Track` (what the route is) and the `SegmentReservations` (who holds what). It does not decide *which* shuttle gets a task — that is the caller's job (record 15). The controller only answers "yes" or "no" to a requested route.
- `TryReserveRoute(shuttleId, fromLane, toLane)` does exactly two things that matter: it asks `track.Route(fromLane, toLane)`, then asks `reservations.TryReserveAll(shuttleId, route)`. If the reservation is refused, it reports which segment and which shuttle blocked it (using `OwnerOf`) so the reason can be logged, and (in M5) raised as an alarm.
- `Release(shuttleId)` forwards to the table.
- `TrafficDecision` is a `record` — immutable value data, the dotnet-style choice for a small result. `Granted` is the answer, `Segments` is the route, and `Reason` is `null` on a grant.

The whole rule is those two steps: **get the route, reserve all of it or none.** Everything else is bookkeeping.

**The real trade-offs (be honest).**

- **Starvation, not deadlock.** Under heavy load, a long route can keep losing to short routes. The controller is fair only if the caller is. We accept this for a correct, explainable rule — the spec's non-goal says *"A correct, explainable approach beats a clever one"* — and note that a fairness rule or reservation expiry is the fix if it becomes a measured problem.
- **A dead shuttle blocks its route** until `Release` is called. M5's recovery is the caller that will do it; this lesson guarantees the hook exists.
- **Lower throughput.** Whole-route grants hold more space for longer than segment-by-segment would. That is the price of removing hold-and-wait completely.
- **It gates moves, not parked shuttles.** An idle shuttle holds nothing in this slice. The run-loop lesson says how a working shuttle's station is handled.

**The contrast, in one toy.** If a shuttle instead grabs one segment and then waits for the next (hold-and-wait), the *same* head-on case deadlocks. The worked example runs both and shows the difference in the output.

Where you will meet this again: the run loop asks `TryReserveRoute` **before** `TaskDispatcher.AssignNextAsync`, so the dispatcher only ever gets a task whose route is already safe. M5 calls `Release` when it declares a shuttle lost. M7 reads the table to draw taken lanes.

### Recap in plain words

- A **segment** is one lane's worth of track. A **route** is every segment between two lanes, inclusive. The track is **derived from the rack**, not typed in.
- A **reservation** gives a segment one owner. The **reservation table** is the single owner of that fact. You reserve the whole route or nothing.
- A **deadlock** needs all four ingredients. We **break hold-and-wait**: a waiting shuttle holds nothing, so no circle can form. The rule is written down in record 16.

---

## Worked example (toy, not the assignment)

A tiny toy shows the whole idea at a size you can hold in one glance: a real `Rack` gives the lanes, a toy route function cuts them into segments, a toy reservation table owns who holds what, and the all-or-nothing rule moves four shuttles with no collision and no deadlock. Then the same head-on case is shown deadlocking under hold-and-wait. It is **not** the assignment. The assignment uses the real `Track`, `SegmentReservations`, and `TrafficController`, in the new `TrafficCore` project, with xUnit tests and a 4+ shuttle stress test. Do not copy this into `src/`.

Everything below ran in `/tmp/opencode/l10toy`, compiled against the real `src/ShuttleCore` source files, and was deleted afterwards. It is one console project. `L10Toy.csproj` (complete):

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
  </ItemGroup>

</Project>
```

`Program.cs` (complete):

```csharp
using ShuttleCore;

// Worked-example toy for lesson 10: a track of segments, a segment reservation
// table, and the all-or-nothing traffic rule that avoids deadlock.
// This is a TOY, clearly not the assignment. It compiles the real ShuttleCore
// sources; nothing under src/ is changed.

// 1) The track. A real Rack gives the lanes; a segment is one lane's worth of
//    track, and a route is every segment between two lanes, inclusive.
var rack = new Rack(1, 6, 1);
var lanes = rack.LaneId.OrderBy(l => rack.DistanceInLanes("A01-L01", l)).ToArray();
Console.WriteLine($"track segments: {string.Join(" ", lanes)}");

IReadOnlyList<string> Route(string from, string to)
{
    var lo = Math.Min(Array.IndexOf(lanes, from), Array.IndexOf(lanes, to));
    var hi = Math.Max(Array.IndexOf(lanes, from), Array.IndexOf(lanes, to));
    return lanes[lo..(hi + 1)];
}

// 2) The single owner of "who holds which segment". A list per segment lets the
//    toy SHOW a collision if the rule is wrong (it should stay at one holder).
var bookings = new Dictionary<string, List<string>>();

bool TryReserve(string shuttle, IReadOnlyList<string> route)
{
    foreach (var segment in route)
        if (bookings.TryGetValue(segment, out var holders) && holders.Any(h => h != shuttle))
            return false;                                  // whole route refused, nothing taken
    foreach (var segment in route)
    {
        if (!bookings.TryGetValue(segment, out var holders))
            bookings[segment] = holders = new List<string>();
        if (!holders.Contains(shuttle)) holders.Add(shuttle);
    }
    return true;
}

void Release(string shuttle)
{
    foreach (var segment in bookings.Keys.ToList())
    {
        bookings[segment].Remove(shuttle);
        if (bookings[segment].Count == 0) bookings.Remove(segment);
    }
}

int Collisions() => bookings.Count(kv => kv.Value.Count > 1);

int RunAllOrNothing(string title, string[] shuttles,
    Dictionary<string, string> positions, Dictionary<string, string> destinations)
{
    Console.WriteLine($"--- {title} ---");
    bookings.Clear();
    var holders = new Dictionary<string, string>();
    var done = new HashSet<string>();
    var round = 0;
    while (done.Count < shuttles.Length && round < 30)
    {
        round++;
        foreach (var shuttle in shuttles)
            if (holders.Remove(shuttle, out var heldRoute))
            {
                done.Add(shuttle);
                Release(shuttle);
                Console.WriteLine($"  round {round}: {shuttle} arrived at {positions[shuttle]}, released [{heldRoute}]");
            }
        foreach (var shuttle in shuttles)
            if (!done.Contains(shuttle) && !holders.ContainsKey(shuttle))
            {
                var route = Route(positions[shuttle], destinations[shuttle]);
                if (TryReserve(shuttle, route))
                {
                    positions[shuttle] = destinations[shuttle];
                    holders[shuttle] = string.Join(",", route);
                    Console.WriteLine($"  round {round}: {shuttle} granted [{string.Join(",", route)}]");
                }
            }
    }
    var collisions = Collisions();
    Console.WriteLine($"collisions (segments with more than one holder): {collisions}");
    Console.WriteLine($"all {done.Count} shuttles finished in {round} rounds");
    return collisions;
}

// 3) Four shuttles, whole route or wait.
RunAllOrNothing(
    "all-or-nothing: 4 shuttles, whole route or wait",
    new[] { "SH-01", "SH-02", "SH-03", "SH-04" },
    new Dictionary<string, string>
    {
        ["SH-01"] = "A01-L01", ["SH-02"] = "A01-L06",
        ["SH-03"] = "A01-L02", ["SH-04"] = "A01-L05",
    },
    new Dictionary<string, string>
    {
        ["SH-01"] = "A01-L04", ["SH-02"] = "A01-L03",
        ["SH-03"] = "A01-L05", ["SH-04"] = "A01-L02",
    });

// 4) Head-on: opposite directions on one track. One wins the whole route, the
//    other waits holding nothing, then goes. No deadlock.
RunAllOrNothing(
    "head-on the safe way: two shuttles, opposite directions",
    new[] { "SH-A", "SH-B" },
    new Dictionary<string, string> { ["SH-A"] = "A01-L01", ["SH-B"] = "A01-L04" },
    new Dictionary<string, string> { ["SH-A"] = "A01-L04", ["SH-B"] = "A01-L01" });

// 5) The unsafe rule, for contrast: take a piece now, then wait for the next.
//    This is hold-and-wait, and it deadlocks on the same head-on case.
Console.WriteLine("--- the unsafe way: hold a piece, then wait for the next (hold-and-wait) ---");
var held = new Dictionary<string, List<string>>
{
    ["SH-A"] = new List<string> { "A01-L01" },
    ["SH-B"] = new List<string> { "A01-L04" },
};
var target = new Dictionary<string, string> { ["SH-A"] = "A01-L04", ["SH-B"] = "A01-L01" };
for (var step = 1; step <= 6; step++)
{
    var movedAny = false;
    foreach (var shuttle in new[] { "SH-A", "SH-B" })
    {
        var current = held[shuttle][^1];
        var direction = Math.Sign(Array.IndexOf(lanes, target[shuttle]) - Array.IndexOf(lanes, current));
        if (direction == 0) continue;
        var next = lanes[Array.IndexOf(lanes, current) + direction];
        var blocker = held.FirstOrDefault(kv => kv.Key != shuttle && kv.Value.Contains(next));
        if (blocker.Key is null)
        {
            held[shuttle].Add(next);
            Console.WriteLine($"  step {step}: {shuttle} takes free segment {next} -> holds [{string.Join(",", held[shuttle])}]");
            movedAny = true;
        }
        else
        {
            Console.WriteLine($"  step {step}: {shuttle} wants {next} (held by {blocker.Key}) -> waits, still holding [{string.Join(",", held[shuttle])}]");
        }
    }
    if (!movedAny)
    {
        Console.WriteLine("  deadlock: each shuttle waits for a segment the other holds; neither can release");
        break;
    }
}
```

Run it:

```bash
cd /tmp/opencode/l10toy
dotnet run
```

Observed output (exit 0):

```
track segments: A01-L01 A01-L02 A01-L03 A01-L04 A01-L05 A01-L06
--- all-or-nothing: 4 shuttles, whole route or wait ---
  round 1: SH-01 granted [A01-L01,A01-L02,A01-L03,A01-L04]
  round 2: SH-01 arrived at A01-L04, released [A01-L01,A01-L02,A01-L03,A01-L04]
  round 2: SH-02 granted [A01-L03,A01-L04,A01-L05,A01-L06]
  round 3: SH-02 arrived at A01-L03, released [A01-L03,A01-L04,A01-L05,A01-L06]
  round 3: SH-03 granted [A01-L02,A01-L03,A01-L04,A01-L05]
  round 4: SH-03 arrived at A01-L05, released [A01-L02,A01-L03,A01-L04,A01-L05]
  round 4: SH-04 granted [A01-L02,A01-L03,A01-L04,A01-L05]
  round 5: SH-04 arrived at A01-L02, released [A01-L02,A01-L03,A01-L04,A01-L05]
collisions (segments with more than one holder): 0
all 4 shuttles finished in 5 rounds
--- head-on the safe way: two shuttles, opposite directions ---
  round 1: SH-A granted [A01-L01,A01-L02,A01-L03,A01-L04]
  round 2: SH-A arrived at A01-L04, released [A01-L01,A01-L02,A01-L03,A01-L04]
  round 2: SH-B granted [A01-L01,A01-L02,A01-L03,A01-L04]
  round 3: SH-B arrived at A01-L01, released [A01-L01,A01-L02,A01-L03,A01-L04]
collisions (segments with more than one holder): 0
all 2 shuttles finished in 3 rounds
--- the unsafe way: hold a piece, then wait for the next (hold-and-wait) ---
  step 1: SH-A takes free segment A01-L02 -> holds [A01-L01,A01-L02]
  step 1: SH-B takes free segment A01-L03 -> holds [A01-L04,A01-L03]
  step 2: SH-A wants A01-L03 (held by SH-B) -> waits, still holding [A01-L01,A01-L02]
  step 2: SH-B wants A01-L02 (held by SH-A) -> waits, still holding [A01-L04,A01-L03]
  deadlock: each shuttle waits for a segment the other holds; neither can release
```

Read the output.

- `track segments: A01-L01 … A01-L06` — the toy built the segments from a real `Rack(1, 6, 1)`, not from a list in the code. That is the "derived from the model" rule in one line.
- `round 1: SH-01 granted [A01-L01,A01-L02,A01-L03,A01-L04]` — SH-01 got its **whole** route in one grant. Notice SH-02, SH-03 and SH-04 got nothing that round; their routes overlap SH-01's, so `TryReserve` returned `false` and wrote nothing. That is all-or-nothing.
- `collisions (segments with more than one holder): 0` — the toy keeps a *list* of holders per segment, so it could print a number greater than zero if the rule were wrong. It prints zero on every scenario. That is "no collisions".
- `all 4 shuttles finished in 5 rounds` — every shuttle eventually moved. Nobody was stuck forever. That is "no permanent deadlock" at this size.
- `round 2: SH-01 arrived … released …` then `round 2: SH-02 granted …` — the safe head-on case: one shuttle takes the whole track, finishes, releases, and then the other goes. No standstill.
- The last block is the **contrast**. Under hold-and-wait, `SH-A` takes `A01-L02` and `SH-B` takes `A01-L03`, then each wants the other's segment. `step 2` shows both waiting while still holding. The next line is the deadlock. Same shuttle problem, same track — only the rule changed.

Your assignment is this toy with the real names and the real plumbing: a `Track` in `ShuttleCore`, a `SegmentReservations` and a `TrafficController` in the new `TrafficCore` project, and xUnit tests including a 4+ shuttle stress test. You do **not** wire it into a run loop in this lesson.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. What is a segment in this project, and where does the list of segments come from? Why must `Track` take a `Rack` instead of a hard-coded array of lane ids?
2. Why does `Route(from, to)` include every segment between the two lanes, and not just the destination? What goes wrong if you reserve only the destination?
3. `TryReserveAll` checks every segment and *then* writes. What bug would appear if it wrote each segment inside the same loop it was checking? Tie your answer to "a waiting shuttle holds nothing".
4. Name the four ingredients of a deadlock. Which one does the all-or-nothing rule remove, and why can a circular wait not form once it is gone? Is this detection or avoidance?
5. In the toy, why does the safe head-on case finish but the hold-and-wait case deadlock? Walk the two `step 1` lines and the two `step 2` lines.
6. Why does traffic logic live in a package-free `TrafficCore` and not in `FleetCore`? Which decision record forbids a reservation in `FleetCore`, and what would `FleetCore` have to learn about the world?
7. The rule can starve a long route under heavy load. Why is that acceptable for M4, and what are the two named fixes if it becomes a measured problem?
8. What two things does `TrafficController.TryReserveRoute` actually do, and why is it the run loop's job — not the controller's — to decide which shuttle gets a task and what to do when a request is refused?

---

## The assignment

Build the M4 traffic core as pure decision logic: a `Track` in the warehouse model, a segment reservation table, and a `TrafficController` whose rule is all-or-nothing whole-route reservation. Prove it with a 4+ shuttle stress test. Do **not** wire it into a run loop.

### Requirements

1. **Add the track model to the warehouse project: `src/ShuttleCore/Track.cs`.**
   - `public sealed class Track` with constructor `Track(Rack rack)`.
   - `public IReadOnlyList<string> SegmentIds { get; }` — ordered, one per lane, and **the segment id is the lane id** (record 16 §3).
   - `public bool IsSegment(string segmentId)`.
   - `public IReadOnlyList<string> Route(string fromLane, string toLane)` — every segment from the lower lane to the higher lane, **inclusive of both ends**; a same-lane route returns that one segment; an unknown lane throws `ArgumentException`.
   - The file must contain **no** hard-coded lane list (no literal like `"A01-L01"`) and **no** I/O. It derives everything from the `Rack`.
   - Tests: `src/ShuttleCore.Tests/TrackTests.cs` with these three `[Fact]`s, each with an `Assert`:
     - `Route_between_a_lane_and_itself_is_that_single_segment`
     - `Route_covers_every_segment_between_two_lanes_in_order`
     - `Route_to_an_unknown_lane_is_rejected`

2. **Add the pure traffic project: `src/TrafficCore/` — a package-free class library referencing `ShuttleCore` only.**
   - `src/TrafficCore/SegmentReservations.cs`:
     - `public bool TryReserveAll(string ownerId, IEnumerable<string> segmentIds)` — check every segment first; if any has a different owner, return `false` and write nothing; otherwise write them all and return `true`.
     - `public void Release(string ownerId)` — free every segment that owner holds.
     - `public string? OwnerOf(string segmentId)`.
     - `public IReadOnlyCollection<string> SegmentsOwnedBy(string ownerId)`.
   - `src/TrafficCore/TrafficController.cs`:
     - constructor `TrafficController(Track track, SegmentReservations reservations)`;
     - `public TrafficDecision TryReserveRoute(string shuttleId, string fromLane, string toLane)` — get `track.Route(fromLane, toLane)`, call `reservations.TryReserveAll(shuttleId, route)`, and return a decision (on refusal, name the blocking segment and its owner);
     - `public void Release(string shuttleId)`.
   - `src/TrafficCore/TrafficDecision.cs`: `public sealed record TrafficDecision(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason);`
   - The project must contain **no** `Npgsql`, `MQTTnet`, `Messaging`, `Contracts`, `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, or literal lane id, and **no** `PackageReference`. The rule is all-or-nothing (record 16 §5): the controller must call the atomic `TryReserveAll`, never a partial write.

3. **Add the pure tests: `src/TrafficCore.Tests/` — an xUnit test project referencing `TrafficCore`.**
   - Delete the template's placeholder `UnitTest1.cs`, so the project contains exactly the six tests below.
   - `src/TrafficCore.Tests/TrafficControllerTests.cs` with these six `[Fact]`s, each with an `Assert`:
     - `TryReserveRoute_grants_the_whole_route_when_every_segment_is_free`
     - `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken`
     - `Release_frees_every_segment_the_owner_held`
     - `Two_shuttles_cannot_hold_the_same_segment`
     - `Head_on_requests_resolve_without_deadlock`
     - `Four_or_more_shuttles_never_share_a_segment_and_all_finish`
   - These tests are **pure**: no `Integration` trait, no database, no broker. Build the track from a real `Rack` (for example `new Rack(1, 8, 1)`), never from a literal lane list.
   - The stress test is the M4 done-when at the decision level. It must: build a track of at least four shuttles with overlapping routes; loop granting any pending request whose whole route is free and then completing and releasing a granted move; after **every** round assert that no segment has more than one owner; and assert every shuttle finishes within a bounded number of rounds (so a deadlock would fail the assertion rather than hang). Use a fixed base time only if you need a time; never `DateTime.UtcNow`.

4. **Add both projects to the solution.** `dotnet new classlib` and `dotnet new xunit`, add the `ShuttleCore` reference to `TrafficCore`, then `dotnet sln src/ShuttleFleet.slnx add src/TrafficCore/TrafficCore.csproj src/TrafficCore.Tests/TrafficCore.Tests.csproj`. The solution goes from 14 to 16 projects.

5. **Keep purity.** `ShuttleCore` gains only `Track` and stays free of `Npgsql`, `MQTTnet`, `Messaging`, and `Contracts`. `TrafficCore` is package-free. `TrafficCore.Tests` has no `Integration` trait and no database reference.

6. **Do not change the dispatcher, `Program.cs`, the contract, or the Dockerfile.** `TaskDispatcher.cs` keeps its exact shape (record 15a §2). The run-loop gate that calls `TrafficController` before `AssignNextAsync` is the **next** M4 lesson; do not wire it here. The contract stays v2 with no `v3.md`.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the track and the traffic core exist, with the pinned names
test -f src/ShuttleCore/Track.cs || echo "missing Track.cs"
test -f src/TrafficCore/SegmentReservations.cs || echo "missing SegmentReservations.cs"
test -f src/TrafficCore/TrafficController.cs || echo "missing TrafficController.cs"
test -f src/TrafficCore/TrafficDecision.cs || echo "missing TrafficDecision.cs"
grep -qE 'class Track' src/ShuttleCore/Track.cs
grep -qE 'class SegmentReservations' src/TrafficCore/SegmentReservations.cs
grep -qE 'class TrafficController' src/TrafficCore/TrafficController.cs
grep -qE 'record TrafficDecision' src/TrafficCore/TrafficDecision.cs
for m in Route SegmentIds IsSegment; do grep -qE "\b$m\b" src/ShuttleCore/Track.cs || echo "missing Track member: $m"; done
for m in TryReserveAll Release OwnerOf SegmentsOwnedBy; do grep -qE "\b$m\b" src/TrafficCore/SegmentReservations.cs || echo "missing reservations member: $m"; done
for m in TryReserveRoute Release; do grep -qE "\b$m\b" src/TrafficCore/TrafficController.cs || echo "missing controller member: $m"; done
grep -qE 'Track\(Rack' src/ShuttleCore/Track.cs || echo "Track must be built from a Rack"
grep -qE 'TryReserveAll' src/TrafficCore/TrafficController.cs || echo "controller must call the atomic TryReserveAll"
```
```bash
# Phase 2 — purity, and no magic lane list (scan the whole project, not one file)
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/TrafficCore --include='*.cs'            # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres' src/TrafficCore --include='*.cs' --include='*.csproj' # nothing
grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/TrafficCore --include='*.cs'                                              # nothing
grep -rniE 'Npgsql|MQTTnet|Postgres|Messaging|Contracts' src/ShuttleCore --include='*.cs' --include='*.csproj' # nothing
grep -c 'PackageReference' src/TrafficCore/TrafficCore.csproj   # prints 0
grep -c 'ProjectReference' src/TrafficCore/TrafficCore.csproj   # prints 1
```
```bash
# Phase 3 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired: review 09 L2.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 79 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 79 tests"
grep -E 'TrafficCore\.Tests\.dll.*Passed: +6,' "$T"  && echo "TrafficCore.Tests=6 OK"  || echo "FAIL: TrafficCore.Tests should show 6"
grep -E 'ShuttleCore\.Tests\.dll.*Passed: +25,' "$T" && echo "ShuttleCore.Tests=25 OK" || echo "FAIL: ShuttleCore.Tests should show 25"
rm -f "$T"

# Phase 3b — the nine new pure tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in Route_between_a_lane_and_itself_is_that_single_segment \
         Route_covers_every_segment_between_two_lanes_in_order \
         Route_to_an_unknown_lane_is_rejected \
         TryReserveRoute_grants_the_whole_route_when_every_segment_is_free \
         TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken \
         Release_frees_every_segment_the_owner_held \
         Two_shuttles_cannot_hold_the_same_segment \
         Head_on_requests_resolve_without_deadlock \
         Four_or_more_shuttles_never_share_a_segment_and_all_finish; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                            # nothing
rm -f "$L"
cd ..
```
```bash
# Phase 4 — no test side-file launches docker, and the traffic tests are pure.
# Scan EVERY test project, not one file: review 09 L1.
grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs' \
  && echo "FAIL: a test must not start docker" || echo "OK: no test starts docker"
grep -rn 'Trait' src/TrafficCore.Tests --include='*.cs'                                                       # nothing
grep -rniE 'Npgsql|Postgres' src/TrafficCore.Tests --include='*.cs' --include='*.csproj'                      # nothing
```
```bash
# Phase 5 — solution and project graph
grep -c '<Project Path' src/ShuttleFleet.slnx     # prints 16
grep -q 'TrafficCore' src/ShuttleFleet.slnx
grep -q 'TrafficCore.Tests' src/ShuttleFleet.slnx
grep -q 'ShuttleCore' src/TrafficCore/TrafficCore.csproj
```
```bash
# Phase 6 — protected files unchanged, contract still v2, dispatcher shape intact
git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build \
  && echo "OK: dispatcher, Program.cs, contract and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -qE '^Version: 2$' docs/contract/v2.md
grep -qE '^Version: 1$' docs/contract/v1.md
test ! -f docs/contract/v3.md
for m in AssignNextAsync StartAsync CompleteAsync FailAsync; do grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo "missing dispatcher method: $m"; done
```

**Proof details.** The assignment adds two projects (`TrafficCore`, `TrafficCore.Tests`) and six test-side/product files plus the solution edit; the solution goes from **14 to 16** projects, and the pure suite from **7 to 8** `Passed!` lines. The pure test total goes from **70 to 79**: `ShuttleCore.Tests` gains three track tests (22 → 25) and `TrafficCore.Tests` adds six (0 → 6). `TrafficCore` is package-free with exactly one `ProjectReference` (`ShuttleCore`). The stress test must show zero segments with more than one owner and must finish every shuttle within a bounded round count. If a reviewer makes `TryReserveAll` write as it checks (so a refused route leaves a partial hold), `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken` must go red; if the controller is changed to grant each segment separately, the head-on test must go red; if `Track.Route` stops including an endpoint, the route tests must go red. Do **not** wire the gate into a run loop in this lesson.

### Gate hardening (review 09 L1/L2)

Review 09 found two lesson-gate defects. This lesson fixes the pattern:

- **L1 — scan the whole surface, not one file.** Review 09's no-docker grep only scanned `EndToEndTests.cs`, so a docker launch in the new `TestDatabase.cs` would have passed. Phase 4 above scans **`src/*.Tests`**, every test project. Phase 2 scans the **whole** `TrafficCore` project for clock, ids, I/O types, and literal lane ids.
- **L2 — gate the argument and the counts, not word presence.** Review 09 checked `grep -qE 'GetAsync'` (any occurrence). Here Phase 1 checks that the controller actually calls the atomic `TryReserveAll` (the rule, not a word), and that `Track` is built from a `Rack` (derived, not typed in). Phase 3 gates the exact per-assembly counts (`TrafficCore.Tests` = 6, `ShuttleCore.Tests` = 25), the assembly count (8), the total (79), and RC 0 with zero `Failed!`, so a regression that silently drops tests while staying green is caught. Phase 3b anchors test names with `\.$t$`.

### Stretch goal

Prove the M5 hook. Add a test `Release_faulted_shuttle_frees_its_segments`: reserve a route for `SH-01`; attempt an overlapping route for `SH-02` and confirm it is refused; call `TrafficController.Release("SH-01")` (the call M5's recovery will make); assert `SegmentReservations.SegmentsOwnedBy("SH-01")` is empty and the `SH-02` route is now granted. Keep every other suite green.

### Not in this assignment

- **Wiring the traffic gate into a run loop before `TaskDispatcher.AssignNextAsync`** — the next M4 lesson. The dispatcher's shape does not change.
- **Reservation expiry, fault recovery, alarms** — M5. `Release` is the hook; nothing expires yet.
- **Finer segment granularity (within-lane travel)** — later; only `Track.Route` would change.
- **WMS orders** — M6. **Dashboard** — M7.
- **Real shuttle movement, animation, or wire messages.** The contract stays v2.

---

## Closing

Read the spec's M4 line once more: *"Reserve lane segments so two shuttles never occupy the same space. Detect or avoid deadlock. Done when: a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

This lesson covers every clause except the run-loop wiring. The track is cut into segments derived from the rack. A single reservation table owns "who holds which segment". The rule is **all-or-nothing whole-route reservation (no hold-and-wait)**, named and explained in [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md). The pure stress test is the "four or more shuttles, no collisions, no permanent deadlock" claim at the decision level.

Before you call this done, be ready to explain without notes:

- what a segment is and why the track is built from the `Rack`, not typed in;
- why a route is every segment between two lanes, inclusive;
- why `TryReserveAll` checks all segments before writing any of them;
- the four ingredients of a deadlock, which one the rule removes, and why that makes deadlock impossible;
- why traffic lives in a package-free `TrafficCore` and not in `FleetCore`;
- what the rule costs (starvation, a blocked dead shuttle, lower throughput) and the named fixes.

When the reviewer passes this, M4 has its pure traffic core. Next comes the run loop that asks `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, so the dispatcher only ever hands out a route that is already safe.
