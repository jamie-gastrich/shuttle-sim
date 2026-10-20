namespace FleetCore;

public static class WmsOrderLadder
{
    public static string ForTask(TaskState taskState) => taskState switch
    {
        TaskState.Queued => WmsOrderStates.Received,
        TaskState.Assigned => WmsOrderStates.Received,
        TaskState.InProgress => WmsOrderStates.InProgress,
        TaskState.Done => WmsOrderStates.Done,
        TaskState.Failed => WmsOrderStates.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(taskState), taskState, "unknown task state"),
    };

    public static int Rank(string orderState) => orderState switch
    {
        "received" => 0,
        "in_progress" => 1,
        "done" => 2,
        "failed" => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(orderState), orderState, "unknown order state"),
    };
}