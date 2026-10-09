namespace ShuttleCore.Tests;

public class WireNamesTests
{
    [Fact]
    public void State_wire_names_match_the_contract()
    {
        var idle = ShuttleStateWire.ToWireName(ShuttleState.Idle);
        var moving = ShuttleStateWire.ToWireName(ShuttleState.Moving);
        var loading = ShuttleStateWire.ToWireName(ShuttleState.Loading);
        var unloading = ShuttleStateWire.ToWireName(ShuttleState.Unloading);
        var fault = ShuttleStateWire.ToWireName(ShuttleState.Fault);
        
        Assert.Equal("idle", idle);
        Assert.Equal("moving", moving);
        Assert.Equal("loading", loading);
        Assert.Equal("unloading", unloading);
        Assert.Equal("fault", fault);
    }
}