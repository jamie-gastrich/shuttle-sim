using System.Text.Json;
using Contracts;
using FleetCore;
using Messaging;

namespace FleetManager;

public static class DashboardEndpoints
{
    public static void MapDashboardRoutes(
        WebApplication app,
        IMessageBus bus,
        TopicScheme topics,
        ShuttleRegistry registry,
        TaskBoard taskBoard,
        PauseState pauseState,
        IAlarmRepository alarmRepository,
        string[] laneIds,
        int refreshIntervalMs)
    {
        app.MapGet(DashboardRoutes.Config, () =>
            Results.Ok(new { laneIds, refreshIntervalMs }));

        app.MapGet(DashboardRoutes.Shuttles, () =>
            registry.Snapshot().Select(shuttle => new
            {
                shuttle.ShuttleId,
                shuttle.State,
                shuttle.Position,
                shuttle.SeenAt,
                pauseRequested = pauseState.IsPaused(shuttle.ShuttleId),
            }).ToList());

        app.MapGet(DashboardRoutes.Tasks, () =>
            taskBoard.Queued().Select(task => new { task.TaskId, task.State, task.AssignedShuttleId }).ToList());

        app.MapGet(DashboardRoutes.Alarms, async (CancellationToken cancellationToken) =>
            await alarmRepository.LoadAllAsync(cancellationToken));

        app.MapPost(DashboardRoutes.OperatorCommand, async (
            DashboardOperatorCommandRequest request,
            CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ShuttleId) || string.IsNullOrWhiteSpace(request.Kind))
                return Results.BadRequest("shuttleId and kind are required.");

            OperatorCommandMessage.Command? kind = request.Kind switch
            {
                "Pause" => OperatorCommandMessage.Command.Pause,
                "Resume" => OperatorCommandMessage.Command.Resume,
                "Acknowledge" => OperatorCommandMessage.Command.Acknowledge,
                _ => null,
            };
            if (kind is null)
                return Results.BadRequest("kind must be Pause, Resume, or Acknowledge.");

            var command = new OperatorCommandMessage
            {
                ShuttleId = request.ShuttleId,
                Kind = kind.Value,
                Reason = request.Reason,
            };
            await bus.PublishAsync(topics.OperatorCommand, JsonSerializer.Serialize(command),
                MessageFamily.Json, cancellationToken);
            return Results.Ok();
        });
    }
}
