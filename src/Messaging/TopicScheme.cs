namespace Messaging;

public sealed class TopicScheme
{
    private readonly string _root;

    public TopicScheme(string topicRoot)
    {
        if (string.IsNullOrWhiteSpace(topicRoot))
            throw new ArgumentException("topic root is required", nameof(topicRoot));
        _root = topicRoot.Trim('/');
    }

    public string Command(string shuttleId) => $"{_root}/{shuttleId}/command";
    public string Ack(string shuttleId) => $"{_root}/{shuttleId}/ack";
    public string Status(string shuttleId) => $"{_root}/{shuttleId}/status";
    public string AllAcks => $"{_root}/+/ack";
    public string AllStatuses => $"{_root}/+/status";
}