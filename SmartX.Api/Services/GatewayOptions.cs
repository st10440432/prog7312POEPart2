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

    /// <summary>Readings kept per device in the long-term AVL archive used for historical range queries.</summary>
    public int MaxArchivePerDevice { get; set; } = 20000;

    /// <summary>Readings of synthetic history seeded per device into the archive at startup (8640 = a day at 10 s).</summary>
    public int ArchiveBackfillPoints { get; set; } = 8640;

    /// <summary>Where the gateway itself is installed. Sensors in the same place get cheap links to it.</summary>
    public string GatewayLocation { get; set; } = "Utility Room";
}
