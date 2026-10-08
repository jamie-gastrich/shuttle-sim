# Decision 05: ShuttleCore and the rack model

**Date:** 2026-10-08
**Milestone:** M1 (Warehouse model and one shuttle) — where the shuttle's pure decision logic lives, and the shape of the warehouse it moves through.
**Status:** Accepted. Written for lesson 04, which assigns both.
**Supersedes:** none. Record 01, section 9 deliberately left two questions open: *"does the M1 state machine get its own project?"* and *"which library, and its name, is decided by the record that introduces it."* This record answers the project half. Record 06 answers the machine half. This record also finishes record 01 §9's `ProjectReference` sketch for M1 — facts record 01 could not know when it was written.
**Replaced by:** a later record that renames, splits, or merges `ShuttleCore`. Records are append-only; the replacement links back here.

---

## 1. What we are deciding

M1 asks for *"a rack model (lanes, slots, pallets)"* and *"a single simulated shuttle with an explicit state machine … driven by local calls only, with no network yet."* The first sentence is data; the second is behaviour. This record decides, for both:

1. **where the pure logic lives** — which project, with which references, so it can be unit-tested without a process, a broker, or a database (AGENTS.md: *"Decision logic is separated from input and output"*);
2. **the rack model** — the aisle/lane/slot hierarchy, how a slot's identity is written, what occupancy means, and how far apart two lanes are.

What this record does **not** decide: the shuttle's states, events, and transitions (record 06); lane segments and reservations (M4); persistence (M3); any transport (record 02, M2). This milestone still has no broker on the wire — the spec's *"no network yet"*.

---

## 2. Survey: where the pure logic lives

Three honest placements for *"the shuttle's decision logic, testable alone"*:

| | A: new `ShuttleCore` class library (chosen) | B: inside `ShuttleSim`, the console app | C: inside `Contracts` |
|---|---|---|---|
| Can a unit test run the logic without starting the app? | yes — a plain library | awkward: the logic shares the app's `Program` entry point and its output; tests would reference an exe project | yes |
| Does it respect "the contract is messages only"? | yes — `Contracts` stays a message library | yes, but the app now mixes I/O and logic | **no** — the shared message contract would carry rack geometry, and every consumer of messages would drag the warehouse in |
| AGENTS.md "decision logic separate from I/O" | the rule's exact shape | mixes the two | mixes concerns across a project boundary that exists to prevent exactly this |
| M2's shell (a broker-backed shuttle process) | references `ShuttleCore`; the same logic runs behind real messages | already entangled with the demo's `Console.WriteLine` | would force the contract to grow warehouse types |

**Decision 1: a new class library `ShuttleCore`, with no `ProjectReference` at all.** It holds the rack and the state machine. It cannot reach the broker, the database, or `Contracts`, so the compiler — not a reviewer — enforces the separation. Record 01 §9 already fixed the class-library role for *"the first pure logic (M1)"*; this record fixes the name.

**Decision 1b (the graph).** M1 adds four projects and six edges, for **ten `ProjectReference` lines** in total:

```
new edges (M1)
ShuttleCore.Tests -> ShuttleCore
ShuttleSim        -> ShuttleCore, Contracts
ShuttleSim.Tests  -> ShuttleSim, ShuttleCore, Contracts

unchanged (M0)
Contracts.Tests    -> Contracts
FleetManager.Tests -> FleetManager, Contracts
FleetManager       -> Contracts
```

`ShuttleSim` references `Contracts` because the demo emits `ShuttleStatus` on the wire — that is the message the contract already defines, not a new one. `ShuttleSim.Tests` references `ShuttleCore` directly to build the scenario it launches, and `Contracts` to parse the JSON the demo prints. Nothing references `FleetManager`, and `ShuttleSim` does not reference it: the two sides meet at the contract, and only there. Record 01 §9 sketched `ShuttleSim.Tests -> ShuttleSim, Contracts`; this record appends the `ShuttleCore` link the sketch could not yet name.

---

## 3. Survey: how a slot is identified

| | A: build every legal id from the rack's dimensions (chosen) | B: parse arbitrary ids with a regex | C: model aisles and lanes as objects, slots as fields |
|---|---|---|---|
| Where does "is this id valid?" come from? | membership in the set the rack already built | a second source of truth: the pattern must agree with the dimensions | traversal of object graphs |
| What happens to `"A01-L03-S01"` in a 2-lane aisle? | rejected — not in the set | the regex accepts it; a separate range check must remember the dimensions | awkward to ask |
| Cost when the rack grows | one loop | regex plus bounds check | new fields |
| What the contract needs | the `Position` lane string, e.g. `A02-L04` | same | same |

