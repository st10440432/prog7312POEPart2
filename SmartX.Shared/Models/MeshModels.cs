//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

/// <summary>One node on the mesh map: a sensor, or the central gateway.</summary>
public sealed class MeshNodeInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Gateway" or "Sensor".</summary>
    public string Kind { get; set; } = "Sensor";

    public SensorCategory? Category { get; set; }
    public string Location { get; set; } = string.Empty;

    /// <summary>"Up" (can reach the gateway), "Down" (failed or silent) or "Isolated" (alive but cut off from the gateway).</summary>
    public string Status { get; set; } = "Up";

    /// <summary>For a down node: "Manual" (simulated failure) or "Disconnected" (stopped reporting).</summary>
    public string? DownReason { get; set; }

    /// <summary>Fewest radio hops to the gateway, or null if it cannot reach it.</summary>
    public int? HopsToGateway { get; set; }

    /// <summary>Next hop towards the gateway along the minimum spanning tree, or null.</summary>
    public string? MstParentId { get; set; }

    public bool HasActiveAlert { get; set; }
}

/// <summary>One radio link between two nodes.</summary>
public sealed class MeshLinkInfo
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public double CostMilliwatts { get; set; }

    /// <summary>True if the link is part of the minimum spanning tree.</summary>
    public bool InMst { get; set; }
}

public sealed class MeshSnapshot
{
    public string GatewayId { get; set; } = string.Empty;
    public List<MeshNodeInfo> Nodes { get; set; } = new();
    public List<MeshLinkInfo> Links { get; set; } = new();

    public int UpCount { get; set; }
    public int DownCount { get; set; }
    public int IsolatedCount { get; set; }

    public int MstLinkCount { get; set; }
    public double MstTotalMilliwatts { get; set; }
    public double AllLinksTotalMilliwatts { get; set; }
    public DateTime GeneratedUtc { get; set; }
}

/// <summary>A route through the mesh, from a sensor towards the gateway.</summary>
public sealed class MeshRoutePath
{
    public List<string> NodeIds { get; set; } = new();
    public List<string> NodeNames { get; set; } = new();
    public int Hops { get; set; }
    public double CostMilliwatts { get; set; }
}

public sealed class MeshRouteReport
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>"Up", "Down" or "Isolated".</summary>
    public string Status { get; set; } = "Up";

    public bool Reachable { get; set; }

    /// <summary>Route with the fewest hops (breadth-first search).</summary>
    public MeshRoutePath? FewestHops { get; set; }

    /// <summary>Route with the lowest total power cost (Dijkstra).</summary>
    public MeshRoutePath? Cheapest { get; set; }

    /// <summary>Route along the minimum spanning tree (Prim).</summary>
    public MeshRoutePath? MstRoute { get; set; }

    /// <summary>The cheapest route if nothing had failed. Compared with <see cref="Cheapest"/> to show a reroute.</summary>
    public MeshRoutePath? Nominal { get; set; }

    /// <summary>Down nodes that sit on the nominal route (empty when the route is unaffected).</summary>
    public List<string> BlockedBy { get; set; } = new();

    public bool Rerouted { get; set; }

    /// <summary>Extra power the current cheapest route costs compared with the nominal one.</summary>
    public double RerouteExtraMilliwatts { get; set; }

    /// <summary>Extra power the MST route costs compared with the cheapest route (0 if they match).</summary>
    public double MstExtraMilliwatts { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class MeshDownNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class MeshConnectivityInfo
{
    /// <summary>Nodes that can reach the gateway (the gateway itself first), in depth-first order.</summary>
    public List<string> Connected { get; set; } = new();

    /// <summary>Groups of live nodes that can talk to each other but are cut off from the gateway.</summary>
    public List<List<string>> Islands { get; set; } = new();

    public List<MeshDownNode> Down { get; set; } = new();
}

public sealed class MeshFailureResult
{
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>False if the node was already in the requested state.</summary>
    public bool Changed { get; set; }

    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int UpCount { get; set; }
    public int DownCount { get; set; }
    public int IsolatedCount { get; set; }
}
