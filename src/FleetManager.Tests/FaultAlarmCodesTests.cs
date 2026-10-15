using Contracts;
using ShuttleCore;

namespace FleetManager.Tests;

public class FaultAlarmCodesTests
{
    [Fact]
    public void A_blocked_path_reason_maps_to_the_blocked_path_alarm_code()
    {
        var code = FaultAlarmCodes.ForReason(FaultReasons.BlockedPath);
        Assert.Equal(AlarmCodes.BlockedPath, code);
    }

    [Fact]
    public void An_unknown_or_blank_reason_maps_to_no_alarm_code()
    {
        Assert.Null(FaultAlarmCodes.ForReason(null));
        Assert.Null(FaultAlarmCodes.ForReason(""));
        Assert.Null(FaultAlarmCodes.ForReason("some other reason"));
    }
}