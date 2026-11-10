//ST10440432
//Matteo Nusca
using Microsoft.Extensions.Options;
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers how the mesh is built from registered sensors and how failures change routes.</summary>
public class MeshTopologyServiceTests
{
    private const string A = "AA:BB:CC:00:00:01";
    private const string B = "AA:BB:CC:00:00:02";
    private const string C = "AA:BB:CC:00:00:03";
    private const string D = "AA:BB:CC:00:00:04";
    private const string E = "AA:BB:CC:00:00:05";
    private const string F = "AA:BB:CC:00:00:06";
    private const string G = "AA:BB:CC:00:00:07";
    private const string H = "AA:BB:CC:00:00:08";
    private const string I = "AA:BB:CC:00:00:09";

    private static (MeshTopologyService Mesh, DeviceRegistry Registry, AlertTracker Alerts) Build(int sensors = 5)
    {
        var registry = new DeviceRegistry();
        var alerts = new AlertTracker();
        string[] ids = { A, B, C, D, E, F, G, H, I };
        string[] places =
        {
            "Greenhouse 1, Zone A, Node 1", "Greenhouse 1, Zone B, Node 2", "Utility Room, Zone C, Node 3",
            "Utility Room, Zone C, Node 4", "Main Valve Station, Zone D, Node 5", "Greenhouse 1, Zone A, Node 6",
            "Greenhouse 1, Zone B, Node 7", "Utility Room, Zone C, Node 8", "Main Valve Station, Zone D, Node 9"
        };

        for (int i = 0; i < sensors; i++)
        {
            registry.TryRegister(new SensorRegistration
            {
                DeviceId = ids[i], DeploymentLocation = places[i], Category = SensorCategory.Environmental,
                FriendlyName = $"Sensor {i + 1}"
            }, out _);
        }

        var mesh = new MeshTopologyService(registry, alerts, Options.Create(new GatewayOptions()));
        return (mesh, registry, alerts);
    }

    [Fact]
    public void Snapshot_ListsTheGatewayAndEverySensor()
    {
        var (mesh, _, _) = Build();

        var snapshot = mesh.Snapshot();

        Assert.Equal(6, snapshot.Nodes.Count);
        Assert.Equal("Gateway", snapshot.Nodes[0].Kind);
        Assert.Equal(5, snapshot.UpCount);
        Assert.Equal(0, snapshot.DownCount);
        Assert.Equal(0, snapshot.IsolatedCount);
    }

    [Fact]
    public void Snapshot_SpanningTreeJoinsEveryNodeAndIsNoDearerThanAllLinks()
    {
        var (mesh, _, _) = Build();

        var snapshot = mesh.Snapshot();

        Assert.Equal(5, snapshot.MstLinkCount);
        Assert.Equal(5, snapshot.Links.Count(l => l.InMst));
        Assert.True(snapshot.MstTotalMilliwatts <= snapshot.AllLinksTotalMilliwatts);
    }

    [Fact]
    public void Mesh_IsTheSameEveryTimeItIsBuilt()
    {
        var first = Build().Mesh.Snapshot();
        var second = Build().Mesh.Snapshot();

        Assert.Equal(first.MstTotalMilliwatts, second.MstTotalMilliwatts);
        Assert.Equal(first.AllLinksTotalMilliwatts, second.AllLinksTotalMilliwatts);
        Assert.Equal(first.Links.Count, second.Links.Count);
    }

    [Fact]
    public void LinkCost_IsTheSameInEitherDirection_AndCheaperInTheSameZone()
    {
        double ab = MeshTopologyService.LinkCostFor("X1", "Greenhouse 1, Zone A, Node 1", "X2", "Greenhouse 1, Zone A, Node 2");
        double ba = MeshTopologyService.LinkCostFor("X2", "Greenhouse 1, Zone A, Node 2", "X1", "Greenhouse 1, Zone A, Node 1");
        double farAway = MeshTopologyService.LinkCostFor("X1", "Greenhouse 1, Zone A, Node 1", "X2", "Utility Room, Zone C, Node 2");

        Assert.Equal(ab, ba);
        Assert.InRange(ab, 3, 7);
        Assert.InRange(farAway, 12, 16);
    }

    [Fact]
    public void Route_ToTheGatewayStartsAtTheDeviceAndEndsAtTheGateway()
    {
        var (mesh, _, _) = Build();

        var report = mesh.Route(C)!;

        Assert.True(report.Reachable);
        Assert.Equal("Up", report.Status);
        Assert.Equal(C, report.Cheapest!.NodeIds[0]);
        Assert.Equal(MeshTopologyService.GatewayId, report.Cheapest.NodeIds[^1]);
        Assert.Equal(C, report.FewestHops!.NodeIds[0]);
        Assert.Equal(C, report.MstRoute!.NodeIds[0]);
        Assert.False(report.Rerouted);
    }

