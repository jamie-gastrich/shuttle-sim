using MockWms;

namespace MockWms.Tests;

public class MockWmsControlTests
{
    [Fact]
    public void An_unknown_mode_is_rejected()
    {
        var control = new MockWmsControl(3000);

        Assert.False(control.TrySetMode(
            new MockModeRequest("offline", null),
            out var state,
            out var errors));
        Assert.Contains("mode", errors.Keys);
        Assert.Equal(new MockWmsControlState(MockWmsControl.Ok, 3000), state);
    }

    [Fact]
    public void A_missing_slow_delay_keeps_the_current_delay()
    {
        var control = new MockWmsControl(3000);

        Assert.True(control.TrySetMode(
            new MockModeRequest(MockWmsControl.Slow, null),
            out var state,
            out var errors));

        Assert.Empty(errors);
        Assert.Equal(new MockWmsControlState(MockWmsControl.Slow, 3000), state);
    }

    [Fact]
    public void A_nonpositive_slow_delay_is_rejected()
    {
        var control = new MockWmsControl(3000);

        Assert.False(control.TrySetMode(
            new MockModeRequest(MockWmsControl.Slow, 0),
            out _,
            out var errors));

        Assert.Contains("slowDelayMs", errors.Keys);
    }
}
