using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager.Tests;
internal sealed class RecordingBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    public string? LastFilter { get; private set; }
    public MessageFamily? LastFamily { get; private set; }
    public Func<MessageReceived, CancellationToken, Task>? Handler { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken)
    {
        Published.Add((topic, payload, family));
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        LastFamily = family;
        Handler = handler;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FleetServeLoopTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    static string StatusJson(string shuttleId, DateTime sentAt) => JsonSerializer.Serialize(
    new ShuttleStatusMessage(
        new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
            Guid.NewGuid().ToString(), null, shuttleId, sentAt),
        new ShuttleStatus(shuttleId, "idle", "A01-L01", sentAt, null)));

    [Fact]
    public async Task The_constructor_rejects_a_blank_fleet_manager_id_and_a_non_positive_interval()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        Assert.Throws<ArgumentException>(() => new FleetServeLoop(bus, topics, health, evaluator, "", TimeSpan.FromSeconds(1), () => Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.Zero, () => Start));
    }

    [Fact]
    public async Task Subscribe_uses_the_all_statuses_filter_and_the_status_family()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start);
        await loop.SubscribeAsync(CancellationToken.None);

        Assert.Equal(topics.AllStatuses, bus.LastFilter);
        Assert.Equal(MessageFamily.Status, bus.LastFamily);
    }

    [Fact]
    public async Task A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start);
        
        await loop.SubscribeAsync(CancellationToken.None);
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", Start)),
            CancellationToken.None);

        await loop.EvaluateAndPublishAsync(Start.AddSeconds(31), CancellationToken.None);

        Assert.Single(bus.Published);
        var (topic, payload, family) = bus.Published[0];
        Assert.Equal("shuttle/SH-01/alarm", topic);
        Assert.Equal(MessageFamily.Alarm, family);
        var alarm = JsonSerializer.Deserialize<AlarmMessage>(payload);
        Assert.NotNull(alarm);
        Assert.Equal(ContractVersion.Current, alarm!.Envelope.ContractVersion);
        Assert.Equal(MessageTypes.Alarm, alarm.Envelope.MessageType);
        Assert.Equal("fleet-manager", alarm.Envelope.SenderId);
        Assert.Null(alarm.Envelope.CorrelationId);
        Assert.Equal(Start.AddSeconds(31), alarm.Envelope.SentAt);
        Assert.Equal("SH-01", alarm.Alarm.ShuttleId);
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Alarm.Code);
        Assert.Equal(AlarmSeverities.Critical, alarm.Alarm.Severity);
        Assert.Null(alarm.Alarm.Detail);
    }

    [Fact]
    public async Task Two_shuttles_are_evaluated_independently()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start);

        await loop.SubscribeAsync(CancellationToken.None);
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", Start)),
            CancellationToken.None);
        await bus.Handler!(new MessageReceived(topics.Status("SH-02"), StatusJson("SH-02", Start)),
            CancellationToken.None);

        await loop.EvaluateAndPublishAsync(Start.AddSeconds(31), CancellationToken.None);
        Assert.Equal(2, bus.Published.Count);
    }
}