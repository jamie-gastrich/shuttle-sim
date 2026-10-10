using System.Text.Json;
using Contracts;
using Messaging;
using ShuttleCore;

internal sealed class FakeBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken ct)
    { Published.Add((topic, payload, family)); return Task.CompletedTask; }
    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ShuttleEndpointTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Duplicate_move_command_is_acknowledged_but_not_run_twice()
    {
        var bus = new FakeBus();
        var shuttle = CreateShuttle();
        var endpoint = CreateEndpoint(shuttle, bus);
        var payload = CommandPayload("SH-01", "A01-L02", "move-1");

        await endpoint.HandleCommandAsync(payload, Start, CancellationToken.None);
        await endpoint.HandleCommandAsync(payload, Start, CancellationToken.None);

        var acknowledgements = bus.Published
            .Where(message => message.Family == MessageFamily.Ack)
            .Select(message => JsonSerializer.Deserialize<CommandAckMessage>(message.Payload)!)
            .ToArray();
        Assert.Equal(2, acknowledgements.Length);
        Assert.All(acknowledgements, acknowledgement => Assert.True(acknowledgement.Accepted));
        Assert.Equal("A01-L02", shuttle.Position);
        Assert.Equal(2, bus.Published.Count(message => message.Family == MessageFamily.Status));
    }

    [Fact]
    public async Task Move_command_for_another_shuttle_is_ignored()
    {
        var bus = new FakeBus();
        var endpoint = CreateEndpoint(CreateShuttle(), bus);

        await endpoint.HandleCommandAsync(
            CommandPayload("SH-02", "A01-L02", "move-other"), Start, CancellationToken.None);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Rejected_move_is_acknowledged_as_not_accepted()
    {
        var bus = new FakeBus();
        var endpoint = CreateEndpoint(CreateShuttle(), bus);

        await endpoint.HandleCommandAsync(
            CommandPayload("SH-01", "A99-L99", "move-bad"), Start, CancellationToken.None);

        var acknowledgement = Assert.Single(bus.Published);
        Assert.Equal(MessageFamily.Ack, acknowledgement.Family);
        var ack = JsonSerializer.Deserialize<CommandAckMessage>(acknowledgement.Payload)!;
        Assert.False(ack.Accepted);
        Assert.Contains("unknown lane", ack.Reason);
    }

    [Fact]
    public async Task Accepted_move_reports_moving_then_idle()
    {
        var bus = new FakeBus();
        var endpoint = CreateEndpoint(CreateShuttle(), bus);

        await endpoint.HandleCommandAsync(
            CommandPayload("SH-01", "A01-L02", "move-valid"), Start, CancellationToken.None);

        var statuses = bus.Published
            .Where(message => message.Family == MessageFamily.Status)
            .Select(message => JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload)!)
            .ToArray();
        Assert.Collection(statuses,
            moving =>
            {
                Assert.Equal("moving", moving.Status.State);
                Assert.Equal("A01-L01", moving.Status.Position);
                Assert.Equal(Start, moving.Status.Timestamp);
            },
            idle =>
            {
                Assert.Equal("idle", idle.Status.State);
                Assert.Equal("A01-L02", idle.Status.Position);
                Assert.Equal(Start.AddSeconds(5), idle.Status.Timestamp);
            });
    }

    private static Shuttle CreateShuttle()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        return new Shuttle("SH-01", rack, "A01-L01", timing);
    }

    private static ShuttleEndpoint CreateEndpoint(Shuttle shuttle, FakeBus bus) =>
        new(shuttle, bus, new TopicScheme("shuttle"), "SH-01", new IdempotencyGuard(10));

    private static string CommandPayload(string shuttleId, string toLane, string messageId)
    {
        var envelope = new MessageEnvelope(
            ContractVersion.Current,
            MessageTypes.MoveCommand,
            messageId,
            null,
            "fleet-manager",
            Start);
        return JsonSerializer.Serialize(new MoveCommandMessage(envelope, shuttleId, toLane));
    }
}
