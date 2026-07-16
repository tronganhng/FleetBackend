using System;

namespace FleetBackend.Models
{
    public class DeliveryTask
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString();
        public string PickupLocation { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? AssignedRobotId { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public void AssignedTo(RobotStateDto robotState)
        {
            AssignedRobotId = robotState.RobotId;
            robotState.CurrentTaskId = TaskId;
            Status = TaskStatus.Assigned;
        }
    }
}
