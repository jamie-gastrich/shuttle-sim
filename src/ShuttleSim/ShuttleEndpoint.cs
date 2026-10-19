using System.Text.Json;
using Contracts;
using Messaging;
using ShuttleCore;

public sealed class ShuttleEndpoint
{
    private readonly Shuttle _shuttle;
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly string _shuttleId;
    private readonly IdempotencyGuard _guard;

    public ShuttleEndpoint(Shuttle shuttle, IMessageBus bus, TopicScheme topics,
        string shuttleId, IdempotencyGuard guard)
    {
        _shuttle = shuttle;
        _bus = bus;
        _topics = topics;
        _shuttleId = shuttleId;
        _guard = guard;
    }

    public async Task HandleCommandAsync(string payload, DateTime now, CancellationToken cancellationToken)
    {
        MessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EnvelopeWrapper>(payload)?.Envelope;
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.MessageId) ||
            envelope.ContractVersion != ContractVersion.Current ||
            envelope.MessageType != MessageTypes.MoveCommand)
            return;

        MoveCommandMessage? command;
        try
        {
            command = JsonSerializer.Deserialize<MoveCommandMessage>(payload);
        }
        catch (JsonException)
        {
            return;
        }

        if (command is null || command.Envelope is null ||
            command.Envelope.MessageId != envelope.MessageId ||
            command.ShuttleId != _shuttleId || string.IsNullOrWhiteSpace(command.ToLane))
            return;

        var messageId = command.Envelope.MessageId;
        if (_guard.IsDuplicate(messageId))
        {
            await AcknowledgeAsync(command, accepted: true, reason: null, now, cancellationToken);
            return;
        }

        try
        {
            _shuttle.StartMove(command.ToLane, now);
        }
        catch (ShuttleRejectedException exception)
        {
            await AcknowledgeAsync(command, accepted: false, exception.Message, now, cancellationToken);
            return;
        }

        if (_shuttle.State == ShuttleState.Fault)
        {
            await AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, now, cancellationToken);
            await PublishStatusAsync(now, cancellationToken); 
            return;
        }

        _guard.MarkCompleted(messageId);
        await AcknowledgeAsync(command, accepted: true, reason: null, now, cancellationToken);
        await PublishStatusAsync("moving", now, cancellationToken);

        var arrival = now + _shuttle.TravelTimeTo(command.ToLane);
        _shuttle.CompleteMove(arrival);
        await PublishStatusAsync("idle", arrival, cancellationToken);
    }

    public async Task HandleRecoveryCommandAsync(string payload, DateTime now, CancellationToken cancellationToken)
    {
        MessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EnvelopeWrapper>(payload)?.Envelope;
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.MessageId) ||
            envelope.ContractVersion != ContractVersion.Current ||
            envelope.MessageType != MessageTypes.RecoveryCommand)
            return;

        RecoveryCommandMessage? command;
        try
        {
            command = JsonSerializer.Deserialize<RecoveryCommandMessage>(payload);
        }
        catch (JsonException)
        {
            return;
        }

        if (command is null || command.Envelope is null ||
            command.Envelope.MessageId != envelope.MessageId ||
            command.ShuttleId != _shuttleId)
            return;

        if (_shuttle.State == ShuttleState.Fault)
        {
            try
            {
                _shuttle.Recover(now);
            }
            catch (ShuttleRejectedException exception)
            {
                await RecoveryAcknowledgeAsync(command, accepted: false, exception.Message, now, cancellationToken);
                return;
            }
            await PublishStatusAsync(now, cancellationToken);
        }
        
        await RecoveryAcknowledgeAsync(command, accepted: true, reason: null, now, cancellationToken);
    }

    public Task PublishStatusAsync(DateTime sentAt, CancellationToken cancellationToken) =>
    PublishStatusAsync(_shuttle.State.ToWireName(), sentAt, cancellationToken);

    private Task AcknowledgeAsync(MoveCommandMessage command, bool accepted, string? reason,
        DateTime sentAt, CancellationToken cancellationToken)
    {
        var envelope = MessageEnvelopes.Create(
            MessageTypes.CommandAck, _shuttleId, sentAt, command.Envelope.MessageId);
        var acknowledgement = new CommandAckMessage(envelope, accepted, reason);
        return _bus.PublishAsync(_topics.Ack(_shuttleId), JsonSerializer.Serialize(acknowledgement),
            MessageFamily.Ack, cancellationToken);
    }

    private Task RecoveryAcknowledgeAsync(RecoveryCommandMessage command, bool accepted, string? reason,
        DateTime sentAt, CancellationToken cancellationToken)
    {
        var envelope = MessageEnvelopes.Create(
            MessageTypes.RecoveryAck, _shuttleId, sentAt, command.Envelope.MessageId);
        var acknowledgement = new RecoveryAckMessage(envelope, accepted, reason);
        return _bus.PublishAsync(_topics.RecoveryAck(_shuttleId), JsonSerializer.Serialize(acknowledgement),
            MessageFamily.Ack, cancellationToken);
    }

    private Task PublishStatusAsync(string state, DateTime sentAt, CancellationToken cancellationToken)
    {
        var status = new ShuttleStatus(
            _shuttleId, state, _shuttle.Position, sentAt, _shuttle.FaultReason);
        var envelope = MessageEnvelopes.Create(MessageTypes.ShuttleStatus, _shuttleId, sentAt);
        var message = new ShuttleStatusMessage(envelope, status);
        return _bus.PublishAsync(_topics.Status(_shuttleId), JsonSerializer.Serialize(message),
            MessageFamily.Status, cancellationToken);
    }

    private sealed record EnvelopeWrapper(MessageEnvelope Envelope);
}