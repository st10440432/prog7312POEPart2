using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the generic TelemetryPacket&lt;T&gt; and the operators overloaded on it (Part 1 generics + operator overloading).</summary>
public class TelemetryPacketTests
{
    private static readonly DateTime Earlier = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddSeconds(30);

    private static NumericTelemetryPacket<int> Power(string id, string zone, int watts, DateTime at) =>
        new(id, zone, SensorCategory.PowerConsumption, watts, at);

    [Fact]
    public void Plus_AggregatesTwoMeters_AndMergesIds()
    {
        var meter1 = Power("M1", "Zone C", 480, Earlier);
        var meter2 = Power("M2", "Zone C", 300, Later);

        var meter3 = meter1 + meter2;

        Assert.Equal(780, meter3.Value);
        Assert.Equal("M1+M2", meter3.DeviceId);
        Assert.Equal("Zone C", meter3.Zone);
        Assert.Equal(Later, meter3.Timestamp);
    }

    [Fact]
    public void Plus_AcrossZones_LabelsTheResultAggregate()
    {
        var sum = Power("M1", "Zone C", 100, Earlier) + Power("M2", "Zone D", 50, Earlier);

        Assert.Equal("Aggregate", sum.Zone);
    }

    [Fact]
    public void Minus_ReturnsSignedDelta()
    {
        var before = Power("M1", "Zone C", 480, Earlier);
        var after = Power("M1", "Zone C", 1600, Later);

        Assert.Equal(1120, after - before);
        Assert.Equal(-1120, before - after);
        Assert.Equal(1120, after.AbsoluteDelta(before));
    }

    [Fact]
    public void Plus_WorksOnFloatPackets_WithoutBoxing()
    {
        var a = new NumericTelemetryPacket<float>("S1", "Zone A", SensorCategory.Environmental, 40.5f, Earlier);
        var b = new NumericTelemetryPacket<float>("S2", "Zone A", SensorCategory.Environmental, 2.5f, Earlier);

        Assert.Equal(43.0f, (a + b).Value);
    }

    [Fact]
    public void ComparisonOperators_RankByValue()
    {
        var low = new TelemetryPacket<float>("S1", "Zone A", SensorCategory.Environmental, 5f, Earlier);
        var high = new TelemetryPacket<float>("S2", "Zone A", SensorCategory.Environmental, 95f, Earlier);

        Assert.True(high > low);
        Assert.True(low < high);
        Assert.True(high >= high);
        Assert.True(low <= low);
        Assert.False(low > high);
    }

    [Fact]
    public void ComparisonOperators_WorkOnBooleanActuatorPackets()
    {
        var off = new TelemetryPacket<bool>("P1", "Zone A", SensorCategory.Actuator, false, Earlier);
        var on = new TelemetryPacket<bool>("P1", "Zone A", SensorCategory.Actuator, true, Later);

        Assert.True(on > off);
    }

    [Fact]
    public void CompareTo_TreatsNullAsSmaller()
    {
        var packet = Power("M1", "Zone C", 1, Earlier);

        Assert.Equal(1, packet.CompareTo(null));
    }

    [Fact]
    public void From_ReusesNumericPacket_AndWrapsPlainPacket()
    {
        var numeric = Power("M1", "Zone C", 10, Earlier);
        Assert.Same(numeric, NumericTelemetryPacket<int>.From(numeric));

        var plain = new TelemetryPacket<int>("M2", "Zone C", SensorCategory.PowerConsumption, 20, Later);
        var wrapped = NumericTelemetryPacket<int>.From(plain);

        Assert.Equal(20, wrapped.Value);
        Assert.Equal("M2", wrapped.DeviceId);
        Assert.Equal(Later, wrapped.Timestamp);
    }
}
