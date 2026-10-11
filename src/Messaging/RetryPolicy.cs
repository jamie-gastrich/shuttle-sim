namespace Messaging;
public sealed record RetryPolicy(int MaxAttempts, TimeSpan AckTimeout, TimeSpan Backoff);

public static class RetryPolicyExtensions
{
    public static void Validate(this RetryPolicy policy)
    {
        if (policy.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(policy.MaxAttempts), "MaxAttempts must be greater than or equal to 1.");
        if (policy.AckTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy.AckTimeout), "AckTimeout must be greater than TimeSpan.Zero.");
        if (policy.Backoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy.Backoff), "Backoff must be greater than or equal to TimeSpan.Zero.");
    }
}