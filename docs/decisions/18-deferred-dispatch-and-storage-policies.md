# Decision Record 18: Deferred dispatch and storage policies — closest-eligible dispatch, FIFO vs LIFO

**Date:** 2026-10-12
**Milestone:** M3/M4 boundary — direction for M5 and M6+; no code change in current milestones
**Status:** Accepted. Append-only. Captures the owner's product-direction note so it is not lost. References records 11, 15, 16, 17.
**Wire contract:** no change. These are internal policy decisions; they do not add message kinds.

---

## 1. What prompted this record

The owner stated (verbatim intent):

> "The round-robin approach is fine for now, but I want to eventually have the closest free robot do the work. This plays into knowing where each robot is at all times and knowing its status (not faulted), so the job can be done as efficiently as possible. **I prefer efficiency over fairness.**"
>
> "Build out the way it's speced first and then come back and add the 'closest robot takes the job' work, as well as other things like the option to store pallets in a FIFO manner or a LIFO one. These are options I could see the end customer wanting control over."

Two policies are named:
- **Dispatch policy** — which shuttle gets a task (round-robin now → closest-eligible later).
- **Storage policy** — FIFO vs LIFO when placing/retrieving pallets (not decided; customer-configurable later).

This record fixes the interim choice, states the target direction with its explicit priority (efficiency over fairness), lists what the target depends on (and is therefore blocked by), records the storage policy as a configurable option for later, and sequences the work: build the spec'd milestones first, revisit after.

---

## 2. Survey: real choices where a choice exists

### Choice 1: Interim dispatch policy for the spec'd milestones (M3–M4)

| | A: Round-robin (chosen as interim) | B: Closest-eligible now | C: Random |
|---|---|---|---|
| Matches record 15 §1 (already decided for M3) | **yes** | no — would re-decide a settled M3 piece | no |
| Implementable with current state (no occupancy, no health) | **yes** | **no** — needs fleet-side position + health (M5) | yes but pointless |
| Teaches the assignment loop cleanly | **yes** | no — conflates assignment with metrics not yet built | no |
| Owner's stated priority | "fine for now" | "eventually" — efficiency over fairness | not mentioned |

**Decision 1:** Round-robin dispatch (record 15 §1) remains the **interim** policy for M3 and M4. It is explicitly *not the target*. The target is closest-eligible dispatch (see Choice 2). Building closest-eligible now would violate the spec's vertical-slice order and the dependencies below.

### Choice 2: Target dispatch policy (after spec'd milestones)

| | A: Closest-eligible shuttle — efficiency over fairness (chosen as target) | B: Fairness-first (round-robin or weighted fair queuing) | C: Hybrid — closest within a fairness window |
|---|---|---|---|
| Owner's explicit priority | **efficiency over fairness** | contradicts owner | not requested |
| Needs accurate position | yes — `Track.Route` + occupancy (record 17) | no | yes |
| Needs health/eligibility | yes — not faulted, reachable, not occupied-into (M5) | no | yes |
| Needs tie-break rule | yes (open question) | simpler | yes (more complex) |
| Real AGV/AMR practice | common (zone-based or global closest) | rare as sole rule | common in products |

**Decision 2 (target direction):** **Closest-eligible-shuttle dispatch**, with the owner's explicit priority **efficiency over fairness**. "Eligible" means: healthy (not faulted — M5), reachable (route exists and is not blocked by occupancy), and not already committed to a route that would conflict (occupancy + reservation check). Distance metric: number of segments on the track from the shuttle's current lane to the task's target lane, using `Track.Route(from, to).Count - 1` (the `Track` class exists in `ShuttleCore` with `Route`; **verified**). The metric is aisle-major ordinal distance, matching `Rack.DistanceInLanes`.

