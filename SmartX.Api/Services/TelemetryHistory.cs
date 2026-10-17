//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Sort key for the per-device timeline: timestamp first, then an ingestion sequence number so two readings
/// with the same timestamp never collide (SortedDictionary throws on duplicate keys).
/// </summary>
public readonly record struct HistoryKey(DateTime Timestamp, long Sequence) : IComparable<HistoryKey>
{
    public int CompareTo(HistoryKey other)
    {
        int byTime = Timestamp.CompareTo(other.Timestamp);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }
}

public readonly record struct HistoryPoint(
    string DeviceId, SensorCategory Category, string Zone, double Value, DateTime Timestamp);

/// <summary>
/// Historical sensor logs. One SortedDictionary per device keeps readings ordered by timestamp, so timeline
/// range queries come back already sorted and the chart can render them directly.
/// </summary>
public sealed class TelemetryHistory
{
    private readonly Dictionary<string, SortedDictionary<HistoryKey, HistoryPoint>> _byDevice =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly int _maxPerDevice;
    private long _sequence;

    public TelemetryHistory(Microsoft.Extensions.Options.IOptions<GatewayOptions> options)
    {
        _maxPerDevice = Math.Max(20, options.Value.MaxHistoryPerDevice);
    }

    public void Add(HistoryPoint point)
    {
        var key = new HistoryKey(point.Timestamp, Interlocked.Increment(ref _sequence));
        lock (_gate)
        {
            if (!_byDevice.TryGetValue(point.DeviceId, out var timeline))
            {
                timeline = new SortedDictionary<HistoryKey, HistoryPoint>();
                _byDevice[point.DeviceId] = timeline;
            }

            timeline.Add(key, point);

            //Retention: drop the oldest reading once the cap is reached
            while (timeline.Count > _maxPerDevice)
            {
                timeline.Remove(timeline.First().Key);
            }
        }
    }

    /// <summary>Ascending (oldest to newest) readings in [from, to], newest 'max' of them.</summary>
    public List<HistoryPoint> Range(string deviceId, DateTime? from, DateTime? to, int max)
    {
        lock (_gate)
        {
            if (!_byDevice.TryGetValue(deviceId.Trim(), out var timeline)) return new List<HistoryPoint>();

            var inRange = timeline.Values.Where(p =>
                (from is null || p.Timestamp >= from) && (to is null || p.Timestamp <= to));

            var all = inRange.ToList();
            return all.Count <= max ? all : all.GetRange(all.Count - max, max);
        }
    }

    public List<HistoryPoint> Tail(string deviceId, int count) => Range(deviceId, null, null, count);

    /// <summary>Builds the Part 1 dashboard payload: the newest readings per category.</summary>
    public TelemetryDashboardResponse Dashboard(int perCategory)
    {
        List<HistoryPoint> everything;
        lock (_gate)
        {
            everything = _byDevice.Values.SelectMany(t => t.Values).ToList();
        }

        List<HistoryPoint> Newest(SensorCategory c) => everything
            .Where(p => p.Category == c)
            .OrderByDescending(p => p.Timestamp)
            .Take(perCategory)
            .OrderBy(p => p.Timestamp)
            .ToList();

        return new TelemetryDashboardResponse
        {
            Environmental = Newest(SensorCategory.Environmental)
                .Select(p => new TelemetryPacket<float>(p.DeviceId, p.Zone, p.Category, (float)p.Value, p.Timestamp)).ToList(),
            Power = Newest(SensorCategory.PowerConsumption)
                .Select(p => new TelemetryPacket<int>(p.DeviceId, p.Zone, p.Category, (int)p.Value, p.Timestamp)).ToList(),
            Actuator = Newest(SensorCategory.Actuator)
                .Select(p => new TelemetryPacket<bool>(p.DeviceId, p.Zone, p.Category, p.Value >= 0.5, p.Timestamp)).ToList()
        };
    }
}
