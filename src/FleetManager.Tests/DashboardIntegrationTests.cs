using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contracts;
using Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using FleetCore;

namespace FleetManager.Tests;

public class DashboardIntegrationTests
{
    [Fact]
    public async Task Config_returns_configured_lanes_and_refresh_interval()
    {
        var app = await StartAppAsync();
        try
        {
            using var client = app.Application.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Config);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<ConfigDto>();
            Assert.NotNull(payload);
            Assert.Equal(new[] { "A01-L01", "A01-L02" }, payload!.laneIds);
            Assert.Equal(1500, payload.refreshIntervalMs);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Shuttles_returns_snapshots_with_pause_requested()
    {
        var app = await StartAppAsync();
        try
        {
            app.Registry.Record(new ShuttleStatus("SH-01", "idle", "A01-L01", DateTime.UtcNow, null));
            app.PauseState.Pause("SH-01");

            using var client = app.Application.GetTestClient();
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
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Tasks_returns_queued_tasks()
    {
        var app = await StartAppAsync();
        try
        {
            app.TaskBoard.Add(new FleetTask("T1", "A01-L02", DateTime.UtcNow));

            using var client = app.Application.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Tasks);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<List<TaskDto>>();
            Assert.NotNull(payload);
            Assert.Single(payload!);
            Assert.Equal("T1", payload[0].taskId);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Alarms_returns_saved_alarms_from_the_production_route()
    {
        var app = await StartAppAsync();
        try
        {
            var raisedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            app.Alarms.Alarms.Add(new PostgresAlarmRepository.Alarm(
                "SH-ALARM-TEST", AlarmSeverities.Critical, AlarmCodes.LostCommunication, raisedAt));

            using var client = app.Application.GetTestClient();
            var res = await client.GetAsync(DashboardRoutes.Alarms);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var payload = await res.Content.ReadFromJsonAsync<List<AlarmDto>>();
            Assert.NotNull(payload);
            Assert.Single(payload!);
            Assert.Equal("SH-ALARM-TEST", payload[0].shuttleId);
            Assert.Equal(AlarmCodes.LostCommunication, payload[0].code);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Operator_command_valid_publishes_on_operator_topic()
    {
        var app = await StartAppAsync();
        try
        {
            using var client = app.Application.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "SH-01", kind = "Pause", reason = (string?)null });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Single(app.Bus.Published);
            var published = app.Bus.Published[0];
            Assert.Equal("shuttle/operator/command", published.Topic);
            Assert.Equal(MessageFamily.Json, published.Family);

            var command = JsonSerializer.Deserialize<OperatorCommandMessage>(published.Payload);
            Assert.NotNull(command);
            Assert.Equal("SH-01", command!.ShuttleId);
            Assert.Equal(OperatorCommandMessage.Command.Pause, command.Kind);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Operator_command_invalid_kind_is_rejected_and_not_published()
    {
        var app = await StartAppAsync();
        try
        {
            using var client = app.Application.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "SH-01", kind = "Dance", reason = (string?)null });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Empty(app.Bus.Published);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    [Fact]
    public async Task Operator_command_blank_shuttle_id_is_rejected()
    {
        var app = await StartAppAsync();
        try
        {
            using var client = app.Application.GetTestClient();
            var res = await client.PostAsJsonAsync(DashboardRoutes.OperatorCommand,
                new { shuttleId = "   ", kind = "Pause", reason = (string?)null });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Empty(app.Bus.Published);
        }
        finally
        {
            await StopAsync(app.Application);
        }
    }

    private static async Task<DashboardTestApp> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var bus = new RecordingBus();
        var alarms = new RecordingAlarmRepository();
        var registry = new ShuttleRegistry();
        var taskBoard = new TaskBoard();
        var pauseState = new PauseState();

        // The test uses the same route mapping as Program.cs, so a change to
        // the real routes is exercised here.
        DashboardEndpoints.MapDashboardRoutes(app, bus, new TopicScheme("shuttle"),
            registry, taskBoard, pauseState, alarms,
            new[] { "A01-L01", "A01-L02" }, 1500);

        await app.StartAsync();
        return new DashboardTestApp(app, bus, alarms, registry, taskBoard, pauseState);
    }

    private static async Task StopAsync(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private sealed record DashboardTestApp(
        WebApplication Application,
        RecordingBus Bus,
        RecordingAlarmRepository Alarms,
        ShuttleRegistry Registry,
        TaskBoard TaskBoard,
        PauseState PauseState);

    private sealed class RecordingAlarmRepository : IAlarmRepository
    {
        public List<PostgresAlarmRepository.Alarm> Alarms { get; } = new();

        public Task SaveAsync(PostgresAlarmRepository.Alarm alarm, CancellationToken cancellationToken)
        {
            Alarms.Add(alarm);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PostgresAlarmRepository.Alarm>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PostgresAlarmRepository.Alarm>>(Alarms.ToList());

        public Task<PostgresAlarmRepository.Alarm?> GetAsync(string shuttleId, string code, CancellationToken cancellationToken) =>
            Task.FromResult(Alarms.FirstOrDefault(alarm => alarm.ShuttleId == shuttleId && alarm.Code == code));

        public Task<int> CountUnacknowledgedAsync(string shuttleId, CancellationToken cancellationToken) =>
            Task.FromResult(Alarms.Count(alarm => alarm.ShuttleId == shuttleId && alarm.AcknowledgedAt is null));

        public Task AcknowledgeAsync(string shuttleId, CancellationToken cancellationToken)
        {
            for (var index = 0; index < Alarms.Count; index++)
            {
                if (Alarms[index].ShuttleId == shuttleId && Alarms[index].AcknowledgedAt is null)
                    Alarms[index] = Alarms[index] with { AcknowledgedAt = DateTime.UtcNow };
            }
            return Task.CompletedTask;
        }
    }

    private sealed record ConfigDto(string[] laneIds, int refreshIntervalMs);
    private sealed record ShuttleDto(string shuttleId, string state, string position, DateTime seenAt, bool pauseRequested);
    private sealed record TaskDto(string taskId, TaskState state, string? assignedShuttleId);
    private sealed record AlarmDto(string shuttleId, string severity, string code, DateTime raisedAt, DateTime? acknowledgedAt);
}