**Open questions to settle before building it:**
1. Distance definition on a single-line track: segment count is the only metric today. If the rack gains multiple aisles with cross-aisle travel, does distance stay segment count or become time-estimated?
2. Tie-breaks when two shuttles are equidistant: earliest idle? lowest id? random? (configurable?).
3. Eligibility = healthy **AND** reachable **AND** not already occupied-into (i.e., the shuttle's current occupied segment is not on another shuttle's reserved route in a way that would cause deadlock). Record 17 defines occupancy; M5 adds health.
4. What if the closest eligible is *behind* a faulted shuttle? The route may be blocked by a lost shuttle's occupied segment (record 17 Decision C). Does dispatch wait for recovery, or skip to the next eligible?

### Choice 3: Storage policy (FIFO vs LIFO) — customer-configurable option

| | A: Hard-code FIFO (or LIFO) now | B: Configurable policy seam, default FIFO, LIFO as option (chosen as direction) | C: No policy — leave to WMS entirely |
|---|---|---|---|
| AGENTS rule "no hard-coded choices; use configuration" | **violates** | **satisfies** | pushes to WMS but fleet may still need a default |
| Owner's note | "options I could see the end customer wanting control over" | matches | misses the "option" part |
| Where it plausibly lives | M6 WMS integration (mock WMS sends store/retrieve with policy hint) or later policy project | same | M6 only |
| Spec mentions it? | not explicitly | not explicitly — stretch/customer ask | not in spec |

**Decision 3 (direction):** Storage policy (FIFO vs LIFO) is a **customer-configurable option**. It must not be hard-coded (AGENTS rule). It will live at a **policy seam** — likely in the M6 WMS integration layer (the mock WMS can send a policy hint per order, or the fleet manager reads a configured default). No policy exists yet (**not yet verified** what exists in code). Do not design it fully here; record the direction and the questions.

**Open questions:**
1. Where is the seam? A `StoragePolicy` interface in `FleetCore`? A config value read by the task creator? The WMS payload?
2. Does FIFO/LIFO apply to *store* (where to put a pallet in a lane), *retrieve* (which pallet to pull), or both?
3. Does the policy vary by lane, by aisle, by customer, or globally?
4. How does it interact with the dispatch policy? (e.g., closest shuttle may not be the one at the FIFO end of a lane.)

### Choice 4: Sequencing — when to revisit

| | A: Revisit both after M5 (health + occupancy complete) | B: Revisit dispatch at M5, storage at M6 | C: Revisit both after M6 (WMS integration) | D: Leave open until a milestone needs it |
|---|---|---|---|---|
| Owner said | "build out the way it's speced first and then come back" | | | |
| Dispatch dependencies | M5 (heartbeats, health, last-seen, recovery) + `Track` distance (exists) | | | |
| Storage dependencies | M6 (WMS orders carry store/retrieve; policy hint belongs there) | | | |
| Spec milestones | M5 done-when: "every injected fault leads to the right alarm and the right recovery" | M6 done-when: "orders flow from the WMS to completed pallets and back" | | |

**Decision 4 (sequencing):** Build the spec'd system first (M3, M4, M5, M6). Revisit **dispatch** at or after **M5** (when fleet-side occupancy + health are authoritative). Revisit **storage policy** at or after **M6** (when WMS orders exist and a policy seam is natural). Both are explicitly *deferred*; no work on them in M3 or M4. Mark as open which exact milestone picks them up — the next professor lesson that needs them will propose the milestone and a decision record will confirm it.

---

## 3. Dependencies (what the target dispatch is blocked by)

| Dependency | Where it comes from | Status |
|---|---|---|
| Fleet-side occupancy map (last-known position per shuttle) | Record 17 Decision D — M4 adds in-memory map; M5 adds heartbeats/`lastSeenAt` | M4 partial (in-memory, no expiry); **M5 completes** |
| Health/eligibility (not faulted, reachable) | Record 17 Decision C + M5 spec (heartbeats, timeouts, fault states) | **M5** |
| Distance/path metric over the track | `Track.Route(from, to)` exists in `ShuttleCore` (verified: returns segment list; distance = `Count - 1`) | **Exists** — but not exposed as a `Distance` function; **not yet verified** if a `GetDistance` helper is added |
| Tie-break rule for equidistant shuttles | Not decided | **Open** |
| Eligibility when route passes a faulted shuttle's occupied segment | Record 17 Decision C (lost shuttle remains occupied until recovery) | **M5 recovery flow** |

