using System.Numerics;
using FleetBackend.Services;

public interface IPathFinder
{
    float GetShortestDistance(string startNode, string endNode);
}


public class AStarPathFinder : IPathFinder
{
    private readonly IMapManager _mapManager;

    public AStarPathFinder(IMapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public float GetShortestDistance(string startNode, string endNode)
    {
        if (startNode == endNode)
            return 0f;

        var open = new PriorityQueue<string, float>();
        var gScore = new Dictionary<string, float>();

        open.Enqueue(startNode, 0);
        gScore[startNode] = 0;

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current == endNode)
                return gScore[current];

            foreach (var lane in _mapManager.GetConnectedLanes(current))
            {
                var neighbor = lane.StartNode == current
                    ? lane.EndNode
                    : lane.StartNode;

                float newCost = gScore[current] + lane.Distance;

                if (!gScore.TryGetValue(neighbor, out float oldCost) || newCost < oldCost)
                {
                    gScore[neighbor] = newCost;

                    float heuristic = EstimateDistance(neighbor, endNode);

                    open.Enqueue(neighbor, newCost + heuristic);
                }
            }
        }

        return float.PositiveInfinity;
    }

    private float EstimateDistance(string from, string to)
    {
        var n1 = _mapManager.GetNode(from);
        var n2 = _mapManager.GetNode(to);

        return Vector2.Distance(new Vector2(n1.Position[0], n1.Position[1]), new Vector2(n2.Position[0], n2.Position[1]));
    }
}