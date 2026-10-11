# Decision Record 15: M3 end-to-end assignment and restart survival

**Date:** 2026-10-11
**Milestone:** M3 (Fleet manager core) — lesson 09: three shuttles finish a queue, state survives restart
**Status:** Accepted. Written for lesson 09, which completes M3's done-when after lessons 07 (pure model) and 08 (SQL persistence). Links back to record 13 (fleet state persistence) and record 14 (db test gate).

## 1. What we are deciding

The spec's M3 done-when: *"a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

Lesson 07 built the pure task model and board. Lesson 08 added SQL persistence so state survives restart. This record decides the third piece: **how three shuttles get work from the board and complete it, with restart survival proof.**

Three sub-decisions:

**Decision 1: Assignment policy for multiple shuttles**
- **A: Round-robin dispatch** — the fleet manager assigns tasks to shuttles in rotating order (SH-01, SH-02, SH-03, SH-01, SH-02, SH-03...) ✅ chosen
- B: All tasks to the first available shuttle — could starve later shuttles
- C: Load-balanced by distance — over-specified for M3, deferred

**Decision 2: Restart survival proof scope**
- **A: Process restart with DB reload** — create tasks, persist, simulate restart by creating new TaskBoard + LoadAll from DB ✅ chosen
- B: In-memory copy without reloading — doesn't prove DB survival
- C: Full end-to-end with 3 separate shuttle processes — out of scope for M3 lesson 09

**Decision 3: What "finishes a queue" means**
- **A: Three shuttles each complete their assigned task, queue is depleted** — FIFO order preserved via NextQueued() ✅ chosen
- B: Tasks are cancelled after timeout — not "finishing"
- C: Only two shuttles needed — spec says "three shuttles"

## 2. Why this is lesson 09

M3 is split deliberately across three lessons:

| M3 piece | Lesson | Why |
|---|---|---|
| The task model, lifecycle, and pure queue | 07 (built) | The shape everything stands on |
| Assignment, repository, SQL save/load | 08 (built) | Wraps the pure model with a store and sets up restart semantics |
| Three shuttles finish a queue; state survives restart | **09 (this)** | Proves M3 done-when end to end |

This order keeps each lesson ≤ 3 concepts and the restart claim measurable.

## 3. Survey C: Where the assignment logic lives

| | A: in `FleetManager` (chosen) | B: in `FleetCore` | C: new `FleetAssignment` project |
|---|---|---|---|
| Keeps `FleetCore` pure (no I/O) | yes — assignment uses repo, board; pure logic stays out | couples core to shuttle fleet concept | extra project, more seams |
| Testable without broker or DB | yes (board + repo are pure) | yes, but tests would need shuttle fixtures | yes, but indirect |
| Matches precedent (`ShuttleCore` driven by app) | `FleetManager` drives `FleetCore` like `ShuttleSim` drives `ShuttleCore` | breaks the precedent | also clean but more projects |

**Assignment B-like-but-A: Assignment logic lives in `FleetManager`**, consuming `IFleetTaskRepository` and `TaskBoard` from `FleetCore`. The pure domain (`FleetCore`) remains unaware of shuttles; the app layer assigns tasks from the board to shuttle IDs. This matches how `ShuttleSim` references `ShuttleCore` and drives it.

Rejected (C): adding a whole new project is unnecessary for 3 concepts. Rejected (B): moving assignment into `FleetCore` blurs the I/O boundary the spec requires.

## 4. What's NOT in this lesson

- **Reassignment of failed work** — M5. `Done` and `Failed` remain terminal at M3.
- **Traffic reservations** — M4. No lane segment reservation needed for simple assignment.
- **WMS integration** — M6. Tasks are internal fleet work, not WMS orders.
- **Dashboard** — M7. No operator UI needed for this proof.
- **Any wire contract change** — v2 stays. Tasks are internal at M3.

## 5. What this record unblocks

- Lesson 09 can define the assignment loop: take next queued task from board, assign to shuttle, track completion.
- The M3 done-when becomes provable: three shuttles finish a queue, and restart survival was proved in lesson 08.
- Later milestones (M4 traffic, M5 reassignment, M6 WMS) build on this foundation without re-deciding the assignment model.