namespace FleetCore.Tests;

public class WmsDeliveryOutcomeTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(299)]
    public void A_2xx_reply_is_delivered(int statusCode)
    {
        Assert.Equal(WmsDeliveryResult.Delivered, WmsDeliveryOutcome.FromStatus(statusCode));
    }

    [Fact]
    public void A_500_reply_is_retry()
    {
        Assert.Equal(WmsDeliveryResult.Retry, WmsDeliveryOutcome.FromStatus(500));
    }

    [Fact]
    public void A_408_and_429_are_retry()
    {
        Assert.Equal(WmsDeliveryResult.Retry, WmsDeliveryOutcome.FromStatus(408));
        Assert.Equal(WmsDeliveryResult.Retry, WmsDeliveryOutcome.FromStatus(429));
    }

    [Fact]
    public void A_400_and_404_and_409_are_permanent()
    {
        Assert.Equal(WmsDeliveryResult.Permanent, WmsDeliveryOutcome.FromStatus(400));
        Assert.Equal(WmsDeliveryResult.Permanent, WmsDeliveryOutcome.FromStatus(404));
        Assert.Equal(WmsDeliveryResult.Permanent, WmsDeliveryOutcome.FromStatus(409));
    }

    [Fact]
    public void No_reply_is_retry()
    {
        Assert.Equal(WmsDeliveryResult.Retry, WmsDeliveryOutcome.FromStatus(null));
    }
}
