# Decision Record 24: The aisle/lane model seam — identity names on a flat ordinal line

**Date:** 2026-10-16
**Milestone:** spans M1 (record 05's rack model) and M4/M5 (records 16, 17, 23, which all sit on that model). Documentation only: no milestone code changes, no tests, no wire change.
**Status:** Accepted. Append-only. Supersedes nothing — record 05 stands as written; this record names a seam *inside* its model, it does not change any of its decisions. Links back to records 05, 16, 17, 18, 23.
**Builds on:** [record 05](05-shuttle-core-and-rack-model.md) (lane ids, the aisle-major ordinal, `DistanceInLanes`), [record 16](16-traffic-segments-and-deadlock-rule.md) (a segment is a lane; `Track.Route` is the same ordinal range), [record 17](17-occupancy-and-fault-reservations.md) (occupancy keyed by lane), [record 18](18-deferred-dispatch-and-storage-policies.md) (its open question about cross-aisle distance), [record 23](23-blocked-path-fault-and-second-alarm.md) (`IsRouteBlocked` — the check that made the seam visible).
**Replaced by:** a later record that adds per-aisle routing, deep-lane slots, multi-level racks, or real travel geometry. Each links here. Records are append-only.
**Wire contract:** **no change, no version bump.** `Position` stays the lane string documented since v1 — *"The rack lane the shuttle is in, e.g. `A02-L04`"* (`docs/contract/v1.md` line 13; unchanged in `docs/contract/v3.md` line 188). `ContractVersion.Current` stays `3`. `docs/contract/` and `src/` are untouched.

---

## 1. What we are deciding

During lesson 16's `IsRouteBlocked` test, the owner noticed that in a 2×2 rack, both `A01-L02` and `A02-L01` are "on the route" from `A01-L01` to `A02-L02`. The cause is the rack model itself: one flat aisle-major ordinal line (`src/ShuttleCore/Rack.cs:22-37, 51-52, 90-98`). Every geometry-shaped operation — distance, route, blocking — uses that single ordinal. Nothing in the code treats "aisle" as a concept after construction. So "aisle" and "lane" are functionally identical today; only the id string's shape differs.

The owner asked the fair question: why does the aisle/lane distinction exist at all, and does this rack design correspond to a real warehouse?

We agreed on the answer: **the distinction is a deliberate placeholder, not a bug and not a re-architecture job.** The names mirror real warehouse vocabulary and the frozen contract; the flat line underneath them is a simplification that is honest for within-aisle moves and unfaithful for cross-aisle ones. What was missing was the seam itself, written down — so it is named, reasoned, and parked, not rediscovered as a surprise at 1 a.m.

This record therefore decides six things:

**A.** What the model is today (measured, not remembered).
**B.** What a real pallet-shuttle warehouse is, and how its axes differ from the flat line.
**C.** The specific unfaithfulness: cross-aisle routes.
**D.** The choice: keep the names, keep the flat model, write the seam down.
**E.** What is explicitly out of scope (everything structural).
**F.** What events would each get their own record, linking back here.

---

## 2. What the model is today: one flat aisle-major ordinal

The constructor walks aisles, then lanes, and hands out one running ordinal:

```csharp
// src/ShuttleCore/Rack.cs:22-28 (excerpt)
var ordinal = 0;
for (var aisle = 1; aisle <= AisleCount; aisle++)
{
    for (var lane = 1; lane <= LanesPerAisle; lane++)
    {
        var laneId = $"A{aisle:00}-L{lane:00}";
        _laneOrdinals[laneId] = ordinal++;
```

Line by line: `ordinal` starts at zero. The outer loop walks aisles in order. The inner loop walks the lanes of one aisle. The id string formats the two numbers — that is the **only** place the aisle number survives. The dictionary maps each id to its flat ordinal. After this loop, no code ever splits an id back into an aisle and a lane.

Distance is the difference of ordinals, with no aisle term anywhere:

```csharp
// src/ShuttleCore/Rack.cs:51-52
public int DistanceInLanes(string fromLane, string toLane) =>
    Math.Abs(Ordinal(fromLane) - Ordinal(toLane));
```

Route blocking is the same idea one step further: a route is the ordinal range between two lanes, and a blocked lane anywhere in that range stops the move:

```csharp
// src/ShuttleCore/Rack.cs:90-98 (excerpt)
public bool IsRouteBlocked(string fromLane, string toLane)
{
    var start = Math.Min(Ordinal(fromLane), Ordinal(toLane));
    var end = Math.Max(Ordinal(fromLane), Ordinal(toLane));
    foreach (var blocked in _blockedLanes)
        if (_laneOrdinals[blocked] >= start && _laneOrdinals[blocked] <= end)
            return true;
    return false;
}
```

Outside tests, the word "aisle" appears in `src/` only in this constructor, its parameters, its two properties, and the two `Program.cs` sites that construct a rack (measured — see the verification table). No type anywhere takes an aisle as a *routing* concept. `TrafficCore.Track` builds its segment list from `Rack.LaneId` in this same order (record 16 §3), so traffic inherits the flat line unchanged.

The result: **the names suggest a 2-D grid; the model is a 1-D line.**

| The names suggest (a grid) | The model is (a line) |
|---|---|
| Aisles are corridors side by side; lanes sit *within* each corridor | Every lane is one point on one sequence: `A01-L01`=0, `A01-L02`=1, `A02-L01`=2, `A02-L02`=3 |
| `A02-L01` is "next to" `A01-L01` at the aisle mouth | `A02-L01` is **two** steps from `A01-L01` — past `A01-L02` |
| Turning into an aisle is a special move with its own cost | There are no turns; only forward and back along the ordinal |
| Two shuttles in two aisles move on independent geometry | Both draw from one shared ordinal space; records 16/17 treat it as one track |

---

## 3. What a real pallet-shuttle warehouse is

These are standard facts about deep-lane pallet-shuttle systems. They are domain knowledge, not something a command in this repo can measure; the verification table says so plainly.

An **aisle** is the physical corridor a shuttle travels down. In the usual design, one shuttle — or one small fleet under one controller — works one aisle. A shuttle does not drive from one aisle into another. Getting a pallet across aisles needs extra hardware: a transfer car, a lift, or a forklift.

A **lane** (also called a channel, sometimes a level) is a storage track that runs *inside* an aisle. Pallet-shuttle warehouses are usually **deep-lane**: a lane holds pallets several deep, stacked nose to tail, fed from one end. The shuttle enters the lane to deposit or retrieve a pallet, then returns to the corridor.

A **slot** is one position *within* a lane — think of it as the depth index: first pallet deep, second deep, and so on.

So a real rack moves on two axes. The shuttle travels along the aisle (the corridor axis). The pallet sits at a depth inside a lane (the lane axis). A real id like `A02-L04-S02` names all of that: aisle 2, lane 4 of that aisle, second slot deep.

Our ids carry the same numbers. Only one of the axes moves: the flat ordinal. The aisle number is a label on a group; the lane number orders positions on a line; the slot number names storage positions that the shuttle walks between locally (record 05 deferred within-lane travel to M4).

---

## 4. The specific unfaithfulness: cross-aisle routes

In a 2×2 rack, the model says the route from `A01-L01` to `A02-L02` is three lanes wide, and that it passes through `A01-L02` and `A02-L01`. Measured, not remembered — the toy in `/tmp/opencode/d24toy` (a scratch copy of `Rack.cs`, deleted afterwards) printed:

```
Distance from A01-L01 to every lane:
  A01-L01 -> A01-L01: 0
  A01-L01 -> A01-L02: 1
  A01-L01 -> A02-L01: 2
  A01-L01 -> A02-L02: 3

Distance A01-L01 -> A02-L02: 3
Route A01-L01 -> A02-L02 blocked when A01-L02 is blocked: True
Route A01-L01 -> A02-L02 blocked when A02-L01 is blocked: True
Within-aisle route A01-L01 -> A01-L02 blocked by A02-L01: False
```

Read what that implies. To "drive" from `A01-L01` to `A02-L02`, the shuttle passes through `A01-L02` (still in aisle 1) and then through `A02-L01` (now in aisle 2). In the model, crossing between aisles is just two more steps on the same line. Physically, that would mean the shuttle drives from one aisle into the next **on one rail**. Real shuttles do not do that. A real cross-aisle move needs a transfer car or a lift, or it does not happen at all.

The unfaithfulness is specific and bounded:

- **Cross-aisle routes are unfaithful.** Distance, route, and blocking across an aisle boundary describe a straight drive that has no physical counterpart. Record 18 §5/§6 already flagged cross-aisle distance as an open question; this record names why it is open, and defers the metric to the M4 real-travel record.
- **Within-aisle routes are fine.** `A01-L01 → A01-L02` walks lanes in physical order along one corridor. The toy confirms a blocked lane in aisle 2 never touches an aisle-1 route.
- **The honest limit, stated:** record 23's blocked-path check is correct *as a model of one corridor*. It becomes wrong only when a route crosses an aisle boundary — and no current scenario requires that move to be physically real.

---

## 5. Why the names are kept: identity, not geometry

**Decision D: keep the `A{nn}-L{nn}` names and the flat model exactly as they are. Write the seam down (this record). Do not re-architect.**

The contract has fixed `Position` as the lane string since v1: *"The rack lane the shuttle is in, e.g. `A02-L04`"* — still the wording in `v3.md` (line 188, measured). A real shuttle is identified by the aisle and lane it sits in; operators, WMS hosts, and fleet software all speak that vocabulary. The wire already speaks it too.

The two names also carve out the seam where the real hierarchy will live. When per-aisle routing arrives, "aisle" becomes a routing boundary with no rename. When deep-lane storage arrives, "lane" keeps its meaning and "slot" gains depth. The information is already in every id, waiting for code that knows what to do with it. **The naming is identity, not geometry:** `A02` says which aisle-group a shuttle is named under; `L04` says which lane within it. It does not say how to drive there — and today it cannot.

Rejected alternatives:

- **Rename to one flat lane list** (`L01`…`Lnn`): honest to the model, but breaks the contract's `Position` format and record 05's id scheme, forces a version bump and fixture churn, and destroys the future seam — when hierarchy arrives, every id on the wire changes *again*. Cost now, cost later, gain never.
- **Re-architect to a real hierarchical model now** (per-aisle routing, deep-lane depth): no milestone needs it. M4's `Track` works on the flat order today (record 16 §3). Building hierarchy ahead of need is the throwaway work AGENTS.md forbids, and it would reopen records 05, 16, 17, and 23 simultaneously for no behavior change.
- **Add an `Aisle` field to status messages**: `Position` already carries the whole id; a second, redundant field could disagree with it and would need its own validation. Nothing consumes it yet.

---

## 6. Explicitly out of scope

This record changes nothing structural. Concretely:

- No change to `Rack` — not a rename, not a new type, not a new method.
- No per-aisle routing. `Track.Route` and `IsRouteBlocked` stay ordinal-range.
- No depth or deep-lane modeling. Slots stay storage positions, not travel segments.
- No timing change. Per-lane durations (record 05 §4) are untouched.
- No wire change. `Position` unchanged; no version bump; `docs/contract/` untouched.
- Records 05, 16, 17, 18, and 23 are **not** amended. This record stands beside them.

The fleet-control skill's honesty rule applies here the same way it applies to timing and radios: *state plainly where the sim is kinder than a warehouse.* A warehouse cannot drive between aisles on one rail. Our model can, for exactly as long as this seam is unfixed.

---

## 7. What would change the choice

Each of these events gets **its own record**, linking back to this one. None of them is a reason to move today.

- **M4 segment geometry / real travel** (within-lane position, metres, acceleration, transfer times) → the distance model and `Track.Route` gain physical shape; per-aisle boundaries appear for free. Record 16 §6 already owns the granularity half of this question.
- **Traffic control that must honor per-aisle reachability** → two shuttles at `A01-L01` and `A02-L01` are on *adjacent* segments today, so head-on rules apply between them; in a real rack they are in different corridors and never meet. Records 16 and 17 reopen together the day multi-aisle fleets run.
- **Deep-lane storage** (pallets several deep; slot becomes a travel position) → record 05 §5's own deferred item; the slot axis stops being storage-only.
- **Multi-level racks** (a vertical lift axis) → a third naming axis; a new model record decides where it lives in the id and in the geometry.
- **A second consumer that needs true geometry** (a scenario runner, an M7 dashboard map showing where shuttles actually are) → the consumer forces the hierarchy; new record.

---

## 8. Verification table (measured)

Everything below was checked on **2026-10-16** by a command or a file read. The toy ran at `/tmp/opencode/d24toy` (a scratch copy of `Rack.cs`; deleted afterwards). No file under `src/` was changed, and no existing decision record was edited.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Next decision number is 24 | `ls docs/decisions/` | latest existing record is `23-blocked-path-fault-and-second-alarm.md` |
| The rack builds one flat ordinal; the aisle survives only in the id format string | read `src/ShuttleCore/Rack.cs:22-37` | single loop, `ordinal++`; `$"A{aisle:00}-L{lane:00}"` at line 27 |
| Distance is the ordinal difference, no aisle term | read `src/ShuttleCore/Rack.cs:51-52` | `Math.Abs(Ordinal(fromLane) - Ordinal(toLane))` |
| Route blocking is an ordinal range, inclusive, spanning aisles | read `src/ShuttleCore/Rack.cs:90-98` | `min`/`max` of the two ordinals; any blocked lane in range returns `true` |
| Nothing else in `src/` treats "aisle" as a concept | `grep -rniE "aisle" src --include='*.cs'` (tests excluded) | only `Rack.cs` constructor/parameters/properties and the two `Program.cs` rack-construction sites |
| The contract fixes `Position` as the lane string `A02-L04` | read `docs/contract/v1.md:13` and `docs/contract/v3.md:188` | "The rack lane the shuttle is in, e.g. `A02-L04`" on both pages |
| Cross-aisle route distance in a 2×2 rack is 3, through both intermediate lanes | toy at `/tmp/opencode/d24toy`, `dotnet run` | prints `0, 1, 2, 3` per lane from `A01-L01`; `A01-L01 -> A02-L02: 3` |
| Blocking either intermediate lane blocks the cross-aisle route | same run | `True` for `A01-L02` blocked; `True` for `A02-L01` blocked |
| Within-aisle routes ignore the other aisle | same run | `A01-L01 -> A01-L02` with `A02-L01` blocked: `False` |
| No existing record names this seam | `grep -rniE "deep.?lane\|cross.?aisle\|transfer" docs/decisions spec` | only forward-looking "what would change" lines in records 05 §5, 16 §6, 17 §6, 18 §5/§6; none states that aisle and lane are identical in the model today |
| Real-warehouse facts (aisle = corridor, one shuttle per aisle, deep-lane, slot depth, transfer car/lift) | domain knowledge; no command in this repo can measure a physical warehouse | **not verified by command** — stated as domain facts, flagged as such in §3 |

Not yet verified: how a transfer car or lift would appear in the model (no design exists); whether the M4 real-travel model keeps per-lane durations or replaces them (deferred to that record); whether any future multi-aisle scenario changes record 16's deadlock rule (reopens with records 16/17, per §7).

---

## 9. What this record unblocks

- The next reader who hits the cross-aisle surprise in an `IsRouteBlocked` test finds the seam named here instead of re-deriving it from scratch.
- Lessons on traffic (M4) and faults (M5) can keep using the flat model without re-defending the aisle/lane names each time: they link to this record.
- Record 05 stays fully valid — this record narrows none of its decisions. Its own last "Not yet verified" bullet (*"Rack shapes beyond `aisles × lanes × slots` (multi-level, deep-lane shuttles)"*) is exactly the seam, and now it has a name and a home.
- When real geometry lands — M4 travel, deep-lane storage, or per-aisle traffic — its record starts from a written baseline instead of archaeology.
