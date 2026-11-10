using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the anomaly thresholds that drive the alert highlighting (the Part 1 engagement strategy).</summary>
public class AnomalyRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static NumericTelemetryPacket<int> Power(int watts) =>
        new("M1", "Zone C", SensorCategory.PowerConsumption, watts, Now);

    [Theory]
    [InlineData(14.9f, true)]
    [InlineData(15f, false)]
    [InlineData(50f, false)]
    [InlineData(85f, false)]
    [InlineData(85.1f, true)]
    public void IsEnvironmentalAnomaly_UsesSafeRange(float value, bool expected)
    {
        Assert.Equal(expected, AnomalyRules.IsEnvironmentalAnomaly(value));
    }

    [Theory]
    [InlineData(9.9f, true)]
    [InlineData(10f, false)]
    [InlineData(90f, false)]
    [InlineData(90.1f, true)]
    public void IsEnvironmentalSevere_UsesSevereRange(float value, bool expected)
    {
        Assert.Equal(expected, AnomalyRules.IsEnvironmentalSevere(value));
    }

    [Fact]
    public void IsPowerSpike_NeedsAPreviousReading()
    {
        Assert.False(AnomalyRules.IsPowerSpike(Power(5000), null));
    }

    [Fact]
    public void IsPowerSpike_DetectsJumpsBeyondThreshold_InEitherDirection()
    {
        Assert.True(AnomalyRules.IsPowerSpike(Power(1600), Power(520)));   // +1080 W
        Assert.True(AnomalyRules.IsPowerSpike(Power(100), Power(900)));    // -800 W
        Assert.False(AnomalyRules.IsPowerSpike(Power(1020), Power(520)));  // exactly 500 W is not a spike
    }

    [Fact]
    public void IsPowerRise_IgnoresDrops()
    {
        Assert.True(AnomalyRules.IsPowerRise(Power(1600), Power(520)));
        Assert.False(AnomalyRules.IsPowerRise(Power(100), Power(900)));
    }
}
