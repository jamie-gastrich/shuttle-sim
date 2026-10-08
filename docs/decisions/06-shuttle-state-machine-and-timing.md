# Decision 06: The shuttle state machine and time as an input

**Date:** 2026-10-08
**Milestone:** M1 (Warehouse model and one shuttle) — the shuttle's states, events, guards, and how its duration is made testable.
**Status:** Accepted. Written for lesson 04, which assigns the machine.
**Supersedes:** none. Record 05 put the logic in `ShuttleCore` and fixed the rack; this record fixes the machine that drives it. It also makes explicit an assumption the whole simulator rests on: the world clock is *passed in*, never slept on.
**Replaced by:** a later record that changes the state set, the event shapes, or the clock abstraction. The spec names these five states, so a change is a milestone-level event; the replacement links here.

---

## 1. What we are deciding

The spec's M1 says: *"a single simulated shuttle with an explicit state machine (idle, moving, loading, unloading, fault) and simple timing … Done when: a scripted sequence moves a pallet from one slot to another, and every state transition is covered by a test."* This record decides:

1. how a state is represented and how the contract's lowercase names are produced;
2. the event methods and their guards, and how a rejected call signals failure;
3. what "simple timing" means — where durations come from and how the clock enters the code;
4. how far fault handling goes at M1.

Not decided here: heartbeats, timeouts, fault *recovery*, alarms (M5); task assignment (M3); traffic (M4); serialization and topics (record 02, M2).

---

## 2. Survey: how a state is represented

| | A: an enum plus a guarded stateful object (chosen) | B: the contract's strings as the state | C: a pure reducer, `(state, event) => state` |
|---|---|---|---|
| A typo in a state name | compile error | a silently different `"movng"` reaches the wire | compile error |
| Guard logic ("loading needs a pallet in this lane") | lives in the method that owns the data | same, but around loose strings | needs the rack and the payload threaded through every call |
| The wire name | one small tested mapping | free — but nothing keeps it in sync with the contract | one small tested mapping |
| Explainability for this owner | states, transitions and fields read as one object | deceptively simple until the first reject | elegant, but a second concept (functional cores) not yet taught |

**Decision 1: a C# `enum ShuttleState { Idle, Moving, Loading, Unloading, Fault }`, driven by a stateful, guarded `Shuttle` object.** The enum makes the five spec states compiler-checked. The contract speaks lowercase strings, so exactly one function maps enum to wire name — `ToWireName()` in `ShuttleCore`, unit tested against the contract's five names. A reducer is a fine shape for a later, larger machine; at five states with a rack in hand, the object is the smallest thing that teaches the same lesson.

---

## 3. Decision: the events, the guards, and rejection

The machine is driven by **seven local method calls**, each taking the event's time as a `DateTime`:

| Event | Allowed from | Guards besides state | Effect on success |
|---|---|---|---|
| `StartMove(toLane, now)` | `Idle` | `toLane` is a rack lane | `Moving`; remembers target and start time |
| `CompleteMove(now)` | `Moving` | elapsed ≥ per-lane duration × lane distance | position becomes target; `Idle` |
| `StartLoad(slotId, now)` | `Idle` | slot is in the current lane, occupied, and the shuttle carries nothing | `Loading`; remembers the slot |
| `CompleteLoad(now)` | `Loading` | elapsed ≥ load duration | pallet leaves the rack and rides the shuttle; `Idle` |
| `StartUnload(slotId, now)` | `Idle` | slot is in the current lane, empty, and a pallet is aboard | `Unloading`; remembers the slot |
| `CompleteUnload(now)` | `Unloading` | elapsed ≥ unload duration | pallet leaves the shuttle into the rack; `Idle` |
| `RaiseFault(reason, now)` | any non-`Fault` state | `reason` is not blank | `Fault`; remembers the reason |

The five transitions *into* a state from `Idle`, plus the three completes *back* to `Idle`, plus the four fault entries, are the ten rows the "every state transition is covered by a test" clause demands.

**Rejection is an exception, `ShuttleRejectedException`.** The *dotnet-style* skill calls for *"specific exception types or result values for expected failures, such as a rejected command"*; a rejected call is a bug in the caller, not a crash, and a named type makes the wrong-call tests specific. Rejected as an alternative: returning a `bool` or a result object — the owner would then have to remember to inspect every return, and a forgotten check becomes a silent no-op. Also rejected: exceptions per reason — one type, many messages, because the guards, not the types, are what the tests assert.

