using System.Net;
using System.Net.Http.Json;
using Contracts;
using Messaging;
using Microsoft.AspNetCore.Http;
using FleetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace FleetManager.Tests;

public class DashboardIntegrationTests
{
    private static WebApplication BuildApp(RecordingBus bus, out ShuttleRegistry registry, out TaskBoard taskBoard, out PauseState pauseState)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        registry = new ShuttleRegistry();
        taskBoard = new TaskBoard();
        pauseState = new PauseState();

        var laneIds = new[] { "A01-L01", "A01-L02" };
        const int refreshMs = 1500;

        var topics = new TopicScheme("shuttle");
        var pause = pauseState;
        var reg = registry;
        var board = taskBoard;

        app.MapGet(DashboardRoutes.Config, () =>
            Results.Ok(new { laneIds = laneIds, refreshIntervalMs = refreshMs }));
        app.MapGet(DashboardRoutes.Shuttles, () =>
            reg.Snapshot().Select(shuttle => new
            {
                shuttle.ShuttleId,
                shuttle.State,
                shuttle.Position,
                shuttle.SeenAt,
                pauseRequested = pause.IsPaused(shuttle.ShuttleId),
            }).ToList());
        app.MapGet(DashboardRoutes.Tasks, () =>
            board.Queued().Select(task => new { task.TaskId, task.State, task.AssignedShuttleId }).ToList());
        app.MapPost(DashboardRoutes.OperatorCommand, async (DashboardOperatorCommandRequest request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ShuttleId) || string.IsNullOrWhiteSpace(request.Kind))
                return Results.BadRequest();
            OperatorCommandMessage.Command kind = request.Kind switch
            {
                "Pause" => OperatorCommandMessage.Command.Pause,
                "Resume" => OperatorCommandMessage.Command.Resume,
                "Acknowledge" => OperatorCommandMessage.Command.Acknowledge,
                _ => (OperatorCommandMessage.Command)(-1),
            };
            if ((int)kind < 0) return Results.BadRequest();
            var command = new OperatorCommandMessage { ShuttleId = request.ShuttleId, Kind = kind, Reason = request.Reason };
            await bus.PublishAsync(topics.OperatorCommand, System.Text.Json.JsonSerializer.Serialize(command), MessageFamily.Json, CancellationToken.None);
            return Results.Ok();
        });

        return app;
    }

    [Fact]
    public async Task Config_returns_lanes_and_refresh_interval()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out _, out _, out _);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Config);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<ConfigDto>();
            Assert.NotNull(payload);
            Assert.Equal(2, payload!.laneIds.Length);
            Assert.Equal(1500, payload.refreshIntervalMs);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Shuttles_returns_snapshots_with_pause_requested()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out var registry, out _, out var pauseState);
        registry.Record(new ShuttleStatus("SH-01", "idle", "A01-L01", DateTime.UtcNow, null));
        pauseState.Pause("SH-01");
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Shuttles);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<List<ShuttleDto>>();
            Assert.NotNull(payload);
            Assert.Single(payload!);
            Assert.Equal("SH-01", payload[0].shuttleId);
            Assert.True(payload[0].pauseRequested);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tasks_returns_queued_tasks()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out _, out var taskBoard, out _);
        taskBoard.Add(new FleetTask("T1", "A01-L02", DateTime.UtcNow));
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Tasks);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<List<TaskDto>>();
            Assert.NotNull(payload);
            Assert.Single(payload!);
            Assert.Equal("T1", payload[0].taskId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Operator_command_valid_publishes_on_operator_topic()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out _, out _, out _);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "SH-01", kind = "Pause", reason = (string?)null });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Single(bus.Published);
            Assert.Equal("shuttle/operator/command", bus.Published[0].Topic);
            Assert.Equal(MessageFamily.Json, bus.Published[0].Family);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Operator_command_invalid_kind_is_rejected_and_not_published()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out _, out _, out _);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "SH-01", kind = "Dance", reason = (string?)null });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Empty(bus.Published);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Operator_command_blank_shuttle_id_is_rejected()
    {
        var bus = new RecordingBus();
        var app = BuildApp(bus, out _, out _, out _);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "   ", kind = "Pause", reason = (string?)null });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Empty(bus.Published);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed record ConfigDto(string[] laneIds, int refreshIntervalMs);
    private sealed record ShuttleDto(string shuttleId, string state, string position, DateTime seenAt, bool pauseRequested);
    private sealed record TaskDto(string taskId, TaskState state, string? assignedShuttleId);
}
