using System.Text.Json;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services.Elevator;

public sealed record ElevatorRecoveryEntry(string ElevatorId, string RobotId,
    DateTimeOffset StartedAtUtc, bool IsActive, string Reason);

// Journal before commanding hardware. Incomplete trips survive session and process resets.
public sealed class ElevatorRecoveryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ElevatorRecoveryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    public ElevatorRecoveryStore(IOptions<ElevatorMqttOptions> options)
    {
        _path = Path.GetFullPath(options.Value.RecoveryFilePath);
        if (!File.Exists(_path)) return;
        // Corrupt/unreadable journals prevent startup rather than silently admitting a new trip.
        var entries = JsonSerializer.Deserialize<ElevatorRecoveryEntry[]>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Invalid elevator recovery journal.");
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ElevatorId) || string.IsNullOrWhiteSpace(entry.RobotId))
                throw new InvalidDataException("Invalid elevator recovery entry.");
            _entries.Add(entry.ElevatorId, entry with { IsActive = false, Reason = "Backend restarted; physical outcome requires reconciliation." });
        }
    }

    public IReadOnlyList<ElevatorRecoveryEntry> GetAll() { lock (_gate) return _entries.Values.ToArray(); }
    public bool IsBlocked(string elevatorId) { lock (_gate) return _entries.ContainsKey(elevatorId); }
    public bool RequiresRecovery(string elevatorId)
    {
        lock (_gate) return _entries.TryGetValue(elevatorId, out var entry) && !entry.IsActive;
    }

    public void Begin(string elevatorId, string robotId)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(elevatorId)) throw new InvalidOperationException("Elevator is active or requires recovery.");
            _entries.Add(elevatorId, new(elevatorId, robotId, DateTimeOffset.UtcNow, true, "Physical trip in progress."));
            Save(); // If persistence fails, retain the in-memory block and send no hardware command.
        }
    }

    public void RequireRecovery(string elevatorId, string robotId, string reason)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(elevatorId, out var entry) && entry.RobotId == robotId)
            {
                _entries[elevatorId] = entry with { IsActive = false, Reason = reason };
                Save();
            }
        }
    }

    public void Complete(string elevatorId, string robotId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(elevatorId, out var entry) || entry.RobotId != robotId)
                throw new InvalidOperationException("Elevator lease does not belong to this robot.");
            _entries.Remove(elevatorId);
            try { Save(); }
            catch { _entries[elevatorId] = entry; throw; }
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _entries.Values.ToArray());
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
    }
}
