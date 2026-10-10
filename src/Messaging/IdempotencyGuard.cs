namespace Messaging;

public sealed class IdempotencyGuard
{
    private readonly int _capacity;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _completed;

    public IdempotencyGuard(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _completed = new HashSet<string>();
    }

    public bool IsDuplicate(string messageId) => _completed.Contains(messageId);

    public void MarkCompleted(string messageId)
    {
        if (_completed.Add(messageId)) _order.Enqueue(messageId);
        while (_order.Count > _capacity) _completed.Remove(_order.Dequeue());
    }
}