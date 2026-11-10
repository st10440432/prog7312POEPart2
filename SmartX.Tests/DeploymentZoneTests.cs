using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>Covers the recursive deployment-hierarchy parsing and validation (Part 1 recursion requirement).</summary>
public class DeploymentZoneTests
{
    [Fact]
    public void Parse_BuildsOrderedChain()
    {
        var zone = DeploymentZone.Parse("Facility A, Zone 1, Sub-Zone B");

        Assert.Equal("Facility A", zone.Name);
        Assert.Equal(3, zone.CountZones());
        Assert.Equal(3, zone.Depth());
        Assert.Equal("Facility A -> Zone 1 -> Sub-Zone B", zone.LeafPath());
    }

    [Fact]
    public void Validate_AcceptsValidHierarchy()
    {
        var zone = DeploymentZone.Parse("Greenhouse 1, Zone A, Node 1");

        Assert.True(zone.Validate(out var invalidPath));
        Assert.Equal(string.Empty, invalidPath);
    }

    [Fact]
    public void Validate_RejectsEmptySegment_AndNamesTheOffendingPath()
    {
        var zone = DeploymentZone.Parse("Facility A,, Node 1");

        Assert.False(zone.Validate(out var invalidPath));
        Assert.Contains("empty zone name", invalidPath);
        Assert.StartsWith("Facility A", invalidPath);
    }

    [Fact]
    public void Validate_RejectsEmptyLocation()
    {
        var zone = DeploymentZone.Parse(string.Empty);

        Assert.False(zone.Validate(out _));
    }

    [Fact]
    public void Validate_RejectsNameLongerThanLimit()
    {
        var zone = DeploymentZone.Parse($"Facility A, {new string('x', DeploymentZone.MaxNameLength + 1)}");

        Assert.False(zone.Validate(out var invalidPath));
        Assert.Contains("longer than", invalidPath);
    }

    [Fact]
    public void Validate_AcceptsExactlyMaxDepth_AndRejectsOneBeyond()
    {
        string atLimit = string.Join(", ", Enumerable.Range(1, DeploymentZone.MaxDepth).Select(i => $"L{i}"));
        string beyond = string.Join(", ", Enumerable.Range(1, DeploymentZone.MaxDepth + 1).Select(i => $"L{i}"));

        Assert.True(DeploymentZone.Parse(atLimit).Validate(out _));

        Assert.False(DeploymentZone.Parse(beyond).Validate(out var invalidPath));
        Assert.Contains("deeper than", invalidPath);
    }
}
