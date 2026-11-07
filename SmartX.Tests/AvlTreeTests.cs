using SmartX.Shared.Structures;

namespace SmartX.Tests;

/// <summary>Covers the AVL tree used for historical telemetry range queries.</summary>
public class AvlTreeTests
{
    private static int MaxAvlHeight(int count) => (int)Math.Ceiling(1.4405 * Math.Log2(count + 2));

    private static HashSet<int> RandomKeys(int seed, int count, int maxExclusive)
    {
        var random = new Random(seed);
        var keys = new HashSet<int>();
        while (keys.Count < count)
        {
            keys.Add(random.Next(0, maxExclusive));
        }

        return keys;
    }

    [Fact]
    public void Insert_AscendingKeys_StaysBalanced()
    {
        // Timestamps arrive in ascending order, the worst case for an unbalanced tree.
        var tree = new AvlTree<int, int>();
        const int n = 1024;

        for (int i = 0; i < n; i++)
        {
            tree.Insert(i, i * 2);
        }

        Assert.Equal(n, tree.Count);
        Assert.True(tree.ValidateInvariants());
        Assert.True(tree.Height <= MaxAvlHeight(n), $"height {tree.Height} is too tall for {n} nodes");
    }

    [Fact]
    public void Insert_DescendingKeys_StaysBalanced()
    {
        var tree = new AvlTree<int, int>();
        const int n = 1000;

        for (int i = n; i > 0; i--)
        {
            tree.Insert(i, i);
        }

        Assert.True(tree.ValidateInvariants());
        Assert.True(tree.Height <= MaxAvlHeight(n));
    }

    [Fact]
    public void Insert_RandomKeys_KeepsInvariantsAfterEveryInsert()
    {
        var tree = new AvlTree<int, int>();

        foreach (int key in RandomKeys(seed: 11, count: 400, maxExclusive: 10_000))
        {
            tree.Insert(key, key);
            Assert.True(tree.ValidateInvariants());
        }
    }

