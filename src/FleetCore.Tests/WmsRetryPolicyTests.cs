namespace FleetCore.Tests;

public class WmsRetryPolicyTests
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    [Fact]
    public void The_first_failure_waits_the_base_delay()
    {
        var policy = new WmsRetryPolicy(BaseDelay, MaxDelay);

        Assert.Equal(BaseDelay, policy.DelayFor(1));
    }

    [Fact]
    public void The_delay_doubles_each_failure()
    {
        var policy = new WmsRetryPolicy(BaseDelay, MaxDelay);

        Assert.Equal(TimeSpan.FromSeconds(2), policy.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.DelayFor(3));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.DelayFor(4));
    }

    [Fact]
    public void The_delay_stops_at_the_cap()
    {
        var policy = new WmsRetryPolicy(BaseDelay, MaxDelay);

        Assert.Equal(MaxDelay, policy.DelayFor(6));
        Assert.Equal(MaxDelay, policy.DelayFor(20));
    }

    [Fact]
    public void A_zero_failure_count_is_rejected()
    {
        var policy = new WmsRetryPolicy(BaseDelay, MaxDelay);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.DelayFor(0));
    }

    [Fact]
    public void Invalid_delay_bounds_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WmsRetryPolicy(TimeSpan.Zero, MaxDelay));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WmsRetryPolicy(BaseDelay, TimeSpan.FromMilliseconds(1)));
    }
}
