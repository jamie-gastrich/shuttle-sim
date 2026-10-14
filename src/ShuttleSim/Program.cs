using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Contracts;
using Messaging;
using MqttMessaging;
using ShuttleCore;

if (args.Length == 0)
{
    RunDemo();
    return 0;
}

if (args.Length == 1 && args[0] == "serve")
    return await RunServerAsync();

Console.Error.WriteLine("Usage: ShuttleSim [serve]");
return 2;

void RunDemo()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var timing = new ShuttleTiming(
        MovePerLane: TimeSpan.FromSeconds(5),
        Load: TimeSpan.FromSeconds(2),
        Unload: TimeSpan.FromSeconds(2));
    var shuttle = new Shuttle("SH-01", rack, startLane: "A01-L01", timing);

    var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    rack.Place("A01-L01-S01", "PLT-001");

    Report(start);
    shuttle.StartLoad("A01-L01-S01", start);
    Report(start);

    var loaded = start.Add(timing.Load);
    shuttle.CompleteLoad(loaded);
    Report(loaded);

    shuttle.StartMove("A02-L02", loaded);
    Report(loaded);

    var travel = TimeSpan.FromTicks(rack.DistanceInLanes("A01-L01", "A02-L02") * timing.MovePerLane.Ticks);
    var arrived = loaded.Add(travel);
    shuttle.CompleteMove(arrived);
    Report(arrived);

    shuttle.StartUnload("A02-L02-S02", arrived);
    Report(arrived);

    var placed = arrived.Add(timing.Unload);
    shuttle.CompleteUnload(placed);
    Report(placed);

    void Report(DateTime at)
    {
        var status = new ShuttleStatus(
            shuttle.ShuttleId,
            shuttle.State.ToWireName(),
            shuttle.Position,
            at,
            shuttle.FaultReason);
        Console.WriteLine(JsonSerializer.Serialize(status));
    }
}

async Task<int> RunServerAsync()
{
    var configuration = LoadConfiguration();
    var messagingSection = configuration.GetSection("Messaging");
    var messaging = messagingSection.Get<MessagingOptions>()
        ?? throw new InvalidOperationException("Messaging configuration is missing.");
    var mqtt = messagingSection.Get<MqttMessageBusOptions>()
        ?? throw new InvalidOperationException("MQTT configuration is missing.");
    var shuttleId = configuration["Shuttle:Id"]
        ?? throw new InvalidOperationException("Shuttle:Id is missing.");
    var startLane = configuration["Shuttle:StartLane"]
        ?? throw new InvalidOperationException("Shuttle:StartLane is missing.");

    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var timing = new ShuttleTiming(
        MovePerLane: TimeSpan.FromSeconds(5),
        Load: TimeSpan.FromSeconds(2),
        Unload: TimeSpan.FromSeconds(2));
    var shuttle = new Shuttle(shuttleId, rack, startLane, timing);
    var topics = new TopicScheme(messaging.TopicRoot);

    await using var bus = new LoggingMessageBus(
        new MqttMessageBus(mqtt with { ClientId = $"{mqtt.ClientId}-{shuttleId}" }));
    var connectedEndpoint = new ShuttleEndpoint(shuttle, bus, topics, shuttleId,
        new IdempotencyGuard(messaging.IdempotencyCacheSize));
    await bus.ConnectAsync(CancellationToken.None);
    await bus.SubscribeAsync(topics.Command(shuttleId), MessageFamily.Command,
        (message, ct) =>
        {
            Console.WriteLine($"handled {message.Topic} {MessageIdOf(message.Payload)}");
            return connectedEndpoint.HandleCommandAsync(message.Payload, DateTime.UtcNow, ct);
        },
        CancellationToken.None);
    
    var heartbeatIntervalMs = configuration.GetValue<int>("Shuttle:HeartbeatIntervalMs");

    if (heartbeatIntervalMs < 1)
        throw new InvalidOperationException("Shuttle:HeartbeatIntervalMs must be positive.");
    using var stopping = new CancellationTokenSource();
    var heartbeat = new HeartbeatPublisher(connectedEndpoint, TimeSpan.FromMilliseconds(heartbeatIntervalMs));
    var heartbeatTask = heartbeat.RunAsync(() => DateTime.UtcNow, stopping.Token);

    Console.WriteLine($"Shuttle {shuttleId} listening on {topics.Command(shuttleId)}");
    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopped.TrySetResult();
    };
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await stopped.Task;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
    stopping.Cancel();
    try
    {
        await heartbeatTask;
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        // Expected: the heartbeat loop stopped because we cancelled it.
    }

    return 0;
}

IConfigurationRoot LoadConfiguration() => new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

static string MessageIdOf(string payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("Envelope", out var envelope) &&
               envelope.TryGetProperty("MessageId", out var id)
            ? id.GetString() ?? "(unknown)"
            : "(unknown)";
    }
    catch (JsonException)
    {
        return "(unknown)";
    }
}

sealed class LoggingMessageBus : IMessageBus
{
    private readonly IMessageBus _inner;

    public LoggingMessageBus(IMessageBus inner) => _inner = inner;

    public Task ConnectAsync(CancellationToken cancellationToken) => _inner.ConnectAsync(cancellationToken);

    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken)
    {
        Console.WriteLine($"published {topic} {payload}");
        return _inner.PublishAsync(topic, payload, family, cancellationToken);
    }

    public Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken)
        => _inner.SubscribeAsync(topicFilter, family, handler, cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
