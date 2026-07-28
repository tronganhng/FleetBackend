using System.Collections.Concurrent;

namespace FleetBackend.Services
{
    public interface ITrafficManager
    {
        void Clear();
        bool RequestAccess(string robotId, string resourceId);
        bool ReleaseAccess(string robotId, string resourceId);
        bool IsResourceLocked(string resourceId);
        string? GetResourceOwner(string resourceId);
        void ForceReleaseRobot(string robotId);
    }

    public record ResourceLock(string ResourceId, string RobotId, DateTime LockedAt);

    public class TrafficManager : ITrafficManager
    {
        private readonly ConcurrentDictionary<string, ResourceLock> _locks = new();

        public void Clear()
        {
            _locks.Clear();
        }

        public bool RequestAccess(string robotId, string resourceId)
        {
            if (string.IsNullOrWhiteSpace(robotId) || string.IsNullOrWhiteSpace(resourceId))
            {
                return false;
            }

            var newLock = new ResourceLock(resourceId, robotId, DateTime.UtcNow);

            if (_locks.TryAdd(resourceId, newLock))
            {
                return true;
            }

            if (_locks.TryGetValue(resourceId, out var existingLock) && existingLock.RobotId == robotId)
            {
                return true;
            }

            return false;
        }

        public bool ReleaseAccess(string robotId, string resourceId)
        {
            if (string.IsNullOrWhiteSpace(robotId) || string.IsNullOrWhiteSpace(resourceId))
            {
                return false;
            }

            if (_locks.TryGetValue(resourceId, out var existingLock))
            {
                if (existingLock.RobotId == robotId)
                {
                    return _locks.TryRemove(resourceId, out _);
                }
            }

            return false;
        }

        public bool IsResourceLocked(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                return false;

            return _locks.ContainsKey(resourceId);
        }

        public string? GetResourceOwner(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                return null;

            return _locks.TryGetValue(resourceId, out var existingLock) ? existingLock.RobotId : null;
        }

        public void ForceReleaseRobot(string robotId)
        {
            if (string.IsNullOrWhiteSpace(robotId))
                return;

            var keysToRemove = _locks.Where(kvp => kvp.Value.RobotId == robotId)
                                     .Select(kvp => kvp.Key)
                                     .ToList();

            foreach (var key in keysToRemove)
            {
                _locks.TryRemove(key, out _);
            }
        }
    }
}

