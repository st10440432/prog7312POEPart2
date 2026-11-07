using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers sensor registration validation and the Dictionary-backed O(1) device lookup.</summary>
public class DeviceRegistryTests
{
    private static SensorRegistration Sensor(string id, string location = "Greenhouse 1, Zone A, Node 1") => new()
    {
        DeviceId = id,
        DeploymentLocation = location,
        Category = SensorCategory.Environmental,
        FriendlyName = "Test sensor"
    };

    [Fact]
    public void TryRegister_AcceptsAValidSensor()
    {
        var registry = new DeviceRegistry();

        var outcome = registry.TryRegister(Sensor("AA:BB:CC:00:00:01"), out var error);

        Assert.Equal(RegisterOutcome.Created, outcome);
        Assert.Null(error);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryRegister_RejectsDuplicateIds_CaseInsensitively()
    {
        var registry = new DeviceRegistry();
        registry.TryRegister(Sensor("AA:BB:CC:00:00:01"), out _);

        var outcome = registry.TryRegister(Sensor("aa:bb:cc:00:00:01"), out var error);

        Assert.Equal(RegisterOutcome.Conflict, outcome);
        Assert.Contains("already registered", error);
        Assert.Equal(1, registry.Count);
    }

    [Theory]
    [InlineData("", "Greenhouse 1, Zone A")]
    [InlineData("   ", "Greenhouse 1, Zone A")]
    [InlineData("AA:BB:CC:00:00:02", "")]
    [InlineData("AA:BB:CC:00:00:02", "   ")]
    public void TryRegister_RejectsMissingIdOrLocation(string id, string location)
    {
        var registry = new DeviceRegistry();

        var outcome = registry.TryRegister(Sensor(id, location), out var error);

        Assert.Equal(RegisterOutcome.Invalid, outcome);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void TryRegister_RunsRecursiveHierarchyValidation()
    {
        var registry = new DeviceRegistry();

        var outcome = registry.TryRegister(Sensor("AA:BB:CC:00:00:03", "Facility A,, Node 1"), out var error);

        Assert.Equal(RegisterOutcome.Invalid, outcome);
        Assert.Contains("Invalid deployment hierarchy", error);
    }

    [Fact]
    public void FindSensor_IsTrimmedAndCaseInsensitive()
    {
        var registry = new DeviceRegistry();
        registry.TryRegister(Sensor("AA:BB:CC:00:00:01"), out _);

        Assert.NotNull(registry.FindSensor("  aa:bb:cc:00:00:01 "));
        Assert.Null(registry.FindSensor("AA:BB:CC:00:00:99"));
        Assert.True(registry.Exists("AA:BB:CC:00:00:01"));
    }

    [Fact]
    public void Sensors_ComesBackInRegistrationOrder()
    {
        var registry = new DeviceRegistry();
        registry.TryRegister(Sensor("id-3"), out _);
        registry.TryRegister(Sensor("id-1"), out _);
        registry.TryRegister(Sensor("id-2"), out _);

        Assert.Equal(new[] { "id-3", "id-1", "id-2" }, registry.Sensors().Select(s => s.DeviceId));
    }

    [Fact]
    public void ResolveBest_PrefersExactMatch_ThenUniquePartialMatch()
    {
        var registry = new DeviceRegistry();
        registry.TryRegister(Sensor("AA:BB:CC:00:00:01"), out _);
        registry.TryRegister(Sensor("AA:BB:CC:00:00:02"), out _);

        Assert.Equal("AA:BB:CC:00:00:02", registry.ResolveBest("aa:bb:cc:00:00:02")?.Id);
        Assert.Null(registry.ResolveBest("AA:BB"));              // ambiguous partial match
        Assert.Equal("AA:BB:CC:00:00:01", registry.ResolveBest("00:01")?.Id);
    }
}
