using FleetCore;
using Microsoft.AspNetCore.Http;

namespace FleetManager;

public static class WmsOrderCancelEndpoint
{
    public static IResult Handle(WmsOrderBoard board, TaskBoard taskBoard, string orderId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(taskBoard);

        if (!board.TryGet(orderId, out var order) || order is null)
            return Results.Problem(statusCode: 404, title: "Unknown order");

        if (!taskBoard.TryGet(order.OrderId, out var task) || task is null)
            return Results.Problem(statusCode: 404, title: "Task not found for order");

        if (task.State == TaskState.Cancelled)
        {
            board.SyncFromTask(task.TaskId);
            var finalState = board.TryGet(orderId, out var updatedOrder) && updatedOrder is not null
                ? updatedOrder.State
                : WmsOrderStates.Cancelled;
            return Results.Ok(new { orderId, newState = finalState });
        }

        task.Cancel(now);
        board.SyncFromTask(task.TaskId);

        var finalStateAfterCancel = board.TryGet(orderId, out var currentOrder) && currentOrder is not null
            ? currentOrder.State
            : WmsOrderStates.Cancelled;
        return Results.Ok(new { orderId, newState = finalStateAfterCancel });
    }
}
