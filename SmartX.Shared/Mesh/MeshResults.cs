//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Mesh;

/// <summary>One undirected radio link between two mesh nodes and what it costs to send a packet over it.</summary>
public sealed record MeshEdge(string From, string To, LinkCost Cost);

/// <summary>A route through the mesh: the nodes in order (source first) and the summed link cost.</summary>
public sealed record RouteResult(IReadOnlyList<string> Path, LinkCost TotalCost)
{
    public int Hops => Path.Count - 1;
}

/// <summary>Output of a breadth-first search: visit order, hops from the start node, and each node's parent on its shortest-hop path.</summary>
public sealed record TraversalResult(
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, int> Hops,
    IReadOnlyDictionary<string, string> Parent);

/// <summary>
/// Which live nodes can still reach the gateway, and which are cut off in groups of their own ("islands").
/// Down nodes are listed separately; they belong to neither group.
/// </summary>
public sealed record ConnectivityReport(
    IReadOnlyList<string> Connected,
    IReadOnlyList<IReadOnlyList<string>> Islands,
    IReadOnlyList<string> DownNodes);

/// <summary>
/// A minimum spanning tree rooted at the gateway: the cheapest set of links that still joins every reachable node.
/// <see cref="Parent"/> maps each node to the next hop towards the gateway, so a node's route to the server is
/// found by following parents (see <see cref="PathToRoot"/>).
/// </summary>
public sealed class MstResult
{
    private readonly StringComparer _comparer;

    public MstResult(
        string root,
        IReadOnlyList<MeshEdge> edges,
        LinkCost totalCost,
        IReadOnlyDictionary<string, string> parent,
        IReadOnlyList<string> unreached,
        StringComparer comparer)
    {
        Root = root;
        Edges = edges;
        TotalCost = totalCost;
        Parent = parent;
        Unreached = unreached;
        _comparer = comparer;
    }

    public string Root { get; }

    public IReadOnlyList<MeshEdge> Edges { get; }

    public LinkCost TotalCost { get; }

    /// <summary>Child node to parent node (the next hop towards the root). The root itself has no entry.</summary>
    public IReadOnlyDictionary<string, string> Parent { get; }

    /// <summary>Live nodes that no path from the gateway reaches, so the tree could not include them.</summary>
    public IReadOnlyList<string> Unreached { get; }

    /// <summary>True if the link between the two nodes is one of the tree's edges (in either direction).</summary>
    public bool IsTreeEdge(string a, string b) =>
        (Parent.TryGetValue(a, out var parentOfA) && _comparer.Equals(parentOfA, b))
        || (Parent.TryGetValue(b, out var parentOfB) && _comparer.Equals(parentOfB, a));

    /// <summary>
    /// The route from <paramref name="node"/> up to the gateway along tree edges, node first, root last.
    /// Returns null if the node is not in the tree.
    /// </summary>
    public IReadOnlyList<string>? PathToRoot(string node)
    {
        if (!_comparer.Equals(node, Root) && !Parent.ContainsKey(node))
        {
            return null;
        }

        var path = new List<string>();
        BuildPath(node, path);
        return path;
    }

    // Recursive: add this node, then follow its parent until the root (which has no parent).
    private void BuildPath(string node, List<string> path)
    {
        path.Add(node);

        if (Parent.TryGetValue(node, out var next))
        {
            BuildPath(next, path);
        }
    }
}
