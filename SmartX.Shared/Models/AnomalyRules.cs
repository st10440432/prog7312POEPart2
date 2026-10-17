//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public static class AnomalyRules
{
    public const float EnvironmentalLow = 15f;
    public const float EnvironmentalHigh = 85f;
    public const float EnvironmentalSevereLow = 10f;
    public const float EnvironmentalSevereHigh = 90f;
    public const int PowerSpikeWatts = 500;
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromSeconds(60);

    public static bool IsEnvironmentalAnomaly(float value) =>
        value < EnvironmentalLow || value > EnvironmentalHigh;

    public static bool IsEnvironmentalSevere(float value) =>
        value < EnvironmentalSevereLow || value > EnvironmentalSevereHigh;

    //Uses overloaded '-' operator on NumericTelemetryPacket<int>
    public static bool IsPowerSpike(NumericTelemetryPacket<int> current, NumericTelemetryPacket<int>? previous) =>
        previous is not null && Math.Abs(current - previous) > PowerSpikeWatts;

    //Rising edge only
    public static bool IsPowerRise(NumericTelemetryPacket<int> current, NumericTelemetryPacket<int>? previous) =>
        previous is not null && (current - previous) > PowerSpikeWatts;
}
