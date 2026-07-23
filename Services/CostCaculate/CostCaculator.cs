using FleetBackend.Models;
using FleetBackend.Services;

public interface ICostCaculator
{
    float GetCost(DeliveryTask task, RobotStateDto robot);
}

public class CostCaculator : ICostCaculator
{
    private readonly IMapManager _mapManager;
    private readonly IPathFinder _pathFinder;

    public CostCaculator(IMapManager mapManager, IPathFinder pathFinder)
    {
        _mapManager = mapManager;
        _pathFinder = pathFinder;
    }

    public float GetCost(DeliveryTask task, RobotStateDto robot)
    {
        float distance = DistanceCost(task, robot);
        float battery = BatteryCost(robot);

        float totalCost = distance + battery;

        Logger.Log($"Total Cost {robot.RobotId}: " + totalCost);

        return totalCost;
    }

    private float DistanceCost(DeliveryTask task, RobotStateDto robot)
    {
        var robotPoint = _mapManager.GetRobotPoint(robot);

        if (robotPoint == null) return float.MaxValue;

        float distance = _pathFinder.GetShortestDistance(robotPoint.PointName, task.PickupLocation);

        float cost = Math.Clamp(distance / 100f, 0, 1);

        // Logger.Log($"Distance Cost {robot.RobotId} form {robotPoint.PointName} to {task.PickupLocation}: " + cost);

        return cost;
    }

    private float BatteryCost(RobotStateDto robot)
    {
        float battery = (float)robot.Battery;
        return 1f - battery / 100f;
    }
}