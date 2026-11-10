using SmartX.Shared.Mesh;

namespace SmartX.Tests;

/// <summary>
/// Known-answer tests on a small mesh. "A" is the gateway. The layout is the classic textbook Prim example, so the
/// expected results (minimum spanning tree cost 39, cheapest A to G route cost 22) can be checked by hand.
/// </summary>
public class MeshGraphTests
{
    private static MeshGraph Textbook()
    {
        var graph = new MeshGraph("A");
        graph.AddEdge("A", "B", new LinkCost(7));
        graph.AddEdge("A", "D", new LinkCost(5));
        graph.AddEdge("B", "C", new LinkCost(8));
        graph.AddEdge("B", "D", new LinkCost(9));
        graph.AddEdge("B", "E", new LinkCost(7));
        graph.AddEdge("C", "E", new LinkCost(5));
        graph.AddEdge("D", "E", new LinkCost(15));
        graph.AddEdge("D", "F", new LinkCost(6));
        graph.AddEdge("E", "F", new LinkCost(8));
        graph.AddEdge("E", "G", new LinkCost(9));
        graph.AddEdge("F", "G", new LinkCost(11));
        return graph;
    }

    // ---------- building ----------

    [Fact]
    public void Graph_CountsNodesAndEdges()
    {
        var graph = Textbook();

        Assert.Equal(7, graph.NodeCount);
        Assert.Equal(11, graph.EdgeCount);
        Assert.Equal(11, graph.Edges().Count);
    }

    [Fact]
    public void AddEdge_OnAnExistingLink_UpdatesTheCostInBothDirections()
    {
        var graph = Textbook();

        bool isNew = graph.AddEdge("D", "A", new LinkCost(2));

        Assert.False(isNew);
        Assert.Equal(11, graph.EdgeCount);
        Assert.True(graph.TryGetCost("A", "D", out var forward));
        Assert.True(graph.TryGetCost("D", "A", out var backward));
        Assert.Equal(new LinkCost(2), forward);
        Assert.Equal(new LinkCost(2), backward);
    }

