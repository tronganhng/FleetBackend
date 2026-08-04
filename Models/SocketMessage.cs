using System.Text.Json;
using FleetBackend.Models;

public class SocketMessage
{
    public SocketMessageType Type { get; set; }
    public string? RequestId { get; set; }
    public string? RobotId { get; set; }
    public JsonElement Payload { get; set; }
}