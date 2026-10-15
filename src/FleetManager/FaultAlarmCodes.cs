using Contracts;
using ShuttleCore;

namespace FleetManager;

public static class FaultAlarmCodes
{
    public static string? ForReason(string? faultReason) => faultReason switch
    {
        FaultReasons.BlockedPath => AlarmCodes.BlockedPath,
        _ => null,
    };
}