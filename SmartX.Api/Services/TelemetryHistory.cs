//ST10440432
//Matteo Nusca
using System.Diagnostics;
using SmartX.Shared.Models;
using SmartX.Shared.Structures;

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

/// <summary>Result of an archive range query, including how much of the tree the search had to touch.</summary>
public sealed record ArchiveQueryResult(
    List<HistoryPoint> Points, int TotalMatches, int NodesVisited, int ArchiveSize, int TreeHeight,
    double ElapsedMicroseconds, DateTime? Oldest, DateTime? Newest);

/// <summary>
/// Historical sensor logs. One SortedDictionary per device keeps readings ordered by timestamp, so timeline
/// range queries come back already sorted and the chart can render them directly.
/// Alongside it, every device also has an AVL tree archive (longer retention) that answers "readings between
/// time A and time B" in O(log n + k) instead of scanning the whole log.
/// </summary>
public sealed class TelemetryHistory
{
    private readonly Dictionary<string, SortedDictionary<HistoryKey, HistoryPoint>> _byDevice =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Dictionary<string, AvlTree<HistoryKey, HistoryPoint>> _archive =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxPerDevice;
    private readonly int _maxArchive;
    private long _sequence;

    public TelemetryHistory(Microsoft.Extensions.Options.IOptions<GatewayOptions> options)
    {
        _maxPerDevice = Math.Max(20, options.Value.MaxHistoryPerDevice);
        _maxArchive = Math.Max(_maxPerDevice, options.Value.MaxArchivePerDevice);
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

            AddToArchive(point, key);
        }
    }

    /// <summary>Adds readings to the archive only (used to seed long-term history without touching the live timeline).</summary>
    public void BackfillArchive(IEnumerable<HistoryPoint> points)
    {
        lock (_gate)
        {
            foreach (var point in points)
            {
                AddToArchive(point, new HistoryKey(point.Timestamp, Interlocked.Increment(ref _sequence)));
            }
        }
    }

    //Caller must hold _gate
    private void AddToArchive(HistoryPoint point, HistoryKey key)
    {
        if (!_archive.TryGetValue(point.DeviceId, out var tree))
        {
            tree = new AvlTree<HistoryKey, HistoryPoint>();
            _archive[point.DeviceId] = tree;
        }

        tree.Insert(key, point);

        //Retention: drop the oldest archived reading once the cap is reached
        while (tree.Count > _maxArchive && tree.TryGetMin(out var oldest, out _))
        {
            tree.Remove(oldest);
        }
    }

    /// <summary>Number of readings archived for a device.</summary>
    public int ArchiveCount(string deviceId)
    {
        lock (_gate)
        {
            return _archive.TryGetValue(deviceId.Trim(), out var tree) ? tree.Count : 0;
        }
    }

    /// <summary>
    /// Readings in [from, to] from the AVL archive, oldest first. If more than 'max' match, the newest 'max' are
    /// returned and the totals still describe the full match.
    /// </summary>
    public ArchiveQueryResult QueryArchive(string deviceId, DateTime? from, DateTime? to, int max)
    {
        DateTime low = from ?? DateTime.MinValue;
        DateTime high = to ?? DateTime.MaxValue;
        max = Math.Max(1, max);

        lock (_gate)
        {
            if (!_archive.TryGetValue(deviceId.Trim(), out var tree) || tree.IsEmpty)
            {
                return new ArchiveQueryResult(new List<HistoryPoint>(), 0, 0, 0, 0, 0, null, null);
            }

            tree.TryGetMin(out var oldest, out _);
            tree.TryGetMax(out var newest, out _);

            long started = Stopwatch.GetTimestamp();
            var hits = tree.Range(new HistoryKey(low, long.MinValue), new HistoryKey(high, long.MaxValue), out int visited);
            double micros = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000.0;

            var points = hits.Count <= max
                ? hits.Select(h => h.Value).ToList()
                : hits.Skip(hits.Count - max).Select(h => h.Value).ToList();

            return new ArchiveQueryResult(points, hits.Count, visited, tree.Count, tree.Height, micros,
                oldest.Timestamp, newest.Timestamp);
        }
    }

    /// <summary>Times the same range query on the AVL archive and on a plain scan of every archived reading.</summary>
    public ArchiveBenchmarkResponse Benchmark(string deviceId, DateTime? from, DateTime? to, int repetitions)
    {
        repetitions = Math.Clamp(repetitions, 1, 2000);
        DateTime low = from ?? DateTime.MinValue;
        DateTime high = to ?? DateTime.MaxValue;
        var lowKey = new HistoryKey(low, long.MinValue);
        var highKey = new HistoryKey(high, long.MaxValue);

        lock (_gate)
        {
            if (!_archive.TryGetValue(deviceId.Trim(), out var tree) || tree.IsEmpty)
            {
                return new ArchiveBenchmarkResponse { DeviceId = deviceId, Repetitions = repetitions, ResultsMatch = true };
            }

            //Snapshot in key order for the scan, so the scan runs over a flat list like a plain log would.
            var flat = tree.InOrder().Select(kv => kv.Value).ToList();

            int treeMatches = 0, visited = 0;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < repetitions; i++)
            {
                treeMatches = tree.Range(lowKey, highKey, out visited).Count;
            }
            double treeMicros = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000.0 / repetitions;

            int linearMatches = 0;
            started = Stopwatch.GetTimestamp();
            for (int i = 0; i < repetitions; i++)
            {
                int count = 0;
                foreach (var p in flat)
                {
                    if (p.Timestamp >= low && p.Timestamp <= high)
                    {
                        count++;
                    }
                }

                linearMatches = count;
            }
            double linearMicros = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000.0 / repetitions;

            return new ArchiveBenchmarkResponse
            {
                DeviceId = deviceId,
                ArchiveSize = tree.Count,
                TreeHeight = tree.Height,
                Matches = treeMatches,
                NodesVisited = visited,
                Repetitions = repetitions,
                TreeMicroseconds = treeMicros,
                LinearMicroseconds = linearMicros,
                SpeedUp = treeMicros > 0 ? linearMicros / treeMicros : 0,
                ResultsMatch = treeMatches == linearMatches
            };
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
