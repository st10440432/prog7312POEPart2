//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Tracks currently-active error states and disconnected nodes. The HashSet stores one unique key per
/// (device, alert type): HashSet.Add returns false for a key that is already present, so a duplicate
/// disconnect / out-of-range alert for the same sensor is detected in O(1) and never processed twice.
/// </summary>
public sealed class AlertTracker
{
    public const string OutOfRange = "OutOfRange";
    public const string PowerSpike = "PowerSpike";
    public const string Disconnected = "Disconnected";

    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlertInfo> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private long _duplicatesSuppressed;

    public long DuplicatesSuppressed => Interlocked.Read(ref _duplicatesSuppressed);

    public int ActiveCount
    {
        get { lock (_gate) return _active.Count; }
    }

    public static string KeyFor(string deviceId, string type) => $"{deviceId.Trim()}:{type}";

    /// <summary>Returns true if the alert is new; false if it was already active (duplicate suppressed).</summary>
    public bool TryRaise(string deviceId, string deviceName, string type, string message)
    {
        string key = KeyFor(deviceId, type);
        lock (_gate)
        {
            if (!_active.Add(key))
            {
                Interlocked.Increment(ref _duplicatesSuppressed);
                return false;
            }

            _details[key] = new AlertInfo
            {
                Key = key,
                DeviceId = deviceId,
                DeviceName = deviceName,
                Type = type,
                Message = message,
                RaisedUtc = DateTime.UtcNow
            };
            return true;
        }
    }

    public bool Clear(string deviceId, string type)
    {
        string key = KeyFor(deviceId, type);
        lock (_gate)
        {
            if (!_active.Remove(key)) return false;
            _details.Remove(key);
            return true;
        }
    }

    public bool IsActive(string deviceId, string type)
    {
        lock (_gate) return _active.Contains(KeyFor(deviceId, type));
    }

    public List<AlertInfo> Snapshot()
    {
        lock (_gate)
        {
            return _details.Values.OrderByDescending(a => a.RaisedUtc).ToList();
        }
    }
}
