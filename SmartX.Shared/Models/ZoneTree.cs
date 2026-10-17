//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

public sealed class ZoneNodeDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int DirectSensors { get; set; }
    public int DirectAlerts { get; set; }
    public int TotalSensors { get; set; }
    public int TotalAlerts { get; set; }
    public List<ZoneNodeDto> Children { get; set; } = new();
}

public static class ZoneTreeBuilder
{
    public static ZoneNodeDto Build(IEnumerable<(string Location, int ActiveAlerts)> sensors)
    {
        var root = new ZoneNodeDto { Name = "All Sites", Path = "All Sites" };
        foreach (var (location, alerts) in sensors)
        {
            var segments = (location ?? string.Empty).Split(',')
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            Insert(root, segments, 0, alerts);
        }
        Rollup(root);
        return root;
    }

    private static void Insert(ZoneNodeDto node, string[] segments, int index, int alerts)
    {
        if (index == segments.Length)
        {
            node.DirectSensors++;
            node.DirectAlerts += alerts;
            return;
        }

        var child = node.Children.FirstOrDefault(c =>
            string.Equals(c.Name, segments[index], StringComparison.OrdinalIgnoreCase));

        if (child is null)
        {
            child = new ZoneNodeDto { Name = segments[index], Path = $"{node.Path} -> {segments[index]}" };
            node.Children.Add(child);
        }

        Insert(child, segments, index + 1, alerts);
    }

    private static (int Sensors, int Alerts) Rollup(ZoneNodeDto node)
    {
        int sensors = node.DirectSensors;
        int alerts = node.DirectAlerts;
        foreach (var child in node.Children)
        {
            var (s, a) = Rollup(child);
            sensors += s;
            alerts += a;
        }
        node.TotalSensors = sensors;
        node.TotalAlerts = alerts;
        return (sensors, alerts);
    }
}
