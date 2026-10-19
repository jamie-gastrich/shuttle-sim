namespace FleetCore;

public static class WmsOrderKinds
{
    public const string Store = "store";
    public const string Retrieve = "retrieve";

    public static bool IsKnown(string? kind)
        => kind == Store || kind == Retrieve;
}