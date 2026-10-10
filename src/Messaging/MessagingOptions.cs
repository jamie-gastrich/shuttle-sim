namespace Messaging;

public record MessagingOptions(
    string Host,
    int Port,
    string ClientId,
    string TopicRoot,
    string FleetManagerId,
    int IdempotencyCacheSize
);