using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the recursive build and roll-up of the deployment hierarchy shown on the Sensor Ingestion page.</summary>
public class ZoneTreeBuilderTests
{
    private static readonly (string Location, int ActiveAlerts)[] Sensors =
    {
        ("Greenhouse 1, Zone A, Node 1", 1),
        ("Greenhouse 1, Zone A, Node 6", 0),
        ("Greenhouse 1, Zone B, Node 2", 2),
        ("Utility Room, Zone C, Node 3", 0),
    };

    [Fact]
    public void Build_RollsSensorAndAlertTotalsUpTheTree()
    {
        var root = ZoneTreeBuilder.Build(Sensors);

        Assert.Equal("All Sites", root.Name);
        Assert.Equal(4, root.TotalSensors);
        Assert.Equal(3, root.TotalAlerts);
        Assert.Equal(2, root.Children.Count);

        var greenhouse = root.Children.Single(c => c.Name == "Greenhouse 1");
        Assert.Equal(3, greenhouse.TotalSensors);
        Assert.Equal(3, greenhouse.TotalAlerts);

        var zoneA = greenhouse.Children.Single(c => c.Name == "Zone A");
        Assert.Equal(2, zoneA.TotalSensors);
        Assert.Equal(1, zoneA.TotalAlerts);
    }

    [Fact]
    public void Build_PutsSensorsOnTheirLeafNode()
    {
        var root = ZoneTreeBuilder.Build(Sensors);

        var node1 = root.Children.Single(c => c.Name == "Greenhouse 1")
            .Children.Single(c => c.Name == "Zone A")
            .Children.Single(c => c.Name == "Node 1");

        Assert.Equal(1, node1.DirectSensors);
        Assert.Equal(1, node1.DirectAlerts);
        Assert.Empty(node1.Children);
        Assert.Equal("All Sites -> Greenhouse 1 -> Zone A -> Node 1", node1.Path);
    }

    [Fact]
    public void Build_MergesSegmentsThatDifferOnlyByCase()
    {
        var root = ZoneTreeBuilder.Build(new (string Location, int ActiveAlerts)[]
        {
            ("Greenhouse 1, Zone A", 0),
            ("greenhouse 1, Zone B", 0),
        });

        Assert.Single(root.Children);
        Assert.Equal(2, root.Children[0].Children.Count);
    }

    [Fact]
    public void Build_WithNoSensors_ReturnsEmptyRoot()
    {
        var root = ZoneTreeBuilder.Build(Array.Empty<(string Location, int ActiveAlerts)>());

        Assert.Equal(0, root.TotalSensors);
        Assert.Empty(root.Children);
    }
}