**Clock order is guarded too.** Every event rejects a timestamp earlier than the previous event's (`now < lastEventAt`). Equal timestamps are allowed — two events that happen at one instant (arriving, then starting to unload) are legal; only a backwards clock is a fault in the caller. This is the seed of M5's timeout reasoning: once "now" is explicit, a late or missing event is visible instead of hidden.

---

## 4. Survey: where time comes from

| | A: pass `now` into every call; inject durations (chosen) | B: `TimeProvider` (the built-in .NET abstraction) | C: read the wall clock and `Thread.Sleep` inside the shuttle |
|---|---|---|---|
| Can the whole scenario be tested in microseconds? | yes — the test owns the clock | yes, with a fake provider | no: real waiting, slow and flaky |
| How many concepts does M1 add? | zero — a parameter | one abstraction and a fake | zero, but it hides the rule |
| Does the code "wait"? | never | never | yes |
| Where the milestone can go next | M2's async shell wraps this with `TimeProvider` if it wants | correct for I/O timing, but unnecessary at M1 | — |

**Decision 2: durations are injected as a `record ShuttleTiming(TimeSpan MovePerLane, TimeSpan Load, TimeSpan Unload)`, and every event takes the time as `DateTime now`.** The shuttle computes *whether* enough time has passed; it never *waits* for time to pass. The acceptance block greps the whole `src/` for `Thread.Sleep` and `Task.Delay` and forbids both, and greps `ShuttleCore` for duration literals — a per-lane minute, a load second, or any `new TimeSpan(...)` in the library is exactly the hard-coded timeout AGENTS.md bans. `TimeProvider` is explicitly **not** rejected as wrong; it is deferred, because M2's shell and M3's persistence will introduce async and it composes naturally there. A later record can move the clock behind it without changing any transition rule.

**Scenario numbers are data, not configuration.** The demo's `5 s / 2 s / 2 s` and the tests' timings are fixed input to a fixed scenario — the same class of thing as lesson 02's example payload. Configuration (addresses, ports, and later real timings) arrives with M2 and record 02. That is why the no-literal gate is scoped to `ShuttleCore`: the rule bites where the decision lives.

---

## 5. Decision: fault is a sink at M1

`RaiseFault(reason, now)` is reachable from any state except `Fault`, records `FaultReason`, and — at M1 — the machine does not leave it. Every event from `Fault` is rejected. Recovery, reassignment, alarm severity, and acknowledgement are M5's *"right alarm and the right recovery"*; inventing a recovery path now would put a fifth concept in this lesson and a wrong shape in the code. The demo does not inject a fault, but a test does, so the transition is covered.

---

## 6. Verification table

The reference shape was built and run in `/tmp/opencode/lesson04-scratch/` (deleted afterwards).

| Claim | How checked | Result |
|---|---|---|
| Every one of the ten transition rows is covered by a test | `dotnet test --list-tests` anchored greps for the ten names | all present, all passed |
| Rejections are real | six named rejection tests; `Assert.Throws<ShuttleRejectedException>` | 7 assertions present, all passed |
| Early arrival is rejected | `CompleteMove_before_travel_time_has_passed_is_rejected` | passed |
| A scripted sequence moves `PLT-001` from `A01-L01-S01` to `A01-L02-S02` and leaves one place for it | `Scripted_sequence_moves_a_pallet_from_one_slot_to_another` | passed |
| The wire names equal the contract's five | `State_wire_names_match_the_contract` | passed |
| No sleeping and no duration literal in the library | `grep` over `src/` and `src/ShuttleCore` | prints nothing |
| The demo's move takes three lanes × 5 s | demo output at `2026-01-01T00:00:02Z` (start) and `…00:00:17Z` (arrival) | measured |

Not yet verified:

- The owner's implementation — this is the assignment.
- M5's fault recovery and heartbeats; M4's segment timing; M2's real round trip.
- Any clock abstraction (`TimeProvider`) — deferred with its reasons above.

---

## 7. What would change this choice

- **M2** introduces async I/O and may move the clock to `TimeProvider`; the transition rules stay, the signatures may grow a provider.
- **M5** needs faults to exit (recovery, ack) → this record's sink rule is replaced by a recovery record that links here.
- The state count grows (charging, homing) → the enum changes; the spec's list is the current truth.
- Timing becomes physical (acceleration, slot-level moves) → M4's record replaces `MovePerLane` arithmetic.

---

## 8. What this record unblocks

- Lesson 04's assignment can specify the exact event table, the exception type, and the timing rule, and let the owner write every line.
- The reviewer can test "every transition covered" by name and prove it by deleting one.
- M2's wire messages have a state source: `Shuttle.State.ToWireName()` is the contract's lowercase vocabulary, already tested.
