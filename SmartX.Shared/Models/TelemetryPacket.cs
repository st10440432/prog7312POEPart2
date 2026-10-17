//ST10440432
//Matteo Nusca
using System.Numerics;

namespace SmartX.Shared.Models;

public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}

public class TelemetryPacket<T> : IComparable<TelemetryPacket<T>> where T : struct, IComparable<T>
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

    public int CompareTo(TelemetryPacket<T>? other) => other is null ? 1 : Value.CompareTo(other.Value);

    //Threshold / ranking comparisons - overloaded directly on the generic packet
    public static bool operator >(TelemetryPacket<T> a, TelemetryPacket<T> b) => a.Value.CompareTo(b.Value) > 0;
    public static bool operator <(TelemetryPacket<T> a, TelemetryPacket<T> b) => a.Value.CompareTo(b.Value) < 0;
    public static bool operator >=(TelemetryPacket<T> a, TelemetryPacket<T> b) => a.Value.CompareTo(b.Value) >= 0;
    public static bool operator <=(TelemetryPacket<T> a, TelemetryPacket<T> b) => a.Value.CompareTo(b.Value) <= 0;

    public override string ToString() => $"[{Timestamp:O}] {DeviceId} ({Category}) @ {Zone} = {Value}";
}

/// <summary>
/// Numeric specialisation (float, int, ...) that adds arithmetic operators using .NET generic math
/// (INumber&lt;T&gt;), so aggregation and deltas run on the raw value type with no boxing.
///   Meter3 = Meter1 + Meter2   -> aggregate load packet
///   delta  = current - previous -> signed change between two readings
/// </summary>
public class NumericTelemetryPacket<T> : TelemetryPacket<T> where T : struct, INumber<T>
{
    public NumericTelemetryPacket(string deviceId, string zone, SensorCategory category, T value, DateTime timestamp)
        : base(deviceId, zone, category, value, timestamp)
    {
    }

    public static NumericTelemetryPacket<T> From(TelemetryPacket<T> packet) =>
        packet as NumericTelemetryPacket<T>
        ?? new NumericTelemetryPacket<T>(packet.DeviceId, packet.Zone, packet.Category, packet.Value, packet.Timestamp);

    //Aggregation: combined load of two devices
    public static NumericTelemetryPacket<T> operator +(NumericTelemetryPacket<T> a, NumericTelemetryPacket<T> b) =>
        new($"{a.DeviceId}+{b.DeviceId}",
            a.Zone == b.Zone ? a.Zone : "Aggregate",
            a.Category,
            a.Value + b.Value,
            a.Timestamp >= b.Timestamp ? a.Timestamp : b.Timestamp);

    //Delta comparison: signed change from b to a
    public static T operator -(NumericTelemetryPacket<T> a, NumericTelemetryPacket<T> b) => a.Value - b.Value;

    public T AbsoluteDelta(NumericTelemetryPacket<T> other) => T.Abs(Value - other.Value);
}
