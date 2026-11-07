//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Structures;

/// <summary>
/// A self-balancing binary search tree (AVL tree) keyed by <typeparamref name="TKey"/>.
/// After every insert or delete the tree rotates itself so the heights of any node's two subtrees differ by at most one,
/// which keeps the height at O(log n). That makes lookups, inserts, deletes and range queries fast
/// no matter what order the keys arrive in (telemetry timestamps arrive in ascending order, which would turn a plain
/// binary search tree into a linked list).
/// <para>
/// Range queries are the reason this exists: <see cref="Range(TKey, TKey)"/> skips every subtree that cannot contain a match,
/// so it costs O(log n + k) for k results instead of O(n) for a scan of the whole history.
/// </para>
/// Not thread-safe; callers that share an instance must lock around it.
/// </summary>
public sealed class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public TKey Key;
        public TValue Value;
        public Node? Left;
        public Node? Right;
        public int Height = 1;

        public Node(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    private Node? _root;

    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    /// <summary>Height of the tree in nodes (0 for an empty tree, 1 for a single node).</summary>
    public int Height => HeightOf(_root);

    // ---------- insert ----------

    /// <summary>Adds the key, or replaces the value if the key is already present.</summary>
    public void Insert(TKey key, TValue value)
    {
        bool added = false;
        _root = Insert(_root, key, value, ref added);
        if (added)
        {
            Count++;
        }
    }

    private static Node Insert(Node? node, TKey key, TValue value, ref bool added)
    {
        if (node is null)
        {
            added = true;
            return new Node(key, value);
        }

        int cmp = key.CompareTo(node.Key);
        if (cmp < 0)
        {
            node.Left = Insert(node.Left, key, value, ref added);
        }
        else if (cmp > 0)
        {
            node.Right = Insert(node.Right, key, value, ref added);
        }
        else
        {
            node.Value = value;
            return node;
        }

        return Rebalance(node);
    }

    // ---------- lookup ----------

    public bool TryGetValue(TKey key, out TValue value)
    {
        Node? node = _root;
        while (node is not null)
        {
            int cmp = key.CompareTo(node.Key);
            if (cmp == 0)
            {
                value = node.Value;
                return true;
            }

            node = cmp < 0 ? node.Left : node.Right;
        }

        value = default!;
        return false;
    }

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetMin(out TKey key, out TValue value)
    {
        if (_root is null)
        {
            key = default!;
            value = default!;
            return false;
        }

        Node node = MinNode(_root);
        key = node.Key;
        value = node.Value;
        return true;
    }

    public bool TryGetMax(out TKey key, out TValue value)
    {
        if (_root is null)
        {
            key = default!;
            value = default!;
            return false;
        }

        Node node = _root;
        while (node.Right is not null)
        {
            node = node.Right;
        }

        key = node.Key;
        value = node.Value;
        return true;
    }

    // ---------- delete ----------

    /// <summary>Removes the key. Returns false if it was not present.</summary>
    public bool Remove(TKey key)
    {
        bool removed = false;
        _root = Remove(_root, key, ref removed);
        if (removed)
        {
            Count--;
        }

        return removed;
    }

    private static Node? Remove(Node? node, TKey key, ref bool removed)
    {
        if (node is null)
        {
            return null;
        }

        int cmp = key.CompareTo(node.Key);
        if (cmp < 0)
        {
            node.Left = Remove(node.Left, key, ref removed);
        }
        else if (cmp > 0)
        {
            node.Right = Remove(node.Right, key, ref removed);
        }
        else
        {
            removed = true;

            // Zero or one child: splice the node out.
            if (node.Left is null)
            {
                return node.Right;
            }

            if (node.Right is null)
            {
                return node.Left;
            }

            // Two children: copy in the in-order successor, then delete that successor from the right subtree.
            Node successor = MinNode(node.Right);
            node.Key = successor.Key;
            node.Value = successor.Value;
            bool ignored = false;
            node.Right = Remove(node.Right, successor.Key, ref ignored);
        }

        return Rebalance(node);
    }

    // ---------- range query ----------

    /// <summary>All entries with <paramref name="low"/> &lt;= key &lt;= <paramref name="high"/>, in ascending key order.</summary>
    public List<KeyValuePair<TKey, TValue>> Range(TKey low, TKey high) => Range(low, high, out _);

    /// <summary>
    /// Same as <see cref="Range(TKey, TKey)"/> but also reports how many tree nodes the search actually touched,
    /// which is what the benchmark compares against the size of the whole history.
    /// </summary>
    public List<KeyValuePair<TKey, TValue>> Range(TKey low, TKey high, out int nodesVisited)
    {
        var results = new List<KeyValuePair<TKey, TValue>>();
        nodesVisited = 0;

        if (low.CompareTo(high) <= 0)
        {
            CollectRange(_root, low, high, results, ref nodesVisited);
        }

        return results;
    }

    private static void CollectRange(Node? node, TKey low, TKey high, List<KeyValuePair<TKey, TValue>> results, ref int visited)
    {
        if (node is null)
        {
            return;
        }

        visited++;

        int cmpLow = low.CompareTo(node.Key);   // < 0 means the node is above the lower bound
        int cmpHigh = high.CompareTo(node.Key); // > 0 means the node is below the upper bound

        // Only descend left if smaller keys could still be in range.
        if (cmpLow < 0)
        {
            CollectRange(node.Left, low, high, results, ref visited);
        }

        if (cmpLow <= 0 && cmpHigh >= 0)
        {
            results.Add(new KeyValuePair<TKey, TValue>(node.Key, node.Value));
        }

        // Only descend right if larger keys could still be in range.
        if (cmpHigh > 0)
        {
            CollectRange(node.Right, low, high, results, ref visited);
        }
    }

    // ---------- enumeration ----------

    /// <summary>Every entry in ascending key order (iterative in-order walk).</summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> InOrder()
    {
        var stack = new Stack<Node>();
        Node? current = _root;

        while (current is not null || stack.Count > 0)
        {
            while (current is not null)
            {
                stack.Push(current);
                current = current.Left;
            }

            current = stack.Pop();
            yield return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
            current = current.Right;
        }
    }

    public void Clear()
    {
        _root = null;
        Count = 0;
    }

    // ---------- self-check ----------

    /// <summary>
    /// Verifies the AVL invariants (keys in order, stored heights correct, every balance factor within -1..1).
    /// Used by the unit tests; cheap enough to call in a diagnostic endpoint too.
    /// </summary>
    public bool ValidateInvariants()
    {
        return Validate(_root, default, false, default, false, out _);
    }

    private static bool Validate(Node? node, TKey? min, bool hasMin, TKey? max, bool hasMax, out int height)
    {
        if (node is null)
        {
            height = 0;
            return true;
        }

        if (hasMin && node.Key.CompareTo(min!) <= 0)
        {
            height = 0;
            return false;
        }

        if (hasMax && node.Key.CompareTo(max!) >= 0)
        {
            height = 0;
            return false;
        }

        bool leftOk = Validate(node.Left, min, hasMin, node.Key, true, out int leftHeight);
        bool rightOk = Validate(node.Right, node.Key, true, max, hasMax, out int rightHeight);

        height = 1 + Math.Max(leftHeight, rightHeight);

        return leftOk
            && rightOk
            && Math.Abs(leftHeight - rightHeight) <= 1
            && node.Height == height;
    }

    // ---------- balancing ----------

    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private static int BalanceOf(Node node) => HeightOf(node.Left) - HeightOf(node.Right);

    private static void UpdateHeight(Node node) => node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));

    private static Node Rebalance(Node node)
    {
        UpdateHeight(node);
        int balance = BalanceOf(node);

        if (balance > 1)
        {
            // Left-heavy. If the left child leans right, rotate it left first (left-right case).
            if (BalanceOf(node.Left!) < 0)
            {
                node.Left = RotateLeft(node.Left!);
            }

            return RotateRight(node);
        }

        if (balance < -1)
        {
            // Right-heavy. If the right child leans left, rotate it right first (right-left case).
            if (BalanceOf(node.Right!) > 0)
            {
                node.Right = RotateRight(node.Right!);
            }

            return RotateLeft(node);
        }

        return node;
    }

    private static Node RotateRight(Node node)
    {
        Node pivot = node.Left!;
        node.Left = pivot.Right;
        pivot.Right = node;

        UpdateHeight(node);
        UpdateHeight(pivot);
        return pivot;
    }

    private static Node RotateLeft(Node node)
    {
        Node pivot = node.Right!;
        node.Right = pivot.Left;
        pivot.Left = node;

        UpdateHeight(node);
        UpdateHeight(pivot);
        return pivot;
    }

    private static Node MinNode(Node node)
    {
        while (node.Left is not null)
        {
            node = node.Left;
        }

        return node;
    }
}
