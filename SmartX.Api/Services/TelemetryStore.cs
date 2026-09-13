//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;
public sealed class TelemetryStore
{
    public List<SensorRegistration> Sensors { get; } = new();

    public List<TelemetryPacket<float>> EnvironmentalReadings { get; } = new();
    public List<TelemetryPacket<int>> PowerReadings { get; } = new();
    public List<TelemetryPacket<bool>> ActuatorReadings { get; } = new();

    public SensorRegistration? FindSensor(string deviceId) =>
        Sensors.FirstOrDefault(s => s.DeviceId == deviceId);
}