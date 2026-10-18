namespace Contracts;

public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
    public const string Alarm = "alarm";
    public const string RecoveryCommand = "recoveryCommand";
    public const string RecoveryAck = "recoveryAck";
}

public static class ContractVersion
{
    public const int Current = 4;
}