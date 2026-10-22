# Lesson 26 hints: The dashboard slice

## Tier 1 — nudge

- The dashboard needs three GET endpoints — where do the route constants live?
- Alarms need a table in SQL — what columns match the in-memory `Alarm` record?
- Operator commands travel on MQTT — what topic name is used for the `fleet/operator/command` topic?

## Tier 2 — direction

- Add three `webApp.MapGet` calls in `Program.RunServeAsync`, after the existing order GET at line 281. Use `WmsRoutes.Shuttle("{shuttleId}")` and `WmsRoutes.TaskQueue`. The body looks up from `registry` and `taskBoard` respectively and returns `Results.Ok` or `Results.Problem`.
- Add `PostgresAlarmRepository.SaveAlarmRow` (or `SaveAsync`) that upserts into an `alarms` table with columns `shuttle_id, code, severity, raised_at, acknowledged_at`. Use `ON CONFLICT ... DO UPDATE`.
- In the serve loop, after the `AlarmEvaluator` produces alarms, call `alarmRepository.SaveAsync(alarm, CancellationToken.None)` for each new alarm.
- Add `OperatorCommandMessage` record to `Contracts/` with `ShuttleId, Kind, Reason, CorrelationId`. The `Kind` is a nested `enum class Command { Pause, Resume, Acknowledge }`.
- In the MQTT subscription, use `topics.OperatorCommand` (or whatever the topic constant resolves to) and `MessageFamily.Json`.
- Add `PauseAllExcept` and `ResumeAllExcept` to `TaskBoard` — they take a shuttle id and pause/resume every task except the one with that id. If the id is empty, affect all.
- In the operator‑command handler, switch on `cmd.Kind`: `Pause` → call `taskBoard.PauseAllExcept(cmd.ShuttleId)`, `Resume` → call `taskBoard.ResumeAllExcept(cmd.ShuttleId)`, `Acknowledge` → call `alarmRepository.AcknowledgeAsync(cmd.ShuttleId)`.

## Tier 3 — near-solution

- `PostgresAlarmRepository.SaveAsync`:
  ```csharp
  public async Task SaveAsync(Alarm alarm, CancellationToken cancellationToken)
  {
      await using var connection = new NpgsqlConnection(_connectionString);
      await connection.OpenAsync(cancellationToken);
      await using var command = connection.CreateCommand();
      command.CommandText = @"
          INSERT INTO alarms (shuttle_id, code, severity, raised_at, acknowledged_at)
          VALUES (@shuttleId, @code, @severity, @raisedAt, @acknowledgedAt)
          ON CONFLICT (shuttle_id, code)
          DO UPDATE SET acknowledged_at = @acknowledgedAt, raised_at = GREATEST(alarms.raised_at, EXCLUDED.raised_at);";
      command.Parameters.AddWithValue("@shuttleId", alarm.ShuttleId);
      command.Parameters.AddWithValue("@code", alarm.Code);
      command.Parameters.AddWithValue("@severity", alarm.Severity);
      command.Parameters.AddWithValue("@raisedAt", alarm.RaisedAt);
      command.Parameters.AddWithValue("@acknowledgedAt", (object?)alarm.AcknowledgedAt ?? DBNull.Value);
      await command.ExecuteNonQueryAsync(cancellationToken);
  }
  ```
- `Alarm` record (from `FleetCore`):
  ```csharp
  public sealed record Alarm(
      string ShuttleId, string Severity, string Code, DateTime RaisedAt,
      DateTime? AcknowledgedAt = null);
  ```
- Re‑marking in the serve loop (after restore, before the bus connects):
  ```csharp
  foreach (var alarm in newAlarms)
      await alarmRepository.SaveAsync(alarm, CancellationToken.None);
  ```
- Operator‑command handler sketch:
  ```csharp
  static async Task HandleOperatorCommandAsync(MessageReceived message, CancellationToken cancellationToken)
  {
      var cmd = JsonSerializer.Deserialize<OperatorCommandMessage>(message.Payload);
      if (cmd is null) return;
      switch (cmd.Kind)
      {
          case OperatorCommandMessage.Command.Pause:
              await taskBoard.PauseAllExcept(cmd.ShuttleId, cancellationToken);
              break;
          case OperatorCommandMessage.Command.Resume:
              await taskBoard.ResumeAllExcept(cmd.ShuttleId, cancellationToken);
              break;
          case OperatorCommandMessage.Command.Acknowledge:
              await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
              break;
      }
  }
  ```
- `TaskBoard.PauseAllExcept` sketch:
  ```csharp
  public async Task PauseAllExcept(string shuttleId, CancellationToken cancellationToken)
  {
      var all = _tasks.Values.ToList();
      foreach (var t in all)
          if (t.TaskId != shuttleId) // or some other field that identifies the shuttle
              await t.Pause(cancellationToken); // if Task had a Pause method
  }
  ```
  Actually, `PauseAllExcept` would iterate the task board and call whatever method puts a task into a paused state. The exact API depends on how `TaskState` works — you might set `TaskState.Paused` instead of a method call.

- `AlarmRepository.AcknowledgeAsync` sketch:
  ```csharp
  public async Task AcknowledgeAsync(string shuttleId, string code, CancellationToken cancellationToken)
  {
      await using var connection = new NpgsqlConnection(_connectionString);
      await connection.OpenAsync(cancellationToken);
      await using var command = connection.CreateCommand();
      command.CommandText = "UPDATE alarms SET acknowledged_at = NOW() WHERE shuttle_id = @shuttleId AND code = @code AND acknowledged_at IS NULL";
      command.Parameters.AddWithValue("@shuttleId", shuttleId);
      command.Parameters.AddWithValue("@code", code);
      await command.ExecuteNonQueryAsync(cancellationToken);
  }
  ```