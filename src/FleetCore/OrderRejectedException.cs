namespace FleetCore;

public sealed class OrderRejectedException : Exception
{
    public OrderRejectedException(string message) : base(message)
    {
    }
}