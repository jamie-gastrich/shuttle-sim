using Contracts;

namespace FleetManager.Tests;

public class ShuttleRegistryTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_picture_keeps_the_last_state_and_position_of_each_shuttle()
    {
        var registry = new ShuttleRegistry();

        registry.Record(new ShuttleStatus("SH-01", "moving", "A01-L03", Start, null));
        registry.Record(new ShuttleStatus("SH-01", "idle", "A01-L07", Start, null));

        Assert.True(registry.IsIdle("SH-01"));
        Assert.Equal("A01-L07", registry.PositionOf("SH-01"));
        Assert.Contains("SH-01", registry.FreeShuttles());
        Assert.False(registry.IsIdle("SH-99"));
    }
}
