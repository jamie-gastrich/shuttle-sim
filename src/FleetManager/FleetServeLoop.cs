using System.Text.Json;
using Contracts;
using Messaging;
using ShuttleCore;

namespace FleetManager;

public sealed class FleetServeLoop
{
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly ShuttleHealth _health;
    private readonly AlarmEvaluator _evaluator;
    private readonly string _fleetManagerId;
    private readonly TimeSpan _evaluationInterval;
    private readonly Func<DateTime> _clock;
    private readonly object _knownLock = new();
    private readonly HashSet<string> _knownShuttles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);

    public FleetServeLoop(IMessageBus bus, TopicScheme topics, ShuttleHealth health,
    AlarmEvaluator evaluator, string fleetManagerId, TimeSpan evaluationInterval, Func<DateTime> clock)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _topics = topics ?? throw new ArgumentNullException(nameof(topics));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            if (string.IsNullOrWhiteSpace(fleetManagerId))
            {
                throw new ArgumentException("A Fleet Manager id is required.", nameof(fleetManagerId));
            }
        _fleetManagerId = fleetManagerId;
        _evaluationInterval = evaluationInterval > TimeSpan.Zero ? evaluationInterval : throw new ArgumentOutOfRangeException(nameof(evaluationInterval));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }
    public Task SubscribeAsync(CancellationToken cancellationToken) =>
    _bus.SubscribeAsync(_topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await SubscribeAsync(cancellationToken);
        using var timer = new PeriodicTimer(_evaluationInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await EvaluateAndPublishAsync(_clock(), cancellationToken);
    }

    public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        ShuttleStatusMessage? status;
        try
        {
            status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
        }
        catch (JsonException)
        {
            return Task.CompletedTask;
        }

        if (status?.Envelope is not { } envelope
            || envelope.ContractVersion != ContractVersion.Current
            || envelope.MessageType != MessageTypes.ShuttleStatus
            || string.IsNullOrWhiteSpace(envelope.MessageId)
            || string.IsNullOrWhiteSpace(status.Status?.ShuttleId))
        {
            return Task.CompletedTask;
        }

        var shuttleId = status.Status.ShuttleId;
        _health.RecordSeen(shuttleId, _clock());
        lock (_knownLock)
        {
            _knownShuttles.Add(shuttleId);
    
            var code = status.Status.State == ShuttleState.Fault.ToWireName()
                ? FaultAlarmCodes.ForReason(status.Status.FaultReason)
                : null;
            if (code is null) _faultCodes.Remove(shuttleId);
            else _faultCodes[shuttleId] = code;
        }
        return Task.CompletedTask;
    }

    public async Task EvaluateAndPublishAsync(DateTime now, CancellationToken cancellationToken)
    {
        string[] ids;
        Dictionary<string, string> faults;
        lock (_knownLock)
        {
            ids = _knownShuttles.ToArray();
            faults = new Dictionary<string, string>(_faultCodes, StringComparer.Ordinal);
        }

        foreach (var id in ids)
        {
            var lost = _health.IsUnresponsive(id, now);
            var faultCode = faults.TryGetValue(id, out var c) ? c : null;
            foreach (var alarm in _evaluator.Evaluate(id, lost, faultCode))
                await PublishAlarmAsync(alarm, now, cancellationToken);
        }
    }

    private Task PublishAlarmAsync(Alarm alarm, DateTime sentAt, CancellationToken cancellationToken)
    {
        var envelope = MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt);
        var message = new AlarmMessage(envelope, alarm);
        return _bus.PublishAsync(_topics.Alarm(alarm.ShuttleId), JsonSerializer.Serialize(message),
            MessageFamily.Alarm, cancellationToken);
    }
}