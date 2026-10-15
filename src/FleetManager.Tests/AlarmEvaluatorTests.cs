using Contracts;

namespace FleetManager.Tests;

public sealed class AlarmEvaluatorTests
{
    
    [Fact]
    public void A_new_lost_communication_condition_raises_one_alarm()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, "");
        var alarm = Assert.Single(evaluator.Evaluate(ShuttleId, true, null));
        Assert.Equal(ShuttleId, alarm.ShuttleId);
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Code);
        Assert.Null(alarm.Detail);
    }
    
    [Fact]
    public void A_condition_that_stays_true_does_not_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, "");
        Assert.Single(evaluator.Evaluate(ShuttleId, true, null));   // starts: raises
        Assert.Empty(evaluator.Evaluate(ShuttleId, true, null));    // holds: silent
    }
    
    [Fact]
    public void A_cleared_condition_can_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, "");
        Assert.Single(evaluator.Evaluate(ShuttleId, true, null));
        Assert.Empty(evaluator.Evaluate(ShuttleId, false, null));   // clear re-arms
        Assert.Single(evaluator.Evaluate(ShuttleId, true, null));   // raises again
    }
    
    [Fact]
    public void A_healthy_shuttle_raises_nothing()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, "");
        Assert.Empty(evaluator.Evaluate(ShuttleId, false, null));
        Assert.Empty(evaluator.Evaluate(ShuttleId, false, null));   // and latches nothing
    }
    
    [Fact]
    public void Two_shuttles_are_latched_independently()
    {
        const string ShuttleId1 = "SH-01";
        const string ShuttleId2 = "SH-02";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, "");
        Assert.Single(evaluator.Evaluate(ShuttleId1, true, null));   // SH-01 raises
        Assert.Single(evaluator.Evaluate(ShuttleId2, true, null));   // SH-02 raises too
        Assert.Empty(evaluator.Evaluate(ShuttleId1, true, null));    // SH-01 still latched
        Assert.Empty(evaluator.Evaluate(ShuttleId2, false, null));   // clear SH-02 only
        Assert.Empty(evaluator.Evaluate(ShuttleId1, true, null));    // SH-01 untouched
        Assert.Single(evaluator.Evaluate(ShuttleId2, true, null));   // SH-02 re-armed
    }
    
    [Fact]
    public void The_configured_severity_is_carried_on_the_alarm()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Warning, "");
        var alarm = Assert.Single(evaluator.Evaluate(ShuttleId, true, null));
        Assert.Equal(AlarmSeverities.Warning, alarm.Severity);
    }
    
    [Fact]
    public void An_unknown_severity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlarmEvaluator("emergency", ""));
    }
    
    [Fact]
    public void A_blocked_path_fault_raises_one_alarm_with_the_blocked_path_severity()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator("", AlarmSeverities.Warning);
        var alarm = Assert.Single(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));
        Assert.Equal(ShuttleId, alarm.ShuttleId);
        Assert.Equal(AlarmCodes.BlockedPath, alarm.Code);
        Assert.Equal(AlarmSeverities.Warning, alarm.Severity);
    }

    [Fact]
    public void A_blocked_path_fault_that_persists_does_not_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator("", AlarmSeverities.Warning);
        Assert.Single(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));   // starts: raises
        Assert.Empty(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));    // holds: silent
    }

    [Fact]
    public void A_cleared_blocked_path_fault_can_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator("", AlarmSeverities.Warning);
        Assert.Single(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));
        Assert.Empty(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));   // clear re-arms
        Assert.Single(evaluator.Evaluate(ShuttleId, false, AlarmCodes.BlockedPath));   // raises again
    }

    [Fact]
    public void A_faulted_shuttle_that_goes_silent_raises_two_alarms()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning);
        Assert.Equal(2, evaluator.Evaluate(ShuttleId, true, AlarmCodes.BlockedPath).Count());   // raises both alarms
    }

    [Fact]
    public void An_unknown_fault_code_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlarmEvaluator("", "Something Wrong"));
    }
}