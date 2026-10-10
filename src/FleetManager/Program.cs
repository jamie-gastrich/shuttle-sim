using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Contracts;
using Messaging;
using MqttMessaging;

if (args.Length == 0)
{
	Console.WriteLine("Fleet Manager Coming Online");
	return 0;
}

if (args.Length != 3 || args[0] != "move")
{
	Console.Error.WriteLine("Usage: FleetManager move <shuttleId> <toLane>");
	return 2;
}

return await RunMoveAsync(args[1], args[2]);

async Task<int> RunMoveAsync(string shuttleId, string toLane)
{
	var configuration = new ConfigurationBuilder()
		.SetBasePath(AppContext.BaseDirectory)
		.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
		.AddEnvironmentVariables()
		.Build();
	var messagingSection = configuration.GetSection("Messaging");
	var messaging = messagingSection.Get<MessagingOptions>()
		?? throw new InvalidOperationException("Messaging configuration is missing.");
	var mqtt = messagingSection.Get<MqttMessageBusOptions>()
		?? throw new InvalidOperationException("MQTT configuration is missing.");
	var timeoutMs = configuration.GetValue<int>("FleetManager:RoundTripTimeoutMs");
	if (timeoutMs < 1)
		throw new InvalidOperationException("FleetManager:RoundTripTimeoutMs must be positive.");

	var topics = new TopicScheme(messaging.TopicRoot);
	var commandEnvelope = MessageEnvelopes.Create(
		MessageTypes.MoveCommand, messaging.FleetManagerId, DateTime.UtcNow);
	var command = new MoveCommandMessage(commandEnvelope, shuttleId, toLane);
	var acknowledgementReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	var statusReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	await using var bus = new MqttMessageBus(mqtt);
	await bus.ConnectAsync(CancellationToken.None);
	await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, HandleAckAsync, CancellationToken.None);
	await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, CancellationToken.None);

	using var timeout = new CancellationTokenSource(timeoutMs);
	try
	{
		await bus.PublishAsync(topics.Command(shuttleId), JsonSerializer.Serialize(command),
			MessageFamily.Command, timeout.Token);
		await Task.WhenAll(acknowledgementReceived.Task, statusReceived.Task).WaitAsync(timeout.Token);
		return 0;
	}
	catch (OperationCanceledException)
	{
		return 1;
	}

	Task HandleAckAsync(MessageReceived message, CancellationToken _)
	{
		Console.WriteLine(message.Payload);
		try
		{
			var acknowledgement = JsonSerializer.Deserialize<CommandAckMessage>(message.Payload);
			if (acknowledgement?.Envelope?.CorrelationId == commandEnvelope.MessageId)
				acknowledgementReceived.TrySetResult();
		}
		catch (JsonException)
		{
		}

		return Task.CompletedTask;
	}

	Task HandleStatusAsync(MessageReceived message, CancellationToken _)
	{
		Console.WriteLine(message.Payload);
		try
		{
			var status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
			if (status?.Status?.ShuttleId == shuttleId)
				statusReceived.TrySetResult();
		}
		catch (JsonException)
		{
		}

		return Task.CompletedTask;
	}
}
