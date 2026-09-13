//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}
public sealed class TelemetryPacket<T> where T : struct
{
    public string DeviceId { get; }
    public string Zone { get; }
    public SensorCategory Category { get; }
    public T Value { get; }
    public DateTime Timestamp { get; }

    public TelemetryPacket(string deviceId, string zone, SensorCategory category, T value, DateTime timestamp)
    {
        DeviceId = deviceId;
        Zone = zone;
        Category = category;
        Value = value;
        Timestamp = timestamp;
    }

    public override string ToString() => $"[{Timestamp:O}] {DeviceId} ({Category}) @ {Zone} = {Value}";
}