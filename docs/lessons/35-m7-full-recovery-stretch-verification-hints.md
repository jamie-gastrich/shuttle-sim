# Lesson 35 — Hints

## Tier 1: Nudge

- The timer starts where the code creates the `RecoveryCommandMessage` — look at `Program.cs` line 351.
- The checklist checks six places. If you forget one, go back to §2 and count them again.

## Tier 2: Direction

- To observe the scenario, start with `docker compose -f docker/compose.yml up -d`, then `dotnet run --project src/ShuttleSim` with `BlockedLanes` set. Watch the console output for `FaultReason`.
- To measure, compare the `SentAt` value in the recovery message payload (you can print it with `Console.WriteLine`) to the `SentAt` in the next `ShuttleStatusMessage`.
- If the blocked lane does not clear after `Acknowledge`, check whether `Recover()` called `UnmarkBlocked` for the recorded lanes — that is lesson 34's work.

## Tier 3: Near-solution

- The measurement can be as simple as a `DateTime.UtcNow` before `PublishAsync` and another after you see the `Idle` status. Write both values to `/tmp/opencode/l35verify/latency.txt`.
- The checklist passes when: `grep -q "Idle" shuttle-status-output` and `grep -v "A01-L02" rack-blocked-output` and `psql -c "SELECT acknowledged_at FROM alarms WHERE shuttle_id='SH-01';"` shows non-null.
- If `serve` does not start, check the connection string: `export SHUTTLE_DB_CONNECTION_STRING="Host=127.0.0.1;Port=5432;Database=shuttle;Username=shuttle;Password=shuttle"`.
