namespace FleetCore;

public sealed class FleetTask
{
    private DateTime? _lastEventAt;
    public FleetTask(string taskId, string toLane, DateTime createdAt)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentException(nameof(TaskId), "TaskId cannot be empty.");
        if (string.IsNullOrEmpty(toLane))
            throw new ArgumentException(nameof(ToLane), "ToLane cannot be empty.");

        TaskId = taskId;
        ToLane = toLane;
        CreatedAt = createdAt;
    }
    public string TaskId { get; }
    public string ToLane { get; }
    public DateTime CreatedAt { get; }
    public TaskState State { get; private set; } = TaskState.Queued;
    public string? AssignedShuttleId { get; private set; }
    public string? FailureReason { get; private set; }
    public void Assign(string shuttleId, DateTime now)
    {
        // EnsureInOrder; must be Queued; shuttleId not blank; set the two fields; _lastEventAt = now
        EnsureInOrder(now);
        if (State != TaskState.Queued)
            throw new TaskRejectedException("Cannot assign a task that is not Queued.");
        if (string.IsNullOrEmpty(shuttleId))
            throw new ArgumentException(nameof(shuttleId), "ShuttleId cannot be empty.");
        AssignedShuttleId = shuttleId;
        State = TaskState.Assigned;
        _lastEventAt = now;
    }

    public void Start(DateTime now)
    {
        // EnsureInOrder; must be Assigned; State = InProgress; _lastEventAt = now
        EnsureInOrder(now);
        if (State != TaskState.Assigned)
            throw new TaskRejectedException("Cannot start a task that is not Assigned.");
        State = TaskState.InProgress;
        _lastEventAt = now;
    }
    public void Complete(DateTime now)
    {
        // EnsureInOrder; must be InProgress; State = Done; _lastEventAt = now
        EnsureInOrder(now);
        if (State != TaskState.InProgress)
            throw new TaskRejectedException("Cannot complete a task that is not InProgress.");
        State = TaskState.Done;
        _lastEventAt = now;
    }

    public void Fail(string reason, DateTime now)
    {
        // EnsureInOrder; not Done or Failed; reason not blank; set FailureReason and State; _lastEventAt = now
        EnsureInOrder(now);
        if (State == TaskState.Done || State == TaskState.Failed)
            throw new TaskRejectedException("Cannot fail a task that is already Done or Failed.");
        if (string.IsNullOrEmpty(reason))
            throw new ArgumentException(nameof(reason), "Reason cannot be empty.");
        State = TaskState.Failed;
        FailureReason = reason;
        _lastEventAt = now;
        _lastEventAt = now;
    }

    private void EnsureInOrder(DateTime now)
    {
        // reject now earlier than _lastEventAt; equal is allowed
        if (_lastEventAt is DateTime previous && now < previous)
            throw new TaskRejectedException($"clock went backwards: {now:O} < {previous:O}");
    }
}