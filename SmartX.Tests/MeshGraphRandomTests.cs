using SmartX.Shared.Mesh;

namespace SmartX.Tests;

/// <summary>
/// Randomised cross-checks that do not depend on hand-picked examples: Prim must match Kruskal, and Dijkstra
/// must match an independent Floyd-Warshall shortest-path calculation, on many random meshes with random failures.
/// </summary>
public class MeshGraphRandomTests
{
    private static string Id(int index) => $"N{index}";

    private static MeshGraph RandomMesh(Random random, int nodes, int extraLinks, double downChance)
    {
        var graph = new MeshGraph(Id(0));

        // A random spanning tree first, so the mesh starts out connected.
        for (int i = 1; i < nodes; i++)
        {
            graph.AddEdge(Id(random.Next(i)), Id(i), new LinkCost(random.Next(1, 51)));
        }

        for (int extra = 0; extra < extraLinks; extra++)
        {
            int a = random.Next(nodes);
            int b = random.Next(nodes);
            if (a != b)
            {
                graph.AddEdge(Id(a), Id(b), new LinkCost(random.Next(1, 51)));
            }
        }

        for (int i = 1; i < nodes; i++)
        {
            if (random.NextDouble() < downChance)
            {
                graph.SetNodeDown(Id(i), true);
            }
        }

        return graph;
    }

    [Fact]
    public void Prim_AndKruskal_ReachTheSameTotalCost_OnConnectedMeshes()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var random = new Random(seed);
            int nodes = random.Next(5, 41);
            var graph = RandomMesh(random, nodes, extraLinks: nodes * 2, downChance: 0);

            var prim = graph.MinimumSpanningTree();
            var kruskal = graph.MinimumSpanningTreeKruskal();

            Assert.Equal(prim.TotalCost, kruskal.TotalCost);
            Assert.Equal(nodes - 1, prim.Edges.Count);
            Assert.Equal(nodes - 1, kruskal.Edges.Count);
            Assert.Empty(prim.Unreached);
            Assert.Empty(kruskal.Unreached);
        }
    }

    [Fact]
    public void Prim_AndKruskal_StillAgree_WhenRandomNodesFail()
    {
        for (int seed = 100; seed < 150; seed++)
        {
            var random = new Random(seed);
            int nodes = random.Next(6, 41);
            var graph = RandomMesh(random, nodes, extraLinks: nodes * 2, downChance: 0.25);

            var prim = graph.MinimumSpanningTree();
            var kruskal = graph.MinimumSpanningTreeKruskal();

            Assert.Equal(prim.TotalCost, kruskal.TotalCost);
            Assert.Equal(prim.Edges.Count, kruskal.Edges.Count);
            Assert.Equal(prim.Unreached.OrderBy(n => n), kruskal.Unreached.OrderBy(n => n));
        }
    }

    [Fact]
    public void Dijkstra_MatchesFloydWarshall_OnRandomMeshesWithFailures()
    {
        for (int seed = 200; seed < 230; seed++)
        {
            var random = new Random(seed);
            const int nodes = 12;
            var graph = RandomMesh(random, nodes, extraLinks: 14, downChance: 0.2);

            double[,] expected = FloydWarshall(graph, nodes);

            for (int i = 0; i < nodes; i++)
            {
                for (int j = 0; j < nodes; j++)
                {
                    if (!graph.IsAlive(Id(i)) || !graph.IsAlive(Id(j)))
                    {
                        continue;
                    }

                    var route = graph.ShortestPath(Id(i), Id(j));

                    if (double.IsPositiveInfinity(expected[i, j]))
                    {
                        Assert.Null(route);
                    }
                    else
                    {
                        Assert.NotNull(route);
                        Assert.Equal(expected[i, j], route!.TotalCost.Milliwatts);

                        // The reported path must really cost what the route says it does.
                        Assert.Equal(route.TotalCost, graph.PathCost(route.Path));
                    }
                }
            }
        }
    }

    // Independent all-pairs shortest paths over the live part of the graph, used as the test oracle.
    private static double[,] FloydWarshall(MeshGraph graph, int nodes)
    {
        var dist = new double[nodes, nodes];

        for (int i = 0; i < nodes; i++)
        {
            for (int j = 0; j < nodes; j++)
            {
                dist[i, j] = i == j ? 0 : double.PositiveInfinity;
            }
        }

        foreach (var edge in graph.Edges())
        {
            if (!graph.IsAlive(edge.From) || !graph.IsAlive(edge.To))
            {
                continue;
            }

            int a = int.Parse(edge.From.Substring(1));
            int b = int.Parse(edge.To.Substring(1));
            dist[a, b] = Math.Min(dist[a, b], edge.Cost.Milliwatts);
            dist[b, a] = Math.Min(dist[b, a], edge.Cost.Milliwatts);
        }

        for (int k = 0; k < nodes; k++)
        {
            for (int i = 0; i < nodes; i++)
            {
                for (int j = 0; j < nodes; j++)
                {
                    if (dist[i, k] + dist[k, j] < dist[i, j])
                    {
                        dist[i, j] = dist[i, k] + dist[k, j];
                    }
                }
            }
        }

        return dist;
    }
}
