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

        await repository.SaveAsync(
            new PostgresAlarmRepository.Alarm("SH-INT", AlarmSeverities.Critical, AlarmCodes.LostCommunication, Start, null),
            CancellationToken.None);

        var loaded = await repository.LoadAllAsync(CancellationToken.None);
        Assert.Contains(loaded, a => a.ShuttleId == "SH-INT" && a.Code == AlarmCodes.LostCommunication);
    }

    [Fact]
    public async Task FleetServeLoop_saves_raised_alarm_through_repository()
    {
        var connectionString = TestDatabase.BuildConnectionString();
        var repository = new PostgresAlarmRepository(connectionString);

        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning);
        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start, repository);

        await loop.SubscribeAsync(CancellationToken.None);
        await bus.Handler!(new MessageReceived(topics.Status("SH-INT2"),
            JsonSerializer.Serialize(new ShuttleStatusMessage(
                new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus, Guid.NewGuid().ToString(), null, "SH-INT2", Start),
                new ShuttleStatus("SH-INT2", "idle", "A01-L01", Start, null)))), CancellationToken.None);

        await loop.EvaluateAndPublishAsync(Start.AddSeconds(31), CancellationToken.None);

        var loaded = await repository.LoadAllAsync(CancellationToken.None);
        Assert.Contains(loaded, a => a.ShuttleId == "SH-INT2" && a.Code == AlarmCodes.LostCommunication);
    }
}
