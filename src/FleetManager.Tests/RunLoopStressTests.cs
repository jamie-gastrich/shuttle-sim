using FleetCore;
using ShuttleCore;
using TrafficCore;

namespace FleetManager.Tests;

public class RunLoopStressTests
{
    [Fact]
    public async Task Paused_shuttle_does_not_complete_its_move()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 4, slotsPerLane: 1);
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var orderSync = new OrderSync(new WmsOrderBoard(board), new WmsStatusOutbox());
        var dispatcher = new TaskDispatcher(board, repository, orderSync);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", timing);
        occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        board.Add(new FleetTask("task-1", "A01-L03", start));
        var loop = new RunLoop(
            occupancy,
            track,
            traffic,
            gate,
            board,
            dispatcher,
            new[] { shuttle },
            start,
            TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await loop.TickAsync(cts.Token);
        Assert.Equal(ShuttleState.Moving, shuttle.State);
        Assert.Equal(TaskState.InProgress, board.Get("task-1").State);
        var reservedSegments = reservations.SegmentsOwnedBy(shuttle.ShuttleId);
        Assert.NotEmpty(reservedSegments);

        gate.Pause(shuttle.ShuttleId);
        for (var tick = 0; tick < 5; tick++)
        {
            result = await loop.TickAsync(cts.Token);
            Assert.False(result.AllDone);
        }

        Assert.Equal(ShuttleState.Moving, shuttle.State);
        Assert.Equal("A01-L01", shuttle.Position);
        Assert.Equal("A01-L01", occupancy.GetOccupiedLane(shuttle.ShuttleId));
        Assert.Equal(TaskState.InProgress, board.Get("task-1").State);
        Assert.Equal(reservedSegments, reservations.SegmentsOwnedBy(shuttle.ShuttleId));

        gate.Resume(shuttle.ShuttleId);
        for (var tick = 0; tick < 5 && !result.AllDone; tick++)
        {
            result = await loop.TickAsync(cts.Token);
        }

        Assert.True(result.AllDone);
        Assert.Equal(ShuttleState.Idle, shuttle.State);
        Assert.Equal(TaskState.Done, board.Get("task-1").State);
        Assert.Empty(reservations.SegmentsOwnedBy(shuttle.ShuttleId));
    }

    [Fact]
    public async Task Four_or_more_shuttles_finish_with_no_collisions_and_no_deadlock()
    {
        var rack = new Rack(aisleCount: 4, lanesPerAisle: 4, slotsPerLane: 1);
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var orderSync = new OrderSync(new WmsOrderBoard(board), new WmsStatusOutbox());
        var dispatcher = new TaskDispatcher(board, repository, orderSync);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tickInterval = TimeSpan.FromSeconds(1);

        var shuttles = new[]
        {
            new Shuttle("SH-01", rack, "A01-L01", timing),
            new Shuttle("SH-02", rack, "A01-L04", timing),
            new Shuttle("SH-03", rack, "A04-L01", timing),
            new Shuttle("SH-04", rack, "A04-L04", timing)
        };

        foreach (var shuttle in shuttles)
        {
            occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        }

        occupancy.SetOccupiedLane("BLOCKER", "A02-L02");
        Assert.False(gate.CanMove("SH-01", "A02-L04"));

        board.Add(new FleetTask("task-1", "A02-L04", start));
        board.Add(new FleetTask("task-2", "A03-L01", start.AddSeconds(1)));
        board.Add(new FleetTask("task-3", "A01-L03", start.AddSeconds(2)));
        board.Add(new FleetTask("task-4", "A01-L02", start.AddSeconds(3)));

        var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = new RunResult(0, false);
        for (var tick = 0; tick < 50 && !result.AllDone; tick++)
        {
            result = await loop.TickAsync(cts.Token);
            AssertNoSharedLanes(occupancy, shuttles);

            if (board.NextQueued() is null && shuttles.All(shuttle => shuttle.State == ShuttleState.Idle))
            {
                break;
            }
        }

        Assert.True(result.AllDone, $"run loop timed out or stalled after {result.Ticks} ticks");
        Assert.Null(board.NextQueued());
        Assert.All(shuttles, shuttle => Assert.Equal(ShuttleState.Idle, shuttle.State));
        AssertNoSharedLanes(occupancy, shuttles);
    }

    private static void AssertNoSharedLanes(OccupancyMap occupancy, IReadOnlyList<Shuttle> shuttles)
    {
        var occupiedLanes = shuttles
            .Select(shuttle => occupancy.GetOccupiedLane(shuttle.ShuttleId))
            .Where(lane => lane is not null)
            .ToList();

        Assert.Equal(occupiedLanes.Count, occupiedLanes.Distinct(StringComparer.Ordinal).Count());
    }
}