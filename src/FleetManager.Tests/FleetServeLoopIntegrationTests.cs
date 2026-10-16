using System.Text.Json;
using Contracts;
using Messaging;
using MqttMessaging;

namespace FleetManager.Tests;

[Trait("Category", "Integration")]
public class FleetServeLoopIntegrationTests
{
    [Fact]
    public async Task An_alarm_reaches_the_broker_on_the_shuttle_topic()
    {
        var host = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "127.0.0.1";
        var port = int.Parse(Environment.GetEnvironmentVariable("MQTT_HOST_PORT") ?? "1883");

        var listenerOptions = new MqttMessageBusOptions(host, port, "fleet-manager-test-listener",
            new QosOptions(MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce,
                MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce));
        var loopOptions = new MqttMessageBusOptions(host, port, "fleet-manager-test-loop",
            new QosOptions(MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce,
                MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce));

        var topics = new TopicScheme("shuttle");

        await using var listener = new MqttMessageBus(listenerOptions);
        await using var loopBus = new MqttMessageBus(loopOptions);
        await listener.ConnectAsync(CancellationToken.None);
        await loopBus.ConnectAsync(CancellationToken.None);

        var received = new TaskCompletionSource<AlarmMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await listener.SubscribeAsync(topics.AllAlarms, MessageFamily.Alarm, (message, _) =>
        {
            received.TrySetResult(JsonSerializer.Deserialize<AlarmMessage>(message.Payload)!);
            return Task.CompletedTask;
        }, CancellationToken.None);

        var health = new ShuttleHealth(TimeSpan.FromSeconds(30));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var loop = new FleetServeLoop(loopBus, topics, health, evaluator, "fleet-manager",
            TimeSpan.FromSeconds(1), () => start);

        await loop.HandleStatusAsync(new MessageReceived(topics.Status("SH-01"),
            StatusJson("SH-01", start)), CancellationToken.None);

        await loop.EvaluateAndPublishAsync(start.AddSeconds(31), CancellationToken.None);

        var alarm = await received.Task.WaitAsync(TimeSpan.FromSeconds(32));
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Alarm.Code);
    }

    static string StatusJson(string shuttleId, DateTime sentAt) => JsonSerializer.Serialize(
        new ShuttleStatusMessage(
            new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
                Guid.NewGuid().ToString(), null, shuttleId, sentAt),
            new ShuttleStatus(shuttleId, "idle", "A01-L01", sentAt, null)));

}