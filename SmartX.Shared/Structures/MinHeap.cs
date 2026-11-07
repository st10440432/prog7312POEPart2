//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Structures;

/// <summary>
/// A binary min-heap priority queue stored in a flat array: the element with the smallest priority is always at index 0,
/// and a node at index i has its children at 2i+1 and 2i+2. Enqueue and dequeue are O(log n), peek is O(1).
/// The mesh routing code uses it so Dijkstra and Prim can always pull the cheapest link next without sorting.
/// Ties are not ordered in any particular way. Not thread-safe.
/// </summary>
public sealed class MinHeap<TElement, TPriority> where TPriority : IComparable<TPriority>
{
    private (TElement Element, TPriority Priority)[] _items;
    private int _count;

    public MinHeap(int initialCapacity = 16)
    {
        _items = new (TElement, TPriority)[Math.Max(1, initialCapacity)];
    }

    public int Count => _count;

    public bool IsEmpty => _count == 0;

    public void Enqueue(TElement element, TPriority priority)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count] = (element, priority);
        SiftUp(_count);
        _count++;
    }

    public bool TryPeek(out TElement element, out TPriority priority)
    {
        if (_count == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }

        element = _items[0].Element;
        priority = _items[0].Priority;
        return true;
    }

    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        if (_count == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }

        element = _items[0].Element;
        priority = _items[0].Priority;

        _count--;
        _items[0] = _items[_count];
        _items[_count] = default;

        if (_count > 0)
        {
            SiftDown(0);
        }

        return true;
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _count);
        _count = 0;
    }

    private void SiftUp(int index)
    {
        var item = _items[index];

        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (item.Priority.CompareTo(_items[parent].Priority) >= 0)
            {
                break;
            }

            _items[index] = _items[parent];
            index = parent;
        }

        _items[index] = item;
    }

    private void SiftDown(int index)
    {
        var item = _items[index];

        while (true)
        {
            int left = 2 * index + 1;
            if (left >= _count)
            {
                break;
            }

            int right = left + 1;
            int smallest = right < _count && _items[right].Priority.CompareTo(_items[left].Priority) < 0 ? right : left;

            if (_items[smallest].Priority.CompareTo(item.Priority) >= 0)
            {
                break;
            }

            _items[index] = _items[smallest];
            index = smallest;
        }

        _items[index] = item;
    }
}
