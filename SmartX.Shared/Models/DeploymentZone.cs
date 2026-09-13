//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

/// <summary>
/// Represents one node in a nested device deployment hierarchy
/// (e.g. Facility A -> Zone 1 -> Sub-Zone B -> individual sensor node).
/// Validate() recurses down through every descendant zone, so a
/// misconfiguration anywhere in a deeply nested tree is caught without
/// writing a separate loop for each possible depth.
/// </summary>
public sealed class DeploymentZone
{
    public string Name { get; }
    public List<DeploymentZone> Children { get; } = new();

    public DeploymentZone(string name)
    {
        Name = name;
    }

    public void AddChild(DeploymentZone child) => Children.Add(child);
    public bool Validate(out string invalidPath, string parentPath = "")
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? Name : $"{parentPath} -> {Name}";

        if (string.IsNullOrWhiteSpace(Name))
        {
            invalidPath = currentPath;
            return false;
        }

        // Recursive step
        foreach (var child in Children)
        {
            if (!child.Validate(out invalidPath, currentPath))
            {
                return false; //stop at first invalid zone
            }
        }

        invalidPath = string.Empty;
        return true; //zone + all descendants valid
    }
}