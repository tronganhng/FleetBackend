namespace FleetBackend.Models
{
    public class ElevatorDto
    {
        public string ElevatorId { get; set; } = string.Empty;
        public int CurrentFloor { get; set; } = 1;
        public ElevatorStatus Status { get; set; }
    }
}