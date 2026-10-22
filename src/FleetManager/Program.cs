using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Contracts;
using Messaging;
using MqttMessaging;
using FleetManager;
using ShuttleCore;
using FleetCore;

if (args.Length == 0)
{
	Console.WriteLine("Fleet Manager Coming Online");
	return 0;
}

if (args.Length == 1 && args[0] == "serve")
	return await RunServeAsync();


if (args.Length == 2 && args[0] == "recover")
	return await RunRecoverAsync(args[1]);

if (args.Length == 3 && args[0] == "move")
	return await RunMoveAsync(args[1], args[2]);

Console.Error.WriteLine(
	"Usage: FleetManager [serve | move <shuttleId> <toLane> | recover <shuttleId>]");
return 2;

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

	var ackTimeoutMs = configuration.GetValue<int>("FleetManager:AckTimeoutMs");
	if (ackTimeoutMs < 1)
		throw new InvalidOperationException("FleetManager:AckTimeoutMs must be positive.");

	var maxAttempts = configuration.GetValue<int>("FleetManager:MaxAttempts");
	if (maxAttempts < 1)
		throw new InvalidOperationException("FleetManager:MaxAttempts must be positive.");

	var backoffMs = configuration.GetValue<int>("FleetManager:BackoffMs");
	if (backoffMs < 0)
		throw new InvalidOperationException("FleetManager:BackoffMs must not be negative.");

	var topics = new TopicScheme(messaging.TopicRoot);

	var commandEnvelope = MessageEnvelopes.Create(
		MessageTypes.MoveCommand, messaging.FleetManagerId, DateTime.UtcNow);

	var command = new MoveCommandMessage(commandEnvelope, shuttleId, toLane);
	var statusReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	await using var bus = new MqttMessageBus(mqtt);
	await bus.ConnectAsync(CancellationToken.None);
	var retry = new RetryPolicyExtensions.RetryPolicy(
		maxAttempts,
		TimeSpan.FromMilliseconds(ackTimeoutMs),
		TimeSpan.FromMilliseconds(backoffMs));
	var sender = new ReliableCommandSender(bus, topics, retry);
	using var overall = new CancellationTokenSource();
	overall.CancelAfter(timeoutMs);

	await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, HandleAckAsync, CancellationToken.None);
	await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, CancellationToken.None);
	try
	{
		var result = await sender.SendAsync(command, overall.Token);
		switch (result.Status)
		{
			case CommandSendStatus.Acknowledged:
				await statusReceived.Task.WaitAsync(overall.Token);
				return 0;
			case CommandSendStatus.Rejected:
				Console.Error.WriteLine(result.Acknowledgement?.Reason ?? "Command rejected.");
				return 1;
			case CommandSendStatus.NoAcknowledgement:
				Console.Error.WriteLine(
					$"No acknowledgement for shuttle {shuttleId}, command {commandEnvelope.MessageId}, after {result.Attempts} attempts.");
				return 1;
			default:
				throw new InvalidOperationException($"Unexpected command result: {result.Status}.");
		}
	}
	catch (OperationCanceledException)
	{
		return 1;
	}

	async Task HandleAckAsync(MessageReceived message, CancellationToken cancellationToken)
	{
		Console.WriteLine(message.Payload);
		await sender.OnAckReceivedAsync(message, cancellationToken);
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

async Task<int> RunServeAsync()
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

	var livenessMs = configuration.GetValue<int>("FleetManager:LivenessTimeoutMs");
	if (livenessMs < 1)
		throw new InvalidOperationException("FleetManager:LivenessTimeoutMs must be positive.");
	var intervalMs = configuration.GetValue<int>("FleetManager:AlarmEvaluationIntervalMs");
	if (intervalMs < 1)
		throw new InvalidOperationException("FleetManager:AlarmEvaluationIntervalMs must be positive.");
	var severity = configuration.GetValue<string>("FleetManager:LostCommunicationSeverity")
		?? throw new InvalidOperationException("FleetManager:LostCommunicationSeverity is missing.");
	var blockedPathSeverity = configuration.GetValue<string>("FleetManager:BlockedPathSeverity")
	?? throw new InvalidOperationException("FleetManager:BlockedPathSeverity is missing.");
	var listenUrl = configuration.GetValue<string>("Kestrel:Endpoints:Http:Url");
	if (string.IsNullOrWhiteSpace(listenUrl))
		throw new InvalidOperationException("Kestrel:Endpoints:Http:Url must be set.");
	var wmsBaseUrl = configuration.GetValue<string>("Wms:BaseUrl")
		?? throw new InvalidOperationException("Wms:BaseUrl is missing.");
	// Checked before the bus connects: a bad URL must stop serve at startup,
	// not kill the process later when the pusher first tries to send.
	var wmsUri = WmsEndpoint.Parse(wmsBaseUrl);
	var wmsTimeoutMs = configuration.GetValue<int>("Wms:StatusTimeoutMs");
	if (wmsTimeoutMs < 1)
		throw new InvalidOperationException("Wms:StatusTimeoutMs must be positive.");
	var wmsIntervalMs = configuration.GetValue<int>("Wms:StatusIntervalMs");
	if (wmsIntervalMs < 1)
		throw new InvalidOperationException("Wms:StatusIntervalMs must be positive.");
	var tickIntervalMs = configuration.GetValue<int>("FleetManager:TaskExecutionIntervalMs");
	if (tickIntervalMs < 1)
		throw new InvalidOperationException("FleetManager:TaskExecutionIntervalMs must be positive.");
	var ackTimeoutMs = configuration.GetValue<int>("FleetManager:AckTimeoutMs");
	if (ackTimeoutMs < 1)
		throw new InvalidOperationException("FleetManager:AckTimeoutMs must be positive.");
	var maxAttempts = configuration.GetValue<int>("FleetManager:MaxAttempts");
	if (maxAttempts < 1)
		throw new InvalidOperationException("FleetManager:MaxAttempts must be positive.");
	var backoffMs = configuration.GetValue<int>("FleetManager:BackoffMs");
	if (backoffMs < 0)
		throw new InvalidOperationException("FleetManager:BackoffMs must not be negative.");

	var topics = new TopicScheme(messaging.TopicRoot);
	var health = new ShuttleHealth(TimeSpan.FromMilliseconds(livenessMs));
	var evaluator = new AlarmEvaluator(severity, blockedPathSeverity);
	await using var bus = new MqttMessageBus(mqtt);
	await bus.ConnectAsync(CancellationToken.None);
	var loop = new FleetServeLoop(bus, topics, health, evaluator, messaging.FleetManagerId,
		TimeSpan.FromMilliseconds(intervalMs), () => DateTime.UtcNow);
	var taskBoard = new TaskBoard();
	var board = new WmsOrderBoard(taskBoard);
	var outbox = new WmsStatusOutbox();
	var orderSync = new OrderSync(board, outbox);
	var repository = new InMemoryFleetTaskRepository();
	var dispatcher = new TaskDispatcher(taskBoard, repository, orderSync);
	var registry = new ShuttleRegistry();
	var retry = new RetryPolicyExtensions.RetryPolicy(
		maxAttempts, TimeSpan.FromMilliseconds(ackTimeoutMs), TimeSpan.FromMilliseconds(backoffMs));
	var sender = new ReliableCommandSender(bus, topics, retry);
	var execution = new TaskExecutionLoop(dispatcher, registry, sender, messaging.FleetManagerId,
		TimeSpan.FromMilliseconds(tickIntervalMs), () => DateTime.UtcNow);
	using var wmsClient = new HttpClient
	{
		BaseAddress = wmsUri,
		Timeout = TimeSpan.FromMilliseconds(wmsTimeoutMs),
	};
	var pusher = new WmsStatusPusher(board, outbox, wmsClient, TimeSpan.FromMilliseconds(wmsIntervalMs));

	await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, execution.HandleStatusAsync, CancellationToken.None);
	await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);
	Console.WriteLine($"Fleet manager serving on {topics.AllStatuses}");
	using var stopping = new CancellationTokenSource();
	var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
	{
		eventArgs.Cancel = true;
		stopped.TrySetResult();
	};
	Console.CancelKeyPress += cancelHandler;
	var loopTask = loop.RunAsync(stopping.Token);
	var pushTask = pusher.RunAsync(stopping.Token);
	var executionTask = execution.RunAsync(stopping.Token);
	var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
	{
		Args = args,
		ContentRootPath = AppContext.BaseDirectory,   // same folder the config reader uses
	});
	var webApp = webBuilder.Build();
	webApp.MapPost(WmsRoutes.Orders, (SubmitOrderRequest request) =>
	{
		var errors = WmsOrderValidator.Validate(request);
		if (errors.Count > 0)
			return Results.ValidationProblem(errors);

		var result = board.Submit(request, DateTime.UtcNow);
		return result.Outcome switch
		{
			SubmitOutcome.Created => Results.Created(WmsRoutes.Order(result.Order.OrderId), result.Order),
			SubmitOutcome.AlreadyKnown => Results.Ok(result.Order),
			_ => Results.Problem(statusCode: 409, title: "Order id already used"),
		};
	});
	webApp.MapGet(WmsRoutes.Order("{orderId}"), (string orderId) =>
		board.TryGet(orderId, out var order)
			? Results.Ok(order)
			: Results.Problem(statusCode: 404, title: "Unknown order"));
	var webTask = webApp.RunAsync(stopping.Token);
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
		await loopTask;
	}
	catch (OperationCanceledException) when (stopping.IsCancellationRequested)
	{
		// Expected: the loop stopped because we cancelled it.
	}
	catch (Exception ex)
	{
		Console.WriteLine($"fleet manager: the serve loop stopped unexpectedly: {ex.Message}");
	}
	try
	{
		await pushTask;
	}
	catch (OperationCanceledException) when (stopping.IsCancellationRequested)
	{
		// Expected: the pushTask stopped because we cancelled it.
	}
	catch (Exception ex)
	{
		Console.WriteLine($"fleet manager: the status push loop stopped unexpectedly: {ex.Message}");
	}
	try
	{
		await webTask;
	}
	catch (OperationCanceledException) when (stopping.IsCancellationRequested)
	{
		// Expected: the webTask stopped because we cancelled it.
	}
	catch (Exception ex)
	{
		Console.WriteLine($"fleet manager: the web host stopped unexpectedly: {ex.Message}");
	}
	try
	{
		await executionTask;
	}
	catch (OperationCanceledException) when (stopping.IsCancellationRequested)
	{
		// Expected: the tick stopped because we cancelled it.
	}
	catch (Exception ex)
	{
		Console.WriteLine($"fleet manager: the task execution loop stopped unexpectedly: {ex.Message}");
	}
	return 0;
}

