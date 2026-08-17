namespace FleetBackend.Models
{
    public class DeliveryTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string PickupLocation { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? AssignedRobotId { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.MinValue;

        public void AssignedTo(RobotStateDto robotState)
        {
            AssignedRobotId = robotState.RobotId;
            robotState.CurrentTaskId = TaskId;
            Status = TaskStatus.Assigned;
        }

        public void ResetTask()
        {
            AssignedRobotId = null;
            Status = TaskStatus.Pending;
        }

        public void Cancel()
        {
            Status = TaskStatus.Cancelled;
        }
    }
}
