using FleetCore;
using ShuttleCore;
using TrafficCore;

namespace FleetManager.Tests;

public class RunLoopReassignmentTests
{
    private static TaskDispatcher NewDispatcher(TaskBoard board, IFleetTaskRepository repository) =>
        new(board, repository, new OrderSync(new WmsOrderBoard(board), new WmsStatusOutbox()));

    [Fact]
    public async Task A_faulted_shuttle_requeues_its_task_and_another_shuttle_finishes_it()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1);
        rack.MarkBlocked("A01-L02");
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = NewDispatcher(board, repository);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tickInterval = TimeSpan.FromSeconds(1);

        var shuttles = new[]
        {
            new Shuttle("SH-01", rack, "A01-L01", timing),
            new Shuttle("SH-02", rack, "A02-L02", timing),
        };

        foreach (var shuttle in shuttles)
        {
            occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        }


        board.Add(new FleetTask("task-1", "A02-L01", start));

        Assert.True(gate.CanMove("SH-01", "A02-L01"));

        var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = new RunResult(0, false);
        for (var tick = 0; tick < 50 && !result.AllDone; tick++)
        {
            result = await loop.TickAsync(cts.Token);
        }
        Assert.True(result.AllDone, $"run loop stalled after {result.Ticks} ticks");
        Assert.Equal(TaskState.Done, board.Get("task-1").State);
        Assert.Equal("A02-L01", shuttles[0].Position);        // SH-01 finished it
        Assert.Equal(ShuttleState.Idle, shuttles[0].State);
        Assert.Equal("A02-L02", shuttles[1].Position);        // SH-02 stayed
    }

    [Fact]
    public async Task A_faulted_shuttle_releases_the_route_it_never_drove()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1);
        rack.MarkBlocked("A01-L02");
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = NewDispatcher(board, repository);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tickInterval = TimeSpan.FromSeconds(1);

        var shuttles = new[]
        {
            new Shuttle("SH-01", rack, "A01-L01", timing),
            new Shuttle("SH-02", rack, "A02-L02", timing),
        };

        foreach (var shuttle in shuttles)
        {
            occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        }


        board.Add(new FleetTask("task-1", "A02-L01", start));

        Assert.True(gate.CanMove("SH-01", "A02-L01"));

        var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = new RunResult(0, false);
        for (var tick = 0; tick < 50 && shuttles[0].State != ShuttleState.Fault; tick++)
        {
            result = await loop.TickAsync(cts.Token);
        }
        Assert.Equal(ShuttleState.Idle, shuttles[0].State);
        Assert.Empty(reservations.SegmentsOwnedBy("SH-01"));
    }

    [Fact]
    public async Task A_faulted_shuttle_keeps_its_lane_occupied()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1);
        rack.MarkBlocked("A01-L02");
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = NewDispatcher(board, repository);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tickInterval = TimeSpan.FromSeconds(1);

        var shuttles = new[]
        {
            new Shuttle("SH-01", rack, "A01-L01", timing),
            new Shuttle("SH-02", rack, "A02-L02", timing),
        };

        foreach (var shuttle in shuttles)
        {
            occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        }


        board.Add(new FleetTask("task-1", "A02-L01", start));

        Assert.True(gate.CanMove("SH-01", "A02-L01"));

        var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = new RunResult(0, false);
        for (var tick = 0; tick < 50 && shuttles[0].State != ShuttleState.Fault; tick++)
        {
            result = await loop.TickAsync(cts.Token);
        }
        Assert.Equal(ShuttleState.Idle, shuttles[0].State);
        Assert.Equal("A02-L01", shuttles[0].Position);
        Assert.Equal("A02-L01", occupancy.GetOccupiedLane("SH-01"));
    }

    [Fact]
    public async Task The_task_is_not_left_in_progress_when_the_shuttle_faults()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1);
        rack.MarkBlocked("A01-L02");
        var track = new Track(rack);
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = NewDispatcher(board, repository);
        var gate = new RunLoopGate(occupancy, track, traffic);
        var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tickInterval = TimeSpan.FromSeconds(1);

        var shuttles = new[]
        {
            new Shuttle("SH-01", rack, "A01-L01", timing),
        };

        foreach (var shuttle in shuttles)
        {
            occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        }


        board.Add(new FleetTask("task-1", "A02-L01", start));

        Assert.True(gate.CanMove("SH-01", "A02-L01"));

        var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await loop.TickAsync(cts.Token);
        
        Assert.Equal(ShuttleState.Idle, shuttles[0].State);
        Assert.Equal(TaskState.Done, board.Get("task-1").State);
    }
}