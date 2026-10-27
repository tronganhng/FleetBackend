using System.Collections.Concurrent;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IElevatorManager
    {
        void Clear();
        string RegisterElevator(ElevatorDto elevator);
        bool UpdateElevator(ElevatorDto elevator);
        ElevatorDto? GetElevator(string elevatorId);
        IEnumerable<ElevatorDto> GetAllElevators();
    }

    public class ElevatorManager : IElevatorManager
    {
        private readonly ConcurrentDictionary<string, ElevatorDto> _elevators = new(StringComparer.OrdinalIgnoreCase);

        public void Clear()
        {
            _elevators.Clear();
        }

        public string RegisterElevator(ElevatorDto elevator)
        {
            if (!string.IsNullOrWhiteSpace(elevator.ElevatorId))
            {
                _elevators[elevator.ElevatorId] = Copy(elevator);
                return elevator.ElevatorId;
            }

            for (var index = 1; ; index++)
            {
                var elevatorId = $"Elevator_{index:00}";
                var registeredElevator = Copy(elevator);
                registeredElevator.ElevatorId = elevatorId;

                if (_elevators.TryAdd(elevatorId, registeredElevator))
                {
                    elevator.ElevatorId = elevatorId;
                    return elevatorId;
                }
            }
        }

        public bool UpdateElevator(ElevatorDto elevator)
        {
            if (string.IsNullOrWhiteSpace(elevator.ElevatorId) || !_elevators.ContainsKey(elevator.ElevatorId))
                return false;

            _elevators[elevator.ElevatorId] = Copy(elevator);
            return true;
        }

        public ElevatorDto? GetElevator(string elevatorId)
        {
            return _elevators.TryGetValue(elevatorId, out var elevator) ? Copy(elevator) : null;
        }

        public IEnumerable<ElevatorDto> GetAllElevators()
        {
            return _elevators.Values.Select(Copy).ToArray();
        }

        private static ElevatorDto Copy(ElevatorDto elevator)
        {
            return new ElevatorDto
            {
                ElevatorId = elevator.ElevatorId,
                CurrentFloor = elevator.CurrentFloor,
                Status = elevator.Status
            };
        }
    }
}