    [Fact]
    public void Route_CheapestIsNeverDearerThanTheSpanningTreeRoute_AndFewestHopsHasNoMoreHops()
    {
        var (mesh, _, _) = Build();

        foreach (var id in new[] { A, B, C, D, E })
        {
            var report = mesh.Route(id)!;
            Assert.True(report.Cheapest!.CostMilliwatts <= report.MstRoute!.CostMilliwatts);
            Assert.True(report.FewestHops!.Hops <= report.Cheapest.Hops);
        }
    }

    [Fact]
    public void Route_UnknownDevice_ReturnsNull()
    {
        var (mesh, _, _) = Build();

        Assert.Null(mesh.Route("nope"));
        Assert.Null(mesh.SetFailed("nope", true));
    }

    [Fact]
    public void FailingANode_MarksItDown_AndRouteReportSaysSo()
    {
        var (mesh, _, _) = Build();

        var result = mesh.SetFailed(C, true)!;
        var report = mesh.Route(C)!;

        Assert.True(result.Changed);
        Assert.Equal("Down", result.Status);
        Assert.Equal(1, result.DownCount);
        Assert.Equal("Down", report.Status);
        Assert.False(report.Reachable);
        Assert.Null(report.Cheapest);
    }

    [Fact]
    public void FailingTheSameNodeTwice_OnlyChangesItOnce()
    {
        var (mesh, _, _) = Build();

        Assert.True(mesh.SetFailed(C, true)!.Changed);
        Assert.False(mesh.SetFailed(C, true)!.Changed);
        Assert.True(mesh.SetFailed(C, false)!.Changed);
        Assert.False(mesh.SetFailed(C, false)!.Changed);
    }

    [Fact]
    public void RestoringANode_BringsItsRouteBack()
    {
        var (mesh, _, _) = Build();
        var before = mesh.Route(C)!.Cheapest!.CostMilliwatts;

        mesh.SetFailed(C, true);
        mesh.SetFailed(C, false);
        var after = mesh.Route(C)!;

        Assert.Equal("Up", after.Status);
        Assert.Equal(before, after.Cheapest!.CostMilliwatts);
    }

    [Fact]
    public void FailingARelayOnARoute_ReroutesTheNodesThatUsedIt()
    {
        //With the full nine-sensor fleet, the greenhouse node F reaches the gateway through A
        var (mesh, _, _) = Build(sensors: 9);
        var before = mesh.Route(F)!;
        Assert.Equal(new[] { F, A, MeshTopologyService.GatewayId }, before.Cheapest!.NodeIds);

        mesh.SetFailed(A, true);
        var after = mesh.Route(F)!;

        Assert.True(after.Reachable);
        Assert.True(after.Rerouted);
        Assert.Contains(A, after.BlockedBy);
        Assert.DoesNotContain(A, after.Cheapest!.NodeIds);
        Assert.True(after.RerouteExtraMilliwatts > 0);
        Assert.Equal(after.Cheapest.CostMilliwatts - before.Cheapest.CostMilliwatts, after.RerouteExtraMilliwatts);
    }

    [Fact]
    public void ADisconnectedAlert_CountsAsADownNode()
    {
        var (mesh, _, alerts) = Build();

        alerts.TryRaise(B, "Sensor 2", AlertTracker.Disconnected, "silent");
        var snapshot = mesh.Snapshot();

        var node = snapshot.Nodes.Single(n => n.Id == B);
        Assert.Equal("Down", node.Status);
        Assert.Equal("Disconnected", node.DownReason);

        alerts.Clear(B, AlertTracker.Disconnected);
        Assert.Equal("Up", mesh.Snapshot().Nodes.Single(n => n.Id == B).Status);
    }

    [Fact]
    public void Sensors_RegisteredLater_JoinTheMeshAutomatically()
    {
        var (mesh, registry, _) = Build(sensors: 3);
        Assert.Equal(4, mesh.Snapshot().Nodes.Count);

        registry.TryRegister(new SensorRegistration
        {
            DeviceId = D, DeploymentLocation = "Utility Room, Zone C, Node 4", Category = SensorCategory.Environmental
        }, out _);

        var snapshot = mesh.Snapshot();
        Assert.Equal(5, snapshot.Nodes.Count);
        Assert.NotNull(mesh.Route(D));
    }

    [Fact]
    public void Connectivity_ListsDownNodesAndKeepsTheGatewayFirst()
    {
        var (mesh, _, _) = Build();
        mesh.SetFailed(B, true);

        var info = mesh.Connectivity();

        Assert.Equal(MeshTopologyService.GatewayId, info.Connected[0]);
        Assert.Single(info.Down);
        Assert.Equal(B, info.Down[0].Id);
        Assert.Equal("Manual", info.Down[0].Reason);
        Assert.DoesNotContain(B, info.Connected);
    }
}
