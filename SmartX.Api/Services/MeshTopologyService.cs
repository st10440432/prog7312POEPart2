//ST10440432
//Matteo Nusca
using SmartX.Shared.Mesh;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Keeps the sensor mesh as a weighted graph and answers routing questions about it.
/// Sensors are linked automatically as they register: each new node connects to the three cheapest nodes already
/// in the mesh, and link costs come from where the devices are deployed (same zone is cheap, another building is
/// expensive) plus a small fixed offset so the costs are never all identical. The same sensors always give the
/// same mesh. A node counts as down if an operator failed it or if the watchdog has flagged it as disconnected.
/// </summary>
public sealed class MeshTopologyService
{
    public const string GatewayId = "GATEWAY";
    private const int LinksPerNewNode = 3;

    private readonly DeviceRegistry _registry;
    private readonly AlertTracker _alerts;
    private readonly MeshGraph _graph = new(GatewayId);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SensorRegistration> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _gatewayLocation;

    public MeshTopologyService(DeviceRegistry registry, AlertTracker alerts,
        Microsoft.Extensions.Options.IOptions<GatewayOptions> options)
    {
        _registry = registry;
        _alerts = alerts;
        _gatewayLocation = string.IsNullOrWhiteSpace(options.Value.GatewayLocation)
            ? "Utility Room" : options.Value.GatewayLocation.Trim();
    }

    // ---------- link costs ----------

    /// <summary>Stable 32-bit FNV-1a hash (string.GetHashCode changes between runs, which would change the mesh).</summary>
    public static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    private static string Segment(string location, int index)
    {
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return index < parts.Length ? parts[index] : string.Empty;
    }

    /// <summary>Power cost in mW of a link between two deployment locations; the ids only add the fixed offset.</summary>
    public static double LinkCostFor(string idA, string locationA, string idB, string locationB)
    {
        double baseCost;
        if (Segment(locationA, 0).Equals(Segment(locationB, 0), StringComparison.OrdinalIgnoreCase))
        {
            baseCost = Segment(locationA, 1).Equals(Segment(locationB, 1), StringComparison.OrdinalIgnoreCase) ? 3 : 6;
        }
        else
        {
            baseCost = 12;
        }

        //Order the ids so the cost is the same whichever way round the link is built
        string first = string.Compare(idA, idB, StringComparison.OrdinalIgnoreCase) <= 0 ? idA : idB;
        string second = ReferenceEquals(first, idA) ? idB : idA;
        double offset = StableHash(first.ToUpperInvariant() + "|" + second.ToUpperInvariant()) % 5;
        return baseCost + offset;
    }

    // ---------- graph upkeep ----------

    //Caller must hold _gate. Adds nodes for any sensors registered since last time, then refreshes failure flags.
    private void Sync()
    {
        foreach (var sensor in _registry.Sensors())
        {
            if (_known.ContainsKey(sensor.DeviceId)) continue;

            var candidates = new List<(string Id, double Cost)>();
            candidates.Add((GatewayId, LinkCostFor(sensor.DeviceId, sensor.DeploymentLocation, GatewayId, _gatewayLocation)));
            foreach (var (id, other) in _known)
            {
                candidates.Add((id, LinkCostFor(sensor.DeviceId, sensor.DeploymentLocation, id, other.DeploymentLocation)));
            }

            _graph.AddNode(sensor.DeviceId);
            foreach (var pick in candidates.OrderBy(c => c.Cost).ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Take(LinksPerNewNode))
            {
                _graph.AddEdge(sensor.DeviceId, pick.Id, new LinkCost(pick.Cost));
            }

            _known[sensor.DeviceId] = sensor;
        }

        foreach (var id in _known.Keys)
        {
            bool down = _failed.Contains(id) || _alerts.IsActive(id, AlertTracker.Disconnected);
            _graph.SetNodeDown(id, down);
        }
    }

    private string NameOf(string id) =>
        id.Equals(GatewayId, StringComparison.OrdinalIgnoreCase) ? "Gateway"
        : _known.TryGetValue(id, out var s) ? s.DisplayName : id;

