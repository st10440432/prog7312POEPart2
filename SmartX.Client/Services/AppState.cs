//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

/// <summary>
/// App-wide state that lives for the whole browser session. Blazor destroys a page's component state when the
/// user navigates away, so anything that should survive switching between pillars (filters, form drafts,
/// the last telemetry snapshot) is kept here and the pages re-read it when they are created again.
/// </summary>
public sealed class AppState
{
    public StreamViewState Stream { get; } = new();
    public SensorsViewState Sensors { get; } = new();

    /// <summary>Last snapshot received, so the Command Stream page renders instantly when you come back to it.</summary>
    public StreamSnapshot? LastSnapshot { get; set; }
}

public sealed class StreamViewState
{
    public string Search { get; set; } = string.Empty;
    public SensorCategory? Category { get; set; }
    public bool AlertsOnly { get; set; }
    public string? TimelineDeviceId { get; set; }
    public string BulkFilter { get; set; } = "valve";
}

public sealed class SensorsViewState
{
    public SensorRegistration NewSensor { get; set; } = new();
    public string? TelemetryDeviceId { get; set; }
    public string TelemetryValue { get; set; } = string.Empty;
    public string? UploadDeviceId { get; set; }
}
