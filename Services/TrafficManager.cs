using System.Collections.Concurrent;
using FleetBackend.Services.Elevator;

namespace FleetBackend.Services
{
    public interface ITrafficManager
    {
        void Clear();
        bool RequestAccess(string robotId, string resourceId);
        string ReleaseAccess(string robotId, string resourceId);
        void CancelRequest(string robotId, string resourceId);
        void ProtectAccess(string robotId, string resourceId, bool preserveAcrossReset = false);
        void UnprotectAccess(string robotId, string resourceId);
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
            public bool Protected { get; set; }
            public bool PreserveAcrossReset { get; set; }

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

        public TrafficManager(ElevatorRecoveryStore? recovery = null)
        {
            // Reconstruct protected ownership before generic resource requests are accepted.
            foreach (var entry in recovery?.GetAll() ?? Array.Empty<ElevatorRecoveryEntry>())
            {
                var resourceId = $"elevator:{entry.ElevatorId}";
                _resources[resourceId] = new ResourceState(resourceId, entry.RobotId)
                {
                    Protected = true, PreserveAcrossReset = true
                };
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                foreach (var pair in _resources.ToArray())
                {
                    pair.Value.WaitingRobots.Clear();
                    if (!pair.Value.Protected || !pair.Value.PreserveAcrossReset) _resources.TryRemove(pair.Key, out _);
                }
            }
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
                    return !resource.Protected;

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
                if (resource.Protected) return string.Empty;

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
            var releaseOwner = false;
            lock (_gate)
            {
                if (!_resources.TryGetValue(resourceId, out var resource)) return;
                var remaining = resource.WaitingRobots.Where(id => id != robotId).ToArray();
                resource.WaitingRobots.Clear();
                foreach (var id in remaining) resource.WaitingRobots.Enqueue(id);
                releaseOwner = resource.OwnerRobotId == robotId && !resource.Protected;
            }
            if (releaseOwner) ReleaseAccess(robotId, resourceId);
        }

        public void ProtectAccess(string robotId, string resourceId, bool preserveAcrossReset = false)
        {
            lock (_gate)
            {
                if (!_resources.TryGetValue(resourceId, out var resource) || resource.OwnerRobotId != robotId)
                    throw new InvalidOperationException("Robot does not own the resource.");
                resource.Protected = true;
                resource.PreserveAcrossReset = preserveAcrossReset;
            }
        }

        public void UnprotectAccess(string robotId, string resourceId)
        {
            lock (_gate)
                if (_resources.TryGetValue(resourceId, out var resource) && resource.OwnerRobotId == robotId)
                    resource.Protected = false;
        }
    }
}

