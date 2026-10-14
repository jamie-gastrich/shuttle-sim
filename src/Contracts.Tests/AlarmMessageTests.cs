using System.Text.Json;

namespace Contracts.Tests;

public sealed class AlarmMessageTests
{
    
    [Fact]
    public void AlarmMessage_survives_a_json_roundtrip()
    {
        
        var sent = new AlarmMessage(
            new MessageEnvelope(ContractVersion.Current, MessageTypes.Alarm, "alarm-message-id", null, "fleet-manager", DateTime.UtcNow),
            new Alarm("SH-01", AlarmSeverities.Warning, AlarmCodes.BlockedPath, null));
        var json = JsonSerializer.Serialize(sent);
        var received = JsonSerializer.Deserialize<AlarmMessage>(json);
        Assert.Equal(sent, received);
    }
    
    [Fact]
    public void Alarm_json_keeps_the_agreed_field_names()
    {
        var json = JsonSerializer.Serialize(new AlarmMessage(
            new MessageEnvelope(ContractVersion.Current, MessageTypes.Alarm, "alarm-message-id", null, "fleet-manager", DateTime.UtcNow),
            new Alarm("SH-01", AlarmSeverities.Warning, AlarmCodes.BlockedPath, null)));
        Assert.Contains("\"Envelope\"", json);
        Assert.Contains("\"ShuttleId\"", json);
        Assert.Contains("\"Severity\"", json);
        Assert.Contains("\"Code\"", json);
        Assert.Contains("\"Detail\"", json);
    }
    
    [Fact]
    public void Alarm_wire_names_match_the_contract()
    {
        Assert.Equal("alarm", MessageTypes.Alarm);
        Assert.Equal("info", AlarmSeverities.Info);
        Assert.Equal("warning", AlarmSeverities.Warning);
        Assert.Equal("critical", AlarmSeverities.Critical);
        Assert.Equal("lostCommunication", AlarmCodes.LostCommunication);
        Assert.Equal("blockedPath", AlarmCodes.BlockedPath);
        Assert.Equal("palletNotDetected", AlarmCodes.PalletNotDetected);
    }
}