//ST10440432
//Matteo Nusca
using SmartX.Shared.Structures;

namespace SmartX.Shared.Mesh;

/// <summary>
/// The Smart-X mesh network as a weighted, undirected graph stored as an adjacency list: every node (a device, plus the
/// central gateway) maps to its neighbours and the <see cref="LinkCost"/> of each radio link. A node can be marked
/// "down" (silent or failed); every algorithm below treats a down node as if it had been removed from the graph,
/// which is how routes heal around a failure.
/// <para>
/// Algorithms: breadth-first search (hops, reachability), recursive depth-first search (islands), Dijkstra with a
/// <see cref="MinHeap{TElement,TPriority}"/> (cheapest route between two nodes), and Prim (heap) and Kruskal
/// (union-find) for the minimum spanning tree rooted at the gateway.
/// </para>
/// Node ids are matched case-insensitively. Not thread-safe; callers that share an instance must lock around it.
/// </summary>
public sealed class MeshGraph
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, Dictionary<string, LinkCost>> _adjacency = new(Comparer);
    private readonly HashSet<string> _down = new(Comparer);
    private int _edgeCount;

    public MeshGraph(string gatewayId)
    {
        RequireId(gatewayId);
        GatewayId = gatewayId.Trim();
        _adjacency[GatewayId] = new Dictionary<string, LinkCost>(Comparer);
    }

    /// <summary>The central node every telemetry packet is trying to reach. It can never be marked down.</summary>
    public string GatewayId { get; }

    public int NodeCount => _adjacency.Count;

    public int EdgeCount => _edgeCount;

    // ---------- building the graph ----------

    public IReadOnlyList<string> Nodes() => _adjacency.Keys.ToList();

    public IReadOnlyList<string> DownNodes() => _down.ToList();

    public bool ContainsNode(string id) => _adjacency.ContainsKey(id);

    /// <summary>Adds a node with no links. Returns false if it already exists.</summary>
    public bool AddNode(string id)
    {
        RequireId(id);

        if (_adjacency.ContainsKey(id))
        {
            return false;
        }

        _adjacency[id] = new Dictionary<string, LinkCost>(Comparer);
        return true;
    }

    /// <summary>
    /// Adds a link between two nodes (creating either node if missing), or updates its cost if the link exists.
    /// Returns true for a new link and false for an update.
    /// </summary>
    public bool AddEdge(string a, string b, LinkCost cost)
    {
        RequireId(a);
        RequireId(b);

        if (Comparer.Equals(a, b))
        {
            throw new ArgumentException("A node cannot link to itself.");
        }

        if (cost.IsUnreachable)
        {
            throw new ArgumentOutOfRangeException(nameof(cost), "A link cannot have an infinite cost.");
        }

        AddNode(a);
        AddNode(b);

        bool isNew = !_adjacency[a].ContainsKey(b);
        _adjacency[a][b] = cost;
        _adjacency[b][a] = cost;

        if (isNew)
        {
            _edgeCount++;
        }

        return isNew;
    }

    public bool RemoveEdge(string a, string b)
    {
        if (!_adjacency.TryGetValue(a, out var fromA) || !_adjacency.TryGetValue(b, out var fromB) || !fromA.ContainsKey(b))
        {
            return false;
        }

        fromA.Remove(b);
        fromB.Remove(a);
        _edgeCount--;
        return true;
    }

    public bool TryGetCost(string a, string b, out LinkCost cost)
    {
        cost = default;
        return _adjacency.TryGetValue(a, out var neighbours) && neighbours.TryGetValue(b, out cost);
    }

    public IReadOnlyList<(string Neighbor, LinkCost Cost)> NeighborsOf(string id)
    {
        var result = new List<(string Neighbor, LinkCost Cost)>();

        if (_adjacency.TryGetValue(id, out var neighbours))
        {
            foreach (var (neighbour, cost) in neighbours)
            {
                result.Add((neighbour, cost));
            }
        }

        return result;
    }

    /// <summary>Every link exactly once (an undirected link is stored under both of its nodes).</summary>
    public IReadOnlyList<MeshEdge> Edges()
    {
        var edges = new List<MeshEdge>();

        foreach (var (node, neighbours) in _adjacency)
        {
            foreach (var (other, cost) in neighbours)
            {
                if (string.Compare(node, other, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    edges.Add(new MeshEdge(node, other, cost));
                }
            }
        }

        return edges;
    }

    /// <summary>Sum of the link costs along a path, or null if any consecutive pair of nodes is not linked.</summary>
    public LinkCost? PathCost(IReadOnlyList<string> path)
    {
        LinkCost total = LinkCost.Zero;

        for (int i = 0; i + 1 < path.Count; i++)
        {
            if (!TryGetCost(path[i], path[i + 1], out var hopCost))
            {
                return null;
            }

            total += hopCost;
        }

        return total;
    }

    // ---------- node availability ----------

    /// <summary>
    /// Marks a node down or back up. Returns true only if its state changed; unknown nodes and the gateway are ignored.
    /// </summary>
    public bool SetNodeDown(string id, bool down)
    {
        if (!_adjacency.ContainsKey(id) || Comparer.Equals(id, GatewayId))
        {
            return false;
        }

        return down ? _down.Add(id) : _down.Remove(id);
    }

    public bool IsDown(string id) => _down.Contains(id);

    /// <summary>True if the node exists and is not down.</summary>
    public bool IsAlive(string id) => _adjacency.ContainsKey(id) && !_down.Contains(id);

    // ---------- breadth-first search ----------

    /// <summary>
    /// Breadth-first search from <paramref name="start"/> (the gateway by default) over live nodes.
    /// Gives each reachable node its hop count and its parent on a fewest-hops path. Nodes it never reaches are cut off.
    /// </summary>
    public TraversalResult Bfs(string? start = null)
    {
        string origin = start ?? GatewayId;
        var order = new List<string>();
        var hops = new Dictionary<string, int>(Comparer);
        var parent = new Dictionary<string, string>(Comparer);

        if (!IsAlive(origin))
        {
            return new TraversalResult(order, hops, parent);
        }

        var queue = new Queue<string>();
        hops[origin] = 0;
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            order.Add(node);

            foreach (string neighbour in _adjacency[node].Keys)
            {
                if (!IsAlive(neighbour) || hops.ContainsKey(neighbour))
                {
                    continue;
                }

                hops[neighbour] = hops[node] + 1;
                parent[neighbour] = node;
                queue.Enqueue(neighbour);
            }
        }

        return new TraversalResult(order, hops, parent);
    }

    // ---------- depth-first search ----------

    /// <summary>Depth-first (pre-order) visit order from <paramref name="start"/> (the gateway by default) over live nodes.</summary>
    public IReadOnlyList<string> Dfs(string? start = null)
    {
        string origin = start ?? GatewayId;
        var visited = new HashSet<string>(Comparer);
        var order = new List<string>();

        if (IsAlive(origin))
        {
            DfsVisit(origin, visited, order);
        }

        return order;
    }

    // Recursive: visit the node, then fully explore each unvisited live neighbour before moving on.
    private void DfsVisit(string node, HashSet<string> visited, List<string> order)
    {
        visited.Add(node);
        order.Add(node);

        foreach (string neighbour in _adjacency[node].Keys)
        {
            if (IsAlive(neighbour) && !visited.Contains(neighbour))
            {
                DfsVisit(neighbour, visited, order);
            }
        }
    }

    /// <summary>
    /// Splits the live nodes into the group that can still reach the gateway and "islands" that cannot.
    /// Each island is a set of live nodes that can talk to each other but are cut off from the gateway by down nodes
    /// or missing links.
    /// </summary>
    public ConnectivityReport AnalyseConnectivity()
    {
        var visited = new HashSet<string>(Comparer);

        var connected = new List<string>();
        DfsVisit(GatewayId, visited, connected);

        var islands = new List<IReadOnlyList<string>>();

        foreach (string node in _adjacency.Keys)
        {
            if (!IsAlive(node) || visited.Contains(node))
            {
                continue;
            }

            var island = new List<string>();
            DfsVisit(node, visited, island);
            islands.Add(island);
        }

        return new ConnectivityReport(connected, islands, _down.ToList());
    }

    // ---------- Dijkstra ----------

    /// <summary>
    /// The cheapest route between two live nodes in total power cost (Dijkstra with a min-heap), or null if
    /// either node is down or no route exists. With <paramref name="ignoreFailures"/> set, down nodes are treated as
    /// working, which gives the "nominal" route the mesh would use if nothing had failed.
    /// </summary>
    public RouteResult? ShortestPath(string source, string destination, bool ignoreFailures = false)
    {
        if (!Usable(source, ignoreFailures) || !Usable(destination, ignoreFailures))
        {
            return null;
        }

        var distance = new Dictionary<string, LinkCost>(Comparer);
        var previous = new Dictionary<string, string>(Comparer);
        var heap = new MinHeap<string, LinkCost>();

        distance[source] = LinkCost.Zero;
        heap.Enqueue(source, LinkCost.Zero);

        while (heap.TryDequeue(out string node, out LinkCost cost))
        {
            // The heap can hold outdated entries for a node; skip any that a cheaper route has already replaced.
            if (cost > distance[node])
            {
                continue;
            }

            if (Comparer.Equals(node, destination))
            {
                break;
            }

            foreach (var (neighbour, linkCost) in _adjacency[node])
            {
                if (!Usable(neighbour, ignoreFailures))
                {
                    continue;
                }

                LinkCost candidate = cost + linkCost;

                if (!distance.TryGetValue(neighbour, out LinkCost known) || candidate < known)
                {
                    distance[neighbour] = candidate;
                    previous[neighbour] = node;
                    heap.Enqueue(neighbour, candidate);
                }
            }
        }

        if (!distance.TryGetValue(destination, out LinkCost total))
        {
            return null;
        }

        var path = new List<string>();
        string? step = destination;

        while (step is not null)
        {
            path.Add(step);
            step = previous.TryGetValue(step, out string? before) ? before : null;
        }

        path.Reverse();
        return new RouteResult(path, total);
    }

    // ---------- minimum spanning tree ----------

    /// <summary>
    /// Prim's algorithm from the gateway: repeatedly take the cheapest link that reaches a node not yet in the tree
    /// (the min-heap holds the candidate links). Covers every live node the gateway can reach.
    /// </summary>
    public MstResult MinimumSpanningTree()
    {
        var inTree = new HashSet<string>(Comparer) { GatewayId };
        var parent = new Dictionary<string, string>(Comparer);
        var edges = new List<MeshEdge>();
        var heap = new MinHeap<(string Node, string Parent), LinkCost>();
        LinkCost total = LinkCost.Zero;

        PushLinks(GatewayId, inTree, heap);

        while (heap.TryDequeue(out var entry, out LinkCost cost))
        {
            if (inTree.Contains(entry.Node))
            {
                continue;
            }

            inTree.Add(entry.Node);
            parent[entry.Node] = entry.Parent;
            edges.Add(new MeshEdge(entry.Parent, entry.Node, cost));
            total += cost;

            PushLinks(entry.Node, inTree, heap);
        }

        return new MstResult(GatewayId, edges, total, parent, LiveNodesNotIn(inTree), Comparer);
    }

    private void PushLinks(string node, HashSet<string> inTree, MinHeap<(string Node, string Parent), LinkCost> heap)
    {
        foreach (var (neighbour, linkCost) in _adjacency[node])
        {
            if (IsAlive(neighbour) && !inTree.Contains(neighbour))
            {
                heap.Enqueue((neighbour, node), linkCost);
            }
        }
    }

    /// <summary>
    /// Kruskal's algorithm over the live nodes the gateway can reach: sort every link by cost and keep each one that
    /// does not close a loop (a union-find structure tracks which nodes are already joined). Produces a tree with the
    /// same total cost as <see cref="MinimumSpanningTree"/>, so the two cross-check each other.
    /// </summary>
    public MstResult MinimumSpanningTreeKruskal()
    {
        var reachable = new HashSet<string>(Bfs().Order, Comparer);

        var candidates = Edges()
            .Where(link => reachable.Contains(link.From) && reachable.Contains(link.To))
            .OrderBy(link => link.Cost)
            .ToList();

        var sets = new DisjointSet(reachable);
        var chosen = new List<MeshEdge>();
        LinkCost total = LinkCost.Zero;

        foreach (MeshEdge edge in candidates)
        {
            if (sets.Union(edge.From, edge.To))
            {
                chosen.Add(edge);
                total += edge.Cost;

                if (chosen.Count == reachable.Count - 1)
                {
                    break;
                }
            }
        }

        // Kruskal does not grow from a root, so orient the chosen links towards the gateway with a quick walk.
        var treeLinks = new Dictionary<string, List<string>>(Comparer);

        foreach (MeshEdge edge in chosen)
        {
            AddTreeLink(treeLinks, edge.From, edge.To);
            AddTreeLink(treeLinks, edge.To, edge.From);
        }

        var parent = new Dictionary<string, string>(Comparer);
        var seen = new HashSet<string>(Comparer) { GatewayId };
        var queue = new Queue<string>();
        queue.Enqueue(GatewayId);

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();

            if (!treeLinks.TryGetValue(node, out List<string>? children))
            {
                continue;
            }

            foreach (string child in children)
            {
                if (seen.Add(child))
                {
                    parent[child] = node;
                    queue.Enqueue(child);
                }
            }
        }

        return new MstResult(GatewayId, chosen, total, parent, LiveNodesNotIn(seen), Comparer);
    }

    private static void AddTreeLink(Dictionary<string, List<string>> links, string owner, string target)
    {
        if (!links.TryGetValue(owner, out List<string>? list))
        {
            list = new List<string>();
            links[owner] = list;
        }

        list.Add(target);
    }

    private List<string> LiveNodesNotIn(HashSet<string> included) =>
        _adjacency.Keys.Where(node => IsAlive(node) && !included.Contains(node)).ToList();

    // ---------- helpers ----------

    private bool Usable(string id, bool ignoreFailures) =>
        ignoreFailures ? _adjacency.ContainsKey(id) : IsAlive(id);

    private static void RequireId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A node id is required.", nameof(id));
        }
    }

    /// <summary>Union-find with path compression and union by rank, used by Kruskal to detect loops.</summary>
    private sealed class DisjointSet
    {
        private readonly Dictionary<string, string> _parent = new(Comparer);
        private readonly Dictionary<string, int> _rank = new(Comparer);

        public DisjointSet(IEnumerable<string> items)
        {
            foreach (string item in items)
            {
                _parent[item] = item;
                _rank[item] = 0;
            }
        }

        // Recursive: climb to the set's representative, pointing every node on the way straight at it.
        public string Find(string item)
        {
            string parent = _parent[item];

            if (Comparer.Equals(parent, item))
            {
                return item;
            }

            string root = Find(parent);
            _parent[item] = root;
            return root;
        }

        /// <summary>Joins the two sets. Returns false if they were already the same set (the link would close a loop).</summary>
        public bool Union(string a, string b)
        {
            string rootA = Find(a);
            string rootB = Find(b);

            if (Comparer.Equals(rootA, rootB))
            {
                return false;
            }

            if (_rank[rootA] < _rank[rootB])
            {
                (rootA, rootB) = (rootB, rootA);
            }

            _parent[rootB] = rootA;

            if (_rank[rootA] == _rank[rootB])
            {
                _rank[rootA]++;
            }

            return true;
        }
    }
}