    private MeshRoutePath? ToPath(RouteResult? route) => route is null ? null : new MeshRoutePath
    {
        NodeIds = route.Path.ToList(),
        NodeNames = route.Path.Select(NameOf).ToList(),
        Hops = route.Hops,
        CostMilliwatts = route.TotalCost.Milliwatts
    };

    private string DownReason(string id) =>
        _failed.Contains(id) ? "Manual" : "Disconnected";

    // ---------- queries ----------

    public MeshSnapshot Snapshot()
    {
        lock (_gate)
        {
            Sync();

            var bfs = _graph.Bfs();
            var mst = _graph.MinimumSpanningTree();
            var activeAlerts = _alerts.Snapshot().Select(a => a.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var snapshot = new MeshSnapshot { GatewayId = GatewayId, GeneratedUtc = DateTime.UtcNow };

            snapshot.Nodes.Add(new MeshNodeInfo
            {
                Id = GatewayId, Name = "Gateway", Kind = "Gateway", Location = _gatewayLocation,
                Status = "Up", HopsToGateway = 0
            });

            foreach (var sensor in _registry.Sensors())
            {
                string id = sensor.DeviceId;
                var node = new MeshNodeInfo
                {
                    Id = id, Name = sensor.DisplayName, Kind = "Sensor", Category = sensor.Category,
                    Location = sensor.DeploymentLocation, HasActiveAlert = activeAlerts.Contains(id)
                };

                if (_graph.IsDown(id))
                {
                    node.Status = "Down";
                    node.DownReason = DownReason(id);
                    snapshot.DownCount++;
                }
                else if (bfs.Hops.TryGetValue(id, out int hops))
                {
                    node.Status = "Up";
                    node.HopsToGateway = hops;
                    snapshot.UpCount++;
                }
                else
                {
                    node.Status = "Isolated";
                    snapshot.IsolatedCount++;
                }

                if (mst.Parent.TryGetValue(id, out var parent)) node.MstParentId = parent;
                snapshot.Nodes.Add(node);
            }

            foreach (var edge in _graph.Edges())
            {
                snapshot.Links.Add(new MeshLinkInfo
                {
                    From = edge.From, To = edge.To, CostMilliwatts = edge.Cost.Milliwatts,
                    InMst = mst.IsTreeEdge(edge.From, edge.To)
                });
            }

            snapshot.MstLinkCount = mst.Edges.Count;
            snapshot.MstTotalMilliwatts = mst.TotalCost.Milliwatts;
            snapshot.AllLinksTotalMilliwatts = _graph.Edges().Sum(e => e.Cost.Milliwatts);
            return snapshot;
        }
    }

    public MeshRouteReport? Route(string deviceId)
    {
        lock (_gate)
        {
            Sync();

            var sensor = _registry.FindSensor(deviceId);
            if (sensor is null || !_known.ContainsKey(sensor.DeviceId)) return null;
            string id = sensor.DeviceId;

            var report = new MeshRouteReport { DeviceId = id, DeviceName = sensor.DisplayName };

            var nominal = _graph.ShortestPath(id, GatewayId, ignoreFailures: true);
            report.Nominal = ToPath(nominal);

            if (_graph.IsDown(id))
            {
                report.Status = "Down";
                report.Message = $"{sensor.DisplayName} is down ({DownReason(id)}), so it has no route to the gateway.";
                return report;
            }

            var cheapest = _graph.ShortestPath(id, GatewayId);
            if (cheapest is null)
            {
                report.Status = "Isolated";
                report.Message = $"{sensor.DisplayName} is up but every path to the gateway runs through a failed node.";
                if (nominal is not null)
                {
                    report.BlockedBy = nominal.Path.Where(n => _graph.IsDown(n)).ToList();
                }

                return report;
            }

            report.Status = "Up";
            report.Reachable = true;
            report.Cheapest = ToPath(cheapest);

            //Fewest hops: breadth-first search from the gateway, then follow parents back from the device
            var bfs = _graph.Bfs();
            var hopPath = new List<string> { id };
            while (bfs.Parent.TryGetValue(hopPath[^1], out var next))
            {
                hopPath.Add(next);
            }

            var hopCost = _graph.PathCost(hopPath);
            report.FewestHops = new MeshRoutePath
            {
                NodeIds = hopPath,
                NodeNames = hopPath.Select(NameOf).ToList(),
                Hops = hopPath.Count - 1,
                CostMilliwatts = hopCost?.Milliwatts ?? 0
            };

            var mst = _graph.MinimumSpanningTree();
            var mstPath = mst.PathToRoot(id);
            if (mstPath is not null)
            {
                var mstCost = _graph.PathCost(mstPath);
                report.MstRoute = new MeshRoutePath
                {
                    NodeIds = mstPath.ToList(),
                    NodeNames = mstPath.Select(NameOf).ToList(),
                    Hops = mstPath.Count - 1,
                    CostMilliwatts = mstCost?.Milliwatts ?? 0
                };
                report.MstExtraMilliwatts = Math.Max(0, report.MstRoute.CostMilliwatts - cheapest.TotalCost.Milliwatts);
            }

            if (nominal is not null)
            {
                report.BlockedBy = nominal.Path.Where(n => _graph.IsDown(n)).ToList();
                report.Rerouted = report.BlockedBy.Count > 0;
                if (report.Rerouted)
                {
                    report.RerouteExtraMilliwatts = Math.Max(0, cheapest.TotalCost.Milliwatts - nominal.TotalCost.Milliwatts);
                }
            }

            report.Message = report.Rerouted
                ? $"Rerouted around {string.Join(", ", report.BlockedBy.Select(NameOf))}: {cheapest.Hops} hops, {cheapest.TotalCost}."
                : $"{cheapest.Hops} hops to the gateway, {cheapest.TotalCost}.";
            return report;
        }
    }

    public MeshConnectivityInfo Connectivity()
    {
        lock (_gate)
        {
            Sync();

            var report = _graph.AnalyseConnectivity();
            return new MeshConnectivityInfo
            {
                Connected = report.Connected.ToList(),
                Islands = report.Islands.Select(i => i.ToList()).ToList(),
                Down = report.DownNodes.Select(id => new MeshDownNode
                {
                    Id = id, Name = NameOf(id), Reason = DownReason(id)
                }).ToList()
            };
        }
    }

    // ---------- failure simulation ----------

    /// <summary>Marks a node as failed (or restores it). Returns null for an unknown device.</summary>
    public MeshFailureResult? SetFailed(string deviceId, bool failed)
    {
        lock (_gate)
        {
            Sync();

            var sensor = _registry.FindSensor(deviceId);
            if (sensor is null || !_known.ContainsKey(sensor.DeviceId)) return null;

            bool changed = failed ? _failed.Add(sensor.DeviceId) : _failed.Remove(sensor.DeviceId);
            Sync();

            var snapshot = SnapshotCounts(sensor.DeviceId);
            snapshot.Changed = changed;
            snapshot.Message = (failed, changed) switch
            {
                (true, true) => $"{sensor.DisplayName} marked as failed.",
                (true, false) => $"{sensor.DisplayName} was already failed.",
                (false, true) => $"{sensor.DisplayName} restored.",
                _ => $"{sensor.DisplayName} was not failed."
            };
            return snapshot;
        }
    }

    //Caller must hold _gate and have synced
    private MeshFailureResult SnapshotCounts(string deviceId)
    {
        var bfs = _graph.Bfs();
        int up = 0, down = 0, isolated = 0;
        foreach (var id in _known.Keys)
        {
            if (_graph.IsDown(id)) down++;
            else if (bfs.Hops.ContainsKey(id)) up++;
            else isolated++;
        }

        string status = _graph.IsDown(deviceId) ? "Down" : bfs.Hops.ContainsKey(deviceId) ? "Up" : "Isolated";
        return new MeshFailureResult { DeviceId = deviceId, Status = status, UpCount = up, DownCount = down, IsolatedCount = isolated };
    }
}