**Decision 2: the rack builds every legal lane and slot id from its constructor dimensions, and validation is membership.** A rack constructed with `new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2)` knows exactly four lanes and eight slots; asking whether some id is a lane or slot is a dictionary lookup, not a parse. The formats are fixed by the contract's own example — lanes are `A{nn}-L{nn}` and slots append `-S{nn}`, with two-digit fields (`A02-L04` in `docs/contract/v1.md`).

**Decision 3: occupied means "a pallet id is stored for that slot."** `Rack.Place(slot, pallet)` refuses an occupied slot; `Rack.Take(slot)` refuses an empty one. Both are backstops: the shuttle's own guards check before calling. A pallet is in at most one place at a time — the *fleet-control* skill's *"a pallet must not be recorded in two places, or in none"* — and that is exactly what the scripted test and the demo check.

---

## 4. Decision: position is the lane; distance is the ordinal difference

The contract fixes `Position` as *"the rack lane the shuttle is in, e.g. `A02-L04`"*. So a shuttle reports a **lane**, not a slot. The demo's `Position` therefore changes at `CompleteMove`, never at `StartMove`: while travelling, the shuttle has left the old lane but has not arrived in the new one, and the contract has no `"between"` value. Within-lane travel (creeping from one slot to another in the same lane) is real, but it belongs to M4's segment geometry; M1 moves lane-to-lane, and a move to the current lane is allowed with zero travel.

Distance between two lanes is the difference of their **aisle-major ordinals**: lanes are numbered within an aisle, aisles in order, and the ordinal walks aisle 1's lanes, then aisle 2's, and so on. For a 2×2 rack, `A01-L01 → A02-L02` is three lanes (L01, L02 of A01, then L01, L02 of A02), so its travel time is three times the per-lane duration. The rack owns this arithmetic (`DistanceInLanes`); the shuttle multiplies by the configured per-lane duration.

Rejected: modelling distance in metres and a speed. That is more faithful to a real aisle but needs segment geometry and acceleration curves, which the spec defers to M4 (*"reserve lane segments"*). Per-lane time is the smallest model that makes the milestone's timing real and testable.

---

## 5. Verification table

The reference shape was built and run in `/tmp/opencode/lesson04-scratch/` (deleted afterwards); it is a contract, not a report — the owner's files do not exist yet.

| Claim | How checked | Result |
|---|---|---|
| A rack built from `2 × 2 × 2` has four lanes and eight slots, and rejects `A01-L01-S03` and `banana` | `dotnet test` — `Rack_builds_every_lane_and_slot_from_its_dimensions` | passed |
| Aisle-major distance for `A01-L01 → A01-L02`, `A01-L02 → A02-L01`, `A01-L01 → A02-L02` is 1, 1, 3 | `dotnet test` — `Lane_distance_counts_lanes_within_an_aisle_then_aisles` | passed (asserts 1, 1, 3) |
| Placing into an occupied slot throws; taking from an empty slot throws | `dotnet test` — `Placing_a_pallet_marks_the_slot_occupied`, `Taking_a_pallet_leaves_the_slot_empty` | passed (`InvalidOperationException`) |
| `ShuttleCore` references nothing | `grep ProjectReference src/ShuttleCore/ShuttleCore.csproj` | exit 1 (none) |
| The graph has exactly ten references and the four expected new edges | `grep -rn ProjectReference --include=*.csproj src \| wc -l`; per-edge greps | prints 10; every expected edge present |
| No `.cs` file is duplicated across projects | `md5sum` over all non-`bin`/`obj` sources, `uniq -w32 -D` | prints nothing |

Not yet verified:

- Any file the owner is about to write — this is an assignment contract.
- M4's segment reservations and within-lane travel — deferred on purpose.
- Rack shapes beyond `aisles × lanes × slots` (multi-level, deep-lane shuttles).

---

## 6. What would change this choice

- **M4** needs to say *where in a lane* a shuttle is, or reserve a segment rather than a whole lane → position granularity and distance move to a new record that links here.
- The rack gains real hardware traits (unequal lane lengths, two-deep storage) → a model record supersedes decision 2/3.
- A second consumer of `ShuttleCore` appears (for example a scenario runner) → the project may split or move under a solution folder; a new record links here.

---

## 7. What this record unblocks

- Lesson 04 can hand the owner a fixed rack API and id scheme while leaving every line of the implementation to them.
- Record 06 can define the state machine against a `Rack` whose validation and distance rules are already settled.
- The reviewer can check the graph and the rack behaviour with commands, not opinions.
