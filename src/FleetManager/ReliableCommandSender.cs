using System.Collections.Concurrent;
using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager;

public enum CommandSendStatus { Acknowledged, Rejected, NoAcknowledgement }

public sealed record CommandSendResult(
    CommandSendStatus Status, int Attempts, CommandAckMessage? Acknowledgement);

public sealed class ReliableCommandSender
{
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly RetryPolicyExtensions.RetryPolicy _policy;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandAckMessage>> _waiters = new();

    public ReliableCommandSender(IMessageBus bus, TopicScheme topics, RetryPolicyExtensions.RetryPolicy policy)
    {
        _bus = bus;
        _topics = topics;
        _policy = policy;
    }

    public Task OnAckReceivedAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EnvelopeWrapper>(message.Payload)?.Envelope;
            if (envelope is null || envelope.MessageType != MessageTypes.CommandAck ||
                string.IsNullOrWhiteSpace(envelope.CorrelationId))
                return Task.CompletedTask;

            var ack = JsonSerializer.Deserialize<CommandAckMessage>(message.Payload);
            if (ack is null || ack.Envelope is null || string.IsNullOrWhiteSpace(ack.Envelope.CorrelationId))
                return Task.CompletedTask;

            if (!_waiters.TryGetValue(ack.Envelope.CorrelationId, out var waiter))
                return Task.CompletedTask;

            waiter.TrySetResult(ack);
        }
        catch (JsonException)
        {
            // Ignore malformed or unrelated payloads.
        }

        return Task.CompletedTask;
    }

    private sealed record EnvelopeWrapper(MessageEnvelope Envelope);

    public async Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken)
    {
        var id = command.Envelope.MessageId;
        var waiter = new TaskCompletionSource<CommandAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[id] = waiter;

        try
        {
            var payload = JsonSerializer.Serialize(command); // same bytes every attempt
            for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
            {
                await _bus.PublishAsync(_topics.Command(command.ShuttleId), payload, MessageFamily.Command, cancellationToken);
                try
                {
                    var ack = await waiter.Task.WaitAsync(_policy.AckTimeout, cancellationToken);
                    return ack.Accepted
                        ? new CommandSendResult(CommandSendStatus.Acknowledged, attempt, ack)
                        : new CommandSendResult(CommandSendStatus.Rejected, attempt, ack);
                }
                catch (TimeoutException)
                {
                    if (attempt < _policy.MaxAttempts)
                        await Task.Delay(_policy.Backoff, cancellationToken);
                }
            }
            return new CommandSendResult(CommandSendStatus.NoAcknowledgement, _policy.MaxAttempts, null);
        }
        finally
        {
            _waiters.TryRemove(id, out _);
        }
    }
}