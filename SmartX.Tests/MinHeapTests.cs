using SmartX.Shared.Mesh;
using SmartX.Shared.Structures;

namespace SmartX.Tests;

/// <summary>Covers the binary min-heap behind Dijkstra and Prim.</summary>
public class MinHeapTests
{
    [Fact]
    public void Dequeue_ReturnsEntriesInAscendingPriorityOrder()
    {
        var heap = new MinHeap<int, int>();
        var random = new Random(5);
        var values = Enumerable.Range(0, 500).Select(_ => random.Next(0, 10_000)).ToList();

        foreach (int value in values)
        {
            heap.Enqueue(value, value);
        }

        var dequeued = new List<int>();
        while (heap.TryDequeue(out _, out int priority))
        {
            dequeued.Add(priority);
        }

        Assert.Equal(values.OrderBy(v => v).ToList(), dequeued);
    }

    [Fact]
    public void EmptyHeap_CannotPeekOrDequeue()
    {
        var heap = new MinHeap<string, int>();

        Assert.True(heap.IsEmpty);
        Assert.False(heap.TryPeek(out _, out _));
        Assert.False(heap.TryDequeue(out _, out _));
    }

    [Fact]
    public void Peek_ShowsTheSmallestWithoutRemovingIt()
    {
        var heap = new MinHeap<string, int>();
        heap.Enqueue("b", 2);
        heap.Enqueue("a", 1);
        heap.Enqueue("c", 3);

        Assert.True(heap.TryPeek(out string? element, out int priority));
        Assert.Equal("a", element);
        Assert.Equal(1, priority);
        Assert.Equal(3, heap.Count);
    }

    [Fact]
    public void Heap_GrowsPastItsInitialCapacity()
    {
        var heap = new MinHeap<int, int>(initialCapacity: 1);

        for (int i = 100; i > 0; i--)
        {
            heap.Enqueue(i, i);
        }

        Assert.Equal(100, heap.Count);
        Assert.True(heap.TryDequeue(out int smallest, out _));
        Assert.Equal(1, smallest);
    }

    [Fact]
    public void EqualPriorities_AreAllReturned()
    {
        var heap = new MinHeap<string, int>();
        heap.Enqueue("a", 1);
        heap.Enqueue("b", 1);
        heap.Enqueue("c", 1);

        var seen = new List<string>();
        while (heap.TryDequeue(out string? element, out int priority))
        {
            Assert.Equal(1, priority);
            seen.Add(element!);
        }

        Assert.Equal(new[] { "a", "b", "c" }, seen.OrderBy(s => s));
    }

    [Fact]
    public void WorksWithLinkCostPriorities()
    {
        var heap = new MinHeap<string, LinkCost>();
        heap.Enqueue("far", new LinkCost(9));
        heap.Enqueue("near", new LinkCost(1));
        heap.Enqueue("middle", new LinkCost(4));

        Assert.True(heap.TryDequeue(out string? first, out LinkCost cost));
        Assert.Equal("near", first);
        Assert.Equal(new LinkCost(1), cost);
    }

    [Fact]
    public void InterleavedOperations_MatchTheBuiltInPriorityQueue()
    {
        var heap = new MinHeap<int, int>();
        var oracle = new PriorityQueue<int, int>();
        var random = new Random(21);

        for (int step = 0; step < 2000; step++)
        {
            if (oracle.Count > 0 && random.Next(3) == 0)
            {
                oracle.TryDequeue(out _, out int expected);
                Assert.True(heap.TryDequeue(out _, out int actual));
                Assert.Equal(expected, actual);
            }
            else
            {
                int priority = random.Next(1000);
                oracle.Enqueue(priority, priority);
                heap.Enqueue(priority, priority);
            }

            Assert.Equal(oracle.Count, heap.Count);
        }
    }

    [Fact]
    public void Clear_EmptiesTheHeap()
    {
        var heap = new MinHeap<int, int>();
        heap.Enqueue(1, 1);
        heap.Enqueue(2, 2);

        heap.Clear();

        Assert.True(heap.IsEmpty);
        Assert.False(heap.TryDequeue(out _, out _));
    }
}
