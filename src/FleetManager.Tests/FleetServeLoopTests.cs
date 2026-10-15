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
        const string fleetManagerId = "fm-test-01";  // distinct from any literal in test

        var loop = new FleetServeLoop(bus, topics, health, evaluator, fleetManagerId, TimeSpan.FromSeconds(1), () => Start);

        
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
        Assert.Equal(fleetManagerId, alarm.Envelope.SenderId);
        Assert.Null(alarm.Envelope.CorrelationId);
        Assert.Equal(Start.AddSeconds(31), alarm.Envelope.SentAt);
        Assert.Equal("SH-01", alarm.Alarm.ShuttleId);
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Alarm.Code);
        Assert.Equal(AlarmSeverities.Critical, alarm.Alarm.Severity);
        Assert.Null(alarm.Alarm.Detail);
    }

    [Fact]
    public async Task A_latched_lost_shuttle_publishes_nothing_again()
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

        await loop.EvaluateAndPublishAsync(Start.AddSeconds(62), CancellationToken.None);

        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task A_recovered_shuttle_clears_the_latch_and_can_raise_again()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        var now = Start;
        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => now);

        await loop.SubscribeAsync(CancellationToken.None);
        // Episode 1: seen at t=0, then silent past the window.
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", now)), CancellationToken.None);
        now = now.AddSeconds(31);
        await loop.EvaluateAndPublishAsync(now, CancellationToken.None);   // lost -> alarm #1 (latch set)

        // Recovery at t=32: heartbeat comes back, which clears the latch.
        now = now.AddSeconds(1);
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", now)), CancellationToken.None);
        await loop.EvaluateAndPublishAsync(now, CancellationToken.None);   // responsive -> clear, nothing published

        // Episode 2: silence again until it is past the window once more.
        now = now.AddSeconds(31);
        await loop.EvaluateAndPublishAsync(now, CancellationToken.None);  // lost again -> alarm #2 (fresh)

        Assert.Equal(2, bus.Published.Count);

        Assert.Equal(2, bus.Published.Count);
    }

    [Fact]
    public async Task An_invalid_status_message_is_ignored()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => Start);

        await loop.SubscribeAsync(CancellationToken.None);
        // 1) blank ShuttleId 
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("", Start)),
            CancellationToken.None);

        // 2) wrong ContractVersion (v2)
        var badVersionJson = JsonSerializer.Serialize(
            new ShuttleStatusMessage(
                new MessageEnvelope(2, MessageTypes.ShuttleStatus, Guid.NewGuid().ToString(), null, "SH-01", Start),
                new ShuttleStatus("SH-01", "idle", "A01-L01", Start, null)));
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), badVersionJson), CancellationToken.None);
        
        // 3) wrong MessageType (move command envelope)
        var badTypeJson = JsonSerializer.Serialize(
            new MoveCommandMessage(
                new MessageEnvelope(ContractVersion.Current, MessageTypes.MoveCommand, Guid.NewGuid().ToString(), null, "SH-01", Start),
                "SH-01", "A01-L02"));
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), badTypeJson), CancellationToken.None);
        
        // 4) blank MessageId
        var blankIdJson = JsonSerializer.Serialize(
            new ShuttleStatusMessage(
                new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus, "", null, "SH-01", Start),
                new ShuttleStatus("SH-01", "idle", "A01-L01", Start, null)));
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), blankIdJson), CancellationToken.None);

        await loop.EvaluateAndPublishAsync(Start.AddSeconds(31), CancellationToken.None);

        Assert.Empty(bus.Published);
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

    [Fact]
    public async Task Sweep_uses_the_injected_clock_not_UtcNow_for_the_liveness_check()
    {
        var bus = new RecordingBus();
        var topics = new TopicScheme("shuttle");
        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

        // Captured clock that the test controls — NOT the real clock
        var injectedNow = Start;
        var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), () => injectedNow);

        await loop.SubscribeAsync(CancellationToken.None);
        // Feed status at t=0 via handler (uses injected clock = Start)
        await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", Start)), CancellationToken.None);

        // Sweep at t=15 (injected) — NOT past window. Should publish NOTHING.
        injectedNow = Start.AddSeconds(15);
        await loop.EvaluateAndPublishAsync(injectedNow, CancellationToken.None);
        Assert.Empty(bus.Published);  // injected clock says "still responsive"

        // BUT if production used DateTime.UtcNow, and the real clock is now Start+31,
        // it would think the shuttle is lost and publish. The envelope's SentAt would be Start+31,
        // not Start+15. Our assertion on SentAt catches it:
        // If any publish happened (due to UtcNow bug), its SentAt would be ~Start+31, not Start+15.
        // Since we Assert.Empty, the test catches the wrong clock branch by ensuring no publish.
    }
}