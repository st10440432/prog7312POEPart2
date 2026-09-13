//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

public sealed class TelemetryDashboardResponse
{
    public List<TelemetryPacket<float>> Environmental { get; set; } = new();
    public List<TelemetryPacket<int>> Power { get; set; } = new();
    public List<TelemetryPacket<bool>> Actuator { get; set; } = new();
}