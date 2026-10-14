using Contracts;

namespace FleetManager;

public sealed class AlarmEvaluator
{
    private readonly string _lostCommunicationSeverity;
    private readonly object _lock = new();
    private readonly HashSet<(string ShuttleId, string Code)> _raised = new();

    public AlarmEvaluator(string lostCommunicationSeverity)
    {
        if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
        _lostCommunicationSeverity = lostCommunicationSeverity;
    }
    public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
            throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));

        lock (_lock)
        {
            var key = (shuttleId, AlarmCodes.LostCommunication);

            if (lostCommunication && _raised.Add(key))
                return new[] { new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null) };

            if (!lostCommunication)
                _raised.Remove(key);

            return Array.Empty<Alarm>();
        }
    }
}