//ST10440432
//Matteo Nusca
using Microsoft.Extensions.Options;
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this WebApplication app)
    {
        //GET /api/stream/snapshot?search=&category=&alertsOnly=
        //One call returns everything needed by command stream dashboard, client polls a single endpoint.
        app.MapGet("/api/stream/snapshot", (
            DeviceRegistry registry, AlertTracker alerts, IngestionPipeline pipeline,
            CommandService commands, InsightEngine engine, IOptions<GatewayOptions> options,
            string? search, int? category, bool? alertsOnly) =>
        {
            var staleAfter = TimeSpan.FromSeconds(options.Value.StaleAfterSeconds);
            var now = DateTime.UtcNow;

            var alertList = alerts.Snapshot();
            var alertsByDevice = alertList
                .GroupBy(a => a.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(a => a.Type).ToList(), StringComparer.OrdinalIgnoreCase);

            var insights = engine.Build();
            var insightByDevice = insights.Insights
                .GroupBy(i => i.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(i => i.Severity), StringComparer.OrdinalIgnoreCase);

            var views = registry.Views();

            var devices = views
                .Where(v => category is null || (int)v.Category == category)
                .Where(v => DeviceRegistry.Matches(v, search ?? string.Empty))
                .Select(v =>
                {
                    var deviceAlerts = alertsByDevice.GetValueOrDefault(v.Id) ?? new List<string>();
                    var state = new DeviceState
                    {
                        DeviceId = v.Id,
                        FriendlyName = v.Registration.FriendlyName,
                        Location = v.Registration.DeploymentLocation,
                        Category = v.Category,
                        LastValueText = v.LastValueText,
                        LastSeenUtc = v.LastSeenUtc,
                        ActuatorOn = v.Category == SensorCategory.Actuator ? v.ActuatorOn : null,
                        ActiveAlerts = deviceAlerts,
                        Status = StatusOf(v, deviceAlerts, now, staleAfter)
                    };

                    if (insightByDevice.TryGetValue(v.Id, out var severity))
                    {
                        state.HasInsight = true;
                        state.InsightSeverity = severity;
                    }

                    return state;
                })
                .Where(d => alertsOnly != true || d.Status is DeviceStatus.Alert or DeviceStatus.Disconnected or DeviceStatus.Stale)
                .ToList();

            return Results.Ok(new StreamSnapshot
            {
                GeneratedUtc = now,
                Stats = pipeline.Stats(commands.UndoDepth),
                Devices = devices,
                TotalDevices = views.Count,
                Alerts = alertList,
                RecentCommands = commands.History(12),
                Insights = insights
            });
        });
    }

    private static DeviceStatus StatusOf(DeviceView v, List<string> alerts, DateTime now, TimeSpan staleAfter)
    {
        if (alerts.Contains(AlertTracker.Disconnected)) return DeviceStatus.Disconnected;
        if (alerts.Count > 0) return DeviceStatus.Alert;
        if (v.LastSeenUtc is null) return DeviceStatus.NoData;
        return now - v.LastSeenUtc.Value > staleAfter ? DeviceStatus.Stale : DeviceStatus.Live;
    }
}
