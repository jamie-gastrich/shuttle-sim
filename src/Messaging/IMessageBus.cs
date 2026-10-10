namespace Messaging;

public interface IMessageBus : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken);
    Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken);
}