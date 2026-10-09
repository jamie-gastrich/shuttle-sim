namespace ShuttleCore;

public sealed class ShuttleRejectedException : Exception
{
    public ShuttleRejectedException(string message) : base(message)
    {
    }
}