namespace Contracts;

public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
    public const string Alarm = "alarm";
}

public static class ContractVersion
{
    public const int Current = 3;
}