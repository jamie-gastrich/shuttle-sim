namespace FleetCore;

public sealed class OrderSync
{
    private readonly WmsOrderBoard _board;
    private readonly WmsStatusOutbox _outbox;

    public OrderSync(WmsOrderBoard board, WmsStatusOutbox outbox)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public OrderSyncOutcome Note(string taskId)
    {
        var outcome = _board.SyncFromTask(taskId);
        _outbox.NoteSync(taskId, outcome);
        return outcome;
    }
}