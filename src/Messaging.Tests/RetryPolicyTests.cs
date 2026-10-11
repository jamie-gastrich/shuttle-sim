namespace Messaging.Tests;
using Messaging;

public sealed class RetryPolicyTests
{
	[Fact]
	public void RetryPolicy_rejects_fewer_than_one_attempt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyExtensions.RetryPolicy(0, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50)));
    }

	[Fact]
	public void RetryPolicy_rejects_a_non_positive_ack_timeout()
	{
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyExtensions.RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(50)));
    }
    
	[Fact]
	public void RetryPolicy_rejects_a_negative_backoff()
	{
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyExtensions.RetryPolicy(1, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(-50)));
    }
}