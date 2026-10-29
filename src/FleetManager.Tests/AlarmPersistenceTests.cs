using Contracts;
using Messaging;
using ShuttleCore;
using System.Text.Json;

namespace FleetManager.Tests;

[Trait("Category", "Integration")]
[Collection("Database")]
public class AlarmPersistenceTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostgresAlarmRepository_creates_table_and_saves_alarm()
    {
        var connectionString = TestDatabase.BuildConnectionString();
        var repository = new PostgresAlarmRepository(connectionString);
        var shuttleId = $"TEST-ALARM-{Guid.NewGuid():N}";

        // Load first so the table exists before the cleanup statement runs.
        await repository.LoadAllAsync(CancellationToken.None);
        await TestDatabase.ResetAlarmsAsync(CancellationToken.None);
        try
        {
            await repository.SaveAsync(
                new PostgresAlarmRepository.Alarm(shuttleId, AlarmSeverities.Critical, AlarmCodes.LostCommunication, Start, null),
                CancellationToken.None);

            var loaded = await repository.LoadAllAsync(CancellationToken.None);
            Assert.Contains(loaded, a => a.ShuttleId == shuttleId && a.Code == AlarmCodes.LostCommunication);
        }
        finally
        {
            await TestDatabase.ResetAlarmsAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FleetServeLoop_saves_raised_alarm_through_repository()
    {
        var connectionString = TestDatabase.BuildConnectionString();
        var repository = new PostgresAlarmRepository(connectionString);
        var shuttleId = $"TEST-ALARM-{Guid.NewGuid():N}";

        // Load first so the table exists before the cleanup statement runs.
        await repository.LoadAllAsync(CancellationToken.None);
        await TestDatabase.ResetAlarmsAsync(CancellationToken.None);
        try
        {
            var bus = new RecordingBus();
            var topics = new TopicScheme("shuttle");
            var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
            var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning);
            var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start, repository);

            await loop.SubscribeAsync(CancellationToken.None);
            await bus.Handler!(new MessageReceived(topics.Status(shuttleId),
                JsonSerializer.Serialize(new ShuttleStatusMessage(
                    new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus, Guid.NewGuid().ToString(), null, shuttleId, Start),
                    new ShuttleStatus(shuttleId, "idle", "A01-L01", Start, null)))), CancellationToken.None);

            await loop.EvaluateAndPublishAsync(Start.AddSeconds(31), CancellationToken.None);

            var loaded = await repository.LoadAllAsync(CancellationToken.None);
            Assert.Contains(loaded, a => a.ShuttleId == shuttleId && a.Code == AlarmCodes.LostCommunication);
        }
        finally
        {
            await TestDatabase.ResetAlarmsAsync(CancellationToken.None);
        }
    }
}
