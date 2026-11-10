using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the jagged-array to List&lt;T&gt; loader (Part 1 arrays and collections requirement).</summary>
public class TelemetryBatchLoaderTests
{
    private static readonly DateTime End = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LoadHistoricalBatches_FlattensJaggedRows_AndSpacesTimestamps()
    {
        float[][] raw =
        {
            new[] { 1f, 2f, 3f },
            new[] { 4f, 5f },
        };

        var packets = TelemetryBatchLoader.LoadHistoricalBatches(
            raw, new[] { "d1", "d2" }, "Zone A", SensorCategory.Environmental,
            endUtc: End, interval: TimeSpan.FromSeconds(5));

        Assert.Equal(5, packets.Count);

        var d1 = packets.Where(p => p.DeviceId == "d1").ToList();
        Assert.Equal(new[] { 1f, 2f, 3f }, d1.Select(p => p.Value));
        Assert.Equal(End.AddSeconds(-10), d1[0].Timestamp);
        Assert.Equal(End, d1[^1].Timestamp);

        var d2 = packets.Where(p => p.DeviceId == "d2").ToList();
        Assert.Equal(End, d2[^1].Timestamp);
        Assert.All(packets, p => Assert.Equal("Zone A", p.Zone));
    }

    [Fact]
    public void LoadHistoricalBatches_ThrowsWhenRowsAndIdsDoNotMatch()
    {
        float[][] raw = { new[] { 1f }, new[] { 2f } };

        Assert.Throws<ArgumentException>(() =>
            TelemetryBatchLoader.LoadHistoricalBatches(raw, new[] { "only-one" }, "Zone A", SensorCategory.Environmental));
    }

    [Fact]
    public void LoadHistoricalBatches_GivesEveryReadingADistinctTimestampPerDevice()
    {
        float[][] raw = { new[] { 10f, 11f, 12f, 13f } };

        var packets = TelemetryBatchLoader.LoadHistoricalBatches(
            raw, new[] { "d1" }, "Zone A", SensorCategory.Environmental, endUtc: End);

        Assert.Equal(packets.Count, packets.Select(p => p.Timestamp).Distinct().Count());
    }
}
