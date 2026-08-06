using FleetBackend.Models;

public class Robot
{
    public RobotStateDto State { get; private set; }

    public MapPointDto? CurrentDock { get; set; }
    public MapNodeDto? CurrentNode { get; set; }

    public Robot(RobotStateDto state)
    {
        State = state;
    }

    public void ClearCurrentTask()
    {
        State.CurrentTaskId = null;
    }
    
    public void Offline()
    {
        State.Status = RobotStatus.Offline;
    }
}