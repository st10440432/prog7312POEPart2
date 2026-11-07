using SmartX.Shared.Mesh;

namespace SmartX.Tests;

/// <summary>Covers the LinkCost value type and its overloaded operators.</summary>
public class LinkCostTests
{
    [Fact]
    public void Plus_AddsCosts_AndZeroIsTheIdentity()
    {
        var total = new LinkCost(2.5) + new LinkCost(4);

        Assert.Equal(6.5, total.Milliwatts);
        Assert.Equal(new LinkCost(7), new LinkCost(7) + LinkCost.Zero);
    }

    [Fact]
    public void ComparisonOperators_OrderByMilliwatts()
    {
        var low = new LinkCost(1);
        var high = new LinkCost(5);

        Assert.True(low < high);
        Assert.True(high > low);
        Assert.True(low <= low);
        Assert.True(high >= high);
        Assert.False(low > high);
        Assert.True(low == new LinkCost(1));
        Assert.True(low != high);
    }

    [Fact]
    public void Equality_AndHashCode_AreConsistent()
    {
        var a = new LinkCost(3);
        var b = new LinkCost(3);

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals("3"));
    }

    [Fact]
    public void Constructor_RejectsNegativeAndNaNCosts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LinkCost(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LinkCost(double.NaN); });
    }

    [Fact]
    public void Unreachable_StaysUnreachableAndBeatsEveryRealCost()
    {
        var unreachable = LinkCost.Unreachable;

        Assert.True(unreachable.IsUnreachable);
        Assert.True((unreachable + new LinkCost(5)).IsUnreachable);
        Assert.True(unreachable > new LinkCost(1_000_000));
        Assert.False(new LinkCost(1_000_000).IsUnreachable);
    }

    [Fact]
    public void Costs_SortCorrectly()
    {
        var sorted = new[] { new LinkCost(3), new LinkCost(1), new LinkCost(2) }.OrderBy(c => c).ToList();

        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, sorted.Select(c => c.Milliwatts));
    }

    [Fact]
    public void ToString_IsReadable()
    {
        Assert.Equal("7 mW", new LinkCost(7).ToString());
        Assert.Equal("2.5 mW", new LinkCost(2.5).ToString());
        Assert.Equal("unreachable", LinkCost.Unreachable.ToString());
    }
}
