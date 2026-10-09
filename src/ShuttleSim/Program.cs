using System.Text.Json;
using Contracts;
using ShuttleCore;

var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
var timing = new ShuttleTiming(
    MovePerLane: TimeSpan.FromSeconds(5),
    Load: TimeSpan.FromSeconds(2),
    Unload: TimeSpan.FromSeconds(2));
var shuttle = new Shuttle("SH-01", rack, startLane: "A01-L01", timing);

var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
rack.Place("A01-L01-S01", "PLT-001");

Report(start);

shuttle.StartLoad("A01-L01-S01", start);
Report(start);

var loaded = start.Add(timing.Load);
shuttle.CompleteLoad(loaded);
Report(loaded);

shuttle.StartMove("A02-L02", loaded);
Report(loaded);

var travel = TimeSpan.FromTicks(rack.DistanceInLanes("A01-L01", "A02-L02") * timing.MovePerLane.Ticks);
var arrived = loaded.Add(travel);
shuttle.CompleteMove(arrived);
Report(arrived);

shuttle.StartUnload("A02-L02-S02", arrived);
Report(arrived);

var placed = arrived.Add(timing.Unload);
shuttle.CompleteUnload(placed);
Report(placed);

void Report(DateTime at)
{
    var status = new ShuttleStatus(
        shuttle.ShuttleId,
        shuttle.State.ToWireName(),
        shuttle.Position,
        at,
        shuttle.FaultReason);
    Console.WriteLine(JsonSerializer.Serialize(status));
}