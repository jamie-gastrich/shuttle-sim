namespace FleetManager;

public class ShuttleHealthTests
{
    [Fact]
    public void A_recent_heartbeat_keeps_a_shuttle_responsive()
    {
        const string ShuttleId = "SH-01";
        var now = DateTime.UtcNow;
        var shuttleHealth = new ShuttleHealth(TimeSpan.FromSeconds(30));

        shuttleHealth.RecordSeen(ShuttleId, now);

        Assert.False(shuttleHealth.IsUnresponsive(ShuttleId, now.AddSeconds(10)));
    }

    [Fact]
    public void Silence_past_the_timeout_marks_a_shuttle_unresponsive()
    {
        const string ShuttleId = "SH-01";
        var now = DateTime.UtcNow;
        var shuttleHealth = new ShuttleHealth(TimeSpan.FromSeconds(30));

        shuttleHealth.RecordSeen(ShuttleId, now);

        Assert.True(shuttleHealth.IsUnresponsive(ShuttleId, now.AddSeconds(31)));
    }

    [Fact]
    public void A_shuttle_silent_for_exactly_the_timeout_is_still_responsive()
    {
        const string ShuttleId = "SH-01";
        var now = DateTime.UtcNow;
        var shuttleHealth = new ShuttleHealth(TimeSpan.FromSeconds(30));

        shuttleHealth.RecordSeen(ShuttleId, now);

        Assert.False(shuttleHealth.IsUnresponsive(ShuttleId, now.AddSeconds(30)));
    }

    [Fact]
    public void A_new_heartbeat_makes_a_silent_shuttle_responsive_again()
    {
        const string ShuttleId = "SH-01";
        var now = DateTime.UtcNow;
        var shuttleHealth = new ShuttleHealth(TimeSpan.FromSeconds(30));

        shuttleHealth.RecordSeen(ShuttleId, now);
        shuttleHealth.RecordSeen(ShuttleId, now.AddSeconds(60)); // Simulate silence
        shuttleHealth.RecordSeen(ShuttleId, now.AddSeconds(75)); // Simulate new heartbeat
        shuttleHealth.RecordSeen(ShuttleId, now.AddSeconds(10)); // Simulate older heartbeat

        Assert.Equal(now.AddSeconds(75), shuttleHealth.LastSeenAt(ShuttleId));
        Assert.False(shuttleHealth.IsUnresponsive(ShuttleId, now.AddSeconds(75)));
    }

    [Fact]
    public void A_shuttle_never_seen_is_unresponsive()
    {
        const string ShuttleId = "SH-01";
        var now = DateTime.UtcNow;
        var shuttleHealth = new ShuttleHealth(TimeSpan.FromSeconds(30));

        Assert.True(shuttleHealth.IsUnresponsive(ShuttleId, now));
    }
}