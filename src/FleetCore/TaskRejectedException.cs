namespace FleetCore;

public sealed class TaskRejectedException : Exception
{
    public TaskRejectedException(string message) : base(message)
    {
    }
}