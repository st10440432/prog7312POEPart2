using SmartX.Api.Services;

namespace SmartX.Tests;

/// <summary>Covers the HashSet-backed duplicate alert suppression (Part 2 sets requirement).</summary>
public class AlertTrackerTests
{
    private const string Device = "AA:BB:CC:00:00:03";

    [Fact]
    public void TryRaise_ReturnsTrueOnlyForTheFirstAlert()
    {
        var tracker = new AlertTracker();

        Assert.True(tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.PowerSpike, "spike"));
        Assert.False(tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.PowerSpike, "spike again"));

        Assert.Equal(1, tracker.ActiveCount);
        Assert.Equal(1, tracker.DuplicatesSuppressed);
    }

    [Fact]
    public void TryRaise_KeepsDifferentAlertTypesForTheSameDeviceSeparate()
    {
        var tracker = new AlertTracker();

        Assert.True(tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.PowerSpike, "spike"));
        Assert.True(tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.Disconnected, "silent"));

        Assert.Equal(2, tracker.ActiveCount);
        Assert.Equal(0, tracker.DuplicatesSuppressed);
    }

    [Fact]
    public void TryRaise_TreatsDeviceIdsCaseInsensitively()
    {
        var tracker = new AlertTracker();

        Assert.True(tracker.TryRaise("aa:bb:cc:00:00:03", "Smart Meter 3", AlertTracker.OutOfRange, "x"));
        Assert.False(tracker.TryRaise("AA:BB:CC:00:00:03", "Smart Meter 3", AlertTracker.OutOfRange, "x"));
    }

    [Fact]
    public void Clear_AllowsTheAlertToFireAgain()
    {
        var tracker = new AlertTracker();
        tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.Disconnected, "silent");

        Assert.True(tracker.Clear(Device, AlertTracker.Disconnected));
        Assert.False(tracker.IsActive(Device, AlertTracker.Disconnected));
        Assert.False(tracker.Clear(Device, AlertTracker.Disconnected));

        Assert.True(tracker.TryRaise(Device, "Smart Meter 3", AlertTracker.Disconnected, "silent again"));
    }

    [Fact]
    public void Snapshot_ListsActiveAlerts_NewestFirst()
    {
        var tracker = new AlertTracker();
        tracker.TryRaise("dev-1", "One", AlertTracker.OutOfRange, "first");
        Thread.Sleep(25);
        tracker.TryRaise("dev-2", "Two", AlertTracker.OutOfRange, "second");

        var snapshot = tracker.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("dev-2", snapshot[0].DeviceId);
    }
}
