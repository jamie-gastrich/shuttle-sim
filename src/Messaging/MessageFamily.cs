namespace Messaging;

public enum MessageFamily 
{ 
    Command, 
    Ack, 
    Status,
    Alarm,
    Recovery,
    Json
}