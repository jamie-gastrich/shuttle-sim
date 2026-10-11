namespace Messaging.Tests;
using Messaging;

public sealed class RetryPolicyTests
{
	[Fact]
	public void RetryPolicy_rejects_fewer_than_one_attempt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(0, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50)).Validate());
    }

	[Fact]
	public void RetryPolicy_rejects_a_non_positive_ack_timeout()
	{
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(50)).Validate());
    }
    
	[Fact]
	public void RetryPolicy_rejects_a_negative_backoff()
	{
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(1, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(-50)).Validate());
    }
}