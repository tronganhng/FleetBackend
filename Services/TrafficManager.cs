using System.Collections.Concurrent;

namespace FleetBackend.Services
{
    public interface ITrafficManager
    {
        void Clear();
        bool RequestAccess(string robotId, string resourceId);
        string ReleaseAccess(string robotId, string resourceId);
    }


    public class TrafficManager : ITrafficManager
    {
        private class ResourceState
        {
            public string ResourceId { get; }

            public string OwnerRobotId { get; set; }

            public Queue<string> WaitingRobots { get; } = new();

            public DateTime AcquiredTime { get; set; }

            public ResourceState(string resourceId, string ownerRobotId)
            {
                ResourceId = resourceId;
                OwnerRobotId = ownerRobotId;
                AcquiredTime = DateTime.UtcNow;
            }
        }

        private readonly ConcurrentDictionary<string, ResourceState> _resources = new();

        public void Clear()
        {
            _resources.Clear();
        }

        public bool RequestAccess(string robotId, string resourceId)
        {
            if (string.IsNullOrWhiteSpace(robotId) ||
                string.IsNullOrWhiteSpace(resourceId))
            {
                return false;
            }

            var resource = _resources.GetOrAdd(resourceId, _ => new ResourceState(resourceId, robotId));

            lock (resource)
            {
                // Nếu vừa tạo thì robot này đã là owner
                if (resource.OwnerRobotId == robotId)
                    return true;

                if (!resource.WaitingRobots.Contains(robotId))
                {
                    resource.WaitingRobots.Enqueue(robotId);
                }

                return false;
            }
        }

        public string ReleaseAccess(string robotId, string resourceId)
        {
            if (!_resources.TryGetValue(resourceId, out var resource))
                return string.Empty;

            lock (resource)
            {
                if (resource.OwnerRobotId != robotId)
                    return string.Empty;

                if (resource.WaitingRobots.Count == 0)
                {
                    _resources.TryRemove(resourceId, out _);
                    return string.Empty;
                }

                var nextRobot = resource.WaitingRobots.Dequeue();

                resource.OwnerRobotId = nextRobot;
                resource.AcquiredTime = DateTime.UtcNow;

                return nextRobot;
            }
        }
    }
}

