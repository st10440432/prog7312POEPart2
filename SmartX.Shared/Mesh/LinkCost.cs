//ST10440432
//Matteo Nusca
using System.Globalization;

namespace SmartX.Shared.Mesh;

/// <summary>
/// The power cost of sending a packet across one mesh link, in milliwatts. Costs add up along a route, and routing
/// algorithms constantly compare them, so the type overloads <c>+</c> and the comparison operators:
/// <c>distance[node] + linkCost &lt; distance[neighbour]</c> reads exactly like the maths in the Dijkstra and Prim code.
/// A cost can never be negative or NaN, which is what makes Dijkstra and Prim valid on the graph.
/// </summary>
public readonly struct LinkCost : IComparable<LinkCost>, IEquatable<LinkCost>
{
    public double Milliwatts { get; }

    public LinkCost(double milliwatts)
    {
        if (double.IsNaN(milliwatts) || milliwatts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliwatts), milliwatts, "A link cost cannot be negative or NaN.");
        }

        Milliwatts = milliwatts;
    }

    public static LinkCost Zero => new(0);

    /// <summary>Cost of a route that does not exist. Adding anything to it stays infinite.</summary>
    public static LinkCost Unreachable => new(double.PositiveInfinity);

    public bool IsUnreachable => double.IsPositiveInfinity(Milliwatts);

    public static LinkCost operator +(LinkCost left, LinkCost right) => new(left.Milliwatts + right.Milliwatts);

    public static bool operator <(LinkCost left, LinkCost right) => left.Milliwatts < right.Milliwatts;

    public static bool operator >(LinkCost left, LinkCost right) => left.Milliwatts > right.Milliwatts;

    public static bool operator <=(LinkCost left, LinkCost right) => left.Milliwatts <= right.Milliwatts;

    public static bool operator >=(LinkCost left, LinkCost right) => left.Milliwatts >= right.Milliwatts;

    public static bool operator ==(LinkCost left, LinkCost right) => left.Milliwatts == right.Milliwatts;

    public static bool operator !=(LinkCost left, LinkCost right) => left.Milliwatts != right.Milliwatts;

    public int CompareTo(LinkCost other) => Milliwatts.CompareTo(other.Milliwatts);

    public bool Equals(LinkCost other) => Milliwatts == other.Milliwatts;

    public override bool Equals(object? obj) => obj is LinkCost other && Equals(other);

    public override int GetHashCode() => Milliwatts.GetHashCode();

    public override string ToString() =>
        IsUnreachable ? "unreachable" : Milliwatts.ToString("0.##", CultureInfo.InvariantCulture) + " mW";
}
