//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    public static void MapSensorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors");

        //GET /api/sensors - list every registered sensor
        group.MapGet("/", (DeviceRegistry registry) => Results.Ok(registry.Sensors()));

        //GET /api/sensors/{deviceId} - lookup of one sensor using disctionary
        group.MapGet("/{deviceId}", (string deviceId, DeviceRegistry registry) =>
        {
            var sensor = registry.FindSensor(deviceId);
            return sensor is not null ? Results.Ok(sensor) : Results.NotFound();
        });

        //POST /api/sensors - register a new sensor
        group.MapPost("/", (SensorRegistration registration, DeviceRegistry registry) =>
        {
            var outcome = registry.TryRegister(registration, out var error);
            return outcome switch
            {
                RegisterOutcome.Created => Results.Created($"/api/sensors/{registration.DeviceId}", registration),
                RegisterOutcome.Conflict => Results.Conflict(error),
                _ => Results.BadRequest(error)
            };
        });

        //GET /api/zones - deployment hierarchy with sensor / alert counts rolled up recursively
        app.MapGet("/api/zones", (DeviceRegistry registry, AlertTracker alerts) =>
        {
            var alertCounts = alerts.Snapshot()
                .GroupBy(a => a.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var tree = ZoneTreeBuilder.Build(registry.Sensors()
                .Select(s => (s.DeploymentLocation, alertCounts.GetValueOrDefault(s.DeviceId))));

            return Results.Ok(tree);
        });
    }
}
