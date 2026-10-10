using System.Net;
using System.Net.Sockets;
using MQTTnet.Server;
using Messaging;
using MqttMessaging;

public sealed class MqttMessageBusTests
{
    [Fact]
    public async Task Publishes_and_subscribes_over_an_in_process_broker()
    {
        var port = GetFreePort();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .Build();

        var server = new MqttServerFactory().CreateMqttServer(serverOptions);
        await server.StartAsync();

        try
        {
            var bus = new MqttMessageBus(new MqttMessageBusOptions(
                "127.0.0.1",
                serverOptions.DefaultEndpointOptions.Port,
                "mqtt-test-client",
                new QosOptions(
                    MqttQualityOfService.AtLeastOnce,
                    MqttQualityOfService.AtLeastOnce,
                    MqttQualityOfService.AtLeastOnce)));

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await bus.ConnectAsync(CancellationToken.None);
            await bus.SubscribeAsync("sample/topic", MessageFamily.Status,
                (message, _) =>
                {
                    received.TrySetResult(message.Payload);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            await bus.PublishAsync("sample/topic", "hello", MessageFamily.Status, CancellationToken.None);

            Assert.Equal("hello", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await bus.DisposeAsync();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Subscribe_receives_only_the_matching_topic()
    {
        var port = GetFreePort();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .Build();

        var server = new MqttServerFactory().CreateMqttServer(serverOptions);
        await server.StartAsync();

        try
        {
            var bus = new MqttMessageBus(new MqttMessageBusOptions(
                "127.0.0.1",
                serverOptions.DefaultEndpointOptions.Port,
                "mqtt-test-client-2",
                new QosOptions(
                    MqttQualityOfService.AtLeastOnce,
                    MqttQualityOfService.AtLeastOnce,
                    MqttQualityOfService.AtLeastOnce)));

            var received = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var matches = 0;

            await bus.ConnectAsync(CancellationToken.None);
            await bus.SubscribeAsync("sample/+/status", MessageFamily.Status,
                (message, _) =>
                {
                    Interlocked.Increment(ref matches);

                    if (message.Topic == "sample/SH-01/status" && message.Payload == "match")
                        received.TrySetResult(message.Payload);

                    return Task.CompletedTask;
                },
                CancellationToken.None);

            await bus.PublishAsync("sample/SH-01/extra", "miss", MessageFamily.Status, CancellationToken.None);
            await bus.PublishAsync("sample/SH-01/status", "match", MessageFamily.Status, CancellationToken.None);

            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, matches);
            await bus.DisposeAsync();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
