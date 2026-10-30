using System.Collections.Concurrent;

namespace FleetBackend.Services
{
    public interface ITrafficManager
    {
        void Clear();
        bool RequestAccess(string robotId, string resourceId);
        string ReleaseAccess(string robotId, string resourceId);
        void CancelRequest(string robotId, string resourceId);
        event Action<string, string>? AccessGranted;
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
        private readonly object _gate = new();
        public event Action<string, string>? AccessGranted;

        public void Clear()
        {
            lock (_gate) _resources.Clear();
        }

        public bool RequestAccess(string robotId, string resourceId)
        {
            if (string.IsNullOrWhiteSpace(robotId) ||
                string.IsNullOrWhiteSpace(resourceId))
            {
                return false;
            }

            lock (_gate)
            {
                var resource = _resources.GetOrAdd(resourceId, _ => new ResourceState(resourceId, robotId));
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
            string nextRobot;
            lock (_gate)
            {
                if (!_resources.TryGetValue(resourceId, out var resource))
                    return string.Empty;
                if (resource.OwnerRobotId != robotId)
                    return string.Empty;

                if (resource.WaitingRobots.Count == 0)
                {
                    _resources.TryRemove(resourceId, out _);
                    return string.Empty;
                }

                nextRobot = resource.WaitingRobots.Dequeue();

                resource.OwnerRobotId = nextRobot;
                resource.AcquiredTime = DateTime.UtcNow;

            }
            AccessGranted?.Invoke(nextRobot, resourceId);
            return nextRobot;
        }

        public void CancelRequest(string robotId, string resourceId)
        {
            lock (_gate)
            {
                if (!_resources.TryGetValue(resourceId, out var resource)) return;
                var remaining = resource.WaitingRobots.Where(id => id != robotId).ToArray();
                resource.WaitingRobots.Clear();
                foreach (var id in remaining) resource.WaitingRobots.Enqueue(id);
            }
        }
    }
}

