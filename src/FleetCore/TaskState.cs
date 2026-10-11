namespace FleetCore;

public enum TaskState 
{ 
    Queued,
    Assigned,
    InProgress,
    Done,
    Failed
}

public static class TaskStateWire
{
    public static string ToWireName(this TaskState state) => state switch
    {
        TaskState.Queued => "queued",
        TaskState.Assigned => "assigned",
        TaskState.InProgress => "in_progress",
        TaskState.Done => "done",
        TaskState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
    };
}