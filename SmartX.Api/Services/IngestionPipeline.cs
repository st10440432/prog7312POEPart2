//ST10440432
//Matteo Nusca
using System.Globalization;
using Microsoft.Extensions.Options;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>One telemetry packet travelling through the gateway.</summary>
public sealed class IngestItem
{
    public long Sequence { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public SensorCategory Category { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; }
    public DateTime EnqueuedUtc { get; init; }
    public bool Critical { get; init; }
    public int Priority { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>Completed when the packet has been processed, so the HTTP request can report the outcome.</summary>
    public TaskCompletionSource<ProcessedItem> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The ingestion engine.
///   Queue&lt;T&gt;          - standard FIFO lane for normal telemetry.
///   PriorityQueue&lt;T&gt;  - critical lane (power spikes, severe out-of-range). Always drained first,
///                        so a critical alert bypasses any backlog in the FIFO lane.
/// A background worker (PipelineProcessor) pulls one item at a time, priority lane first.
/// </summary>
public sealed class IngestionPipeline
{
    private readonly Queue<IngestItem> _fifo = new();
    private readonly PriorityQueue<IngestItem, (int Priority, long Sequence)> _priority = new();
    private readonly object _queueGate = new();
    private readonly SemaphoreSlim _signal = new(0);

    private readonly DeviceRegistry _registry;
    private readonly TelemetryHistory _history;
    private readonly AlertTracker _alerts;
    private readonly InsightEngine _engine;

    private readonly Queue<ProcessedItem> _recent = new();
    private readonly object _statsGate = new();
    private long _sequence;
    private long _processedTotal, _processedPriority, _processedFifo;
    private double _latencySumPriority, _latencySumFifo;
    private const int RecentCapacity = 40;

    public IngestionPipeline(DeviceRegistry registry, TelemetryHistory history, AlertTracker alerts, InsightEngine engine)
    {
        _registry = registry;
        _history = history;
        _alerts = alerts;
        _engine = engine;
    }

    //Typed entry points (one per sensor value type)

    public IngestItem EnqueueEnvironmental(string deviceId, float value) =>
        Enqueue(deviceId, SensorCategory.Environmental, value);

    public IngestItem EnqueuePower(string deviceId, int value) =>
        Enqueue(deviceId, SensorCategory.PowerConsumption, value);

    public IngestItem EnqueueActuator(string deviceId, bool on) =>
        Enqueue(deviceId, SensorCategory.Actuator, on ? 1 : 0);

    private IngestItem Enqueue(string deviceId, SensorCategory category, double value)
    {
        var (critical, priority, reason) = Classify(deviceId, category, value);

        var item = new IngestItem
        {
            Sequence = Interlocked.Increment(ref _sequence),
            DeviceId = deviceId.Trim(),
            Category = category,
            Value = value,
            Timestamp = DateTime.UtcNow,
            EnqueuedUtc = DateTime.UtcNow,
            Critical = critical,
            Priority = priority,
            Reason = reason
        };

        lock (_queueGate)
        {
            if (critical)
                _priority.Enqueue(item, (priority, item.Sequence));
            else
                _fifo.Enqueue(item);
        }

        _signal.Release();
        return item;
    }

    /// <summary>Decides which lane a packet takes, before it is queued.</summary>
    private (bool Critical, int Priority, string Reason) Classify(string deviceId, SensorCategory category, double value)
    {
        switch (category)
        {
            case SensorCategory.Environmental:
                float reading = (float)value;
                if (AnomalyRules.IsEnvironmentalSevere(reading))
                    return (true, 0, $"Severe out-of-range reading ({reading:0.0})");
                if (AnomalyRules.IsEnvironmentalAnomaly(reading))
                    return (true, 1, $"Out-of-range reading ({reading:0.0})");
                break;

            case SensorCategory.PowerConsumption:
                var previous = _registry.Read(deviceId, e => e.LastPower);
                if (previous is not null)
                {
                    var current = new NumericTelemetryPacket<int>(deviceId, string.Empty, category, (int)value, DateTime.UtcNow);
                    if (AnomalyRules.IsPowerRise(current, previous))
                        return (true, 0, $"Power spike +{current - previous} W");
                }
                break;
        }

        return (false, 2, "Normal telemetry");
    }

    //Used by the background worker

    public Task WaitForWorkAsync(CancellationToken ct) => _signal.WaitAsync(ct);

    /// <summary>Dequeues (priority lane first) and processes one packet. Returns false if both lanes are empty.</summary>
    public bool TryProcessNext()
    {
        IngestItem? item;
        lock (_queueGate)
        {
            if (_priority.TryDequeue(out var critical, out _))
                item = critical;
            else if (_fifo.TryDequeue(out var normal))
                item = normal;
            else
                item = null;
        }

        if (item is null) return false;

        try
        {
            Process(item, countStats: true, syncDesired: false, learn: true);
        }
        catch (Exception ex)
        {
            item.Completion.TrySetException(ex);
        }

        return true;
    }

    /// <summary>Processes a packet straight away, skipping the queues (used for seeding historical data).</summary>
    public void IngestImmediately(string deviceId, SensorCategory category, double value, DateTime timestamp, bool syncDesired = false)
    {
        var item = new IngestItem
        {
            Sequence = Interlocked.Increment(ref _sequence),
            DeviceId = deviceId.Trim(),
            Category = category,
            Value = value,
            Timestamp = timestamp,
            EnqueuedUtc = DateTime.UtcNow,
            Reason = "Seeded history"
        };

        Process(item, countStats: false, syncDesired: syncDesired, learn: false);
    }

    private ProcessedItem Process(IngestItem item, bool countStats, bool syncDesired, bool learn)
    {
        string deviceName = item.DeviceId;
        string canonicalId = item.DeviceId;
        string zone = string.Empty;
        string valueText = string.Empty;
        string? alertType = null;
        bool alertCondition = false;
        string alertMessage = string.Empty;

        bool known = _registry.Update(item.DeviceId, e =>
        {
            canonicalId = e.Registration.DeviceId;
            deviceName = e.Registration.DisplayName;
            zone = e.Registration.DeploymentLocation;

            if (e.LastSeenUtc is null || item.Timestamp > e.LastSeenUtc) e.LastSeenUtc = item.Timestamp;
            e.ReadingCount++;

            switch (item.Category)
            {
                case SensorCategory.Environmental:
                {
                    var packet = new NumericTelemetryPacket<float>(canonicalId, zone, item.Category, (float)item.Value, item.Timestamp);
                    e.LastEnvironmental = packet;
                    valueText = packet.Value.ToString("0.0", CultureInfo.InvariantCulture);
                    alertType = AlertTracker.OutOfRange;
                    alertCondition = AnomalyRules.IsEnvironmentalAnomaly(packet.Value);
                    alertMessage = $"Reading {valueText} is outside the safe range {AnomalyRules.EnvironmentalLow:0}-{AnomalyRules.EnvironmentalHigh:0}";
                    break;
                }
                case SensorCategory.PowerConsumption:
                {
                    var previous = e.LastPower;
                    var packet = new NumericTelemetryPacket<int>(canonicalId, zone, item.Category, (int)item.Value, item.Timestamp);
                    e.LastPower = packet;
                    valueText = $"{packet.Value} W";
                    alertType = AlertTracker.PowerSpike;
                    alertCondition = AnomalyRules.IsPowerRise(packet, previous);
                    alertMessage = previous is null ? string.Empty : $"Power jumped +{packet - previous} W to {packet.Value} W";
                    break;
                }
                default:
                {
                    bool on = item.Value >= 0.5;
                    e.LastActuator = new TelemetryPacket<bool>(canonicalId, zone, item.Category, on, item.Timestamp);
                    valueText = on ? "ON" : "OFF";
                    if (syncDesired || e.ReadingCount == 1) e.DesiredOn = on; //first report = boot state
                    break;
                }
            }

            e.LastValueText = valueText;
        });

        var result = new ProcessedItem
        {
            Sequence = item.Sequence,
            DeviceId = canonicalId,
            DeviceName = deviceName,
            Lane = item.Critical ? "Priority" : "FIFO",
            Value = valueText,
            Reason = item.Reason,
            ProcessedUtc = DateTime.UtcNow,
            LatencyMs = Math.Round((DateTime.UtcNow - item.EnqueuedUtc).TotalMilliseconds, 1),
            Rejected = !known
        };

        if (known)
        {
            _history.Add(new HistoryPoint(canonicalId, item.Category, zone,
                item.Category == SensorCategory.Actuator ? (item.Value >= 0.5 ? 1 : 0) : item.Value, item.Timestamp));

            //A packet from a device that was flagged disconnected means it has reconnected
            _alerts.Clear(canonicalId, AlertTracker.Disconnected);

            if (alertType is not null)
            {
                if (alertCondition)
                {
                    //HashSet inside the tracker rejects an already-active alert (duplicate suppressed)
                    if (_alerts.TryRaise(canonicalId, deviceName, alertType, alertMessage) && learn)
                        _engine.RecordAlert(canonicalId);
                }
                else
                {
                    _alerts.Clear(canonicalId, alertType);
                }
            }
        }

        if (countStats && known)
        {
            lock (_statsGate)
            {
                _processedTotal++;
                if (item.Critical)
                {
                    _processedPriority++;
                    _latencySumPriority += result.LatencyMs;
                }
                else
                {
                    _processedFifo++;
                    _latencySumFifo += result.LatencyMs;
                }

                _recent.Enqueue(result);
                while (_recent.Count > RecentCapacity) _recent.Dequeue();
            }
        }

        item.Completion.TrySetResult(result);
        return result;
    }

    public PipelineStats Stats(int undoDepth)
    {
        var stats = new PipelineStats
        {
            RegisteredDevices = _registry.Count,
            DuplicatesSuppressed = _alerts.DuplicatesSuppressed,
            ActiveAlerts = _alerts.ActiveCount,
            UndoDepth = undoDepth
        };

        lock (_queueGate)
        {
            stats.FifoDepth = _fifo.Count;
            stats.PriorityDepth = _priority.Count;
        }

        lock (_statsGate)
        {
            stats.ProcessedTotal = _processedTotal;
            stats.ProcessedPriority = _processedPriority;
            stats.ProcessedFifo = _processedFifo;
            stats.AvgLatencyPriorityMs = _processedPriority == 0 ? 0 : Math.Round(_latencySumPriority / _processedPriority, 1);
            stats.AvgLatencyFifoMs = _processedFifo == 0 ? 0 : Math.Round(_latencySumFifo / _processedFifo, 1);
            stats.Recent = _recent.Reverse().Take(15).ToList();
        }

        return stats;
    }
}

/// <summary>Background worker that drains the queues: priority lane first, then FIFO.</summary>
public sealed class PipelineProcessor : BackgroundService
{
    private readonly IngestionPipeline _pipeline;
    private readonly GatewayOptions _options;
    private readonly ILogger<PipelineProcessor> _logger;

    public PipelineProcessor(IngestionPipeline pipeline, IOptions<GatewayOptions> options, ILogger<PipelineProcessor> logger)
    {
        _pipeline = pipeline;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _pipeline.WaitForWorkAsync(stoppingToken);

                if (_pipeline.TryProcessNext() && _options.ProcessingDelayMs > 0)
                {
                    //Models the limited throughput of a gateway device, so backlogs (and priority bypass) are visible
                    await Task.Delay(_options.ProcessingDelayMs, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline processor error");
            }
        }
    }
}

/// <summary>Raises a Disconnected alert for any node that stops reporting.</summary>
public sealed class DisconnectWatchdog : BackgroundService
{
    private readonly DeviceRegistry _registry;
    private readonly AlertTracker _alerts;
    private readonly InsightEngine _engine;
    private readonly GatewayOptions _options;
    private readonly ILogger<DisconnectWatchdog> _logger;

    public DisconnectWatchdog(DeviceRegistry registry, AlertTracker alerts, InsightEngine engine,
        IOptions<GatewayOptions> options, ILogger<DisconnectWatchdog> logger)
    {
        _registry = registry;
        _alerts = alerts;
        _engine = engine;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.WatchdogIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { Scan(); }
                catch (Exception ex) { _logger.LogError(ex, "Watchdog scan failed"); }
            }
        }
        catch (OperationCanceledException)
        {
            //shutting down
        }
    }

    private void Scan()
    {
        var staleAfter = TimeSpan.FromSeconds(_options.StaleAfterSeconds);
        var now = DateTime.UtcNow;

        foreach (var device in _registry.Views())
        {
            if (device.LastSeenUtc is null) continue; //never reported: shown as "No data", not "Disconnected"

            var silentFor = now - device.LastSeenUtc.Value;
            if (silentFor <= staleAfter) continue;

            //Each scan re-detects the same silent node; the HashSet makes sure only the first becomes an alert
            if (_alerts.TryRaise(device.Id, device.Name, AlertTracker.Disconnected,
                    $"No data received for {(int)silentFor.TotalSeconds}s"))
            {
                _engine.RecordDisconnect(device.Id);
            }
        }
    }
}