    [Fact]
    public void AddEdge_RejectsSelfLoopsAndInfiniteCosts()
    {
        var graph = new MeshGraph("A");

        Assert.Throws<ArgumentException>(() => graph.AddEdge("A", "a", new LinkCost(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.AddEdge("A", "B", LinkCost.Unreachable));
    }

    [Fact]
    public void NodeIds_AreCaseInsensitive()
    {
        var graph = Textbook();

        Assert.True(graph.ContainsNode("a"));
        Assert.False(graph.AddNode("b"));
        Assert.True(graph.TryGetCost("e", "g", out _));
    }

    [Fact]
    public void RemoveEdge_DropsTheLinkFromBothNodes()
    {
        var graph = Textbook();

        Assert.True(graph.RemoveEdge("D", "F"));
        Assert.False(graph.RemoveEdge("D", "F"));
        Assert.Equal(10, graph.EdgeCount);
        Assert.False(graph.TryGetCost("F", "D", out _));
    }

    [Fact]
    public void PathCost_SumsLinks_AndIsNullWhenNodesAreNotLinked()
    {
        var graph = Textbook();

        var cost = graph.PathCost(new[] { "A", "D", "F", "G" });

        Assert.NotNull(cost);
        Assert.Equal(22, cost!.Value.Milliwatts);
        Assert.Null(graph.PathCost(new[] { "A", "C" }));
    }

    // ---------- breadth-first and depth-first ----------

    [Fact]
    public void Bfs_ReportsFewestHopsFromTheGateway()
    {
        var result = Textbook().Bfs();

        Assert.Equal("A", result.Order[0]);
        Assert.Equal(7, result.Order.Count);
        Assert.Equal(0, result.Hops["A"]);
        Assert.Equal(1, result.Hops["B"]);
        Assert.Equal(1, result.Hops["D"]);
        Assert.Equal(2, result.Hops["C"]);
        Assert.Equal(2, result.Hops["E"]);
        Assert.Equal(2, result.Hops["F"]);
        Assert.Equal(3, result.Hops["G"]);
        Assert.Equal("A", result.Parent["D"]);
    }

    [Fact]
    public void Bfs_SkipsDownNodes()
    {
        var graph = Textbook();
        graph.SetNodeDown("D", true);

        var result = graph.Bfs();

        Assert.DoesNotContain("D", result.Order);
        Assert.Equal(3, result.Hops["F"]); // A-B-E-F now that D is gone
    }

    [Fact]
    public void Bfs_FromADownNode_ReturnsNothing()
    {
        var graph = Textbook();
        graph.SetNodeDown("B", true);

        Assert.Empty(graph.Bfs("B").Order);
    }

    [Fact]
    public void Dfs_VisitsEveryReachableNodeOnce_StartingAtTheGateway()
    {
        var order = Textbook().Dfs();

        Assert.Equal("A", order[0]);
        Assert.Equal(7, order.Count);
        Assert.Equal(7, order.Distinct().Count());
    }

    [Fact]
    public void AnalyseConnectivity_WithNoFailures_HasOneGroupAndNoIslands()
    {
        var report = Textbook().AnalyseConnectivity();

        Assert.Equal(7, report.Connected.Count);
        Assert.Empty(report.Islands);
        Assert.Empty(report.DownNodes);
    }

    [Fact]
    public void AnalyseConnectivity_FindsIslandsCutOffByDownNodes()
    {
        var graph = Textbook();
        graph.SetNodeDown("B", true);
        graph.SetNodeDown("D", true);

        var report = graph.AnalyseConnectivity();

        Assert.Equal(new[] { "A" }, report.Connected);
        var island = Assert.Single(report.Islands);
        Assert.Equal(new[] { "C", "E", "F", "G" }, island.OrderBy(n => n));
        Assert.Contains("B", report.DownNodes);
        Assert.Contains("D", report.DownNodes);
    }

    // ---------- node availability ----------

    [Fact]
    public void SetNodeDown_ReportsWhetherTheStateChanged_AndNeverDownsTheGateway()
    {
        var graph = Textbook();

        Assert.True(graph.SetNodeDown("D", true));
        Assert.False(graph.SetNodeDown("D", true));
        Assert.True(graph.IsDown("D"));
        Assert.False(graph.IsAlive("D"));

        Assert.True(graph.SetNodeDown("D", false));
        Assert.True(graph.IsAlive("D"));

        Assert.False(graph.SetNodeDown("A", true));
        Assert.False(graph.SetNodeDown("nope", true));
        Assert.True(graph.IsAlive("A"));
    }

    // ---------- Dijkstra ----------

    [Fact]
    public void ShortestPath_FindsTheCheapestRoute()
    {
        var route = Textbook().ShortestPath("A", "G");

        Assert.NotNull(route);
        Assert.Equal(new[] { "A", "D", "F", "G" }, route!.Path);
        Assert.Equal(22, route.TotalCost.Milliwatts);
        Assert.Equal(3, route.Hops);
    }

    [Fact]
    public void ShortestPath_FromANodeToItself_IsFree()
    {
        var route = Textbook().ShortestPath("C", "C");

        Assert.NotNull(route);
        Assert.Equal(new[] { "C" }, route!.Path);
        Assert.Equal(0, route.TotalCost.Milliwatts);
        Assert.Equal(0, route.Hops);
    }

    [Fact]
    public void ShortestPath_ReroutesAroundADownNode_AndRecoversWhenItReturns()
    {
        var graph = Textbook();

        graph.SetNodeDown("D", true);
        var detour = graph.ShortestPath("A", "G");
        Assert.NotNull(detour);
        Assert.Equal(new[] { "A", "B", "E", "G" }, detour!.Path);
        Assert.Equal(23, detour.TotalCost.Milliwatts);

        graph.SetNodeDown("D", false);
        var restored = graph.ShortestPath("A", "G");
        Assert.Equal(22, restored!.TotalCost.Milliwatts);
    }

    [Fact]
    public void ShortestPath_IsNullWhenNoRouteExists()
    {
        var graph = Textbook();
        graph.AddNode("Z");

        Assert.Null(graph.ShortestPath("A", "Z"));

        graph.SetNodeDown("E", true);
        Assert.Null(graph.ShortestPath("A", "E"));
        Assert.Null(graph.ShortestPath("E", "A"));
    }

    [Fact]
    public void ShortestPath_AfterRemovingALink_UsesTheNextBestRoute()
    {
        var graph = Textbook();
        graph.RemoveEdge("D", "F");

        var route = graph.ShortestPath("A", "G");

        Assert.Equal(new[] { "A", "B", "E", "G" }, route!.Path);
        Assert.Equal(23, route.TotalCost.Milliwatts);
    }

    // ---------- minimum spanning tree ----------

    [Fact]
    public void Prim_BuildsTheMinimumSpanningTree()
    {
        var mst = Textbook().MinimumSpanningTree();

        Assert.Equal(39, mst.TotalCost.Milliwatts);
        Assert.Equal(6, mst.Edges.Count);
        Assert.Empty(mst.Unreached);
        Assert.Equal("A", mst.Parent["D"]);
        Assert.Equal("A", mst.Parent["B"]);
        Assert.Equal("D", mst.Parent["F"]);
        Assert.Equal("B", mst.Parent["E"]);
        Assert.Equal("E", mst.Parent["C"]);
        Assert.Equal("E", mst.Parent["G"]);
    }

    [Fact]
    public void Kruskal_AgreesWithPrim()
    {
        var graph = Textbook();

        var prim = graph.MinimumSpanningTree();
        var kruskal = graph.MinimumSpanningTreeKruskal();

        Assert.Equal(prim.TotalCost, kruskal.TotalCost);
        Assert.Equal(prim.Edges.Count, kruskal.Edges.Count);
        Assert.Empty(kruskal.Unreached);
        Assert.Equal("E", kruskal.Parent["G"]);
    }

    [Fact]
    public void MstPathToRoot_FollowsTreeEdgesUpToTheGateway()
    {
        var graph = Textbook();
        var mst = graph.MinimumSpanningTree();

        Assert.Equal(new[] { "G", "E", "B", "A" }, mst.PathToRoot("G"));
        Assert.Equal(new[] { "A" }, mst.PathToRoot("A"));
        Assert.Null(mst.PathToRoot("nope"));
    }

    [Fact]
    public void MstRoute_IsNotAlwaysTheCheapestRoute()
    {
        // The tree minimises the cost of connecting everything, not the cost of each node's own route to the gateway.
        var graph = Textbook();
        var mst = graph.MinimumSpanningTree();

        var treeRoute = graph.PathCost(mst.PathToRoot("G")!);
        var cheapest = graph.ShortestPath("A", "G");

        Assert.NotNull(treeRoute);
        Assert.Equal(23, treeRoute!.Value.Milliwatts);
        Assert.Equal(22, cheapest!.TotalCost.Milliwatts);
        Assert.True(treeRoute.Value > cheapest.TotalCost);
    }

    [Fact]
    public void IsTreeEdge_WorksInEitherDirection()
    {
        var mst = Textbook().MinimumSpanningTree();

        Assert.True(mst.IsTreeEdge("A", "D"));
        Assert.True(mst.IsTreeEdge("D", "A"));
        Assert.False(mst.IsTreeEdge("B", "D"));
    }

    [Fact]
    public void Mst_IgnoresDownNodes_AndBothAlgorithmsStillAgree()
    {
        var graph = Textbook();
        graph.SetNodeDown("E", true);

        var prim = graph.MinimumSpanningTree();
        var kruskal = graph.MinimumSpanningTreeKruskal();

        Assert.Equal(37, prim.TotalCost.Milliwatts);
        Assert.Equal(prim.TotalCost, kruskal.TotalCost);
        Assert.Null(prim.PathToRoot("E"));
        Assert.Empty(prim.Unreached);
    }

    [Fact]
    public void Mst_ListsNodesTheGatewayCannotReach()
    {
        var graph = Textbook();
        graph.SetNodeDown("B", true);
        graph.SetNodeDown("D", true);

        var prim = graph.MinimumSpanningTree();
        var kruskal = graph.MinimumSpanningTreeKruskal();

        Assert.Empty(prim.Edges);
        Assert.Equal(0, prim.TotalCost.Milliwatts);
        Assert.Equal(new[] { "C", "E", "F", "G" }, prim.Unreached.OrderBy(n => n));
        Assert.Equal(new[] { "C", "E", "F", "G" }, kruskal.Unreached.OrderBy(n => n));
    }
}
