//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

//Part 2 DTOs shared between the API and the Blazor client

public enum DeviceStatus
{
    Live,
    Stale,
    NoData,
    Alert,
    Disconnected
}

public enum CommandAction
{
    TurnOn,
    TurnOff,
    Toggle
}

public enum InsightSeverity
{
    Info,
    Warning,
    Critical
}

public sealed class DeviceState
{
    public string DeviceId { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? DeviceId : FriendlyName!;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public DeviceStatus Status { get; set; }
    public string LastValueText { get; set; } = "-";
    public DateTime? LastSeenUtc { get; set; }
    public bool? ActuatorOn { get; set; }
    public List<string> ActiveAlerts { get; set; } = new();
    public bool HasInsight { get; set; }
    public InsightSeverity InsightSeverity { get; set; }
}

public sealed class AlertInfo
{
    public string Key { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime RaisedUtc { get; set; }
}

public sealed class ProcessedItem
{
    public long Sequence { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Lane { get; set; } = "FIFO";
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public DateTime ProcessedUtc { get; set; }
    public bool Rejected { get; set; }
}

public sealed class PipelineStats
{
    public int FifoDepth { get; set; }
    public int PriorityDepth { get; set; }
    public long ProcessedTotal { get; set; }
    public long ProcessedPriority { get; set; }
    public long ProcessedFifo { get; set; }
    public double AvgLatencyPriorityMs { get; set; }
    public double AvgLatencyFifoMs { get; set; }
    public long DuplicatesSuppressed { get; set; }
    public int ActiveAlerts { get; set; }
    public int UndoDepth { get; set; }
    public int RegisteredDevices { get; set; }
    public List<ProcessedItem> Recent { get; set; } = new();
}

public sealed class IngestResponse
{
    public long Sequence { get; set; }
    public string Lane { get; set; } = "FIFO";
    public string Status { get; set; } = "processed";
    public string Reason { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
}

public sealed class CommandRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public CommandAction Action { get; set; }
    public string Source { get; set; } = "Manual";
}

public sealed class BulkCommandRequest
{
    public string Filter { get; set; } = string.Empty;
    public CommandAction Action { get; set; }
    public string Source { get; set; } = "Manual";
}

public sealed class CommandRecord
{
    public long Id { get; set; }
    public string Kind { get; set; } = "Command";
    public string Description { get; set; } = string.Empty;
    public CommandAction Action { get; set; }
    public List<string> DeviceIds { get; set; } = new();
    public string Source { get; set; } = "Manual";
    public DateTime TimestampUtc { get; set; }
    public bool Undone { get; set; }

    public CommandRecord Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Description = Description,
        Action = Action,
        DeviceIds = new List<string>(DeviceIds),
        Source = Source,
        TimestampUtc = TimestampUtc,
        Undone = Undone
    };
}

public sealed class SearchEventRequest
{
    public string Term { get; set; } = string.Empty;
}

public sealed class SearchEventResponse
{
    public bool Logged { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class FeedbackRequest
{
    public string SuggestionId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
}

public sealed class SuggestedAction
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "Command";
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int Support { get; set; }
    public double Score { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public CommandAction? Action { get; set; }
}

public sealed class AutomatedInsight
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public InsightSeverity Severity { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class InsightsResponse
{
    public List<SuggestedAction> Suggestions { get; set; } = new();
    public List<AutomatedInsight> Insights { get; set; } = new();
    public int EventsLearned { get; set; }
    public int PatternsKnown { get; set; }
}

public sealed class StreamSnapshot
{
    public DateTime GeneratedUtc { get; set; }
    public PipelineStats Stats { get; set; } = new();
    public List<DeviceState> Devices { get; set; } = new();
    public int TotalDevices { get; set; }
    public List<AlertInfo> Alerts { get; set; } = new();
    public List<CommandRecord> RecentCommands { get; set; } = new();
    public InsightsResponse Insights { get; set; } = new();
}

public sealed class TimelinePoint
{
    public DateTime TimestampUtc { get; set; }
    public double Value { get; set; }
}

public sealed class BatchReading
{
    public string DeviceId { get; set; } = string.Empty;
    public double Value { get; set; }
}

public sealed class BatchIngestRequest
{
    public List<BatchReading> Readings { get; set; } = new();
}

public sealed class BatchIngestResponse
{
    public int Accepted { get; set; }
    public int Rejected { get; set; }
}
