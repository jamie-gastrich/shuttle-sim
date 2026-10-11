namespace Messaging;

public static class RetryPolicyExtensions
{   
    public sealed record RetryPolicy
    {
        public int MaxAttempts { get; }
        public TimeSpan AckTimeout { get; }
        public TimeSpan Backoff { get; }

        public RetryPolicy(int maxAttempts, TimeSpan ackTimeout, TimeSpan backoff)
        {
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than or equal to 1.");
            if (ackTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ackTimeout), "AckTimeout must be greater than TimeSpan.Zero.");
            if (backoff < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(backoff), "Backoff must be greater than or equal to zero.");

            MaxAttempts = maxAttempts;
            AckTimeout = ackTimeout;
            Backoff = backoff;
        }
    }
}