    [Fact]
    public void Insert_ExistingKey_ReplacesTheValueWithoutGrowing()
    {
        var tree = new AvlTree<string, int>();
        tree.Insert("a", 1);
        tree.Insert("a", 2);

        Assert.Equal(1, tree.Count);
        Assert.True(tree.TryGetValue("a", out int value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void TryGetValue_MissingKey_ReturnsFalse()
    {
        var tree = new AvlTree<int, string>();
        tree.Insert(1, "one");

        Assert.False(tree.TryGetValue(2, out _));
        Assert.False(tree.ContainsKey(2));
        Assert.True(tree.ContainsKey(1));
    }

    [Fact]
    public void TryGetMinAndMax_ReturnTheExtremeKeys()
    {
        var tree = new AvlTree<int, string>();
        foreach (int key in new[] { 5, 1, 9, 3, 7 })
        {
            tree.Insert(key, $"v{key}");
        }

        Assert.True(tree.TryGetMin(out int min, out string? minValue));
        Assert.Equal(1, min);
        Assert.Equal("v1", minValue);

        Assert.True(tree.TryGetMax(out int max, out string? maxValue));
        Assert.Equal(9, max);
        Assert.Equal("v9", maxValue);
    }

    [Fact]
    public void EmptyTree_HasNoMinMaxOrRangeResults()
    {
        var tree = new AvlTree<int, int>();

        Assert.True(tree.IsEmpty);
        Assert.Equal(0, tree.Height);
        Assert.False(tree.TryGetMin(out _, out _));
        Assert.False(tree.TryGetMax(out _, out _));
        Assert.Empty(tree.Range(0, 100));
        Assert.True(tree.ValidateInvariants());
    }

    [Fact]
    public void Remove_KeepsTreeValidAndRemovesOnlyTheRequestedKeys()
    {
        var tree = new AvlTree<int, int>();
        var keys = RandomKeys(seed: 7, count: 500, maxExclusive: 20_000).ToList();

        foreach (int key in keys)
        {
            tree.Insert(key, key);
        }

        var removed = keys.Where((_, index) => index % 2 == 0).ToList();
        var kept = keys.Where((_, index) => index % 2 != 0).ToList();

        foreach (int key in removed)
        {
            Assert.True(tree.Remove(key));
            Assert.True(tree.ValidateInvariants());
        }

        Assert.Equal(kept.Count, tree.Count);
        Assert.All(removed, key => Assert.False(tree.ContainsKey(key)));
        Assert.All(kept, key => Assert.True(tree.ContainsKey(key)));
        Assert.False(tree.Remove(removed[0]));
    }

    [Fact]
    public void Remove_EveryKey_LeavesAnEmptyTree()
    {
        var tree = new AvlTree<int, int>();
        for (int i = 0; i < 200; i++)
        {
            tree.Insert(i, i);
        }

        for (int i = 0; i < 200; i++)
        {
            Assert.True(tree.Remove(i));
        }

        Assert.True(tree.IsEmpty);
        Assert.Equal(0, tree.Count);
        Assert.True(tree.ValidateInvariants());
    }

    [Fact]
    public void InOrder_ReturnsKeysInAscendingOrder_WhateverTheInsertOrder()
    {
        var tree = new AvlTree<int, int>();
        var keys = RandomKeys(seed: 3, count: 300, maxExclusive: 5_000).ToList();

        foreach (int key in keys)
        {
            tree.Insert(key, key);
        }

        Assert.Equal(keys.OrderBy(k => k).ToList(), tree.InOrder().Select(pair => pair.Key).ToList());
    }

    [Fact]
    public void Range_MatchesABruteForceFilter_OnRandomData()
    {
        var tree = new AvlTree<int, int>();
        var keys = RandomKeys(seed: 42, count: 3000, maxExclusive: 50_000).OrderBy(k => k).ToList();

        foreach (int key in keys)
        {
            tree.Insert(key, key * 10);
        }

        var random = new Random(99);
        for (int query = 0; query < 100; query++)
        {
            int a = random.Next(-100, 50_100);
            int b = random.Next(-100, 50_100);
            int low = Math.Min(a, b);
            int high = Math.Max(a, b);

            var expected = keys.Where(k => k >= low && k <= high).ToList();
            var actual = tree.Range(low, high);

            Assert.Equal(expected, actual.Select(pair => pair.Key).ToList());
            Assert.All(actual, pair => Assert.Equal(pair.Key * 10, pair.Value));
        }
    }

    [Fact]
    public void Range_IncludesBothEndpoints()
    {
        var tree = new AvlTree<int, int>();
        for (int i = 1; i <= 10; i++)
        {
            tree.Insert(i, i);
        }

        Assert.Equal(new[] { 3, 4, 5, 6 }, tree.Range(3, 6).Select(pair => pair.Key));
        Assert.Equal(new[] { 4 }, tree.Range(4, 4).Select(pair => pair.Key));
    }

    [Fact]
    public void Range_WithLowAboveHigh_ReturnsNothing()
    {
        var tree = new AvlTree<int, int>();
        for (int i = 1; i <= 10; i++)
        {
            tree.Insert(i, i);
        }

        Assert.Empty(tree.Range(8, 3));
    }

    [Fact]
    public void Range_OnANarrowWindow_VisitsFarFewerNodesThanTheTreeHolds()
    {
        var tree = new AvlTree<int, int>();
        const int n = 20_000;

        for (int i = 0; i < n; i++)
        {
            tree.Insert(i, i);
        }

        var results = tree.Range(10_000, 10_009, out int visited);

        Assert.Equal(10, results.Count);
        Assert.True(visited < 100, $"visited {visited} nodes, expected well under 100 of {n}");
    }

    [Fact]
    public void WorksWithDateTimeKeys_LikeTelemetryTimestamps()
    {
        var tree = new AvlTree<DateTime, float>();
        var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

        for (int minute = 0; minute < 240; minute++)
        {
            tree.Insert(start.AddMinutes(minute), minute);
        }

        var window = tree.Range(start.AddMinutes(60), start.AddMinutes(119));

        Assert.Equal(60, window.Count);
        Assert.Equal(60f, window[0].Value);
        Assert.Equal(119f, window[^1].Value);
    }
}
