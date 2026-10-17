//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

public sealed class DeploymentZone
{
    public const int MaxDepth = 8;
    public const int MaxNameLength = 60;

    public string Name { get; }
    public List<DeploymentZone> Children { get; } = new();

    public DeploymentZone(string name)
    {
        Name = name;
    }

    public void AddChild(DeploymentZone child) => Children.Add(child);
    public static DeploymentZone Parse(string location)
    {
        var segments = (location ?? string.Empty).Split(',').Select(s => s.Trim()).ToArray();
        return BuildChain(segments, 0);
    }

    //Recursive: base case is the last segment
    private static DeploymentZone BuildChain(string[] segments, int index)
    {
        var node = new DeploymentZone(segments[index]);
        if (index < segments.Length - 1)
        {
            node.AddChild(BuildChain(segments, index + 1));
        }
        return node;
    }

    public bool Validate(out string invalidPath, string parentPath = "", int depth = 1)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? Name : $"{parentPath} -> {Name}";

        if (string.IsNullOrWhiteSpace(Name))
        {
            invalidPath = $"{currentPath} (empty zone name)";
            return false;
        }

        if (Name.Length > MaxNameLength)
        {
            invalidPath = $"{currentPath} (name longer than {MaxNameLength} characters)";
            return false;
        }

        if (depth > MaxDepth)
        {
            invalidPath = $"{currentPath} (deeper than {MaxDepth} levels)";
            return false;
        }

        //Recursive step
        foreach (var child in Children)
        {
            if (!child.Validate(out invalidPath, currentPath, depth + 1))
            {
                return false; //stop at first invalid zone
            }
        }

        invalidPath = string.Empty;
        return true; //zone + all descendants valid
    }

    public int CountZones() => 1 + Children.Sum(c => c.CountZones());

    public int Depth() => 1 + (Children.Count == 0 ? 0 : Children.Max(c => c.Depth()));

    public string LeafPath() => Children.Count == 0 ? Name : $"{Name} -> {Children[0].LeafPath()}";
}
