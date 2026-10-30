# Decision Record 44: Rack grid model — Manhattan geometry

**Date:** 2026-10-29
**Milestone:** Post-M7 (grid fix). Builds directly on [record 24](24-aisle-lane-model-seam.md) — it does not supersede or replace 24; 24 stays as the written seam. This record builds the grid model that 24 described.
**Status:** Accepted. Append-only.
**Replaced by:** a later record that adds per-aisle routing boundaries, deep-lane depth travel, or multi-level axes.

---

## 1. What we are deciding

Record 24 named the flat ordinal line (`A01-L01`=0, `A01-L02`=1, `A02-L01`=2, ...) as the model's actual geometry, and showed how cross-aisle routes become physically unfaithful. The owner asked to fix the rack to match a real warehouse: an aisle is a corridor axis, a lane is a track inside it, and movement is on a 2-D grid, not a straight line.

This record decides the concrete change to `src/ShuttleCore/Rack.cs`:

1. **Distance** becomes Manhattan (`abs(aisle_diff) + abs(lane_diff)`) instead of ordinal difference.
2. **Route blocking** checks the grid rectangle between the two lanes (inclusive on both axes), not the ordinal range.
3. **No contract change.** `Position` stays the lane string (`A02-L04`). `ContractVersion.Current` stays `4` (or `3` depending on state — unchanged by this).
4. **No new types.** Only `Rack.cs` arithmetic changes; `Track`, `Shuttle`, `Contracts` untouched.

---

## 2. What changes in `Rack.cs`

```csharp
// DistanceInLanes: Manhattan instead of ordinal difference
public int DistanceInLanes(string fromLane, string toLane) =>
    Math.Abs(AisleOf(fromLane) - AisleOf(toLane))
    + Math.Abs(LaneOf(fromLane) - LaneOf(toLane));

// BlockedLanesOnRoute: rectangle on the grid (inclusive)
public IEnumerable<string> BlockedLanesOnRoute(string fromLane, string toLane)
{
    var aMin = Math.Min(AisleOf(fromLane), AisleOf(toLane));
    var aMax = Math.Max(AisleOf(fromLane), AisleOf(toLane));
    var lMin = Math.Min(LaneOf(fromLane), LaneOf(toLane));
    var lMax = Math.Max(LaneOf(fromLane), LaneOf(toLane));
    foreach (var lane in _blockedLanes)
    {
        var a = AisleOf(lane);
        var l = LaneOf(lane);
        if (a >= aMin && a <= aMax && l >= lMin && l <= lMax)
            yield return lane;
    }
}
```

Private helpers `AisleOf` and `LaneOf` parse `A{nn}-L{nn}` with `int.Parse`. Validation stays membership-based (`IsLane`). The ordinal dictionary (`_laneOrdinals`) remains for compatibility with existing callers but is no longer used for distance or blocking.

---

## 3. Verification

- `dotnet build` passes.
- `dotnet test` passes for `ShuttleCore.Tests`.
- Cross-aisle route distance is now `abs(a1-a2) + abs(l1-l2)`; within-aisle routes match before; cross-aisle routes no longer count intermediate lanes from other aisles as straight steps.
- `IsRouteBlocked` on a cross-aisle route only includes lanes inside the rectangle.

---

## 4. What is out of scope

- Per-aisle routing boundaries (a shuttle does not turn from one aisle to another in this model; the rectangle is a mathematical fix, not a traffic-control change).
- Deep-lane depth travel (`Slot` remains storage-only).
- Contract changes (`Position` unchanged).
- `Record 24` is untouched (append-only).
