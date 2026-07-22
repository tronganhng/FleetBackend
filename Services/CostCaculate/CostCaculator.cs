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
        var robotPoint = _mapManager.GetRobotPoint(robot);

        if (robotPoint == null) return float.MaxValue;

        float distance = _pathFinder.GetShortestDistance(robotPoint.PointName, task.PickupLocation);

        Logger.Log($"Cost {robot.RobotId} form {robotPoint.PointName} to {task.PickupLocation}: " + distance);

        return distance;
    }
}