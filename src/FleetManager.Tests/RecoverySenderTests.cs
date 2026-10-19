using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager.Tests;

public sealed class RecoverySenderTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RecoveryAckMessage BuildAck(MessageEnvelope commandEnvelope, bool accepted)
    {
        return new RecoveryAckMessage(
            new MessageEnvelope(
                ContractVersion.Current,
                MessageTypes.RecoveryAck,
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
        var sender = new RecoverySender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/recoveryAck", MessageFamily.Ack, sender.OnRecoveryAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.RecoveryCommand, "fleet-manager-id", Start);
        var command = new RecoveryCommandMessage(commandEnvelope, "shuttle-1");

        bus.QueueNoAck();
        bus.RecoveryQueueAck(BuildAck(commandEnvelope, accepted: true));

        var result = await sender.SendAsync(command, CancellationToken.None);

        var publishedCommands = bus.Published
            .Where(item => item.Family == MessageFamily.Recovery && item.Topic == new TopicScheme("root").RecoveryCommand("shuttle-1"))
            .Select(item => JsonSerializer.Deserialize<RecoveryCommandMessage>(item.Payload))
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
        var sender = new RecoverySender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/recoveryAck", MessageFamily.Ack, sender.OnRecoveryAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.RecoveryCommand, "fleet-manager-id", Start);
        var command = new RecoveryCommandMessage(commandEnvelope, "shuttle-1");

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
        var sender = new RecoverySender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/recoveryAck", MessageFamily.Ack, sender.OnRecoveryAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.RecoveryCommand, "fleet-manager-id", Start);
        var command = new RecoveryCommandMessage(commandEnvelope, "shuttle-1");

        bus.RecoveryQueueAck(BuildAck(commandEnvelope, accepted: false));

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
        var sender = new RecoverySender(bus, new TopicScheme("root"), retry);
        await bus.SubscribeAsync("root/+/recoveryAck", MessageFamily.Ack, sender.OnRecoveryAckReceivedAsync, CancellationToken.None);

        var commandEnvelope = MessageEnvelopes.Create(MessageTypes.RecoveryCommand, "fleet-manager-id", Start);
        var command = new RecoveryCommandMessage(commandEnvelope, "shuttle-1");

        bus.RecoveryQueueAck(BuildAck(commandEnvelope, accepted: true));

        var result = await sender.SendAsync(command, CancellationToken.None);

        Assert.Equal(CommandSendStatus.Acknowledged, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.NotNull(result.Acknowledgement);
        Assert.True(result.Acknowledgement!.Accepted);
        Assert.Single(bus.Published);
    }
}