using Contracts;

namespace FleetManager;

public sealed class AlarmEvaluator
{
    private readonly string _lostCommunicationSeverity;
    private readonly string _blockedPathSeverity;
    private readonly object _lock = new();
    private readonly HashSet<(string ShuttleId, string Code)> _raised = new();

    public AlarmEvaluator(string lostCommunicationSeverity, string blockedPathSeverity)
    {
        if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
        _lostCommunicationSeverity = lostCommunicationSeverity;

        if (blockedPathSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(blockedPathSeverity));
        _blockedPathSeverity = blockedPathSeverity;
    }
    public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication, string? faultCode)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
            throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));

        if (faultCode is not null && faultCode != AlarmCodes.BlockedPath)
            throw new ArgumentOutOfRangeException(nameof(faultCode));

        lock (_lock)
        {
            var alarms = new List<Alarm>(2);

            var lostComms = (shuttleId, AlarmCodes.LostCommunication);
            var blockedPath = (shuttleId, AlarmCodes.BlockedPath);

            if (lostCommunication && _raised.Add(lostComms))
                alarms.Add(new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null));
            else if (!lostCommunication)
                _raised.Remove(lostComms);

            if (faultCode == AlarmCodes.BlockedPath && _raised.Add(blockedPath))
                alarms.Add(new Alarm(shuttleId, _blockedPathSeverity, AlarmCodes.BlockedPath, null));
            else if (faultCode != AlarmCodes.BlockedPath)
                _raised.Remove(blockedPath);

            return alarms;
        }
    }
}