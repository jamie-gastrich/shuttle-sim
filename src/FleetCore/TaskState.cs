namespace FleetCore;

public enum TaskState 
{ 
    Queued,
    Assigned,
    InProgress,
    Done,
    Failed,
    Paused,
    Cancelled
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
        TaskState.Paused => "paused",
        TaskState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
    };

    public static TaskState FromWireName(string wireName)
    {
        ArgumentNullException.ThrowIfNull(wireName);
        return wireName switch
        {
            "queued" => TaskState.Queued,
            "assigned" => TaskState.Assigned,
            "in_progress" => TaskState.InProgress,
            "done" => TaskState.Done,
            "failed" => TaskState.Failed,
            "paused" => TaskState.Paused,
            "cancelled" => TaskState.Cancelled,
            _ => throw new ArgumentException($"Unknown task state wire name '{wireName}'.", nameof(wireName)),
        };
    }
}