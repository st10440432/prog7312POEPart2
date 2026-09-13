//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telemetry");

        //GET /api/telemetry all dashboard
        group.MapGet("/", (TelemetryStore store) => Results.Ok(new TelemetryDashboardResponse
        {
            Environmental = store.EnvironmentalReadings,
            Power = store.PowerReadings,
            Actuator = store.ActuatorReadings
        }));

        //POST /api/telemetry/environmental - readings
        group.MapPost("/environmental", (TelemetryIngestRequest<float> request, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(request.DeviceId);
            if (sensor is null) return Results.NotFound($"No sensor registered with DeviceId '{request.DeviceId}'.");

            var packet = new TelemetryPacket<float>(request.DeviceId, sensor.DeploymentLocation, SensorCategory.Environmental, request.Value, DateTime.UtcNow);
            store.EnvironmentalReadings.Add(packet);
            return Results.Created($"/api/telemetry/environmental/{request.DeviceId}", packet);
        });

        //POST /api/telemetry/power - int readings
        group.MapPost("/power", (TelemetryIngestRequest<int> request, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(request.DeviceId);
            if (sensor is null) return Results.NotFound($"No sensor registered with DeviceId '{request.DeviceId}'.");

            var packet = new TelemetryPacket<int>(request.DeviceId, sensor.DeploymentLocation, SensorCategory.PowerConsumption, request.Value, DateTime.UtcNow);
            store.PowerReadings.Add(packet);
            return Results.Created($"/api/telemetry/power/{request.DeviceId}", packet);
        });

        //POST /api/telemetry/actuator - bool readings
        group.MapPost("/actuator", (TelemetryIngestRequest<bool> request, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(request.DeviceId);
            if (sensor is null) return Results.NotFound($"No sensor registered with DeviceId '{request.DeviceId}'.");

            var packet = new TelemetryPacket<bool>(request.DeviceId, sensor.DeploymentLocation, SensorCategory.Actuator, request.Value, DateTime.UtcNow);
            store.ActuatorReadings.Add(packet);
            return Results.Created($"/api/telemetry/actuator/{request.DeviceId}", packet);
        });
    }
}