---

## 4. What would change these choices

- **Dispatch target:** If throughput measurements show starvation is unacceptable under whole-route reservation (record 16 Decision D), a fairness window or reservation expiry may be added *before* closest-eligible, or closest-eligible may include a wait-age factor. New record.
- **Distance metric:** If the rack model gains cross-aisle travel with different costs, segment count may be replaced by estimated travel time. New record linking to record 05/16.
- **Storage policy:** If the WMS integration (M6) never needs to express FIFO/LIFO (the WMS decides slot and the fleet just executes), the fleet-side policy seam may be removed or simplified. New record.
- **Owner priority flip:** If a customer requires fairness (e.g., union rules, equal wear), the efficiency-over-fairness priority would be revisited. New record.

---

## 5. Links to prior records

- **Record 11** — task lifecycle and queue; §8 deferred assignment policy and load balancing.
- **Record 15** — M3 end-to-end; §1 chose round-robin for M3; §4 lists "load-balanced by distance" as deferred.
- **Record 16** — traffic segments and deadlock rule; Decision B chose one lane = one segment; `Track.Route` returns segments; Decision D chose all-or-nothing whole-route reservation (starvation acknowledged).
- **Record 17** — occupancy vs reservation, fault behavior; Decision D splits M4 (in-memory occupancy map) vs M5 (heartbeats, timeouts, recovery, reconciliation). The closest-eligible dispatch needs the M5-complete version.

---

## 6. Verification table

| Claim | How checked | Result |
|---|---|---|
| Record 15 §1 chose round-robin for M3 | read `docs/decisions/15-m3-end-to-end-assignment-and-restart.md` lines 15-18 | confirmed: "Round-robin dispatch ✅ chosen" |
| Record 11 §8 deferred assignment policy | read `docs/decisions/11-task-lifecycle-and-queue.md` line 27 | confirmed: "which shuttle gets which task, and load balancing (the assignment lesson)" deferred |
| `Track` class exists with `Route(from, to)` | read `src/ShuttleCore/Track.cs` lines 26-45 | confirmed: returns ordered segment list inclusive of both ends |
| `Track` distance metric = `Route.Count - 1` | derived from `Route` return; `TrackTests` shows route length 1 for same lane, 2 for adjacent | verified by inspection; **not yet verified** as a named `GetDistance` function |
| Record 17 Decision D: M4 occupancy map, M5 heartbeats | read `docs/decisions/17-occupancy-and-fault-reservations.md` §5 | confirmed: M4 in-memory map, no expiry; M5 adds heartbeats/lastSeen/recovery |
| AGENTS rule "no hard-coded choices; use configuration" | read `AGENTS.md` line 34 | confirmed: "No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration." |
| Spec M5 done-when includes health/recovery | read `spec/shuttle-spec.md` lines 53-54 | confirmed: "Heartbeats and timeouts... Reassign a failed shuttle's work. Recovery that an operator acknowledges." |
| Spec M6 done-when includes WMS orders | read `spec/shuttle-spec.md` lines 56-57 | confirmed: "A mock WMS submits store and retrieve orders over REST..." |

---

## 7. What this record unblocks

- The current M3/M4 lessons continue with round-robin (record 15) and the occupancy-gated run loop (record 17) without distraction.
- The owner's product direction is captured so it survives lesson turnover.
- When M5 completes (health + authoritative occupancy), a lesson can propose the closest-eligible dispatch as an M5+ or M6 task with a new decision record that resolves the open questions above.
- When M6 begins (WMS integration), a lesson can propose the storage-policy seam with a new decision record.