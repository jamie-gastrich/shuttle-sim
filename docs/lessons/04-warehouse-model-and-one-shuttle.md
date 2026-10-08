# Lesson 04: The warehouse model and one shuttle

**Milestone:** M1 (Warehouse model and one shuttle) · **Tooling:** .NET 10 ·
**Prerequisite:** [Lesson 03: Docker services and the fresh-container build](03-docker-services.md)

**Concepts this assignment requires:**

- [ ] **The rack as data** — a warehouse is a hierarchy (aisles → lanes → slots), a slot's
      identity is a string the contract already speaks, and occupancy is an invariant you can
      test: a pallet is in exactly one slot, or on the shuttle, never both. Taught in
      [The rack is data](#the-rack-is-data). Used by `src/ShuttleCore/Rack.cs` (this
      assignment) and by the display the dashboard will draw at M7.
- [ ] **The explicit state machine** — a small set of named states, a table of allowed
      transitions, guarded methods that reject everything else with a specific exception. Taught
      in [The state machine is the shuttle](#the-state-machine-is-the-shuttle). Used by
      `src/ShuttleCore/Shuttle.cs` (this assignment) and by every status message after it.
- [ ] **Time is an input** — durations are injected, "now" is passed into every event, and the
      code never waits. That is what makes a 17-second scenario testable in milliseconds. Taught
      in [Time is an input](#time-is-an-input). Used by `ShuttleTiming` and all seven events.

Three new concepts. That is the cap.

Three words for later, not today: **segment** (the piece of a lane two shuttles must not share —
M4's traffic record), **heartbeat** (a periodic status that proves a shuttle is alive — M5),
**task** (the fleet manager's unit of work — M3). All three belong to records linked in
[Options survey](#options-survey).

---

## Where this fits

Lesson 01 built the frame, lesson 02 the first message, lesson 03 the environment that proves a
build is fresh. All of that was runway. This is the first **milestone lesson**: M1 spends the
runway on the actual warehouse. The spec says:

> **M1: Warehouse model and one shuttle.** A rack model (lanes, slots, pallets). A single
> simulated shuttle with an explicit state machine (idle, moving, loading, unloading, fault) and
> simple timing. Driven by local calls only, with no network yet.
> *Done when: a scripted sequence moves a pallet from one slot to another, and every state
> transition is covered by a test.*

Two halves, and a boundary between them.

1. **The warehouse.** A rack you can build from its dimensions, ask about, put pallets into, and
   take pallets out of. Nothing moves yet; this is the map.
2. **The shuttle.** One vehicle that loads a pallet in one lane, drives to another lane, and
   unloads into a slot — reporting a state and a position the whole way, in the vocabulary the
   contract already fixed.

3. **The boundary: no network.** *"Driven by local calls only."* The events are ordinary method
   calls in ordinary C#. M2's record already named the broker; wiring the shuttle to it is M2's
   lesson. Keeping M1 local is not a shortcut — it is what lets you test the machine without a
   broker, a database, or a stopwatch.

Here is why the "done when" is two clauses. **"A scripted sequence moves a pallet from one slot
to another"** is end-to-end truth: the pallet really ends up in the new slot, and only there.
**"Every state transition is covered by a test"** is exhaustive truth: not "the happy path
works", but each edge of the state graph is walked, including the ones that reject. A sequence
alone can pass while half the machine is broken; a transition test alone can pass while the
pieces never actually move a pallet. You need both.

### Check what you already know

Answer these out loud before you read on. Treat anything you hesitate on as new material.

1. The contract's `Position` field is *"the rack lane the shuttle is in, e.g. `A02-L04`."* If a
   shuttle is halfway between lane `A01-L01` and `A01-L02`, what lane is it in — and what should
   its status report during the move?
2. Lesson 02 taught that the contract is the source of truth and changing it needs a record and
   a version bump. The five states this lesson uses are already written into `ShuttleStatus`.
   What does that tell you about who decides the state *names*, and where the lowercase wire
   spellings come from?
3. Suppose the shuttle's move used `Thread.Sleep(15000)` for a three-lane trip. How long would
   the suite take once you add ten such tests? What could a test never do while the code sleeps —
   and what does lesson 03's fresh-container gate turn that into?

---

## Verification table

Everything below was checked by running a command on **2026-10-08**. The M1 reference shape
(rack, state machine, tests, demo) and the full acceptance block ran in
`/tmp/opencode/lesson04-scratch/`, a throwaway copy of this repo; the worked-example toy ran in
`/tmp/opencode/lesson04-toy/`. Both were deleted afterwards, along with the mutation copies and
the scratch image. On this repo I ran read-only `find` and `grep`; no experiment touched `src/`.
The fresh-container row built inside Docker and was removed.

| Claim | How I checked | Result |
|---|---|---|
| Installed tools | `dotnet --version`; `docker info --format '{{.ServerVersion}}'` | `10.0.112`; daemon `29.8.1` |
| The reference shape has eight projects and ten edges | `grep -c '<Project Path' src/ShuttleFleet.slnx`; `grep -rn ProjectReference … \| wc -l` | prints 8; prints 10 |
| `ShuttleCore` references nothing | `grep ProjectReference src/ShuttleCore/ShuttleCore.csproj` | exit 1 (none) |
| No source file is duplicated across projects | `md5sum` over all non-`bin`/`obj` `.cs`, `uniq -w32 -D` | prints nothing |
| The reference compiles clean | `dotnet build` from `src/` | `0 Warning(s) 0 Error(s)` |
| The suite is green and all four test projects ran | `dotnet test`; `grep -c 'Passed!'`; `grep -c 'No test is available'` | exit 0; prints 4; prints 0 |
| Test counts | the four `Passed!` lines | ShuttleCore.Tests **22**, Contracts.Tests 2, FleetManager.Tests 1, ShuttleSim.Tests 1 |
| Every state transition is covered by name | `dotnet test --list-tests` + anchored greps for the ten names | all ten present, all passed |
| Rejections are real, not decoration | six named rejection tests; `Assert.Throws<ShuttleRejectedException>` occurrences | 7 assertions, all passed |
| The scripted sequence moves one pallet, once | `Scripted_sequence_moves_a_pallet_from_one_slot_to_another` | passed: `PLT-001` now in `A01-L02-S02`, old slot empty |
| No waiting and no duration literal in the library | `grep -rnE 'Thread\.Sleep\|Task\.Delay' src`; `grep -rnE 'TimeSpan\.(From…)\|new TimeSpan\(' src/ShuttleCore` | both print nothing |
| No service or address vocabulary leaked into code | `grep -rniE 'mqtt\|broker\|localhost\|127\.0\.0\.1\|:1883\|:5432\|postgres\|Host=' src` | prints nothing |
| The demo prints seven `ShuttleStatus` lines | `dotnet run --project ShuttleSim`; `grep -c '"ShuttleId"'` | exit 0; prints 7 |
| The move honours the distance (three lanes × 5 s) | demo timestamps | `00:00:02Z` leaving, `00:00:17Z` arrived |
| Position changes on arrival, not on departure | line 4 of the demo output | `"State":"moving"` with `"Position":"A01-L01"` |
| The fresh container still builds and tests everything | `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .`; `docker run --rm shuttle-fleet-build`; `grep -c 'Passed!'` | build exit 0; run exit 0; prints 4 |
| The contract is unchanged | the lesson-02 version/section/field greps | all pass |
| The acceptance block can fail, not just pass | 12 mutations, one realistic break each | 12 of 12 caught (table below) |
| Two gate defects found and fixed while auditing | the first block draft passed a renamed test (substring match) and an eleventh edge (an unread count) | both now fail (below) |
| Worked-example toy: virtual time drives a state machine with no waiting | `dotnet run` in `/tmp/opencode/lesson04-toy` | exit 0; printed `Red, Green, Amber, Red, Green` at `00:00:00…00:00:12` |

Mutation audit (each line is a break that a reviewer should trust the gate to catch):

| Mutation | Gate that caught it |
|---|---|
| a transition test renamed | `--list-tests` anchored name loop |
| travel-time guard deleted (early arrival accepted) | `dotnet test` (the rejection test fails) and the container run |
| `Thread.Sleep` added to `StartMove` | the sleep grep |
| `TimeSpan.FromSeconds(5)` hard-coded in `ShuttleCore` | the duration-literal grep |
| an eleventh `ProjectReference` added | the edge-count check |
| the xunit template placeholder left in place | the placeholder `find`/`grep` |
| the demo skips the unload (6 lines) | the seven-line count, the state/timestamp greps, the demo test |
| a `[Fact]` with no `Assert` | the assert-per-file loop |
| wire name `moving` renamed to `traveling` | the wire-name grep, the wire test, the demo |
| the demo prints the enum's `ToString()` | the lowercase-state greps and the demo test |
| `Position` updates at `StartMove` | the line-4 position check and the demo test |
| `Rack.Take` forgets to clear the slot | the demo (unload into an occupied slot throws) and the scripted test |

The two defects the audit found, stated plainly so the habit sticks — **a gate is only trusted
after you have seen it go red.** The first draft of Phase 4 checked test names with
`grep -q "$t"`, a substring match, so a test renamed to `…_renamed` *passed*. Anchoring to the
whole line (`grep -qE "\.$t\$"`) fixed it. The first draft *printed* the numbers "8", "10",
"4", and "7" without asserting them, so an eleventh edge and a six-line demo walked through. A
printed number nobody checks is not a gate; each is now compared and fails. The body text under
the acceptance block says which failure each line is *for*.

Not yet verified:

- Any file the owner is about to write — this assignment is a contract, not a report.
- M2's round trip, M3's persistence, M4's segments, M5's recovery — later lessons by design.
- `TimeProvider` as the eventual clock abstraction — deferred with reasons in
  [record 06](../decisions/06-shuttle-state-machine-and-timing.md).
- Whether the owner's machine has a free Docker daemon for the Phase 6 row — the same readiness
  lesson 03 measured.

---

## Options survey

Lesson 03's pattern, three times, because M1's "done when" raises three real design questions:
*where does pure logic live*, *how is a slot identified*, and *how does time enter the machine*.
Two records carry the full reasons; the surveys below are the compressed version.

### Survey 1: where the pure logic lives, and the rack model (→ [decision record 05](../decisions/05-shuttle-core-and-rack-model.md))

Record 01 §9 left the M1 project deliberately open: *"Which library, and its name, is decided by
the record that introduces it."* Three placements for the rack and the machine:

| | A: a new `ShuttleCore` class library | B: inside `ShuttleSim`, the console app | C: inside `Contracts` |
|---|---|---|---|
| Test the logic without starting the app | yes | the logic and the demo's `Console.WriteLine` share an entry point | yes |
| Keeps the message contract messages-only | yes | yes | **no** — the wire library would carry warehouse geometry |
| Matches AGENTS.md "decision logic separate from I/O" | exact | mixed | split across the wrong boundary |

**Decision 1: a new `ShuttleCore` class library that references nothing at all.** The compiler
enforces the separation AGENTS.md asks a person to remember. The record also fixes the rack's
identity scheme (`A01-L01` lanes, `A01-L01-S01` slots, built from dimensions, occupancy by
membership) and the distance rule (aisle-major lane ordinals, so `A01-L01 → A02-L02` is three
lanes). The full survey of the id scheme is in the record.

### Survey 2: the shape of the state machine (→ [decision record 06](../decisions/06-shuttle-state-machine-and-timing.md))

| | A: enum + a guarded stateful `Shuttle` | B: the contract's strings as state | C: a pure reducer `(state, event) => state` |
|---|---|---|---|
| A typo in a state name | compile error | reaches the wire silently | compile error |
| Guards ("loading needs a pallet here") | with the data they guard | with strings near the data | needs the rack threaded through every call |
| The wire names | one tested mapping to lowercase | free, but easy to drift | one tested mapping |

**Decision 2: an `enum ShuttleState`, a guarded `Shuttle` object, and one tested `ToWireName()`
that produces the contract's five lowercase names.** The remaining decision inside that record
is the event table itself: seven events, each taking `now`, each transition covered by a test,
and every wrong call rejected with a specific `ShuttleRejectedException`.

### Survey 3: where time comes from (→ also [record 06](../decisions/06-shuttle-state-machine-and-timing.md))

| | A: pass `now` in; inject durations | B: `TimeProvider` | C: sleep inside the code |
|---|---|---|---|
| A 17-second scenario in a test | milliseconds | milliseconds, with a fake provider | 17 real seconds, flaky |
| Concepts added at M1 | zero | an abstraction plus a fake | zero, but it hides the rule |
| Where it can go next | M2's shell may adopt `TimeProvider` | correct for I/O timing, used when async arrives | nowhere good |

**Decision 3: durations live in an injected `ShuttleTiming` record and every event takes
`DateTime now`; the code never waits.** `TimeProvider` is deferred to the async shell that M2
and M3 will introduce — a later record can move the clock behind it without touching a single
transition rule.

**Numbering note.** Record 04 closed M0, so today writes 05 (placement and rack) and 06 (the
machine and time). The next number available is 07.

---

## Lecture

Today we are going to learn how a warehouse becomes data, how a shuttle becomes an explicit
state machine, and why time should enter a program as an argument instead of a pause.

### The rack is data

A rack is a hierarchy. At the top is an **aisle**; an aisle holds **lanes**; a lane holds
**slots**; a slot holds at most one **pallet**. You have met this shape before in furniture
instructions — and the contract already committed to part of it. `ShuttleStatus.Position` is
*"the rack lane the shuttle is in, e.g. `A02-L04`"*. So the lane names are not ours to invent:
they are `A<aisle>-L<lane>`, two digits each, and a slot appends its own `-S<slot>`:
`A01-L01-S01`. Lesson 02's rule applies — the contract is the source of truth; this lesson reads
it, it does not change it.

Two design choices inside "the rack is data" are worth saying out loud, because they are the
difference between a model you can trust and one that lies.

**Where does "is this a real slot?" come from?** Three answers are possible. You can write a
regular expression that describes the id shape and check a string against it. You can model
aisles, lanes, and slots as objects and walk the graph. Or — the choice here — the rack can
**build every legal id from its own dimensions and remember them**. A rack built as two aisles
of two lanes of two slots knows four lanes and eight slots, because it made them. Asking whether
`A01-L01-S03` is a slot is then a lookup that returns "no"; asking whether `banana` is a lane
returns "no". There is exactly one source of truth, and it is the rack's constructor. A regex
(record 05's rejected option B) creates a second source of truth that must be kept in agreement
with the dimensions by hand — and the day someone changes the rack to three lanes, the regex is
wrong in a way no test notices until a shuttle is dispatched into a lane that does not exist.

**Where does occupancy live?** A slot is either empty or holds a pallet id. The rack stores
that; `Place` refuses an occupied slot and `Take` refuses an empty one. Why store the pallet id
rather than a `bool`? Because the fleet-control rule is *"a pallet must not be recorded in two
places, or in none."* A boolean tells you "something is here"; an id tells you *which* something,
and the scripted test can assert not just "the destination is occupied" but "`PLT-001` is in the
destination and nowhere else." The demo and the test both check the old slot is empty after the
move. That is the invariant made visible.

One more fact of geometry: how far apart are two lanes? At M1 a shuttle moves lane to lane, and
the rack numbers lanes **aisle by aisle**: aisle 1's lanes first, then aisle 2's. The distance
between two lanes is the difference of those ordinals. For a 2×2 rack, `A01-L01 → A02-L02` is
three lanes of travel. Metres and speeds are more faithful to a real aisle, but they need
segment geometry the spec saves for M4; a per-lane time is the smallest model that makes the
timing real and testable, and the rack owns the arithmetic so the rule lives in one place.

### The state machine is the shuttle

A **state machine** is the smallest honest description of a thing that behaves: a list of states
it can be in, and for each state the events it accepts and the state each event leads to. The
spec names the five states — *idle, moving, loading, unloading, fault* — and they are already in
the contract's `State` field. So the states are decided; what this lesson decides is how the
machine is written and what it does with a call it cannot accept.

The representation is an **enum**, not a string. `ShuttleState.Idle` is checked by the compiler;
`"idle"` is a string a typo can silently change to `"idl"`, and strings are exactly what the wire
carries, so an internal typo would eventually leave the building. But the contract speaks
lowercase, so one function must translate — and it is a small, testable function:

```csharp
ShuttleState.Moving.ToWireName()   // "moving"
```

That function is the only place where a state becomes a word. The wire test asserts all five
words against the contract, so an enum rename cannot quietly change the protocol.

The machine is a `Shuttle` object with **seven guarded methods**, each named for the event it
represents: `StartMove`, `CompleteMove`, `StartLoad`, `CompleteLoad`, `StartUnload`,
`CompleteUnload`, `RaiseFault` (record 06's table is the full list of guards). Read one:

```csharp
public void CompleteMove(DateTime now)
{
    EnsureInOrder(now);                        // the clock cannot go backwards
    EnsureState(ShuttleState.Moving, "complete a move");   // only a moving shuttle arrives
    var travel = TravelTime(_position, _moveToLane);       // lanes × per-lane duration
    if (now - _moveStartedAt < travel)
    {
        throw new ShuttleRejectedException(
            $"travel from {_position} to {_moveToLane} takes {travel} and has not elapsed");
    }

    _position = _moveToLane;                   // position changes on arrival, not departure
    _state = ShuttleState.Idle;
    _lastEventAt = now;
}
```

Three habits to notice, because each is a decision a reviewer will ask about.

**Guard first, then change.** Every method checks its state and its preconditions *before* it
touches a field, so a rejected call leaves the shuttle exactly as it was. A machine that
half-applies a bad call is worse than one that crashes.

**A rejected call is an exception, and a specific one.** `ShuttleRejectedException` is a named
type, not a bare `Exception` and not a silent `return;`. The *dotnet-style* skill asks for
*"specific exception types or result values for expected failures, such as a rejected
command."* The alternative — returning `bool` — makes every caller remember to check, and a
forgotten check turns a wrong command into an invisible no-op. One type, many messages: the
tests assert the rejection, not the sentence.

**Every transition is named and testable.** The ten rows of record 06's table — five entries
into a state, three completions back to idle, and fault from four states — are ten tests, each
named for its edge, e.g. `RaiseFault_from_loading_enters_fault`. That is what "every state
transition is covered by a test" means in this project: not a vibe, a list you can read against
the table.

One state is special: **fault is a sink at M1.** A shuttle can be faulted from any other state —
a blocked path, a pallet that was not detected — and once faulted it accepts nothing. Recovery,
reassignment, alarms, and acknowledgement are M5's *"right alarm and the right recovery."*
Inventing a way out now would be a fifth concept in this lesson and almost certainly the wrong
shape; the sink is the honest minimum.

### Time is an input

Here is the trap this section exists to disarm. A shuttle's move takes time. The instinct is to
make the code wait: `Thread.Sleep(15000)` and *then* update the position. It reads fine and it
poisons everything downstream. A test for a three-lane trip now takes fifteen real seconds; a
suite of ten such tests takes minutes and flakes on a loaded machine; and the code can never be
asked "what happens if you arrive *early*?" because there is no way to arrive early — the clock
is buried inside the method.

The alternative: **time is a parameter, and durations are data.** Two facts make it work.

First, the durations are injected, not written into the logic. `ShuttleTiming` is a small record
carrying the per-lane travel time and the load and unload times. The shuttle receives one in its
constructor. Nothing in `ShuttleCore` contains a `5`, a `FromSeconds`, or a `new TimeSpan(...)`
— the acceptance block greps for exactly those and forbids them. AGENTS.md's *"no hard-coded
timeouts"* is not an abstraction here; it is the difference between a machine that can be
tested and one that cannot.

Second, every event takes `DateTime now`. The shuttle compares `now` with when the phase started
and decides *whether* enough time has passed — it never *waits* for time to pass. A test is then
just a walk through the scenario with a fictitious clock:

```csharp
shuttle.StartMove("A01-L02", start);                 // t = 00:00:00
shuttle.CompleteMove(start.AddSeconds(5));           // the test decides the lane cost 5 s
```

The `DateTime` type is the one you already know; `DateTimeKind.Utc` and ISO-8601 formatting are
lesson 02's ground, not new here. The point is where the number comes from: the caller.

`TimeProvider` — the built-in .NET abstraction for "the current time", fakeable in tests — is the
right answer for a bigger system, and M2's async shell may well adopt it (record 06 says so). It
is deferred, not rejected: adopting it today would add an abstraction and a fake to a milestone
whose whole point is a small, explainable machine. Passing `now` is the same idea with zero
ceremony, and every transition rule survives the upgrade unchanged.

A last, small rule with large consequences: **the clock must not go backwards.** Every event
rejects a timestamp earlier than the previous event's (`now < lastEventAt`). Equal timestamps are
allowed — arriving and starting to unload in the same instant is legal. A backwards clock is a
bug in the caller, and catching it now is the seed of M5's timeout thinking: once "now" is
explicit, a late or missing event becomes visible instead of hidden.

### Recap

A rack is a hierarchy of aisles, lanes, and slots whose names the contract already fixed. Build
every legal id from the dimensions and validation is membership; store the pallet id, not a
boolean, so "in exactly one place" is checkable.

A shuttle is an enum of five states driven by seven guarded methods. Guards run before changes; a
wrong call throws a specific exception; every transition has a test; fault is a sink until M5.

Time is injected and passed in. Durations live in `ShuttleTiming`; `now` is an argument; nothing
sleeps; a backwards clock is rejected. This is what turns a 17-second scenario into a
millisecond test and what makes "arrive early" a case you can actually write.

---

## Worked example

A toy: a **traffic light** on a simulated street. It is **not** the assignment — different
domain, no rack, no pallets, no contract — and it exists to show the three concepts at a size
you can hold in one glance: states as an enum, transitions guarded by an injected duration, and
time passed in rather than slept on. Do not copy it. The toy stays a toy.

Everything below ran in `/tmp/opencode/lesson04-toy/` and was deleted afterwards. It is one
`dotnet new console` project; the state, the timing record, and the machine sit in one file
beside `Program.cs`.

```csharp
enum LightState { Red, Green, Amber }

sealed record Timing(TimeSpan Red, TimeSpan Green, TimeSpan Amber);

sealed class TrafficLight
{
    private readonly Timing _timing;
    private LightState _state = LightState.Red;
    private DateTime _changedAt;
    private bool _started;

    public TrafficLight(TimeSpan red, TimeSpan green, TimeSpan amber) =>
        _timing = new Timing(red, green, amber);

    public LightState State => _state;

    public void Tick(DateTime now)
    {
        if (!_started) { _started = true; _changedAt = now; return; }
        if (now - _changedAt < Duration(_state)) return;
        _state = _state switch
        {
            LightState.Red => LightState.Green,
            LightState.Green => LightState.Amber,
            LightState.Amber => LightState.Red,
            _ => throw new ArgumentOutOfRangeException(),
        };
        _changedAt = now;
    }

    private TimeSpan Duration(LightState state) => state switch
    {
        LightState.Red => _timing.Red,
        LightState.Green => _timing.Green,
        LightState.Amber => _timing.Amber,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
```

The driver hands the light a virtual clock and walks it forward:

```csharp
var light = new TrafficLight(red: TimeSpan.FromSeconds(3), green: TimeSpan.FromSeconds(5),
                             amber: TimeSpan.FromSeconds(1));
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
light.Tick(now);
Report();
foreach (var wait in new[] { 3, 5, 1, 3 }) { now = now.AddSeconds(wait); light.Tick(now); Report(); }
```

Observed (exit 0):

```
00:00:00 Red
00:00:03 Green
00:00:08 Amber
00:00:09 Red
00:00:12 Green
```

Three things are visible in one screen. The states are an enum, so a typo is a compile error. The
durations are injected — change `green: 5` to `60` and no logic changes. And `Tick(now)` takes
the clock as an argument: the whole program ran in microseconds while describing twelve seconds
of traffic. `Thread.Sleep` would have exported the same behaviour in twelve real seconds, and the
first test of an early `Tick` would have been impossible. Your shuttle is this toy with a rack, a
pallet, and seven events instead of one.

---

## Say it out loud

Answer these without notes, the way you would in an interview:

1. A teammate asks "why build the rack from dimensions and look ids up? A regex is one line."
   Explain what second source of truth a regex introduces, and name the specific id that a
   `2 × 2 × 2` rack rejects while a regex accepts it.
2. Walk the state machine from `A01-L01` to `A02-L02` with a pallet, naming each of the seven
   events in order and the state after each. Then say which event is impossible to call at each
   moment, and which exception a caller gets for trying.
3. Your suite moves a pallet across a three-lane rack and finishes in milliseconds. Where does
   the 15 seconds live, and what two code shapes does the acceptance block forbid to keep it
   there? What could a test never do if the code slept instead?
4. Position changes at `CompleteMove`, not `StartMove`. Using the contract's `Position` wording,
   explain why the demo's fourth line reports `"State":"moving"` with `"Position":"A01-L01"`.
   What would a status report in the middle of a move be claiming if it said `A02-L02`?
5. During my own audit, two gates passed a broken solution: a renamed test and an eleventh edge.
   Say exactly what each gate was checking, why it was fooled, and the one change that fixed it.
   Then state the property every gate in this project must have.

---

## Assignment

Build M1: the rack, the shuttle's state machine, and the demo that drives one shuttle through
one pallet move. This is your code. Type it, run it, be able to explain every line. The move
from the finished M0 project is four new projects; the contract does not change.

### Requirements

1. **Four new projects, added to `src/ShuttleFleet.slnx`**, with exactly this reference graph
   (decision record 05 fixes it; the total is **ten** `ProjectReference` lines, six of them new):
   - **`src/ShuttleCore/ShuttleCore.csproj`** — a class library (`net10.0`, nullable, implicit
     usings) that **references nothing at all**. This is where the rack and the machine live. It
     must not reach `Contracts`, the broker, or a database: the compiler should enforce the
     separation, not a reviewer's memory.
   - **`src/ShuttleCore.Tests/ShuttleCore.Tests.csproj`** — xUnit (the `dotnet new xunit`
     template), referencing `ShuttleCore` only.
   - **`src/ShuttleSim/ShuttleSim.csproj`** — a console app (`OutputType Exe`), referencing
     `Contracts` and `ShuttleCore`. This is the demo. It never references `FleetManager`.
   - **`src/ShuttleSim.Tests/ShuttleSim.Tests.csproj`** — xUnit, referencing `ShuttleSim`,
     `ShuttleCore`, and `Contracts`. One test runs the demo and reads its output.
   Delete the template placeholders (`Class1.cs`, both `UnitTest1.cs`). No `.cs` file may be
   copied between projects (the acceptance hashes every source to prove it).

2. **`Rack` — the warehouse as data** (`src/ShuttleCore/Rack.cs`). Decision record 05 fixes the
   shape; the names below are fixed so the tests and gates mean something:
   - constructor `Rack(int aisleCount, int lanesPerAisle, int slotsPerLane)`, rejecting a
     dimension below 1;
   - it **builds every legal id** from those dimensions: lanes `A{nn}-L{nn}`, slots
     `{lane}-S{nn}`, two-digit fields (`A02-L04`, `A01-L01-S01`);
   - read surface: `AisleCount`, `LanesPerAisle`, `SlotsPerLane`, `LaneIds`, `SlotIds`,
     `IsLane(laneId)`, `IsSlot(slotId)`, `IsSlotInLane(slotId, laneId)`,
     `DistanceInLanes(fromLane, toLane)`, `IsOccupied(slotId)`, `PalletAt(slotId)`;
   - mutators: `Place(slotId, palletId)` and `Take(slotId)` (returns the pallet id). `Place`
     refuses an occupied slot and `Take` refuses an empty one — one pallet, one place. An
     unknown or malformed id is rejected by membership, never parsed.
3. **`ShuttleState` and its wire names** (`src/ShuttleCore/ShuttleState.cs`). An
   `enum ShuttleState { Idle, Moving, Loading, Unloading, Fault }` and a tested
   `ToWireName()` extension that returns the contract's lowercase `"idle"`, `"moving"`,
   `"loading"`, `"unloading"`, `"fault"`. This is the only place a state becomes a word.
4. **`ShuttleTiming` and `ShuttleRejectedException`** (`src/ShuttleCore/`, one file each). A
   record `ShuttleTiming(TimeSpan MovePerLane, TimeSpan Load, TimeSpan Unload)`, and a public
   exception type `ShuttleRejectedException`. No duration literal and no `TimeSpan` constructor
   call may appear anywhere in `ShuttleCore` — the acceptance greps for it.
5. **`Shuttle` — the machine** (`src/ShuttleCore/Shuttle.cs`). Constructor
   `Shuttle(string shuttleId, Rack rack, string startLane, ShuttleTiming timing)`, validating the
   start lane. Public read surface: `ShuttleId`, `State`, `Position` (the current lane),
   `CarriedPalletId`, `FaultReason`. Seven events, each taking the event time as
   `DateTime now`, exactly as record 06's table fixes them:
   - `StartMove(toLane, now)` / `CompleteMove(now)` with
     travel = `DistanceInLanes(from, to) × MovePerLane`, and `Position` changing only on
     `CompleteMove`;
   - `StartLoad(slotId, now)` / `CompleteLoad(now)` — the pallet leaves the rack onto the
     shuttle;
   - `StartUnload(slotId, now)` / `CompleteUnload(now)` — the pallet leaves the shuttle into the
     rack, and only if the target slot is empty;
   - `RaiseFault(reason, now)` from any non-fault state, with `FaultReason` set; fault is a sink
     at M1 — every later event is rejected;
   - every event: reject a state the event cannot come from, reject a guard failure, and reject a
     `now` earlier than the previous event's (equal is allowed). Rejection is always
     `ShuttleRejectedException`.
6. **The tests, by name** — decision 06 fixes the transition set, so the names are fixed too.
   Every `[Fact]` carries at least one `Assert`; test names say what they prove. In
   `ShuttleCore.Tests`:
   - the **ten transitions**: `StartMove_from_idle_enters_moving`,
     `CompleteMove_from_moving_enters_idle`, `StartLoad_from_idle_enters_loading`,
     `CompleteLoad_from_loading_enters_idle`, `StartUnload_from_idle_enters_unloading`,
     `CompleteUnload_from_unloading_enters_idle`, `RaiseFault_from_idle_enters_fault`,
     `RaiseFault_from_moving_enters_fault`, `RaiseFault_from_loading_enters_fault`,
     `RaiseFault_from_unloading_enters_fault`;
   - the **six rejections**: `StartMove_while_not_idle_is_rejected`,
     `CompleteMove_before_travel_time_has_passed_is_rejected`,
     `StartLoad_from_a_different_lane_is_rejected`,
     `StartUnload_into_an_occupied_slot_is_rejected`, `Events_after_a_fault_are_rejected`,
     `Event_with_a_timestamp_before_the_previous_one_is_rejected`;
   - the **scripted sequence**: `Scripted_sequence_moves_a_pallet_from_one_slot_to_another`, on
     a `2 × 2 × 2` rack, moving `PLT-001` from `A01-L01-S01` to `A01-L02-S02`, asserting the
     destination holds it, the origin is empty, and the shuttle is idle carrying nothing;
   - the **rack facts**: `Rack_builds_every_lane_and_slot_from_its_dimensions`,
     `Lane_distance_counts_lanes_within_an_aisle_then_aisles`,
     `Placing_a_pallet_marks_the_slot_occupied`, `Taking_a_pallet_leaves_the_slot_empty`;
   - the **wire names**: `State_wire_names_match_the_contract`.
7. **The demo** (`src/ShuttleSim/Program.cs`). A fixed scenario, not a menu: a `2 × 2 × 2` rack,
   shuttle `SH-01` starting in `A01-L01`, pallet `PLT-001` in `A01-L01-S01`, timing
   `5 s per lane / 2 s load / 2 s unload`, clock starting at `2026-01-01T00:00:00Z`. Drive the
   full sequence (load, move `A01-L01 → A02-L02`, unload into `A02-L02-S02`) and print **one
   `ShuttleStatus` per step — seven lines**: the initial status, then one after each of the six
   events. Serialize with `System.Text.Json`'s default options (lesson 02: the property names
   are the wire names; `ShuttleStatus` is unchanged). Build the status from
   `shuttle.State.ToWireName()`, `shuttle.Position`, the event time, and `shuttle.FaultReason`.
   The scenario's numbers are **data**, not configuration: M2's record 02 introduces
   configuration; hardcoding addresses or real timeouts remains banned.
8. **The demo test** (`src/ShuttleSim.Tests/DemoTests.cs`). One test,
   `Demo_prints_a_status_line_per_step`, that runs the built demo as a child process and checks
   its behaviour: start `dotnet` on `ShuttleSim.dll` anchored at `AppContext.BaseDirectory` (the
   M0 pattern), bound the wait with a **named timeout constant** and `WaitForExit(<timeout>)`
   (kill the process tree if it overruns — review 03 left this exact gap in `StartTests.cs`),
   then parse the output as `ShuttleStatus` and assert seven statuses, the state sequence
   `idle, loading, idle, moving, idle, unloading, idle`, `SH-01` throughout, `A01-L01` on the
   moving line, `A02-L02` at the end, and the final timestamp `2026-01-01T00:00:19Z`.
9. **The contract does not move.** No change to `docs/contract/v1.md`, `src/Contracts/`, or the
   M0 files. The fresh-container build from lesson 03 must still pass — now with **four**
   `Passed!` lines.

No MQTT, no topics, no broker, no database, no traffic, no recovery. This assignment moves one
pallet with local calls.

### Acceptance criteria (all command-checkable)

From the repository root. Read every exit code directly, never through a pipe — except the
counting convention below (`| wc -l`), where the *printed number* is the thing you judge. When a
line prints a number, compare it; a number nobody checks is not a gate.

```bash
# Phase 1 — four new projects, eight in the solution, ten edges, one graph
for p in ShuttleCore ShuttleCore.Tests ShuttleSim ShuttleSim.Tests; do
  test -f "src/$p/$p.csproj" || echo "missing project: $p"          # prints nothing
done
grep -c '<Project Path' src/ShuttleFleet.slnx                       # prints 8
grep -rn 'ProjectReference' --include=*.csproj src | wc -l          # prints 10
! grep -q 'ProjectReference' src/ShuttleCore/ShuttleCore.csproj     # exit 0: the core references nothing
grep -q '<OutputType>Exe</OutputType>' src/ShuttleSim/ShuttleSim.csproj       # exit 0
! grep -q '<OutputType>Exe</OutputType>' src/ShuttleCore/ShuttleCore.csproj   # exit 0: a class library
! grep -q 'FleetManager' src/ShuttleSim/ShuttleSim.csproj                     # exit 0
ref() { grep -q "$2" "src/$1/$1.csproj" || echo "missing edge: $1 -> $2"; }
ref ShuttleCore.Tests ShuttleCore.csproj
ref ShuttleSim Contracts.csproj
ref ShuttleSim ShuttleCore.csproj
ref ShuttleSim.Tests ShuttleSim.csproj
ref ShuttleSim.Tests ShuttleCore.csproj
ref ShuttleSim.Tests Contracts.csproj                              # prints nothing
find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
                                                                    # prints nothing: no file copied between projects
```

The count is exactly **ten** because record 05 fixes the graph: the six new edges plus M0's
four, and nothing else. `ShuttleCore`'s missing reference is the point of the whole placement —
a library that cannot see the network is a library that cannot quietly depend on it. The `md5sum`
line is lesson 02's duplicate check, kept because "no copied code" is a rule that predates M1 and
still applies.

```bash
# Phase 2 — the fixed vocabulary lives in ShuttleCore
grep -rq 'enum ShuttleState' src/ShuttleCore --include='*.cs'        # exit 0
for s in idle moving loading unloading fault; do
  grep -rq "\"$s\"" src/ShuttleCore --include='*.cs' || echo "missing wire name: $s"
done                                                                 # prints nothing
for e in StartMove CompleteMove StartLoad CompleteLoad StartUnload CompleteUnload RaiseFault; do
  grep -rq "public void $e" src/ShuttleCore --include='*.cs' || echo "missing event: $e"
done                                                                 # prints nothing
grep -rq 'class ShuttleRejectedException' src/ShuttleCore --include='*.cs'   # exit 0
grep -rq 'record ShuttleTiming' src/ShuttleCore --include='*.cs'     # exit 0
grep -rq 'class Rack' src/ShuttleCore --include='*.cs'               # exit 0
grep -rqE 'class Shuttle([^A-Za-z]|$)' src/ShuttleCore --include='*.cs'   # exit 0 — the class itself, not the exception
```

The `class Shuttle([^A-Za-z]|$)` pattern is deliberate: `grep -q 'class Shuttle'` would be
satisfied by `ShuttleRejectedException`, so a solution that forgot the machine entirely could
pass. Since the assignment fixes these names, the gate checks them; the behaviour is checked in
Phase 4 and Phase 5.

```bash
# Phase 3 — the rules (each must print nothing)
grep -rnE 'Thread\.Sleep|Task\.Delay' src --include='*.cs'           # nothing: local calls never wait
grep -rnE 'TimeSpan\.(FromSeconds|FromMilliseconds|FromMinutes|FromHours)|new TimeSpan\(' \
     src/ShuttleCore --include='*.cs'                                # nothing: durations are injected
grep -rniE 'mqtt|broker|localhost|127\.0\.0\.1|:1883|:5432|postgres|Host=' src --include='*.cs'
                                                                     # nothing: no network at M1
find src -name 'Class1.cs' -o -name 'UnitTest1.cs'                   # nothing: placeholders gone
grep -rn 'Test1' src --include='*.cs'                                # nothing
grep -rn 'Hello, World!' src/FleetManager --include='*.cs'           # nothing (scoped: a test may name the string)
for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do
  grep -q 'Assert' "$f" || echo "$f"                                 # nothing: no empty test bodies
done
```

The sleep and duration-literal greps are the two shapes that smuggle time back into the code.
The first searches all of `src/` — nothing, anywhere, may sleep (a "fast test" that sleeps is
still a sleeping program). The second is scoped to `ShuttleCore` on purpose: the demo's `5 s`
and the tests' `2 s` are **scenario data**, like lesson 02's example payload, so they may appear
in `ShuttleSim` and `ShuttleCore.Tests`; what must never appear is a duration literal inside the
decision logic. The `Hello, World!` grep is scoped to the app for the reason review 02 measured
— a test that asserts the string is absent must be allowed to name it.

```bash
# Phase 4 — from src/: build, run the suite, walk the names
cd src
dotnet build                                                         # exit 0
T=$(mktemp); dotnet test > "$T" 2>&1                                  # exit 0 — read $? here
grep -c 'Passed!' "$T"                                               # prints 4 (one per test project)
grep -c 'No test is available' "$T"                                  # prints 0
rm -f "$T"
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
for t in StartMove_from_idle_enters_moving CompleteMove_from_moving_enters_idle \
         StartLoad_from_idle_enters_loading CompleteLoad_from_loading_enters_idle \
         StartUnload_from_idle_enters_unloading CompleteUnload_from_unloading_enters_idle \
         RaiseFault_from_idle_enters_fault RaiseFault_from_moving_enters_fault \
         RaiseFault_from_loading_enters_fault RaiseFault_from_unloading_enters_fault \
         StartMove_while_not_idle_is_rejected CompleteMove_before_travel_time_has_passed_is_rejected \
         StartLoad_from_a_different_lane_is_rejected StartUnload_into_an_occupied_slot_is_rejected \
         Events_after_a_fault_are_rejected Event_with_a_timestamp_before_the_previous_one_is_rejected \
         Scripted_sequence_moves_a_pallet_from_one_slot_to_another \
         Rack_builds_every_lane_and_slot_from_its_dimensions \
         Lane_distance_counts_lanes_within_an_aisle_then_aisles \
         Placing_a_pallet_marks_the_slot_occupied Taking_a_pallet_leaves_the_slot_empty \
         State_wire_names_match_the_contract Demo_prints_a_status_line_per_step; do
  grep -qE "\.$t\$" "$L" || echo "missing test: $t"                  # nothing: every name present
done
rm -f "$L"
grep -rho 'Assert\.Equal(ShuttleState\.' ShuttleCore.Tests --include='*.cs' | wc -l
                                                                     # prints >= 10: one per transition
grep -rho 'Assert\.Throws<ShuttleRejectedException>' ShuttleCore.Tests --include='*.cs' | wc -l
                                                                     # prints >= 6: one per rejection
```

The `--list-tests` greps are **anchored to the end of the line** (`\.$t\$`), and that anchor is
not cosmetic: measured during the audit, a plain substring `grep "$t"` accepted a test renamed
to `…_renamed`, because the original name sat inside the longer one. The name is the requirement
— "every state transition is covered by a test" — so the gate must match the whole name, not a
prefix of it. The two assertion counts guard the other half: a file full of test names whose
bodies assert nothing would pass `--list-tests` while proving nothing.

```bash
# Phase 5 — the demo (from src/)
F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1   # exit 0 — read $? here
grep -c '"ShuttleId"' "$F"                                           # prints 7
for s in idle loading moving unloading; do
  grep -q "\"State\":\"$s\"" "$F" || echo "demo never shows state: $s"   # nothing
done
grep -qF '"Timestamp":"2026-01-01T00:00:17Z"' "$F"                   # exit 0: three lanes × 5 s honoured
grep -qF '"Timestamp":"2026-01-01T00:00:19Z"' "$F"                   # exit 0: the final status
grep -qF '"Position":"A01-L01"' "$F"                                 # exit 0
grep -qF '"Position":"A02-L02"' "$F"                                 # exit 0
sed -n '4p' "$F" | grep -qF '"State":"moving"'                       # exit 0: line 4 is the move
sed -n '4p' "$F" | grep -qF '"Position":"A01-L01"'                   # exit 0: position has not jumped early
grep -qF '"FaultReason":null' "$F"                                   # exit 0
rm -f "$F"
for id in A01-L01-S01 A01-L02-S02 PLT-001; do
  grep -rq "$id" ShuttleCore.Tests --include='*.cs' || echo "scripted scenario missing: $id"
done                                                                 # nothing
for id in A01-L01-S01 A02-L02-S02 PLT-001 SH-01; do
  grep -rq "$id" ShuttleSim/Program.cs || echo "demo scenario missing: $id"   # nothing
done
grep -rq 'JsonSerializer' ShuttleSim.Tests --include='*.cs'          # exit 0
grep -rq 'ShuttleStatus' ShuttleSim.Tests --include='*.cs'           # exit 0
```

The demo gate is behavioural, not decorative: the program's guards must all pass for it to reach
line 7, so a machine that cannot actually carry the pallet crashes with a non-zero exit instead
of printing a neat line. The `00:00:17Z` line is the arithmetic made visible — two seconds to
load, then three lanes × five seconds, arriving seventeen seconds after the start. The
line-4 checks are the two facts a lazy demo gets wrong: state `moving` *and* position still at
the origin. And `grep -c '"ShuttleId"'` = 7 is only true if the demo printed exactly the seven
statuses; measured during the audit, a demo that skipped the unload printed six and every
other line still looked fine.

```bash
# Phase 6 — the contract is untouched and the fresh container still passes (from the repo root)
cd ..
grep -qE '^Version: 1$' docs/contract/v1.md                          # exit 0
grep -q '^## ShuttleStatus$' docs/contract/v1.md                     # exit 0
for f in ShuttleId State Position Timestamp FaultReason; do
  grep -q "$f" docs/contract/v1.md || echo "contract field missing: $f"    # nothing
done
grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs'      # exit 0
T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
                                                                     # exit 0 — read $? here
rm -f "$T"
U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1          # exit 0 — the suite's verdict
grep -c 'Passed!' "$U"                                               # prints 4
rm -f "$U"
```

The contract stays exactly as lesson 02 wrote it — M1 consumes the states and the `Position`
format, it does not edit them. A message shape change needs a record and a version bump. The
fresh-container gate is lesson 03's, now counting **four** `Passed!` lines: the new projects must
survive a machine that has none of your build output.

From `src/`, M0's gates must still hold — `dotnet build` and `dotnet test` exit 0, and the
lesson-02 contract checks still pass. Nothing this lesson adds may move them.

### Stretch goal

Add **awaiting-on-arrival as a guarded wait** without adding a second concept. Give the
`Shuttle` a method that answers a question instead of firing an event — for example
`bool CanCompleteMove(DateTime now)` — and rewrite `CompleteMove` to throw through the same guard.
Then write one test that shows a caller can *ask* whether the travel has elapsed before it acts,
proving the guard logic is shared, not duplicated.

Accept: the ten transition and six rejection tests still pass unchanged, and the new test fails
if you make `CanCompleteMove` always return `true`. Say out loud while you build it: why does
moving the rule into a query make the command and the question impossible to disagree?

### Not in this assignment

MQTT, topics, a broker, command acknowledgement, retries, heartbeats — M2 and M5, against record
02. The database and fleet-manager state — M3, record 03. Lane segments, reservations, and
deadlock — M4, and the reason `Position` is a whole lane for now. Fault recovery, alarms, and
reassignment — M5; fault is a sink here. Within-lane travel and metres-per-second — M4's
segments. The mock WMS — M6. The dashboard — M7. Configuration files — M2; the demo's numbers
are scenario data.

**Carried from M0, for the reviewer's list:** review 03 left two small items in
`src/FleetManager.Tests/StartTests.cs` — R2 (the child process has no bounded wait) and R4 (a
nine-space indent and a missing trailing newline). Neither is this lesson's deliverable, and
this lesson's own demo test must show the bounded-wait pattern; both remain due before M2's
lesson.

---

## Closing M1

Read the spec's line once more: *"a scripted sequence moves a pallet from one slot to another,
and every state transition is covered by a test."* After this assignment, the repo can honestly
claim both: the scripted test walks `PLT-001` from `A01-L01-S01` to `A01-L02-S02` and leaves it
nowhere else, and the ten named transition tests walk every edge of the state graph, including
fault.

Who says "done"? Not you, and not I. AGENTS.md gives the reviewer that line: the milestone table
in `README.md` moves to "done" only when the review passes, with the verifying command written
beside it. The reviewer will run the block above against your committed tree, delete one
transition test and one guard to watch the gates go red, and ask you to explain a line. Be ready
to say, without notes, why the rack rejects `A01-L01-S03`, why `Position` waits for
`CompleteMove`, and why nothing in `ShuttleCore` is allowed to sleep.
