//ST10440432
//Matteo Nusca
using Microsoft.Extensions.Options;
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the AVL archive inside TelemetryHistory: range queries, retention and the benchmark.</summary>
public class TelemetryHistoryArchiveTests
{
    private const string Device = "AA:BB:CC:00:00:01";
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TelemetryHistory NewHistory(int maxArchive = 20000) =>
        new(Options.Create(new GatewayOptions { MaxArchivePerDevice = maxArchive }));

    private static HistoryPoint Point(int second, double value = 1) =>
        new(Device, SensorCategory.Environmental, "Greenhouse 1", value, Start.AddSeconds(second));

    private static TelemetryHistory Filled(int count)
    {
        var history = NewHistory();
        history.BackfillArchive(Enumerable.Range(0, count).Select(i => Point(i, i)));
        return history;
    }

    [Fact]
    public void QueryArchive_ReturnsOnlyReadingsInsideTheRange_OldestFirst()
    {
        var history = Filled(100);

        var result = history.QueryArchive(Device, Start.AddSeconds(10), Start.AddSeconds(19), 500);

        Assert.Equal(10, result.TotalMatches);
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (double)i), result.Points.Select(p => p.Value));
    }

    [Fact]
    public void QueryArchive_IncludesBothEndsOfTheRange()
    {
        var history = Filled(50);

        var result = history.QueryArchive(Device, Start.AddSeconds(5), Start.AddSeconds(5), 10);

        Assert.Single(result.Points);
        Assert.Equal(5, result.Points[0].Value);
    }

    [Fact]
    public void QueryArchive_KeepsTheNewestReadings_WhenMoreMatchThanRequested()
    {
        var history = Filled(100);

        var result = history.QueryArchive(Device, null, null, 5);

        Assert.Equal(100, result.TotalMatches);
        Assert.Equal(new double[] { 95, 96, 97, 98, 99 }, result.Points.Select(p => p.Value));
    }

    [Fact]
    public void QueryArchive_KeepsReadingsWithTheSameTimestamp()
    {
        var history = NewHistory();
        history.BackfillArchive(new[] { Point(5, 1), Point(5, 2), Point(5, 3) });

        var result = history.QueryArchive(Device, Start.AddSeconds(5), Start.AddSeconds(5), 10);

        Assert.Equal(3, result.TotalMatches);
    }

    [Fact]
    public void QueryArchive_ReturnsNothingForAnUnknownDeviceOrEmptyRange()
    {
        var history = Filled(20);

        Assert.Empty(history.QueryArchive("nope", null, null, 10).Points);
        Assert.Empty(history.QueryArchive(Device, Start.AddDays(1), Start.AddDays(2), 10).Points);
    }

    [Fact]
    public void QueryArchive_OnANarrowRange_TouchesFarFewerNodesThanTheArchiveHolds()
    {
        var history = Filled(5000);

        var result = history.QueryArchive(Device, Start.AddSeconds(2000), Start.AddSeconds(2009), 100);

        Assert.Equal(10, result.TotalMatches);
        Assert.Equal(5000, result.ArchiveSize);
        Assert.True(result.NodesVisited < 100, $"visited {result.NodesVisited} nodes");
        Assert.True(result.TreeHeight <= 18, $"height {result.TreeHeight}");
    }

    [Fact]
    public void QueryArchive_ReportsTheOldestAndNewestReading()
    {
        var history = Filled(30);

        var result = history.QueryArchive(Device, null, null, 5);

        Assert.Equal(Start, result.Oldest);
        Assert.Equal(Start.AddSeconds(29), result.Newest);
    }

    [Fact]
    public void Archive_DropsTheOldestReadings_OnceTheCapIsReached()
    {
        var history = NewHistory(maxArchive: 500);
        history.BackfillArchive(Enumerable.Range(0, 600).Select(i => Point(i, i)));

        Assert.Equal(500, history.ArchiveCount(Device));
        var result = history.QueryArchive(Device, null, null, 1);
        Assert.Equal(Start.AddSeconds(100), result.Oldest);
    }

    [Fact]
    public void Add_FeedsBothTheLiveTimelineAndTheArchive()
    {
        var history = NewHistory();

        history.Add(Point(0, 7));

        Assert.Single(history.Range(Device, null, null, 10));
        Assert.Equal(1, history.ArchiveCount(Device));
    }

    [Fact]
    public void BackfillArchive_DoesNotTouchTheLiveTimeline()
    {
        var history = Filled(40);

        Assert.Empty(history.Range(Device, null, null, 10));
        Assert.Equal(40, history.ArchiveCount(Device));
    }

    [Fact]
    public void Benchmark_TreeAndScanFindTheSameReadings()
    {
        var history = Filled(3000);

        var result = history.Benchmark(Device, Start.AddSeconds(100), Start.AddSeconds(199), 5);

        Assert.True(result.ResultsMatch);
        Assert.Equal(100, result.Matches);
        Assert.Equal(3000, result.ArchiveSize);
        Assert.Equal(5, result.Repetitions);
    }

    [Fact]
    public void Benchmark_OnAnUnknownDevice_ReturnsAnEmptyResult()
    {
        var result = NewHistory().Benchmark("nope", null, null, 10);

        Assert.Equal(0, result.ArchiveSize);
        Assert.True(result.ResultsMatch);
    }
}
