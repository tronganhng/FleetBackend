using System;
using System.Text.Json.Serialization;

namespace FleetBackend.Models
{
    public class RobotStateDto
    {
        public string RobotId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public double Battery { get; set; }
        public RobotStatus Status { get; set; }
        public string? CurrentTaskId { get; set; }
        public DateTime LastHeartbeat { get; set; }

        [JsonIgnore]
        public MapPointDto? _currentDock;
        [JsonIgnore]
        public MapNodeDto? _currentNode;

        public void ClearCurrentTask()
        {
            CurrentTaskId = null;
        }

        public void CopyFrom(RobotStateDto other)
        {
            if (other == null) return;

            RobotId = other.RobotId;
            X = other.X;
            Y = other.Y;
            Rotation = other.Rotation;
            Battery = other.Battery;
            Status = other.Status;
            CurrentTaskId = other.CurrentTaskId;
            LastHeartbeat = other.LastHeartbeat;
        }
    }
}
