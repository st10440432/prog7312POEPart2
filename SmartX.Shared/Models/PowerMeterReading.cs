//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public sealed class PowerMeterReading
{
    public string DeviceId { get; set; }
    public double Watts { get; set; }

    public PowerMeterReading(string deviceId, double watts)
    {
        DeviceId = deviceId;
        Watts = watts;
    }

    //Aggregation
    public static PowerMeterReading operator +(PowerMeterReading a, PowerMeterReading b)
        => new PowerMeterReading($"{a.DeviceId}+{b.DeviceId}", a.Watts + b.Watts);

    //Delta comparison
    public static double operator -(PowerMeterReading a, PowerMeterReading b)
        => a.Watts - b.Watts;

    //Threshold comparisons
    public static bool operator >(PowerMeterReading a, PowerMeterReading b) => a.Watts > b.Watts;
    public static bool operator <(PowerMeterReading a, PowerMeterReading b) => a.Watts < b.Watts;

    public override string ToString() => $"{DeviceId}: {Watts:F2} W";
}