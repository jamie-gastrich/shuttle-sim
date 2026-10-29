# Lesson 35: M7 Full-Recovery Stretch Verification

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET · **Prerequisite:** [lesson 34](34-blocked-lane-removal-and-pause-semantics.md)

**Concepts this assignment requires:**

- [ ] **Measuring recovery latency with real timestamps** — how to time `Acknowledge` → shuttle `Idle` with no fault, using message envelopes and alarm SQL rows → [§1](#1-measuring-recovery-latency-with-real-timestamps)
- [ ] **Cross-state consistency checklist after the full cycle** — confirming fault cleared, blocked lane unmarked, alarm acknowledged, dispatch resumed, all in one observation → [§2](#2-cross-state-consistency-checklist)

Two new concepts. That stays under the cap.

**Not in this lesson:** changing the message contract (`ContractVersion.Current` stays `4`), adding a new dashboard feature, writing a new test project, or fixing `TaskBoard` or `PauseState`. Those pieces are already complete from lessons 30–34.

---

## Before we start

Quick check, out loud:

1. Have you started `serve` before, with `docker compose -f docker/compose.yml up -d` running the broker and database?
2. Do you know how a blocked lane is injected — through `ShuttleSim/appsettings.json` (`"BlockedLanes": ["A01-L02"]`) — and that the shuttle faults on its first move into it?
3. Can you explain the three commands in order: what `Pause` does to `PauseState`, what `Acknowledge` does to the alarm SQL table and to the recovery MQTT topic, and what `Resume` lifts?

If any of those feels new, say so. We will walk through it before building.

---

## Where this fits

Lesson 34 made the blocked lane clear and fixed the dispatch pause. Lesson 32 taught the full workflow. The milestone M7 is still `in progress` because the stretch scenario — fault → Pause → Acknowledge → Resume, with a measured time between Acknowledge and the shuttle returning to `Idle` with no fault — was not yet verified. This lesson teaches how to verify it, step by step, with real output you observe yourself.

---

## Verification table

Checked on **2026-10-29** in this repo. Scratch work in `/tmp/opencode/l35verify` (cleaned up). Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | Build is clean after lesson 34 | `cd src && dotnet build ShuttleFleet.slnx -v q` | Build succeeded, 0 warnings, 0 errors |
| 2 | Non-integration tests pass | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | 247 passed, 0 failed |
| 3 | Contract version is still `4` | `grep -r "ContractVersion" src/Contracts/` | `Current = 4` |
| 4 | `HandleOperatorCommandAsync` still publishes `RecoveryCommandMessage` | `grep -A 5 "Acknowledge" src/FleetManager/Program.cs` | `PublishAsync(topics.RecoveryCommand(...)` at line 354 |
| 5 | `PauseState` is shared | `grep -q "PauseState" src/FleetManager/Program.cs` | Confirmed |
| 6 | `Shuttle.Recover()` clears fault and sets `Idle` | `grep -A 3 "_state = ShuttleState.Idle" src/ShuttleCore/Shuttle.cs` | Confirmed at line 152 |
| 7 | `appsettings.json` supports `BlockedLanes` | `cat src/ShuttleSim/appsettings.json` | `"BlockedLanes": []` |

The broker and database were left down after these checks.

---

## Options survey

There is no new design choice in this slice. We are not changing code; we are verifying existing behavior. The only option is how strict to be about the measurement method.

| Option | What it means | Why we do not need to choose |
|---|---|---|
| A. Measure with `DateTime.UtcNow` in a script (chosen by default) | A scratch bash or C# script records start and end times. | The message envelope already carries `SentAt`; we only read it. |
| B. Add a dedicated timing field to the contract | A new `MeasuredAt` or `LatencyMs` field in messages. | Would change the contract version; rejected for this slice. |

No decision record needed. We use the existing envelope `SentAt` and alarm `created_at` / `acknowledged_at` SQL columns.

---

## Decision

No new decision record (`docs/decisions/`). The verification method uses the existing message envelope (`MessageEnvelope.SentAt`), the existing SQL alarm table (`alarmRepository.AcknowledgeAsync`), and the existing `Serve` process wiring. If a future milestone needs automated latency tracking in the message contract, that will need a new decision and a version bump.

---

## The lecture

Today we are going to learn about **measuring the M7 stretch scenario** and **checking cross-state consistency after the full recovery cycle**.

### 1 Measuring recovery latency with real timestamps

**What it is.** The time from the `Acknowledge` command to the shuttle's next status message showing `State = Idle` and `FaultReason = null`. We measure it by comparing the `SentAt` timestamp in the `RecoveryCommandMessage` payload (or the `Acknowledge` publish time) to the `SentAt` timestamp in the next `ShuttleStatusMessage` that reports `Idle` with no fault.

**Why we need it.** The M7 stretch goal asks for this measurement. Without a measured number, the milestone stays unverified. With a measured number — even a rough one like "under 500 ms with a local broker" — you can explain the recovery speed to a CTO and compare it to the pause-only latency from lesson 30.

**A real-world example.** A warehouse supervisor presses *Acknowledge* on the dashboard. The supervisor expects the shuttle to respond quickly. In a real warehouse, a slow response might mean a wireless delay, a dropped MQTT message, or a shuttle that missed the recovery command. In the sim, we measure the same thing to prove the message path is healthy.

**The code to observe.** In `src/FleetManager/Program.cs`, the `Acknowledge` branch (lines 347–356) builds a `RecoveryCommandMessage` with `MessageEnvelope` created from `DateTime.UtcNow`. That `SentAt` is the start of our timer.

```csharp
var recoveryEnvelope = MessageEnvelopes.Create(
    MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
var recoveryCmd = new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId);
await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId),
    JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);
```

The shuttle receives this on `shuttle/{id}/recovery` (topic from `TopicScheme`), deserializes it, and calls `Shuttle.Recover(DateTime.UtcNow)`. The status message it sends back carries its own `MessageEnvelope` with a new `SentAt`. The difference between those two timestamps is the recovery latency.

**How to measure it in practice.** Because the `serve` process runs continuously, you observe it by:

1. Starting `serve` with the broker and database (`docker compose -f docker/compose.yml up -d`).
2. Configuring `ShuttleSim/appsettings.json` with `BlockedLanes: ["A01-L02"]`.
3. Starting `ShuttleSim` for SH-01 (`dotnet run --project src/ShuttleSim -- --id SH-01`).
4. Watching the shuttle's first status message: it will show `State: Fault`, `FaultReason: "blocked path"`, and the alarm will appear in SQL.
5. Publishing the `Acknowledge` command (through the dashboard or directly via MQTT on `fleet/operator/command`).
6. Watching the shuttle's next status message: `State: Idle`, `FaultReason: null`.
7. Recording the `SentAt` values (or just the clock time you observe) in a scratch file.

The toy example below shows the exact sequence on paper. The real measurement uses the running services.

**What this does not do.** It does not add a new message field or change the contract. It does not measure cross-process delay (the shuttle and manager are both on the same machine). It does not measure database latency separately; the alarm acknowledgement and message publish happen in sequence, so the total includes both.

### 2 Cross-state consistency checklist after the full cycle

**What it is.** A checklist you can run out loud after the scenario completes, confirming that every piece of state — shuttle state, rack blocked lane, alarm SQL row, dispatch pause, and task queue — is in the expected state. If any item fails, the scenario is not really done.

**Why we need it.** The M7 done-when says: *"a full scenario, including an injected fault, is run and recovered from the dashboard alone."* A single missing piece — for example, the blocked lane stays blocked, or the alarm stays unacknowledged — means the scenario did not finish. The checklist protects against a partial recovery.

**A real-world example.** A supervisor runs the recovery sequence. The shuttle starts moving again, but the supervisor forgets to check whether the alarm tile cleared on the dashboard or whether the blocked lane is still recorded. Later, the same task tries the same lane and faults again. The supervisor thinks the system failed twice, but it actually failed once and was never fully cleared. The checklist prevents that confusion.

**The checklist.** After `Resume`, confirm:

1. **Shuttle state** (`ShuttleStatusMessage`): `State` is `Idle` or `Moving`; `FaultReason` is `null`; `Position` is unchanged or advancing, not frozen.
2. **Blocked lane** (`Rack.IsBlocked`): the lane that caused the fault (`A01-L02` in our example) is no longer in `IsBlocked`. (Verified by lesson 34's `UnmarkBlocked`.)
3. **Alarm SQL row** (`alarmRepository`): the alarm with the shuttle's id has `acknowledged_at` set (not null) and `severity` unchanged.
4. **Dispatch pause** (`TaskBoard`): `PausedTaskIds` no longer contains tasks assigned to the named shuttle; unassigned tasks are dispatchable.
5. **Movement gate** (`PauseState.IsPaused`): the shuttle id is not in the paused set.
6. **Task retry** (optional but recommended): the retried task completes and the dashboard shows it as `done` or `completed`.

**Where to find each piece.** The shuttle state comes from the MQTT status message on `shuttle/{id}/status`. The blocked lane is in `ShuttleCore/Rack.cs`. The alarm is in the Postgres table (check with `psql -d shuttle -U shuttle -c "SELECT * FROM alarms WHERE shuttle_id = 'SH-01';"`). The dispatch pause is in `FleetCore/TaskBoard.cs`. The movement gate is in `FleetCore/PauseState.cs`.

**What this does not do.** It does not require a new test project or a new script file. It uses the existing code and existing database. It does not check cross-process pause (a separate process observing the MQTT topic). That remains a follow-up.

---

## Recap in plain words

- **Measuring recovery latency.** Compare the `SentAt` timestamp in the `RecoveryCommandMessage` (published by `Acknowledge`) to the `SentAt` timestamp in the next `ShuttleStatusMessage` that reports `Idle` with no fault. Write the number down.
- **Cross-state consistency checklist.** After `Resume`, check six things: shuttle state is healthy, blocked lane is cleared, alarm is acknowledged in SQL, dispatch is resumed, movement gate is open, and the task can retry. If any one is wrong, the scenario is not complete.

---

## Worked example (toy — NOT the assignment)

This toy shows the full sequence on paper. It is not the assignment — the assignment runs the real `serve` process.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l35toy
cd /tmp/opencode/l35toy
dotnet new console --force -o .
```

Put this in `Program.cs`:

```csharp
using System;

class Program
{
    static void Main()
    {
        // Toy timeline of the M7 scenario.
        Console.WriteLine("M7 Full-Recovery Stretch Scenario (paper timeline)");
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine("1. Fault injected: SH-01 hits blocked lane A01-L02");
        Console.WriteLine("2. Operator Pause -> PauseState holds SH-01");
        Console.WriteLine("3. Operator Acknowledge -> Alarm SQL acknowledged,");
        Console.WriteLine("   RecoveryCommandMessage published (SentAt = now)");
        Console.WriteLine("4. Shuttle Recover() -> State = Idle, FaultReason = null,");
        Console.WriteLine("   Rack.UnmarkBlocked('A01-L02') called");
        Console.WriteLine("5. Operator Resume -> PauseState removes SH-01,");
        Console.WriteLine("   TaskBoard resumes, movement gate opens");
        Console.WriteLine("6. Checklist: shuttle Idle + lane clear + alarm ack + dispatch open");
        Console.WriteLine("---");
        Console.WriteLine("Measured latency: compare SentAt(recovery) to SentAt(status-Idle)");
        Console.WriteLine("done");
    }
}
```

Run it:

```bash
cd /tmp/opencode/l35toy
dotnet run
```

Expected output (measured this session):

```
M7 Full-Recovery Stretch Scenario (paper timeline)
---------------------------------------------
1. Fault injected: SH-01 hits blocked lane A01-L02
2. Operator Pause -> PauseState holds SH-01
3. Operator Acknowledge -> Alarm SQL acknowledged,
   RecoveryCommandMessage published (SentAt = now)
4. Shuttle Recover() -> State = Idle, FaultReason = null,
   Rack.UnmarkBlocked('A01-L02') called
5. Operator Resume -> PauseState removes SH-01,
   TaskBoard resumes, movement gate opens
6. Checklist: shuttle Idle + lane clear + alarm ack + dispatch open
---
Measured latency: compare SentAt(recovery) to SentAt(status-Idle)
done
```

Read it line by line:

- Line 1: the fault starts everything.
- Line 3: `Acknowledge` does two things — SQL acknowledgement and MQTT recovery command.
- Line 4: `Recover()` clears both the shuttle state and the blocked lane (lesson 34).
- Line 5: `Resume` lifts the pause, letting the shuttle retry.
- Line 6: the checklist confirms everything before calling the milestone complete.

**What the toy does not show.** It does not connect to MQTT, SQL, or the running `serve` process. Those are the assignment's job.

---

## Say it out loud

Answer these out loud, with no notes:

1. Where does the timer start — in the `Acknowledge` command handler, in the `RecoveryCommandMessage` envelope, or in the SQL alarm row?
2. If the checklist shows `State = Idle` but `IsBlocked('A01-L02')` is still `true`, which piece from lesson 34 is missing?
3. What does the supervisor observe first after clicking `Acknowledge`: the alarm tile clearing, the shuttle status changing, or the blocked lane clearing?
4. Why must the checklist check both the shuttle's `FaultReason` and the `Rack` blocked lane, not just one?
5. What would a very long recovery latency (for example, 30 seconds) suggest about the system — a slow database, a lost MQTT message, or a shuttle that never subscribed?

---

## The assignment

Take the toy and make it real: run the M7 full scenario with a running `serve`, measure the recovery latency, and confirm the checklist passes.

### The files you may touch or add

| File / location | What it is | What you may do |
|---|---|---|
| `src/ShuttleSim/appsettings.json` | Blocked lane config | Edit `BlockedLanes` to inject the fault (`["A01-L02"]`) |
| `/tmp/opencode/l35verify/` | Scratch verification | Create measurement notes or a small verification script |
| `docs/builds/35-m7-full-recovery-stretch-verification-report.md` | Build report (optional) | Record the measured latency and checklist results |

### Out of scope

- Do not edit `src/Contracts/`, `docs/contract/`, `src/FleetCore/TaskBoard.cs`, `src/ShuttleCore/Shuttle.cs`, `src/FleetCore/PauseState.cs`, or `src/FleetManager/Program.cs`. Those files are already correct from lessons 30–34.
- Do not add a new message type or change `ContractVersion`.
- Do not add a new database table or SQL schema.
- Do not leave the `serve` process or broker running after verification. Stop them with `docker compose -f docker/compose.yml down`.

### Requirements

**R1 — scenario execution.** Start `serve` (broker and database up), start a shuttle simulator with `BlockedLanes` configured, observe the fault alarm, publish `Pause`, `Acknowledge`, `Resume` (through the dashboard endpoint or MQTT), and observe recovery.

**R2 — measurement.** Write down the time you observe between the `Acknowledge` click (or MQTT publish) and the shuttle's next status message showing `Idle` with `FaultReason` null. Use either the message `SentAt` timestamp or your wall-clock observation — state which you used.

**R3 — consistency checklist.** Confirm all six checklist items pass (see §2). If any fails, note which and why.

**R4 — cleanup.** After verification, bring down the services (`docker compose down`) and clean up `/tmp/opencode/l35verify/`. Confirm no `dotnet run` or broker process remains (`ps aux | grep dotnet` and `docker ps` should show nothing from this run).

### Acceptance criteria (what a command can check)

```bash
# Verify no contract change
BASE=$(git rev-parse HEAD)
test -z "$(git diff $BASE -- src/Contracts docs/contract src/FleetCore/TaskBoard.cs src/ShuttleCore/Shuttle.cs src/FleetCore/PauseState.cs src/FleetManager/Program.cs)"

# Verify measurement file exists (if written)
ls docs/builds/35-m7-full-recovery-stretch-verification-report.md 2>/dev/null || echo "Verification done in scratch /tmp/opencode/l35verify/"

# Verify no running serve process (after cleanup)
! pgrep -f "dotnet run --project src/FleetManager" 2>/dev/null || echo "WARNING: serve still running"
```

### Stretch goal

**Compare latencies.** Measure the pause-only latency from lesson 30 (`Pause` → `CanMove` false) and compare it to the full-recovery latency (`Acknowledge` → `State = Idle`). Write a one-sentence comparison. Does recovery take about the same time as a pause lift, or noticeably longer? Why?

### What comes next

- The M7 milestone can now be considered verified at the stretch level: the full scenario runs, recovers, and is measured.
- Cross-process pause (a scenario runner in another process observing MQTT) remains open for a future lesson.
- M8 stretch (OPC UA, Modbus, Python scenario runner, or load test) can use this verified scenario as its test case.

---

## What I assumed about what you know

I assumed you are comfortable with: running `serve` with Docker services, reading MQTT topics through the dashboard or a simple MQTT subscriber, inspecting SQL with `psql`, and reading message envelopes from `Contracts/MessageEnvelope.cs`. If any of those is new, say so before building, and I will walk through it.

The verification idea — comparing two timestamps in message envelopes — is taught from first principles. If it is new, say so.
