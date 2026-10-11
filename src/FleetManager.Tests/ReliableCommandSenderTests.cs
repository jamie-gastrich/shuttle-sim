using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager.Tests;

internal sealed class FakeBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    private Func<MessageReceived, CancellationToken, Task>? _handler;
    private readonly Queue<Func<Task>> _plannedResponses = new();

    public Task ConnectAsync(CancellationToken  cancellationToken) => Task.CompletedTask;

    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken  cancellationToken)
    {
        Published.Add((topic, payload, family));

        if (_handler is null || _plannedResponses.Count == 0)
            return Task.CompletedTask;

        var response = _plannedResponses.Dequeue();
        return response();
    }

    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken  cancellationToken)
    {
        _handler = handler;
        return Task.CompletedTask;
    }

    public void QueueNoAck() => _plannedResponses.Enqueue(() => Task.CompletedTask);

    public void QueueAck(CommandAckMessage ack)
    {
        _plannedResponses.Enqueue(() =>
            _handler is null
                ? Task.CompletedTask
                : _handler(new MessageReceived("root/shuttle-1/ack", JsonSerializer.Serialize(ack)), CancellationToken.None));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ReliableCommandSenderTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CommandAckMessage BuildAck(MessageEnvelope commandEnvelope, bool accepted)
    {
        return new CommandAckMessage(
            new MessageEnvelope(
                ContractVersion.Current,
                MessageTypes.CommandAck,
                Guid.NewGuid().ToString(),
                commandEnvelope.MessageId,
                "shuttle-1",
                Start),
            accepted,
            accepted ? null : "rejected");
    }

    [Fact]
    public async Task Lost_acknowledgement_is_retried_with_the_same_message_id()
    {
        var bus = new FakeBus();
        var retry = new RetryPolicyExtensions.RetryPolicy(3, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        var sender = new ReliableCommandSender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/ack", MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager-id", Start);
        var command = new MoveCommandMessage(commandEnvelope, "shuttle-1", "lane-1");

        bus.QueueNoAck();
        bus.QueueAck(BuildAck(commandEnvelope, accepted: true));

        var result = await sender.SendAsync(command, CancellationToken.None);

        var publishedCommands = bus.Published
            .Where(item => item.Family == MessageFamily.Command)
            .Select(item => JsonSerializer.Deserialize<MoveCommandMessage>(item.Payload))
            .ToList();

        Assert.Equal(CommandSendStatus.Acknowledged, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.NotNull(result.Acknowledgement);
        Assert.Equal(2, bus.Published.Count);
        Assert.Equal(2, publishedCommands.Count);
        Assert.All(publishedCommands, published => Assert.Equal(command.Envelope.MessageId, published!.Envelope.MessageId));
    }

    [Fact]
    public async Task Retries_stop_after_the_configured_attempts()
    {
        var bus = new FakeBus();
        var retry = new RetryPolicyExtensions.RetryPolicy(3, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        var sender = new ReliableCommandSender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/ack", MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager-id", Start);
        var command = new MoveCommandMessage(commandEnvelope, "shuttle-1", "lane-1");

        bus.QueueNoAck();
        bus.QueueNoAck();
        bus.QueueNoAck();

        var result = await sender.SendAsync(command, CancellationToken.None);

        Assert.Equal(CommandSendStatus.NoAcknowledgement, result.Status);
        Assert.Equal(retry.MaxAttempts, result.Attempts);
        Assert.Null(result.Acknowledgement);
        Assert.Equal(retry.MaxAttempts, bus.Published.Count);
    }

    [Fact]
    public async Task Rejected_acknowledgement_is_not_retried()
    {
        var bus = new FakeBus();
        var retry = new RetryPolicyExtensions.RetryPolicy(3, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        var sender = new ReliableCommandSender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/ack", MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager-id", Start);
        var command = new MoveCommandMessage(commandEnvelope, "shuttle-1", "lane-1");

        bus.QueueAck(BuildAck(commandEnvelope, accepted: false));

        var result = await sender.SendAsync(command, CancellationToken.None);

        Assert.Equal(CommandSendStatus.Rejected, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.NotNull(result.Acknowledgement);
        Assert.False(result.Acknowledgement!.Accepted);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Accepted_acknowledgement_stops_retrying()
    {
        var bus = new FakeBus();
        var retry = new RetryPolicyExtensions.RetryPolicy(3, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        var sender = new ReliableCommandSender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/ack", MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager-id", Start);
        var command = new MoveCommandMessage(commandEnvelope, "shuttle-1", "lane-1");

        bus.QueueAck(BuildAck(commandEnvelope, accepted: true));

        var result = await sender.SendAsync(command, CancellationToken.None);

        Assert.Equal(CommandSendStatus.Acknowledged, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.NotNull(result.Acknowledgement);
        Assert.True(result.Acknowledgement!.Accepted);
        Assert.Single(bus.Published);
    }
}