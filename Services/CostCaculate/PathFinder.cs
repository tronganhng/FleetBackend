using System.Numerics;
using FleetBackend.Services;

public interface IPathFinder
{
    float GetShortestDistance(string startPoint, string endPoint);
}


public class AStarPathFinder : IPathFinder
{
    private readonly IMapManager _mapManager;

    public AStarPathFinder(IMapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public float GetShortestDistance(string startPoint, string endPoint)
    {
        if (startPoint == endPoint)
            return 0f;

        var open = new PriorityQueue<string, float>();
        var gScore = new Dictionary<string, float>();

        open.Enqueue(startPoint, 0);
        gScore[startPoint] = 0;

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current == endPoint)
                return gScore[current];

            foreach (var lane in _mapManager.GetConnectedLanes(current))
            {
                var neighbor = lane.StartPoint == current
                    ? lane.EndPoint
                    : lane.StartPoint;

                float newCost = gScore[current] + lane.Distance;

                if (!gScore.TryGetValue(neighbor, out float oldCost) || newCost < oldCost)
                {
                    gScore[neighbor] = newCost;

                    float heuristic = EstimateDistance(neighbor, endPoint);

                    open.Enqueue(neighbor, newCost + heuristic);
                }
            }
        }

        return float.PositiveInfinity;
    }

    private float EstimateDistance(string from, string to)
    {
        var p1 = _mapManager.GetPoint(from);
        var p2 = _mapManager.GetPoint(to);

        return Vector2.Distance(new Vector2(p1.Position[0], p1.Position[1]), new Vector2(p2.Position[0], p2.Position[1]));
    }
}