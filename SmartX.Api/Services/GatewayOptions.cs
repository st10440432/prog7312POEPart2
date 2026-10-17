//ST10440432
//Matteo Nusca
namespace SmartX.Api.Services;

/// <summary>Tunable gateway settings (appsettings "Gateway" section or Gateway__X environment variables).</summary>
public sealed class GatewayOptions
{
    /// <summary>Simulated per-packet processing cost of the constrained gateway. Makes queue backlogs visible.</summary>
    public int ProcessingDelayMs { get; set; } = 5;

    /// <summary>A device silent for this long is flagged Stale / Disconnected.</summary>
    public int StaleAfterSeconds { get; set; } = 60;

    public int WatchdogIntervalSeconds { get; set; } = 5;

    public int MaxHistoryPerDevice { get; set; } = 500;
}
