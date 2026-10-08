using System.Text.Json;
using Contracts;

namespace Contracts.Tests;

public class ShuttleStatusTests
{
    [Fact]
    public void ShuttleStatus_survives_a_json_roundtrip()
    {
        var sent = new ShuttleStatus("SH-01", "idle", "A02-L04",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), null);
        var json = JsonSerializer.Serialize(sent);
        var received = JsonSerializer.Deserialize<ShuttleStatus>(json);
        Assert.Equal(sent, received);
    }

    [Fact]
    public void ShuttleStatus_json_keeps_the_agreed_field_names()
    {
        var json = JsonSerializer.Serialize(new ShuttleStatus("SH-01", "fault", "A02-L04",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), "Pallet Not Detected"));
        Assert.Contains("\"ShuttleId\"", json);
        Assert.Contains("\"State\"", json);
        Assert.Contains("\"Position\"", json);
        Assert.Contains("\"Timestamp\"", json);
        Assert.Contains("\"FaultReason\"", json);
    }
}