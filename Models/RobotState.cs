using System;

namespace FleetBackend.Models
{
    public class RobotState
    {
        public string RobotId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public double Battery { get; set; }
        public RobotStatus Status { get; set; }
        public string? CurrentTaskId { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }
}