async Task<int> RunRecoverAsync(string shuttleId)
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

	var ackTimeoutMs = configuration.GetValue<int>("FleetManager:AckTimeoutMs");
	if (ackTimeoutMs < 1)
		throw new InvalidOperationException("FleetManager:AckTimeoutMs must be positive.");

	var maxAttempts = configuration.GetValue<int>("FleetManager:MaxAttempts");
	if (maxAttempts < 1)
		throw new InvalidOperationException("FleetManager:MaxAttempts must be positive.");

	var backoffMs = configuration.GetValue<int>("FleetManager:BackoffMs");
	if (backoffMs < 0)
		throw new InvalidOperationException("FleetManager:BackoffMs must not be negative.");

	var topics = new TopicScheme(messaging.TopicRoot);

	var commandEnvelope = MessageEnvelopes.Create(
		MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);

	var command = new RecoveryCommandMessage(commandEnvelope, shuttleId);
	var statusReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	await using var bus = new MqttMessageBus(mqtt);
	await bus.ConnectAsync(CancellationToken.None);
	var retry = new RetryPolicyExtensions.RetryPolicy(
		maxAttempts,
		TimeSpan.FromMilliseconds(ackTimeoutMs),
		TimeSpan.FromMilliseconds(backoffMs));
	var sender = new RecoverySender(bus, topics, retry);
	using var overall = new CancellationTokenSource();
	overall.CancelAfter(timeoutMs);

	await bus.SubscribeAsync(topics.AllRecoveryAcks, MessageFamily.Ack, HandleAckAsync, CancellationToken.None);
	await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, CancellationToken.None);
	try
	{
		var result = await sender.SendAsync(command, overall.Token);
		switch (result.Status)
		{
			case CommandSendStatus.Acknowledged:
				await statusReceived.Task.WaitAsync(overall.Token);
				return 0;
			case CommandSendStatus.Rejected:
				Console.Error.WriteLine(result.Acknowledgement?.Reason ?? "Command rejected.");
				return 1;
			case CommandSendStatus.NoAcknowledgement:
				Console.Error.WriteLine(
					$"No acknowledgement for shuttle {shuttleId}, command {commandEnvelope.MessageId}, after {result.Attempts} attempts.");
				return 1;
			default:
				throw new InvalidOperationException($"Unexpected command result: {result.Status}.");
		}
	}
	catch (OperationCanceledException)
	{
		return 1;
	}

	async Task HandleAckAsync(MessageReceived message, CancellationToken cancellationToken)
	{
		Console.WriteLine(message.Payload);
		await sender.OnRecoveryAckReceivedAsync(message, cancellationToken);
	}

	Task HandleStatusAsync(MessageReceived message, CancellationToken _)
	{
		Console.WriteLine(message.Payload);
		try
		{
			var status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
			if (status?.Status?.ShuttleId == shuttleId && status?.Status.State == ShuttleState.Idle.ToWireName())
				statusReceived.TrySetResult();
		}
		catch (JsonException)
		{
		}

		return Task.CompletedTask;
	}
}
