using FleetBackend.Models;
using FleetBackend.Services;

public class CostCaculator
{
    private readonly IMapManager _mapManager;

    public CostCaculator(IMapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public void GetCost(RobotStateDto robot)
    {
        
    }
}