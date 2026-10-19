using System.Collections.Concurrent;
using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager;

public enum RecoverySendStatus { Acknowledged, Rejected, NoAcknowledgement }

public sealed record RecoverySendResult(
    RecoverySendStatus Status, int Attempts, RecoveryAckMessage? Acknowledgement);

public class RecoverySender
{
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly RetryPolicyExtensions.RetryPolicy _policy;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RecoveryAckMessage>> _waiters = new();

    public RecoverySender(IMessageBus bus, TopicScheme topics, RetryPolicyExtensions.RetryPolicy policy)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _topics = topics ?? throw new ArgumentNullException(nameof(topics));
        _policy = policy;
    }

    public Task OnAckReceivedAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EnvelopeWrapper>(message.Payload)?.Envelope;
            if (envelope is null || envelope.MessageType != MessageTypes.RecoveryAck ||
                string.IsNullOrWhiteSpace(envelope.CorrelationId))
                return Task.CompletedTask;

            var ack = JsonSerializer.Deserialize<RecoveryAckMessage>(message.Payload);
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

    public async Task<RecoverySendResult> SendAsync(RecoveryCommandMessage command, CancellationToken cancellationToken)
    {
        var id = command.Envelope.MessageId;
        var waiter = new TaskCompletionSource<RecoveryAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[id] = waiter;

        try
        {
            var payload = JsonSerializer.Serialize(command);
            for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
            {
                Console.Error.WriteLine($"attempt {attempt} of {_policy.MaxAttempts} for command {id}");
                await _bus.PublishAsync(_topics.RecoveryCommand(command.ShuttleId), payload, MessageFamily.Recovery, cancellationToken);
                try
                {
                    var ack = await waiter.Task.WaitAsync(_policy.AckTimeout, cancellationToken);
                    return ack.Accepted
                        ? new RecoverySendResult(RecoverySendStatus.Acknowledged, attempt, ack)
                        : new RecoverySendResult(RecoverySendStatus.Rejected, attempt, ack);
                }
                catch (TimeoutException)
                {
                    if (attempt < _policy.MaxAttempts)
                        await Task.Delay(_policy.Backoff, cancellationToken);
                }
            }
            return new RecoverySendResult(RecoverySendStatus.NoAcknowledgement, _policy.MaxAttempts, null);
        }
        finally
        {
            _waiters.TryRemove(id, out _);
        }
    }
}