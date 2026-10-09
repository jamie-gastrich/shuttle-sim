namespace ShuttleCore;

public enum ShuttleState
{
    Idle,
    Moving,
    Loading,
    Unloading,
    Fault,
}

public static class ShuttleStateWire
{
    public static string ToWireName(this ShuttleState state) => state switch
    {
        ShuttleState.Idle => "idle",
        ShuttleState.Moving => "moving",
        ShuttleState.Loading => "loading",
        ShuttleState.Unloading => "unloading",
        ShuttleState.Fault => "fault",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown shuttle state"),
    };
}