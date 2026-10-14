using Contracts;

namespace FleetManager.Tests;

public sealed class AlarmEvaluatorTests
{
    
    [Fact]
    public void A_new_lost_communication_condition_raises_one_alarm()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        var alarm = Assert.Single(evaluator.Evaluate(ShuttleId, true));
        Assert.Equal(ShuttleId, alarm.ShuttleId);
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Code);
        Assert.Null(alarm.Detail);
    }
    
    [Fact]
    public void A_condition_that_stays_true_does_not_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        Assert.Single(evaluator.Evaluate(ShuttleId, true));   // starts: raises
        Assert.Empty(evaluator.Evaluate(ShuttleId, true));    // holds: silent
    }
    
    [Fact]
    public void A_cleared_condition_can_raise_again()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        Assert.Single(evaluator.Evaluate(ShuttleId, true));
        Assert.Empty(evaluator.Evaluate(ShuttleId, false));   // clear re-arms
        Assert.Single(evaluator.Evaluate(ShuttleId, true));   // raises again
    }
    
    [Fact]
    public void A_healthy_shuttle_raises_nothing()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        Assert.Empty(evaluator.Evaluate(ShuttleId, false));
        Assert.Empty(evaluator.Evaluate(ShuttleId, false));   // and latches nothing
    }
    
    [Fact]
    public void Two_shuttles_are_latched_independently()
    {
        const string ShuttleId1 = "SH-01";
        const string ShuttleId2 = "SH-02";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        Assert.Single(evaluator.Evaluate(ShuttleId1, true));   // SH-01 raises
        Assert.Single(evaluator.Evaluate(ShuttleId2, true));   // SH-02 raises too
        Assert.Empty(evaluator.Evaluate(ShuttleId1, true));    // SH-01 still latched
        Assert.Empty(evaluator.Evaluate(ShuttleId2, false));   // clear SH-02 only
        Assert.Empty(evaluator.Evaluate(ShuttleId1, true));    // SH-01 untouched
        Assert.Single(evaluator.Evaluate(ShuttleId2, true));   // SH-02 re-armed
    }
    
    [Fact]
    public void The_configured_severity_is_carried_on_the_alarm()
    {
        const string ShuttleId = "SH-01";
        var evaluator = new AlarmEvaluator(AlarmSeverities.Warning);
        var alarm = Assert.Single(evaluator.Evaluate(ShuttleId, true));
        Assert.Equal(AlarmSeverities.Warning, alarm.Severity);
    }
    
    [Fact]
    public void An_unknown_severity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlarmEvaluator("emergency"));
    }
}