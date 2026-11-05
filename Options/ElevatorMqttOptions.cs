using System.ComponentModel.DataAnnotations;

namespace FleetBackend.Options;

public sealed class ElevatorMqttOptions
{
    public const string SectionName = "ElevatorMqtt";

    // Opt in only after the Operation traversal and robot contract are integrated.
    public bool Enabled { get; set; }

    [Required]
    public string RecoveryFilePath { get; set; } = "Data/elevator-recovery.json";

    [Range(1, 86400)]
    public int AccessTimeoutSeconds { get; set; } = 120;

    [Range(1, 86400)]
    public int RobotMovementTimeoutSeconds { get; set; } = 120;

    [Required]
    public string ElevatorId { get; set; } = "Elevator_01";

    [Range(1, 3600)]
    public int ReconnectDelaySeconds { get; set; } = 5;

    [Range(1, 300)]
    public int ConnectTimeoutSeconds { get; set; } = 10;

    [Required]
    public string Host { get; set; } = "10.11.57.29";

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    [Required]
    public string ClientId { get; set; } = "fleet-backend-elevator";

    [Required]
    public string RequestTopic { get; set; } = "elevator/request";

    [Required]
    public string StatusTopic { get; set; } = "elevator/status";

    [Range(1, 65535)]
    public int KeepAliveSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int StatusTimeoutSeconds { get; set; } = 15;

    [Range(1, int.MaxValue)]
    public int PickupTimeoutSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int TravelTimeoutSeconds { get; set; } = 120;

    [Range(1, int.MaxValue)]
    public int DoorTimeoutSeconds { get; set; } = 15;

    [Range(1, int.MaxValue)]
    public int RobotFloorReadyTimeoutSeconds { get; set; } = 120;
}
